// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_icc_codec.h and lib/jxl/enc_icc_codec.cc (Unshuffle, PredictAndShuffle, EncodeVarInt, PredictICC, WriteICC).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Entropy;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using static PinataLabs.ImagePastel.Formats.Jxl.Icc.JxlIccCodecCommon;

namespace PinataLabs.ImagePastel.Formats.Jxl.Icc;

/// <summary>
/// Writes an ICC profile after the codestream headers: <see cref="PredictIcc"/> turns the profile into
/// commands and data that compress well, and <see cref="WriteIcc"/> entropy-codes them. It is the mirror
/// of <see cref="JxlIccReader"/>.
/// </summary>
internal static class JxlIccWriter
{
    // Fuzzer expects that PredictICC can accept any input, but 1GB should be enough for any purpose.
    private const ulong SizeLimit = uint.MaxValue >> 2;

    /// <summary>
    /// Writes <paramref name="icc"/> as <see cref="JxlIccReader.Read"/> reads it: the encoded size, the
    /// histograms, then one token per encoded byte.
    /// </summary>
    /// <param name="icc">The profile; it must not be empty.</param>
    /// <param name="writer">The writer, after the transform data.</param>
    public static void WriteIcc(byte[] icc, JxlBitWriter writer)
    {
        if (icc.Length == 0)
        {
            JxlThrowHelper.ThrowImageFormatException("ICC must be non-empty");
        }

        List<byte> enc = new();
        PredictIcc(icc, enc);
        CheckPreamble(enc);
        List<JxlToken>[] tokens = { new(enc.Count) };
        JxlFieldCoders.WriteU64((ulong)enc.Count, writer);

        for (int i = 0; i < enc.Count; i++)
        {
            uint ctx = (uint)IccAnsContext(i, i > 0 ? enc[i - 1] : 0, i > 1 ? enc[i - 2] : 0);
            tokens[0].Add(new JxlToken(ctx, enc[i]));
        }

        // libjxl default-constructs the parameters, then sets lz77_method to kOptc256 below 16384 bytes
        // and kLZ77b3w3f above, and force_huffman. Departures (ADR 0001): those LZ77 match finders are
        // not ported, so LZ77 is off; the default kBest clustering runs as the fast clustering, and the
        // kBest hybrid uint search, which is not ported either, keeps the default configuration. The ICC
        // stream is the same at every effort, so this also keeps the Fastest output as E1 wrote it. Each
        // choice gives a valid stream, only larger than libjxl's. force_huffman makes it prefix-coded.
        JxlHistogramParams parameters = new()
        {
            Lz77 = JxlHistogramParams.Lz77Method.None,
            ForceHuffman = true,
        };

        JxlEntropyEncodingData code = new();
        JxlEntropyEncoder.BuildAndEncodeHistograms(parameters, NumIccContexts, tokens, code, writer);
        JxlEntropyEncoder.WriteTokens(tokens[0], code, 0, writer);
    }

