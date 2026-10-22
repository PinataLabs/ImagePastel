// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/icc_codec.h and lib/jxl/icc_codec.cc (ICCReader, UnpredictICC, CheckPreamble).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Icc.JxlIccCodecCommon;

namespace SixLabors.ImageSharp.Formats.Jxl.Icc;

/// <summary>
/// Reads the ICC profile that the codestream embeds after its headers: an entropy-coded stream of
/// commands and data, which <see cref="UnpredictIcc"/> turns back into the profile.
/// </summary>
/// <remarks>
/// libjxl reads the stream as the input arrives, saving a checkpoint every 512 bytes so it can resume.
/// This decoder holds the whole codestream, so running out of bytes at a checkpoint throws instead.
/// </remarks>
internal static class JxlIccReader
{
    // Two base-128 varints at up to 10 bytes each.
    public const int PreambleSize = 20;

    // ANSSymbolReader::kMaxCheckpointInterval.
    private const int MaxCheckpointInterval = 512;

    // Avoid too large memory allocation for invalid file.
    private const ulong MaxEncodedSize = 268435456;

    /// <summary>
    /// Reads the ICC profile. Throws <see cref="InvalidImageContentException"/> for invalid data.
    /// </summary>
    /// <param name="reader">The reader, after the transform data.</param>
    /// <param name="cancellationToken">The cancellation token, checked at each checkpoint.</param>
    /// <returns>The profile.</returns>
    public static byte[] Read(JxlBitReader reader, CancellationToken cancellationToken)
    {
        // ICCReader::Init.
        CheckEoi(reader);
        long usedBitsBase = reader.TotalBitsConsumed;
        ulong encSize = JxlFieldCoders.ReadU64(reader);
        if (encSize > MaxEncodedSize)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Too large encoded profile");
        }

        JxlAnsCode code = new();
        byte[] contextMap = JxlEntropyDecoder.DecodeHistograms(reader, NumIccContexts, code);
        JxlAnsSymbolReader ansReader = new(code, reader);
        int size = (int)encSize;

        // libjxl grows the buffer 0x400 bytes ahead of the symbols it reads, so a false size
        // allocates nothing up front.
        byte[] decompressed = new byte[Math.Min(0x400, size)];
        int i = 0;
        for (; i < Math.Min(2, size); i++)
        {
            decompressed[i] = (byte)ansReader.ReadHybridUint(
                IccAnsContext(i, i > 0 ? decompressed[i - 1] : 0, i > 1 ? decompressed[i - 2] : 0),
                reader,
                contextMap);
        }

        if (size > PreambleSize)
        {
            for (; i < PreambleSize; i++)
            {
                decompressed[i] = (byte)ansReader.ReadHybridUint(IccAnsContext(i, decompressed[i - 1], decompressed[i - 2]), reader, contextMap);
            }

            CheckEoi(reader);
            CheckPreamble(decompressed, decompressed.Length, encSize);
        }

        // ICCReader::Process.
        for (; i < size; i++)
        {
            if (i % MaxCheckpointInterval == 0 && i > 0)
            {
                // libjxl has no cancellation (#293). The token is checked where libjxl saves a checkpoint.
                cancellationToken.ThrowIfCancellationRequested();
                CheckEoi(reader);
                if ((i & 0xFFFF) == 0)
                {
                    float usedBytes = (reader.TotalBitsConsumed - usedBitsBase) / 8.0f;
                    if (i > usedBytes * 256)
                    {
                        JxlThrowHelper.ThrowInvalidImageContentException("Corrupted stream");
                    }
                }

                int length = Math.Min(i + 0x400, size);
                if (length > decompressed.Length)
                {
                    Array.Resize(ref decompressed, Math.Min(Math.Max(length, decompressed.Length * 2), size));
                }
            }

            decompressed[i] = (byte)ansReader.ReadHybridUint(IccAnsContext(i, decompressed[i - 1], decompressed[i - 2]), reader, contextMap);
        }

