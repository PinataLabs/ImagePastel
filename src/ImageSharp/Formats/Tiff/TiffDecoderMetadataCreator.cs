// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using SixLabors.ImageSharp.Common.Helpers;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Tiff;

/// <summary>
/// The decoder metadata creator.
/// </summary>
internal static class TiffDecoderMetadataCreator
{
    public static ImageMetadata Create<TPixel>(List<ImageFrame<TPixel>> frames, bool ignoreMetadata, ByteOrder byteOrder, bool isBigTiff)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (frames.Count < 1)
        {
            TiffThrowHelper.ThrowImageFormatException("Expected at least one frame.");
        }

        ImageMetadata imageMetaData = Create(byteOrder, isBigTiff, frames[0].Metadata.ExifProfile);

        if (!ignoreMetadata)
        {
            for (int i = 0; i < frames.Count; i++)
            {
                SetFrameProfiles(frames[i].Metadata);
            }

            SetImageProfiles(imageMetaData, frames[0].Metadata);
        }

        return imageMetaData;
    }

    /// <summary>
    /// Reads the IPTC, XMP and ICC profiles of a frame from the frame's EXIF profile. The embedded ICC
    /// profile goes to <see cref="TiffFrameMetadata.EmbeddedIccProfile"/>, and is also the frame's
    /// pixel profile unless it is a CMYK profile: the decoded pixels are RGB, so a CMYK profile does
    /// not describe them (it is then removed from the frame's EXIF profile too).
    /// </summary>
    /// <param name="frameMetaData">The frame metadata. Its EXIF profile must be set.</param>
    public static void SetFrameProfiles(ImageFrameMetadata frameMetaData)
    {
        if (TryGetIptc(frameMetaData.ExifProfile.Values, out byte[] iptcBytes))
        {
            frameMetaData.IptcProfile = new IptcProfile(iptcBytes);
        }

        IExifValue<byte[]> xmpProfileBytes = frameMetaData.ExifProfile.GetValue(ExifTag.XMP);
        if (xmpProfileBytes != null)
        {
            frameMetaData.XmpProfile = new XmpProfile(xmpProfileBytes.Value);
        }

        IExifValue<byte[]> iccProfileBytes = frameMetaData.ExifProfile.GetValue(ExifTag.IccProfile);
        if (iccProfileBytes != null)
        {
            var profile = new IccProfile(iccProfileBytes.Value);
            frameMetaData.GetTiffMetadata().EmbeddedIccProfile = profile;
            if (IsCmykProfile(iccProfileBytes.Value, profile))
            {
                frameMetaData.ExifProfile.RemoveValue(ExifTag.IccProfile);
            }
            else
            {
                frameMetaData.IccProfile = profile;
            }
        }
    }

    /// <summary>
    /// Exposes the ICC and IPTC profiles of the first frame at image level.
    /// </summary>
    /// <param name="imageMetaData">The image metadata.</param>
    /// <param name="rootFrameMetaData">The metadata of the first frame.</param>
    public static void SetImageProfiles(ImageMetadata imageMetaData, ImageFrameMetadata rootFrameMetaData)
    {
        // The profiles of the first frame describe the image, so they are also exposed at image level,
        // where other encoders look for them. The instances are shared, so an edit through either one is kept.
        imageMetaData.IccProfile = rootFrameMetaData.IccProfile;
        imageMetaData.IptcProfile = rootFrameMetaData.IptcProfile;
        imageMetaData.GetTiffMetadata().EmbeddedIccProfile = rootFrameMetaData.GetTiffMetadata().EmbeddedIccProfile;
    }

    public static ImageMetadata Create(ByteOrder byteOrder, bool isBigTiff, ExifProfile exifProfile)
    {
        var imageMetaData = new ImageMetadata();
        SetResolution(imageMetaData, exifProfile);

        TiffMetadata tiffMetadata = imageMetaData.GetTiffMetadata();
        tiffMetadata.ByteOrder = byteOrder;
        tiffMetadata.FormatType = isBigTiff ? TiffFormatType.BigTIFF : TiffFormatType.Default;

        return imageMetaData;
    }

    private static void SetResolution(ImageMetadata imageMetaData, ExifProfile exifProfile)
    {
        imageMetaData.ResolutionUnits = exifProfile != null ? UnitConverter.ExifProfileToResolutionUnit(exifProfile) : PixelResolutionUnit.PixelsPerInch;
        double? horizontalResolution = exifProfile?.GetValue(ExifTag.XResolution)?.Value.ToDouble();
        if (horizontalResolution != null)
        {
            imageMetaData.HorizontalResolution = horizontalResolution.Value;
        }

        double? verticalResolution = exifProfile?.GetValue(ExifTag.YResolution)?.Value.ToDouble();
        if (verticalResolution != null)
        {
            imageMetaData.VerticalResolution = verticalResolution.Value;
        }
    }

    // Profiles are kept without validation, so a header too short to read is not a CMYK profile.
    private static bool IsCmykProfile(byte[] bytes, IccProfile profile)
        => bytes.Length >= 128 && profile.DataColorSpace == IccColorSpaceType.Cmyk;

    private static bool TryGetIptc(IReadOnlyList<IExifValue> exifValues, out byte[] iptcBytes)
    {
        iptcBytes = null;
        IExifValue iptc = exifValues.FirstOrDefault(f => f.Tag == ExifTag.IPTC);

        if (iptc != null)
        {
            // A single value is read as a scalar rather than an array. It is too short to hold an IPTC record.
            if ((iptc.DataType == ExifDataType.Byte || iptc.DataType == ExifDataType.Undefined) && iptc.GetValue() is byte[] bytes)
            {
                iptcBytes = bytes;
                return true;
            }

            // Some Encoders write the data type of IPTC as long.
            if (iptc.DataType == ExifDataType.Long && iptc.GetValue() is uint[] iptcValues)
            {
                iptcBytes = new byte[iptcValues.Length * 4];
                Buffer.BlockCopy(iptcValues, 0, iptcBytes, 0, iptcValues.Length * 4);
                if (iptcBytes[0] == 0x1c)
                {
                    return true;
                }
                else if (iptcBytes[3] != 0x1c)
                {
                    return false;
                }

                // Probably wrong endianess, swap byte order.
                Span<byte> iptcBytesSpan = iptcBytes.AsSpan();
                Span<byte> buffer = stackalloc byte[4];
                for (int i = 0; i < iptcBytes.Length; i += 4)
                {
                    iptcBytesSpan.Slice(i, 4).CopyTo(buffer);
                    iptcBytes[i] = buffer[3];
                    iptcBytes[i + 1] = buffer[2];
                    iptcBytes[i + 2] = buffer[1];
                    iptcBytes[i + 3] = buffer[0];
                }

                return true;
            }
        }

        return false;
    }
}
