// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/extras/enc/jxl.cc (the metadata boxes of EncodeImageJXL) and lib/jxl/base/exif.h (IsExif).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers.Binary;
using SixLabors.ImageSharp.Metadata;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// The Exif and XMP data the encoder stores in container boxes, taken from an image's metadata as cjxl
/// takes them from the file it reads. IPTC has no JPEG XL box (cjxl's <c>xml </c> box for IPTC holds
/// IPTC as XMP, which ImageSharp does not convert), and ImageSharp has no profile for JUMBF or gain maps,
/// so neither is written.
/// </summary>
internal sealed class JxlMetadataBoxes
{
    private JxlMetadataBoxes(byte[] exif, byte[] xmp)
    {
        this.Exif = exif;
        this.Xmp = xmp;
    }

    /// <summary>
    /// Gets the Exif data from the TIFF header on, or null when no <c>Exif</c> box is written. The box adds
    /// the 4-byte TIFF-header offset in front.
    /// </summary>
    public byte[] Exif { get; }

    /// <summary>
    /// Gets the XMP data, or null when no <c>xml </c> box is written.
    /// </summary>
    public byte[] Xmp { get; }

    /// <summary>
    /// Gets a value indicating whether there is no box to write. libjxl uses a container when boxes are
    /// used (<c>MustUseContainer</c>), so the encoder writes a bare codestream then.
    /// </summary>
    public bool IsEmpty => this.Exif is null && this.Xmp is null;

    /// <summary>
    /// Takes the Exif and XMP data of <paramref name="metadata"/>, after the profiles are synchronised with
    /// the metadata, as every ImageSharp encoder does. Throws <see cref="ImageFormatException"/> for a box
    /// that this library's decoder would refuse.
    /// </summary>
    /// <param name="metadata">The image metadata.</param>
    /// <returns>The boxes.</returns>
    public static JxlMetadataBoxes Create(ImageMetadata metadata)
    {
        metadata.SyncProfiles();

        // cjxl prepends the 4-byte offset only when the blob looks like Exif, and writes no Exif box
        // otherwise. Departure: cjxl still uses a container for such a blob (its use_boxes tests that the
        // blob is not empty), with the codestream in a jxlc box; this encoder writes a box or nothing, so
        // a blob that is not Exif gives a bare codestream. After SyncProfiles the profile is parsed, so its
        // bytes come from ExifWriter, which starts them with a TIFF header; a profile parsed from bytes that
        // are not Exif has no values and gives no bytes.
        byte[] exif = metadata.ExifProfile?.ToByteArray();
        if (exif is not null && !IsExif(exif))
        {
            exif = null;
        }

        byte[] xmp = metadata.XmpProfile?.Data;
        if (xmp is { Length: 0 })
        {
            xmp = null;
        }

        // Never more than the decoder reads (JxlContainer.BlockSizeLimit), as the ICC writer never writes
        // a profile over 2^22 bytes: what this encoder writes, this library's decoder reads.
        if (exif is not null)
        {
            CheckBoxSize(4L + exif.Length, "Exif");
        }

        if (xmp is not null)
        {
            CheckBoxSize(xmp.Length, "XMP");
        }

        return new JxlMetadataBoxes(exif, xmp);
    }

    // IsExif in lib/jxl/base/exif.h: at least 12 bytes, starting with a little- or big-endian TIFF header.
    private static bool IsExif(ReadOnlySpan<byte> exif)
    {
        // not enough bytes for a valid exif blob
        if (exif.Length < 12)
        {
            return false;
        }

        uint header = BinaryPrimitives.ReadUInt32LittleEndian(exif);
        return header is 0x2A004D4D or 0x002A4949;
    }

    private static void CheckBoxSize(long size, string kind)
    {
        if (size > JxlContainer.BlockSizeLimit)
        {
            JxlThrowHelper.ThrowImageFormatException($"The {kind} box would be {size} bytes, over the {JxlContainer.BlockSizeLimit}-byte limit of the JPEG XL decoder.");
        }
    }
}
