// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using PinataLabs.ImagePastel.Common.Helpers;
using PinataLabs.ImagePastel.Formats.Tiff.Constants;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.Metadata.Profiles.Exif;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.Metadata.Profiles.Iptc;
using PinataLabs.ImagePastel.Metadata.Profiles.Xmp;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats.Tiff;

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
                SetFrameProfiles(frames[i].Metadata, frames[i].Metadata.GetTiffMetadata().PhotometricInterpretation);
            }

            SetImageProfiles(imageMetaData, frames[0].Metadata);
        }

        return imageMetaData;
    }

    /// <summary>
    /// Reads the IPTC, XMP and ICC profiles of a frame from the frame's EXIF profile. The embedded ICC
    /// profile goes to <see cref="TiffFrameMetadata.EmbeddedIccProfile"/>, and is also the frame's
    /// pixel profile when it describes the decoded pixels: a gray profile for a grayscale frame, an
    /// RGB profile for a color one. Separated (CMYK) frames decode to RGB through a formula, so no
    /// embedded profile describes them. A profile that is not the pixel profile is also removed from
    /// the frame's EXIF profile, so an encoder does not pair it with the pixels.
    /// </summary>
    /// <param name="frameMetaData">The frame metadata. Its EXIF profile must be set.</param>
    /// <param name="photometricInterpretation">The frame's photometric interpretation.</param>
    public static void SetFrameProfiles(ImageFrameMetadata frameMetaData, TiffPhotometricInterpretation? photometricInterpretation)
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
            if (CanBePixelProfile(iccProfileBytes.Value, profile, photometricInterpretation))
            {
                frameMetaData.IccProfile = profile;
            }
            else
            {
                frameMetaData.ExifProfile.RemoveValue(ExifTag.IccProfile);
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

    // Profiles are kept without validation, so a header too short to read stays the pixel profile.
    private static bool CanBePixelProfile(byte[] bytes, IccProfile profile, TiffPhotometricInterpretation? photometricInterpretation)
        => photometricInterpretation switch
        {
            TiffPhotometricInterpretation.Separated => false,
            _ when bytes.Length < 128 => true,
            TiffPhotometricInterpretation.BlackIsZero or TiffPhotometricInterpretation.WhiteIsZero => DecoderColorProfileConversion.CanBePixelProfile(profile, gray: true),
            _ => DecoderColorProfileConversion.CanBePixelProfile(profile, gray: false),
        };

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
