// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// ParseBoxHeader, the box stages of HandleBoxes and JxlDecoderStruct::InjectNextBufferedJxlpBox in
// lib/jxl/decode.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;

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

    // kBlockSizeLimit in HandleBoxes: the largest Exif or XMP box libjxl decompresses for JPEG
    // reconstruction. Departure (#112): this decoder caps every Exif and XMP box it reads at this size,
    // plain or Brotli-compressed, and throws for a larger one. libjxl applies the limit only to the
    // boxes it decodes for JPEG reconstruction ("EXIF/XMP box is too large"), and hands other boxes to
    // the caller at any size.
    internal const int BlockSizeLimit = 64 << 20;

    // The buffer that measures a Brotli-compressed box before it is decompressed into its own array.
    private const int MeasureBufferSize = 64 << 10;

    /// <summary>
    /// Reads the codestream in <paramref name="data"/>, which starts with the container signature, and
    /// the Exif and XMP boxes when <paramref name="readMetadata"/> is set. Throws
    /// <see cref="InvalidImageContentException"/> for an invalid container.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <param name="readMetadata">Whether to read the Exif and XMP boxes.</param>
    /// <param name="cancellationToken">
    /// The cancellation token, checked per box and per buffer of a Brotli-compressed box. libjxl has no
    /// cancellation (#293).
    /// </param>
    /// <returns>The contents.</returns>
    public static JxlContainerContents Read(byte[] data, bool readMetadata, CancellationToken cancellationToken)
    {
        // djxl keeps the last box of each kind: each Exif or xml box overwrites the one before.
        ReadOnlyMemory<byte>? exifBox = null;
        ReadOnlyMemory<byte>? xmlBox = null;
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
            cancellationToken.ThrowIfCancellationRequested();

            // Another codestream, bare or boxed, can follow the last codestream box.
            ReadOnlySpan<byte> rest = data.AsSpan(pos);
            if (lastCodestreamSeen && rest.Length >= 2 && rest[0] == JxlConstants.CodestreamSignature[0] && rest[1] == JxlConstants.CodestreamSignature[1])
            {
                break;
            }

            string error = ParseBoxHeader(data, pos, out ReadOnlySpan<byte> type, out long boxSize, out int headerSize);
            if (error != null)
            {
                // libjxl stops once the codestream is done, so it never reads what follows the last
                // codestream box. Broken bytes there only end the walk.
                if (lastCodestreamSeen)
                {
                    break;
                }

                JxlThrowHelper.ThrowInvalidImageContentException(error);
            }

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
            else if (readMetadata && type.SequenceEqual("Exif"u8))
            {
                CheckMetadataBoxSize(contents.Length, "Exif");
                exifBox = contents;
            }
            else if (readMetadata && type.SequenceEqual("xml "u8))
            {
                CheckMetadataBoxSize(contents.Length, "XMP");
                xmlBox = contents;
            }
            else if (readMetadata && type.SequenceEqual("brob"u8) && contents.Length >= 4)
            {
                // A brob box starts with the type of the box it compresses. It never holds the
                // codestream: libjxl skips it when it looks for one.
                ReadOnlySpan<byte> decodedType = contents.Span.Slice(0, 4);
                if (decodedType.SequenceEqual("Exif"u8))
                {
                    exifBox = Decompress(contents.Slice(4), "Exif", cancellationToken) ?? exifBox;
                }
                else if (decodedType.SequenceEqual("xml "u8))
                {
                    xmlBox = Decompress(contents.Slice(4), "XMP", cancellationToken) ?? xmlBox;
                }
            }

            pos = contentsEnd;
        }

        if (!haveCodestream)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("The JPEG XL container has no codestream");
        }

        // jxlp boxes still buffered leave a gap, which the codestream reader finds as truncation.
        return new JxlContainerContents
        {
            Codestream = Join(codestream),
            Exif = exifBox == null ? null : ExifFromBox(exifBox.Value.Span),
            Xmp = xmlBox?.ToArray(),
        };
    }

#if NETCOREAPP3_0_OR_GREATER
    /// <summary>
    /// Returns the decompressed size of a Brotli stream that has to end with the box, or -1 for a stream
    /// that cannot be read. Throws <see cref="InvalidImageContentException"/> as soon as the size passes
    /// the cap. Internal for the cancellation tests.
    /// </summary>
    /// <param name="input">The compressed contents of a <c>brob</c> box, after its type.</param>
    /// <param name="kind">"Exif" or "XMP", for the message.</param>
    /// <param name="cancellationToken">
    /// The cancellation token, checked per buffer: a box at the cap takes 1,024 of them. libjxl has no
    /// cancellation (#293).
    /// </param>
    /// <returns>The decompressed size, or -1.</returns>
    internal static long MeasureDecompressedSize(ReadOnlySpan<byte> input, string kind, CancellationToken cancellationToken)
    {
        System.IO.Compression.BrotliDecoder decoder = default;
        try
        {
            byte[] buffer = new byte[MeasureBufferSize];
            long size = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                System.Buffers.OperationStatus status = decoder.Decompress(input, buffer, out int consumed, out int produced);
                input = input.Slice(consumed);
                size += produced;

                // "EXIF/XMP box is too large"
                CheckMetadataBoxSize(size, kind);
                switch (status)
                {
                    case System.Buffers.OperationStatus.Done:
                        // The Brotli stream has to end with the box.
                        return input.IsEmpty ? size : -1;
                    case System.Buffers.OperationStatus.DestinationTooSmall:
                        break;
                    default:
                        // Truncated (NeedMoreData) or invalid.
                        return -1;
                }
            }
        }
        finally
        {
            decoder.Dispose();
        }
    }