        CheckEoi(reader);
        if (!ansReader.CheckAnsFinalState)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Corrupted ICC profile");
        }

        List<byte> icc = new();
        UnpredictIcc(decompressed, size, icc);

        // JxlDecoderReadAllHeaders rejects an empty profile.
        if (icc.Count == 0)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("The embedded ICC profile is empty");
        }

        return icc.ToArray();
    }

    // Mimics the beginning of UnpredictICC for quick validity check. At least PreambleSize bytes of data
    // should be valid at invocation time; size is the size of the buffer so far.
    public static void CheckPreamble(byte[] data, int size, ulong encSize)
    {
        int pos = 0;
        ulong osize = DecodeVarInt(data, size, ref pos);
        CheckIs32Bit(osize);
        ulong csize = DecodeVarInt(data, size, ref pos);
        CheckIs32Bit(csize);
        CheckOutOfBounds((ulong)pos, csize, (ulong)size);

        // We expect that UnpredictICC inflates input, not the other way round.
        if (osize + 65536 < encSize)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Malformed ICC");
        }

        // libjxl allows 1 << 28 here ("64 MiB ICC should be enough for everything!?"). Departure (#112):
        // the limit is tightened to 1 << 22 bytes, the Level 5 limit on the ICC size (encode.cc), so a
        // hostile profile cannot make the decoder build 256 MiB. JxlIccWriter runs this check too, so
        // the encoder refuses a profile that this decoder would not read.
        const ulong outputLimit = 1 << 22;
        if (osize > outputLimit)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Decoded ICC is too large");
        }
    }

    // Decodes the result of PredictICC back to a valid ICC profile.
    private static void UnpredictIcc(byte[] enc, int size, List<byte> result)
    {
        int pos = 0;
        ulong osize = DecodeVarInt(enc, size, ref pos); // Output size
        CheckIs32Bit(osize);
        ulong csize = DecodeVarInt(enc, size, ref pos); // Commands size

        // Every command is translated to at least one byte.
        CheckIs32Bit(csize);
        int cpos = pos; // pos in commands stream
        CheckOutOfBounds((ulong)pos, csize, (ulong)size);
        int commandsEnd = cpos + (int)csize;
        pos = commandsEnd; // pos in data stream

        // Header
        byte[] header = IccInitialHeaderPrediction((uint)osize);
        for (int i = 0; i <= IccHeaderSize; i++)
        {
            if ((ulong)result.Count == osize)
            {
                if (cpos != commandsEnd)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Not all commands used");
                }

                if (pos != size)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Not all data used");
                }

                return; // Valid end
            }

            if (i == IccHeaderSize)
            {
                break; // Done
            }

            IccPredictHeader(result, result.Count, header, i);
            if (pos >= size)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Out of bounds");
            }

            result.Add((byte)(enc[pos++] + header[i]));
        }

        if (cpos >= commandsEnd)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Out of bounds");
        }

        // Tag list
        ulong numtags = DecodeVarInt(enc, commandsEnd, ref cpos);
        if (numtags != 0)
        {
            numtags--;
            CheckIs32Bit(numtags);
            AppendUint32((uint)numtags, result);
            ulong prevtagstart = IccHeaderSize + (numtags * 12);
            ulong prevtagsize = 0;
            while (true)
            {
                if ((ulong)result.Count > osize)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid result size");
                }

                if (cpos > commandsEnd)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Out of bounds");
                }

                if (cpos == commandsEnd)
                {
                    break; // Valid end
                }

                byte command = enc[cpos++];
                int tagcode = command & 63;
                uint tag;
                if (tagcode == 0)
                {
                    break;
                }
                else if (tagcode == CommandTagUnknown)
                {
                    CheckOutOfBounds((ulong)pos, 4, (ulong)size);
                    tag = DecodeKeyword(enc, size, pos);
                    pos += 4;
                }
                else if (tagcode == CommandTagTrc)
                {
                    tag = RtrcTag;
                }
                else if (tagcode == CommandTagXyz)
                {
                    tag = RxyzTag;
                }
                else
                {
                    if (tagcode - CommandTagStringFirst >= TagStrings.Length)
                    {
                        JxlThrowHelper.ThrowInvalidImageContentException("Unknown tagcode");
                    }

                    tag = TagStrings[tagcode - CommandTagStringFirst];
                }

                AppendKeyword(tag, result);

                ulong tagstart;
                ulong tagsize = prevtagsize;
                if (tag is RxyzTag or GxyzTag or BxyzTag or KxyzTag or WtptTag or BkptTag or LumiTag)
                {
                    tagsize = 20;
                }

                if ((command & FlagBitOffset) != 0)
                {
                    tagstart = DecodeVarInt(enc, commandsEnd, ref cpos);
                }
                else
                {
                    CheckIs32Bit(prevtagstart);
                    tagstart = prevtagstart + prevtagsize;
                }

                CheckIs32Bit(tagstart);
                AppendUint32((uint)tagstart, result);
                if ((command & FlagBitSize) != 0)
                {
                    tagsize = DecodeVarInt(enc, commandsEnd, ref cpos);
                }

                CheckIs32Bit(tagsize);
                AppendUint32((uint)tagsize, result);
                prevtagstart = tagstart;
                prevtagsize = tagsize;

                if (tagcode == CommandTagTrc)
                {
                    AppendKeyword(GtrcTag, result);
                    AppendUint32((uint)tagstart, result);
                    AppendUint32((uint)tagsize, result);
                    AppendKeyword(BtrcTag, result);
                    AppendUint32((uint)tagstart, result);
                    AppendUint32((uint)tagsize, result);
                }

                if (tagcode == CommandTagXyz)
                {
                    CheckIs32Bit(tagstart + (tagsize * 2));
                    AppendKeyword(GxyzTag, result);
                    AppendUint32((uint)(tagstart + tagsize), result);
                    AppendUint32((uint)tagsize, result);
                    AppendKeyword(BxyzTag, result);
                    AppendUint32((uint)(tagstart + (tagsize * 2)), result);
                    AppendUint32((uint)tagsize, result);
                }
            }
        }

        // Main Content
        while (true)
        {
            if ((ulong)result.Count > osize)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid result size");
            }

            if (cpos > commandsEnd)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Out of bounds");
            }

            if (cpos == commandsEnd)
            {
                break; // Valid end
            }

            byte command = enc[cpos++];
            if (command == CommandInsert)
            {
                ulong num = DecodeVarInt(enc, commandsEnd, ref cpos);
                CheckOutOfBounds((ulong)pos, num, (ulong)size);
                for (ulong i = 0; i < num; i++)
                {
                    result.Add(enc[pos++]);
                }
            }
            else if (command is CommandShuffle2 or CommandShuffle4)
            {
                ulong num = DecodeVarInt(enc, commandsEnd, ref cpos);
                CheckOutOfBounds((ulong)pos, num, (ulong)size);
                byte[] shuffled = enc.AsSpan(pos, (int)num).ToArray();
                Shuffle(shuffled, command == CommandShuffle2 ? 2 : 4);
                result.AddRange(shuffled);
                pos += (int)num;
            }
            else if (command == CommandPredict)
            {
                CheckOutOfBounds((ulong)cpos, 2, (ulong)commandsEnd);
                byte flags = enc[cpos++];

                int width = (flags & 3) + 1;
                if (width == 3)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid width");
                }

                int order = (flags & 12) >> 2;
                if (order == 3)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid order");
                }

                ulong stride = (ulong)width;
                if ((flags & 16) != 0)
                {
                    stride = DecodeVarInt(enc, commandsEnd, ref cpos);
                    if (stride < (ulong)width)
                    {
                        JxlThrowHelper.ThrowInvalidImageContentException("Invalid stride");
                    }
                }

                // If stride * 4 >= result->size(), return failure. The check
                // "size == 0 || ((size - 1) >> 2) < stride" corresponds to
                // "stride * 4 >= size", but does not suffer from integer overflow.
                // This check is more strict than necessary but follows the specification
                // and the encoder should ensure this is followed.
                if (result.Count == 0 || ((ulong)(result.Count - 1) >> 2) < stride)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid stride");
                }

                ulong num = DecodeVarInt(enc, commandsEnd, ref cpos); // in bytes
                CheckOutOfBounds((ulong)pos, num, (ulong)size);

                byte[] shuffled = enc.AsSpan(pos, (int)num).ToArray();
                if (width > 1)
                {
                    Shuffle(shuffled, width);
                }

                int start = result.Count;
                for (int i = 0; i < shuffled.Length; i++)
                {
                    byte predicted = LinearPredictIccValue(result, start, i, (int)stride, width, order);
                    result.Add((byte)(predicted + shuffled[i]));
                }

                pos += (int)num;
            }
            else if (command == CommandXyz)
            {
                AppendKeyword(XyzTypeTag, result);
                AppendUint32(0, result);
                CheckOutOfBounds((ulong)pos, 12, (ulong)size);
                for (int i = 0; i < 12; i++)
                {
                    result.Add(enc[pos++]);
                }
            }
            else if (command >= CommandTypeStartFirst && command < CommandTypeStartFirst + TypeStrings.Length)
            {
                AppendKeyword(TypeStrings[command - CommandTypeStartFirst], result);
                AppendUint32(0, result);
            }
            else
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Unknown command");
            }
        }

        if (pos != size)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Not all data used");
        }

        if ((ulong)result.Count != osize)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid result size");
        }
    }

    // Shuffles or interleaves bytes, for example with width 2, turns "ABCDabcd" into "AaBbCcDd".
    // Transposes a matrix of ceil(size / width) columns and width rows. There are size elements, size
    // may be < width * height, if so the last elements of the rightmost column are missing, the missing
    // spots are transposed along with the filled spots, and the result has the missing elements at the
    // end of the bottom row. The input is the input matrix in scanline order but with missing elements
    // skipped (which may occur in multiple locations), the output is the result matrix in scanline order
    // (with no need to skip missing elements as they are past the end of the data).
    private static void Shuffle(byte[] data, int width)
    {
        int size = data.Length;
        int height = (size + width - 1) / width; // amount of rows of output
        byte[] result = new byte[size];

        // i = output index, j input index
        int s = 0;
        int j = 0;
        for (int i = 0; i < size; i++)
        {
            result[i] = data[j];
            j += height;
            if (j >= size)
            {
                j = ++s;
            }
        }

        result.CopyTo(data, 0);
    }

    // Decodes a base-128 unsigned varint, advancing pos by the exact number of bytes consumed. Throws if
    // the input is truncated, if the terminator (top-bit-clear byte) is not seen within 10 bytes, or if
    // the 10th byte encodes a value that does not fit in a ulong.
    private static ulong DecodeVarInt(byte[] input, int inputSize, ref int pos)
    {
        ulong ret = 0;

        // 9 bytes cover bits 0..62; the 10th byte may only contribute bit 63.
        for (int i = 0; i < 9; ++i)
        {
            if (pos >= inputSize)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("DecodeVarInt: truncated input");
            }

            byte b = input[pos++];
            ret |= (ulong)(b & 0x7F) << (7 * i);
            if ((b & 0x80) == 0)
            {
                return ret;
            }
        }

        if (pos >= inputSize)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("DecodeVarInt: truncated input (10th byte)");
        }

        byte last = input[pos++];
        if ((last & 0x80) != 0)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("DecodeVarInt: varint exceeds 10 bytes");
        }

        if ((last & 0x7E) != 0)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("DecodeVarInt: value exceeds 2^64 - 1");
        }

        ret |= (ulong)(last & 0x01) << 63;
        return ret;
    }

    private static void CheckEoi(JxlBitReader reader)
    {
        if (!reader.AllReadsWithinBounds())
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Not enough bytes for reading ICC profile");
        }
    }
}
