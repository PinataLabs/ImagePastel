// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// ParseBoxHeader and the box stages of HandleBoxes in lib/jxl/decode.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers.Binary;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Finds the codestream in a JPEG XL container. This reads one <c>jxlc</c> box and skips the other
/// boxes; partial codestreams (<c>jxlp</c>), compressed boxes (<c>brob</c>) and 64-bit box sizes
/// come with a later slice.
/// </summary>
internal static class JxlContainer
{
    /// <summary>
    /// Returns the codestream in <paramref name="data"/>, which starts with the container signature.
    /// Throws <see cref="InvalidImageContentException"/> for an invalid container.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <returns>The contents of the <c>jxlc</c> box.</returns>
    public static byte[] ExtractCodestream(byte[] data)
    {
        int pos = 0;
        int boxCount = 0;
        while (pos < data.Length)
        {
            ReadOnlySpan<byte> type = ParseBoxHeader(data, pos, out long boxSize, out int headerSize);
            boxCount++;
            if (boxCount == 2 && !type.SequenceEqual("ftyp"u8))
            {
                JxlThrowHelper.ThrowInvalidImageContentException("the second box must be the ftyp box");
            }

            if (type.SequenceEqual("ftyp"u8) && boxCount != 2)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("the ftyp box must come second");
            }

            long contentsBegin = pos + headerSize;
            long contentsEnd = boxSize == 0 ? data.Length : pos + boxSize;
            if (contentsEnd > data.Length)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("The JPEG XL box is truncated");
            }

            ReadOnlySpan<byte> contents = data.AsSpan((int)contentsBegin, (int)(contentsEnd - contentsBegin));
            if (type.SequenceEqual("ftyp"u8))
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
            }
            else if (type.SequenceEqual("jxlc"u8))
            {
                return contents.ToArray();
            }
            else if (type.SequenceEqual("jxlp"u8))
            {
                JxlThrowHelper.ThrowNotSupportedException("Partial codestream boxes");
            }
            else if (type.SequenceEqual("brob"u8))
            {
                // libjxl skips these, but they can hold the Exif and XMP that a later slice reads.
                JxlThrowHelper.ThrowNotSupportedException("Brotli-compressed boxes");
            }

            pos = (int)contentsEnd;
        }

        JxlThrowHelper.ThrowInvalidImageContentException("The JPEG XL container has no codestream");
        return null;
    }

    // Reads the box header at pos: the type, the total box size including the header (0 for a box
    // that runs to the end of the file), and the header size.
    private static ReadOnlySpan<byte> ParseBoxHeader(byte[] data, int pos, out long boxSize, out int headerSize)
    {
        if (data.Length - pos < 8)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("The JPEG XL box header is truncated");
        }

        boxSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
        ReadOnlySpan<byte> type = data.AsSpan(pos + 4, 4);
        if (boxSize == 1)
        {
            JxlThrowHelper.ThrowNotSupportedException("64-bit box sizes");
        }

        headerSize = 8;
        if (boxSize > 0 && boxSize < headerSize)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("invalid box size");
        }

        return type;
    }
}