#endif

    // The Exif box starts with the offset of the TIFF header. djxl drops the bytes before it, and
    // warns and keeps the box as it is when there is no TIFF header there (lib/extras/dec/jxl.cc);
    // ImageSharp has no profile for bytes that are not Exif, so this gives none.
    private static byte[] ExifFromBox(ReadOnlySpan<byte> box)
    {
        // 16 = 4 + 12 = offset + min EXIF payload.
        if (box.Length < 16)
        {
            return null;
        }

        uint offset = BinaryPrimitives.ReadUInt32BigEndian(box);
        if (offset > box.Length - 16)
        {
            return null;
        }

        // IsExif in lib/jxl/base/exif.h: a little- or big-endian TIFF header.
        ReadOnlySpan<byte> exif = box.Slice(4 + (int)offset);
        uint header = BinaryPrimitives.ReadUInt32LittleEndian(exif);
        return header is 0x2A004D4D or 0x002A4949 ? exif.ToArray() : null;
    }

    // Throws for an Exif or XMP box whose contents, decompressed if they were compressed, pass the cap.
    private static void CheckMetadataBoxSize(long size, string kind)
    {
        if (size > BlockSizeLimit)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"{kind} box is too large: {size} bytes, over the limit of {BlockSizeLimit}");
        }
    }

    // Decompresses the contents of a brob box after its type, as JxlBoxContentDecoder does. Returns
    // null where there is no Brotli decoder, and for a stream that cannot be read: libjxl fails the
    // decode there, but ImageSharp drops unreadable metadata, as it has to on netstandard2.1 anyway.
    // Throws for a box that decompresses to more than the cap.
    private static byte[] Decompress(ReadOnlyMemory<byte> compressed, string kind, CancellationToken cancellationToken)
    {
#if NETCOREAPP3_0_OR_GREATER
        // The stream is decompressed twice: once into a small buffer to measure it, stopping as soon as
        // it passes the cap, then into an array of its size. libjxl grows its buffer as it goes instead;
        // measuring first means a box over the cap is never held, and a box under it allocates its size.
        long size = MeasureDecompressedSize(compressed.Span, kind, cancellationToken);
        if (size < 0)
        {
            return null;
        }

        // The second pass is one call, so the token is checked once before it.
        cancellationToken.ThrowIfCancellationRequested();

        // Not a using declaration: that would make the struct read-only, and each call would mutate a copy.
        System.IO.Compression.BrotliDecoder decoder = default;
        try
        {
            byte[] output = new byte[size];
            System.Buffers.OperationStatus status = decoder.Decompress(compressed.Span, output, out int consumed, out int written);
            return status == System.Buffers.OperationStatus.Done && consumed == compressed.Length && written == size ? output : null;
        }
        finally
        {
            decoder.Dispose();
        }
#else
        // netstandard2.1 has no BrotliDecoder, so a compressed Exif or XMP box is dropped and the image
        // still decodes (issue #165).
        _ = compressed;
        _ = kind;
        _ = cancellationToken;
        return null;
#endif
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
    // that runs to the end of the file), and the header size. Returns the error for an invalid header,
    // or null.
    private static string ParseBoxHeader(byte[] data, int pos, out ReadOnlySpan<byte> type, out long boxSize, out int headerSize)
    {
        type = default;
        boxSize = 0;
        headerSize = 8;
        if (data.Length - pos < 8)
        {
            return "The JPEG XL box header is truncated";
        }

        ulong size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
        type = data.AsSpan(pos + 4, 4);
        if (size == 1)
        {
            headerSize = 16;
            if (data.Length - pos < 16)
            {
                return "The JPEG XL box header is truncated";
            }

            size = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(pos + 8));
        }

        if (size > 0 && size < (ulong)headerSize)
        {
            return "invalid box size";
        }

        // libjxl's "Box size overflow" checks guard size_t; a box past the end of this in-memory
        // file is truncated.
        if (size > (ulong)(data.Length - pos))
        {
            return "The JPEG XL box is truncated";
        }

        boxSize = (long)size;
        return null;
    }
}