    /// <summary>
    /// Outputs a transformed form of the given ICC profile. The result itself is not particularly
    /// smaller than the input data in bytes, but it will be in a form that is easier to compress (more
    /// zeroes, ...) and will compress better. Any byte sequence can be encoded, not only a valid profile;
    /// one the encoder does not recognise is predicted less well.
    /// </summary>
    /// <param name="icc">The profile.</param>
    /// <param name="result">Receives the encoded bytes.</param>
    public static void PredictIcc(byte[] icc, List<byte> result)
    {
        // libjxl's helpers read the profile as a byte array; the shared C# predictors take the List<byte>
        // that the decoder builds, so the profile is also held as one.
        List<byte> iccList = new(icc);
        ulong size = (ulong)icc.Length;
        List<byte> commands = new();
        List<byte> data = new();

        if (size > SizeLimit)
        {
            JxlThrowHelper.ThrowImageFormatException("ICC profile is too large");
        }

        EncodeVarInt(size, result);

        // Header
        byte[] header = IccInitialHeaderPrediction((uint)size);
        for (int i = 0; i < IccHeaderSize && (ulong)i < size; i++)
        {
            IccPredictHeader(iccList, (int)size, header, i);
            data.Add((byte)(icc[i] - header[i]));
        }

        if (size <= IccHeaderSize)
        {
            EncodeVarInt(0, result); // 0 commands
            result.AddRange(data);
            return;
        }

        List<uint> tags = new();
        List<uint> tagstarts = new();
        List<uint> tagsizes = new();
        Dictionary<ulong, int> tagmap = new();

        // Tag list
        ulong pos = IccHeaderSize;
        if (pos + 4 <= size)
        {
            ulong numtags = DecodeUint32(iccList, (int)size, (long)pos);
            pos += 4;
            EncodeVarInt(numtags + 1, commands);
            ulong prevtagstart = IccHeaderSize + (numtags * 12);
            uint prevtagsize = 0;
            for (ulong i = 0; i < numtags; i++)
            {
                if (pos + 12 > size)
                {
                    break;
                }

                uint tag = DecodeKeyword(icc, (long)size, (long)pos);
                uint tagstart = DecodeUint32(iccList, (int)size, (long)pos + 4);
                uint tagsize = DecodeUint32(iccList, (int)size, (long)pos + 8);
                pos += 12;

                tags.Add(tag);
                tagstarts.Add(tagstart);
                tagsizes.Add(tagsize);
                tagmap[tagstart] = tags.Count - 1;

                byte tagcode = CommandTagUnknown;
                for (int j = 0; j < TagStrings.Length; j++)
                {
                    if (tag == TagStrings[j])
                    {
                        tagcode = (byte)(j + CommandTagStringFirst);
                        break;
                    }
                }

                if (tag == RtrcTag && pos + 24 < size)
                {
                    bool ok = true;
                    ok &= DecodeKeyword(icc, (long)size, (long)pos) == GtrcTag;
                    ok &= DecodeKeyword(icc, (long)size, (long)pos + 12) == BtrcTag;
                    if (ok)
                    {
                        for (int kk = 0; kk < 8; kk++)
                        {
                            int p = (int)pos;
                            if (icc[p - 8 + kk] != icc[p + 4 + kk])
                            {
                                ok = false;
                            }

                            if (icc[p - 8 + kk] != icc[p + 16 + kk])
                            {
                                ok = false;
                            }
                        }
                    }

                    if (ok)
                    {
                        tagcode = CommandTagTrc;
                        pos += 24;
                        i += 2;
                    }
                }

                if (tag == RxyzTag && pos + 24 < size)
                {
                    bool ok = true;
                    ok &= DecodeKeyword(icc, (long)size, (long)pos) == GxyzTag;
                    ok &= DecodeKeyword(icc, (long)size, (long)pos + 12) == BxyzTag;
                    uint offsetr = tagstart;
                    uint offsetg = DecodeUint32(iccList, (int)size, (long)pos + 4);
                    uint offsetb = DecodeUint32(iccList, (int)size, (long)pos + 16);
                    uint sizer = tagsize;
                    uint sizeg = DecodeUint32(iccList, (int)size, (long)pos + 8);
                    uint sizeb = DecodeUint32(iccList, (int)size, (long)pos + 20);
                    ok &= sizer == 20;
                    ok &= sizeg == 20;
                    ok &= sizeb == 20;
                    ok &= offsetg == unchecked(offsetr + 20);
                    ok &= offsetb == unchecked(offsetr + 40);
                    if (ok)
                    {
                        tagcode = CommandTagXyz;
                        pos += 24;
                        i += 2;
                    }
                }

                byte command = tagcode;
                ulong predictedTagstart = prevtagstart + prevtagsize;
                if (predictedTagstart != tagstart)
                {
                    command |= FlagBitOffset;
                }

                ulong predictedTagsize = prevtagsize;
                if (tag is RxyzTag or GxyzTag or BxyzTag or KxyzTag or WtptTag or BkptTag or LumiTag)
                {
                    predictedTagsize = 20;
                }

                if (predictedTagsize != tagsize)
                {
                    command |= FlagBitSize;
                }

                commands.Add(command);
                if (tagcode == 1)
                {
                    AppendKeyword(tag, data);
                }

                if ((command & FlagBitOffset) != 0)
                {
                    EncodeVarInt(tagstart, commands);
                }

                if ((command & FlagBitSize) != 0)
                {
                    EncodeVarInt(tagsize, commands);
                }

                prevtagstart = tagstart;
                prevtagsize = tagsize;
            }
        }

        // Indicate end of tag list or varint indicating there's none
        commands.Add(0);

        // Main content
        // The main content in a valid ICC profile contains tagged elements, with the tag types (4 letter
        // names) given by the tag list above, and the tag list pointing to the start and indicating the
        // size of each tagged element. It is allowed for tagged elements to overlap, e.g. the curve for
        // R, G and B could all point to the same one.
        // libjxl leaves the tag uninitialised here; the first pass of the loop always sets it to the
        // nonsensical value, because the position is past tagstart + tagsize = 0.
        uint currentTag = 0;
        ulong currentTagstart = 0;
        ulong currentTagsize = 0;
        ulong clutstart = 0;

        // Should always check TagSane before doing math with tagsize.
        bool TagSane() => (currentTagsize > 8) && (currentTagsize < SizeLimit);

        ulong last0 = pos;

        // This loop appends commands to the output, processing some sub-section of a current tagged
        // element each time. We need to keep track of the tagtype of the current element, and update it
        // when we encounter the boundary of a next one.
        // It is not required that the input data is a valid ICC profile, if the encoder does not
        // recognize the data it will still be able to output bytes but will not predict as well.
        while (pos <= size)
        {
            ulong last1 = pos;
            List<byte> commandsAdd = new();
            List<byte> dataAdd = new();

            // This means the loop brought the position beyond the tag end.
            // If tagsize is nonsensical, any pos looks "ok-ish".
            if ((pos > currentTagstart + currentTagsize) && (currentTagsize < SizeLimit))
            {
                currentTag = 0; // nonsensical value
            }

            if (commandsAdd.Count == 0 && dataAdd.Count == 0 && tagmap.TryGetValue(pos, out int index) && pos + 4 <= size)
            {
                currentTag = DecodeKeyword(icc, (long)size, (long)pos);
                currentTagstart = tagstarts[index];
                currentTagsize = tagsizes[index];

                if (currentTag == MlucTag && TagSane() && pos + currentTagsize <= size && IsZeroUint32(icc, pos + 4))
                {
                    ulong num = currentTagsize - 8;
                    commandsAdd.Add(CommandTypeStartFirst + 3);
                    pos += 8;
                    commandsAdd.Add(CommandShuffle2);
                    EncodeVarInt(num, commandsAdd);
                    int start = dataAdd.Count;
                    for (ulong i = 0; i < num; i++)
                    {
                        dataAdd.Add(icc[pos]);
                        pos++;
                    }

                    Unshuffle(dataAdd, start, (int)num, 2);
                }

                if (currentTag == CurvTag && TagSane() && pos + currentTagsize <= size && IsZeroUint32(icc, pos + 4))
                {
                    ulong num = currentTagsize - 8;
                    if (num > 16 && num < (1 << 28) && pos + num <= size && pos > 0)
                    {
                        commandsAdd.Add(CommandTypeStartFirst + 5);
                        pos += 8;
                        commandsAdd.Add(CommandPredict);
                        const int order = 1;
                        const int width = 2;
                        const int stride = width;
                        commandsAdd.Add((order << 2) | (width - 1));
                        EncodeVarInt(num, commandsAdd);
                        PredictAndShuffle(stride, width, order, num, iccList, size, ref pos, dataAdd);
                    }
                }
            }

            if (currentTag is MabTag or MbaTag)
            {
                uint subTag = DecodeKeyword(icc, (long)size, (long)pos);
                if (pos + 12 < size && (subTag is CurvTag or VcgtTag) && DecodeUint32(iccList, (int)size, (long)pos + 4) == 0)
                {
                    uint num = unchecked(DecodeUint32(iccList, (int)size, (long)pos + 8) * 2);
                    if (num > 16 && num < (1 << 28) && pos + 12 + num <= size)
                    {
                        pos += 12;
                        last1 = pos;
                        commandsAdd.Add(CommandPredict);
                        const int order = 1;
                        const int width = 2;
                        const int stride = width;
                        commandsAdd.Add((order << 2) | (width - 1));
                        EncodeVarInt(num, commandsAdd);
                        PredictAndShuffle(stride, width, order, num, iccList, size, ref pos, dataAdd);
                    }
                }

                if (pos == currentTagstart + 24 && pos + 4 < size)
                {
                    // Note that this value can be remembered for next iterations of the loop, so the
                    // "pos == clutstart" if below can trigger during a later iteration.
                    clutstart = currentTagstart + DecodeUint32(iccList, (int)size, (long)pos);
                }

                if (pos == clutstart && clutstart + 16 < size)
                {
                    ulong numi = icc[currentTagstart + 8];
                    ulong numo = icc[currentTagstart + 9];
                    ulong width = icc[clutstart + 16];
                    ulong stride = width * numo;
                    ulong num = width * numo;
                    for (ulong i = 0; i < numi && clutstart + i < size; i++)
                    {
                        num = unchecked(num * icc[clutstart + i]);
                    }

                    if ((width == 1 || width == 2) && num > 64 && num < (1 << 28) && pos + num <= size && pos > stride * 4)
                    {
                        commandsAdd.Add(CommandPredict);
                        const int order = 1;
                        byte flags = (byte)((order << 2) | (int)(width - 1) | (stride == width ? 0 : 16));
                        commandsAdd.Add(flags);
                        if ((flags & 16) != 0)
                        {
                            EncodeVarInt(stride, commandsAdd);
                        }

                        EncodeVarInt(num, commandsAdd);
                        PredictAndShuffle(stride, width, order, num, iccList, size, ref pos, dataAdd);
                    }
                }
            }

            if (commandsAdd.Count == 0 && dataAdd.Count == 0 && currentTag == GbdTag && TagSane() && pos == currentTagstart + 8 && pos + currentTagsize - 8 <= size && pos > 16)
            {
                const ulong width = 4;
                const int order = 0;
                const ulong stride = width;
                ulong num = currentTagsize - 8;

                // libjxl's flags are (order << 2) | (width - 1) | (stride == width ? 0 : 16). With order 0
                // and stride equal to width, that is width - 1, and no stride follows.
                const byte flags = (byte)(width - 1);
                commandsAdd.Add(CommandPredict);
                commandsAdd.Add(flags);
                EncodeVarInt(num, commandsAdd);
                PredictAndShuffle(stride, width, order, num, iccList, size, ref pos, dataAdd);
            }

            if (commandsAdd.Count == 0 && dataAdd.Count == 0 && pos + 20 <= size)
            {
                uint subTag = DecodeKeyword(icc, (long)size, (long)pos);
                if (subTag == XyzTypeTag && DecodeUint32(iccList, (int)size, (long)pos + 4) == 0)
                {
                    commandsAdd.Add(CommandXyz);
                    pos += 8;
                    for (int j = 0; j < 12; j++)
                    {
                        dataAdd.Add(icc[pos++]);
                    }
                }
            }

            if (commandsAdd.Count == 0 && dataAdd.Count == 0 && pos + 8 <= size)
            {
                if (DecodeUint32(iccList, (int)size, (long)pos + 4) == 0)
                {
                    uint subTag = DecodeKeyword(icc, (long)size, (long)pos);
                    for (int i = 0; i < TypeStrings.Length; i++)
                    {
                        if (subTag == TypeStrings[i])
                        {
                            commandsAdd.Add((byte)(CommandTypeStartFirst + i));
                            pos += 8;
                            break;
                        }
                    }
                }
            }

            if (!(commandsAdd.Count == 0 && dataAdd.Count == 0) || pos == size)
            {
                if (last0 < last1)
                {
                    commands.Add(CommandInsert);
                    EncodeVarInt(last1 - last0, commands);
                    while (last0 < last1)
                    {
                        data.Add(icc[last0++]);
                    }
                }

                commands.AddRange(commandsAdd);
                data.AddRange(dataAdd);
                last0 = pos;
            }

            if (commandsAdd.Count == 0 && dataAdd.Count == 0)
            {
                pos++;
            }
        }

        EncodeVarInt((ulong)commands.Count, result);
        result.AddRange(commands);
        result.AddRange(data);
    }

