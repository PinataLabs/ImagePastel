// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/encode_internal.h (MakeContainerHeader, JxlEncoderStruct::MustUseContainer) and
// lib/jxl/encode.cc (WriteBoxHeader, WriteJxlpBoxCounter, and the container boxes of
// JxlEncoder::ProcessOneEnqueuedInput).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers.Binary;
using System.IO;

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>
/// Writes an encoded codestream to a stream: bare, or in a JPEG XL container with a level box and
/// metadata boxes, in the layout libjxl gives when the boxes are added before the frame (as cjxl adds them).
/// </summary>
/// <remarks>
/// libjxl streams its output and patches each box header once the box is written. This encoder holds the
/// whole codestream, so each header is written with its size first.
/// </remarks>
internal static class JxlContainerWriter
{
    /// <summary>
    /// The size of the TIFF-header offset that the <c>Exif</c> box holds in front of the Exif data
    /// (lib/extras/enc/jxl.cc).
    /// </summary>
    public const int ExifOffsetSize = 4;

    // kSmallBoxHeaderSize: a 4-byte size, then the type.
    private const int BoxHeaderSize = 8;

    // The jxlp box index (WriteJxlpBoxCounter).
    private const int JxlpIndexSize = 4;

    // ftyp_version 0: the standard delivery order (output mode 2, which needs version 1, is not ported).
    private const byte FtypVersion = 0;

    /// <summary>
    /// Writes <paramref name="codestream"/> to <paramref name="stream"/>: bare when there are no boxes and
    /// the level is 5, otherwise in a container, with a <c>jxll</c> box when the level is 10 and the
    /// boxes between the header and the frame.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="codestream">The whole codestream.</param>
    /// <param name="headerSize">
    /// The size of the codestream headers and the ICC profile, which end on a byte boundary before the frame.
    /// </param>
    /// <param name="boxes">The metadata boxes.</param>
    /// <param name="level">
    /// The codestream level, <see cref="JxlCodestreamLevel.Level5"/> or <see cref="JxlCodestreamLevel.Level10"/>.
    /// </param>
    public static void Write(Stream stream, ReadOnlySpan<byte> codestream, int headerSize, JxlMetadataBoxes boxes, int level)
    {
        // MustUseContainer: use_boxes, or a codestream level other than 5. cjxl uses boxes only when there
        // is metadata to store.
        if (boxes.IsEmpty && level == JxlCodestreamLevel.Level5)
        {
            stream.Write(codestream);
            return;
        }

        WriteContainerHeader(stream);

        // The jxll box, right after ftyp, gives the codestream level.
        if (level != JxlCodestreamLevel.Level5)
        {
            WriteBoxHeader(stream, "jxll"u8, 1);
            stream.WriteByte((byte)level);
        }

        // The frame is the last, so with no boxes the whole codestream goes in one jxlc box.
        if (boxes.IsEmpty)
        {
            WriteBoxHeader(stream, "jxlc"u8, codestream.Length);
            stream.Write(codestream);
            return;
        }

        // partial_header: the boxes are queued before the frame, so the basic info and the colour profile go
        // in an early jxlp box, ahead of the metadata.
        uint jxlpCounter = 0;
        WriteJxlpBox(stream, jxlpCounter++, false, codestream.Slice(0, headerSize));

        // cjxl's order: Exif, then xml. Departure from cjxl, whose --compress_boxes defaults to 1: the boxes
        // are never Brotli-compressed into brob boxes. netstandard2.1 has no BrotliEncoder, and this
        // library's netstandard2.1 decoder drops brob boxes (#317). libjxl leaves compression to the caller.
        if (boxes.Exif is not null)
        {
            WriteBoxHeader(stream, "Exif"u8, ExifOffsetSize + boxes.Exif.Length);
            Span<byte> offset = stackalloc byte[ExifOffsetSize];
            offset.Clear();
            stream.Write(offset);
            stream.Write(boxes.Exif);
        }

        if (boxes.Xmp is not null)
        {
            WriteBoxHeader(stream, "xml "u8, boxes.Xmp.Length);
            stream.Write(boxes.Xmp);
        }

        // The frame: a jxlc box only when it is the last frame and no jxlp box was written yet, so here the
        // last jxlp box.
        WriteJxlpBox(stream, jxlpCounter, true, codestream.Slice(headerSize));
    }

    // MakeContainerHeader: the JXL signature box, then ftyp with major brand "jxl ", the minor version,
    // and compatible brand "jxl ".
    private static void WriteContainerHeader(Stream stream)
    {
        stream.Write(JxlConstants.ContainerSignature);
        WriteBoxHeader(stream, "ftyp"u8, 12);
        stream.Write("jxl "u8);
        stream.Write(stackalloc byte[] { 0, 0, 0, FtypVersion });
        stream.Write("jxl "u8);
    }

    // A jxlp box: the index, with bit 31 set on the last (WriteJxlpBoxCounter), then the part.
    private static void WriteJxlpBox(Stream stream, uint counter, bool last, ReadOnlySpan<byte> part)
    {
        WriteBoxHeader(stream, "jxlp"u8, JxlpIndexSize + part.Length);
        if (last)
        {
            counter |= 0x80000000;
        }

        Span<byte> index = stackalloc byte[JxlpIndexSize];
        BinaryPrimitives.WriteUInt32BigEndian(index, counter);
        stream.Write(index);
        stream.Write(part);
    }

    // WriteBoxHeader with the 8-byte form: the size of the whole box, big-endian, then the type. libjxl uses
    // the 16-byte form for contents of 2^32 - 8 bytes or more; contents here are a span or an array, which
    // are shorter than that.
    private static void WriteBoxHeader(Stream stream, ReadOnlySpan<byte> type, int contentsSize)
    {
        Span<byte> header = stackalloc byte[BoxHeaderSize];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)(BoxHeaderSize + (long)contentsSize));
        type.CopyTo(header.Slice(4));
        stream.Write(header);
    }
}
