// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// ParseBoxHeader, the box stages of HandleBoxes and JxlDecoderStruct::InjectNextBufferedJxlpBox in
// lib/jxl/decode.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Reads a JPEG XL container: joins the codestream from its <c>jxlc</c> or <c>jxlp</c> boxes.
/// </summary>
/// <remarks>
/// libjxl walks the boxes as the input arrives and stops once the codestream is decoded. This
/// decoder holds the whole file, so it walks every box first.
/// </remarks>
internal static class JxlContainer
{
    // JxlDecoderStruct::kNumBuffersLimit: the most out-of-order jxlp boxes held at once.
    private const int NumBuffersLimit = 1 << 20;

    /// <summary>
    /// Returns the codestream in <paramref name="data"/>, which starts with the container signature.
    /// Throws <see cref="InvalidImageContentException"/> for an invalid container.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <returns>The codestream.</returns>
    public static byte[] ExtractCodestream(byte[] data)
    {
        List<ReadOnlyMemory<byte>> codestream = new();
        Dictionary<uint, (ReadOnlyMemory<byte> Part, bool IsLast)> outOfOrder = new();
        bool lastCodestreamSeen = false;
        uint nextJxlpIndex = 0;
        uint fileFormatVersion = 0;
        bool haveCodestream = false;

        int pos = 0;
        int boxCount = 0;
        while (pos < data.Length)
        {
            // Another codestream, bare or boxed, can follow the last codestream box.
            ReadOnlySpan<byte> rest = data.AsSpan(pos);
            if (lastCodestreamSeen && rest.Length >= 2 && rest[0] == JxlConstants.CodestreamSignature[0] && rest[1] == JxlConstants.CodestreamSignature[1])
            {
                break;
            }

            ReadOnlySpan<byte> type = ParseBoxHeader(data, pos, out long boxSize, out int headerSize);
            boxCount++;
            if (lastCodestreamSeen && type.SequenceEqual("JXL "u8))
            {
                break;
            }

            if (boxCount == 2 && !type.SequenceEqual("ftyp"u8))
            {
                JxlThrowHelper.ThrowInvalidImageContentException("the second box must be the ftyp box");
            }

            if (type.SequenceEqual("ftyp"u8) && boxCount != 2)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("the ftyp box must come second");
            }

            int contentsBegin = pos + headerSize;
            int contentsEnd = boxSize == 0 ? data.Length : (int)(pos + boxSize);
            ReadOnlyMemory<byte> contents = data.AsMemory(contentsBegin, contentsEnd - contentsBegin);
            if (type.SequenceEqual("ftyp"u8))
            {
                fileFormatVersion = ReadFtyp(contents.Span);
            }
            else if (type.SequenceEqual("jxlc"u8))
            {
                if (lastCodestreamSeen)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("there can only be one jxlc box");
                }

                lastCodestreamSeen = true;
                haveCodestream = true;
                codestream.Add(contents);
            }
            else if (type.SequenceEqual("jxlp"u8))
            {
                if (lastCodestreamSeen)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("cannot have jxlp box after last jxlp box");
                }

                if (contents.Length < 4)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("jxlp box too small to contain index");
                }

                uint jxlpIndex = BinaryPrimitives.ReadUInt32BigEndian(contents.Span);
                uint counter = jxlpIndex & 0x7FFFFFFF;
                bool isLast = (jxlpIndex & 0x80000000) != 0;
                if (counter < nextJxlpIndex)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException($"jxlp box index {counter} is a duplicate (already processed)");
                }

                ReadOnlyMemory<byte> part = contents.Slice(4);
                haveCodestream = true;
                if (counter == nextJxlpIndex)
                {
                    nextJxlpIndex++;
                    lastCodestreamSeen |= isLast;
                    codestream.Add(part);

                    // InjectNextBufferedJxlpBox: the buffered boxes that now follow on.
                    while (outOfOrder.Remove(nextJxlpIndex, out (ReadOnlyMemory<byte> Part, bool IsLast) next))
                    {
                        codestream.Add(next.Part);
                        lastCodestreamSeen |= next.IsLast;
                        nextJxlpIndex++;
                    }
                }
                else if (fileFormatVersion >= 1)
                {
                    // Out-of-order box (version 1+): buffer payload for later injection. libjxl also
                    // caps the bytes buffered; a byte array this decoder holds stays under that cap.
                    if (outOfOrder.Count >= NumBuffersLimit)
                    {
                        JxlThrowHelper.ThrowInvalidImageContentException("Too many out-of-order jxlp boxes");
                    }

                    if (!outOfOrder.TryAdd(counter, (part, isLast)))
                    {
                        JxlThrowHelper.ThrowInvalidImageContentException($"duplicate jxlp box index {counter}");
                    }
                }
                else
                {
                    JxlThrowHelper.ThrowInvalidImageContentException($"jxlp box index {counter} is out of order (expected {nextJxlpIndex}); out-of-order jxlp boxes require file format version 1 in the ftyp box");
                }
            }

            // libjxl skips every other box, brob included: a brob box never holds the codestream.
            pos = contentsEnd;
        }

        if (!haveCodestream)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("The JPEG XL container has no codestream");
        }

        // jxlp boxes still buffered leave a gap, which the codestream reader finds as truncation.
        return Join(codestream);
    }

    // The file type box: the major brand "jxl " and the file format version.
    private static uint ReadFtyp(ReadOnlySpan<byte> contents)
    {
        if (contents.Length < 12)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("file type box too small");
        }

        if (!contents.StartsWith("jxl "u8))
        {
            JxlThrowHelper.ThrowInvalidImageContentException("file type box major brand must be \"jxl \"");
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(contents.Slice(4));
        if (version > 1)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"unknown jxl file format version {version} (known versions: 0, 1)");
        }

        return version;
    }

    private static byte[] Join(List<ReadOnlyMemory<byte>> parts)
    {
        int length = 0;
        foreach (ReadOnlyMemory<byte> part in parts)
        {
            length += part.Length;
        }

        byte[] joined = new byte[length];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> part in parts)
        {
            part.Span.CopyTo(joined.AsSpan(offset));
            offset += part.Length;
        }

        return joined;
    }

    // Reads the box header at pos: the type, the total box size including the header (0 for a box
    // that runs to the end of the file), and the header size.
    private static ReadOnlySpan<byte> ParseBoxHeader(byte[] data, int pos, out long boxSize, out int headerSize)
    {
        if (data.Length - pos < 8)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("The JPEG XL box header is truncated");
        }

        ulong size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
        ReadOnlySpan<byte> type = data.AsSpan(pos + 4, 4);
        headerSize = 8;
        if (size == 1)
        {
            headerSize = 16;
            if (data.Length - pos < 16)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("The JPEG XL box header is truncated");
            }

            size = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(pos + 8));
        }

        if (size > 0 && size < (ulong)headerSize)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("invalid box size");
        }

        // libjxl's "Box size overflow" checks guard size_t; a box past the end of this in-memory
        // file is truncated.
        if (size > (ulong)(data.Length - pos))
        {
            JxlThrowHelper.ThrowInvalidImageContentException("The JPEG XL box is truncated");
        }

        boxSize = (long)size;
        return type;
    }
}