    // Departure (ADR 0001): libjxl writes any profile that PredictICC encodes, but its reader
    // (ICCReader::Init) checks the preamble against only the first 0x400 bytes it has decoded, so it
    // rejects a profile whose commands do not end within them, such as one with hundreds of tags at
    // unpredictable offsets. The encoder must not write a stream that the decoders reject, so it runs
    // the reader's own check first and throws when it fails.
    private static void CheckPreamble(List<byte> enc)
    {
        if (enc.Count <= JxlIccReader.PreambleSize)
        {
            return;
        }

        byte[] preamble = enc.GetRange(0, Math.Min(0x400, enc.Count)).ToArray();
        try
        {
            JxlIccReader.CheckPreamble(preamble, preamble.Length, (ulong)enc.Count);
        }
        catch (InvalidImageContentException ex)
        {
            JxlThrowHelper.ThrowImageFormatException("The ICC profile cannot be embedded: libjxl's ICC reader would reject its encoded form.", ex);
        }
    }

    // Unshuffles or de-interleaves bytes, for example with width 2, turns "AaBbCcDc" into "ABCDabcd",
    // this for example de-interleaves UTF-16 bytes into first all the high order bytes, then all the low
    // order bytes. Transposes a matrix of width columns and ceil(size / width) rows. There are size
    // elements, size may be < width * height, if so the last elements of the bottom row are missing, the
    // missing spots are transposed along with the filled spots, and the result has the missing elements
    // at the bottom of the rightmost column. The input is the input matrix in scanline order, the output
    // is the result matrix in scanline order, with missing elements skipped over (this may occur at
    // multiple positions). libjxl takes a pointer into the buffer; the port takes the buffer and an offset.
    private static void Unshuffle(List<byte> data, int offset, int size, int width)
    {
        int height = (size + width - 1) / width; // amount of rows of input
        byte[] result = new byte[size];

        // i = input index, j output index
        int s = 0;
        int j = 0;
        for (int i = 0; i < size; i++)
        {
            result[j] = data[offset + i];
            j += height;
            if (j >= size)
            {
                j = ++s;
            }
        }

        for (int i = 0; i < size; i++)
        {
            data[offset + i] = result[i];
        }
    }

    // This is performed by the encoder, the encoder must be able to encode any random byte stream (not
    // just byte streams that are a valid ICC profile), so an error returned by this function is an
    // implementation error.
    private static void PredictAndShuffle(ulong stride, ulong width, int order, ulong num, List<byte> data, ulong size, ref ulong pos, List<byte> result)
    {
        CheckOutOfBounds(pos, num, size);

        // Required by the specification, see decoder. stride * 4 must be < *pos.
        if (pos == 0 || ((pos - 1u) >> 2) < stride)
        {
            JxlThrowHelper.ThrowImageFormatException("Invalid stride");
        }

        if (pos < stride * 4)
        {
            JxlThrowHelper.ThrowImageFormatException("Too large stride");
        }

        int start = result.Count;
        for (ulong i = 0; i < num; i++)
        {
            byte predicted = LinearPredictIccValue(data, (int)pos, (int)i, (int)stride, (int)width, order);
            result.Add((byte)(data[(int)(pos + i)] - predicted));
        }

        pos += num;
        if (width > 1)
        {
            Unshuffle(result, start, (int)num, (int)width);
        }
    }

    // LEB128: while more than 7 bits of data are left, store 7 bits and set the next byte flag.
    private static void EncodeVarInt(ulong value, List<byte> data)
    {
        while (value > 127)
        {
            // |128: Set the next byte flag
            data.Add((byte)((value & 127) | 128));

            // Remove the seven bits we just wrote
            value >>= 7;
        }

        data.Add((byte)(value & 127));
    }

    // libjxl's icc[pos] == 0 && icc[pos + 1] == 0 && icc[pos + 2] == 0 && icc[pos + 3] == 0.
    private static bool IsZeroUint32(byte[] icc, ulong pos)
        => icc[pos] == 0 && icc[pos + 1] == 0 && icc[pos + 2] == 0 && icc[pos + 3] == 0;
}
