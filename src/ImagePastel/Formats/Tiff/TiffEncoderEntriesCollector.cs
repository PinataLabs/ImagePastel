// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using PinataLabs.ImagePastel.Common.Helpers;
using PinataLabs.ImagePastel.Formats.Tiff.Constants;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.Metadata.Profiles.Exif;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.Metadata.Profiles.Iptc;
using PinataLabs.ImagePastel.Metadata.Profiles.Xmp;

namespace PinataLabs.ImagePastel.Formats.Tiff;

internal class TiffEncoderEntriesCollector
{
    private const string SoftwareValue = "ImageSharp";

    public List<IExifValue> Entries { get; } = new List<IExifValue>();

    public void ProcessMetadata(ImageFrame frame, ImageMetadata imageMetadata)
        => new MetadataProcessor(this).Process(frame, imageMetadata);

    public void ProcessFrameInfo(ImageFrame frame, ImageMetadata imageMetadata)
        => new FrameInfoProcessor(this).Process(frame, imageMetadata);

    public void ProcessImageFormat(TiffEncoderCore encoder)
        => new ImageFormatProcessor(this).Process(encoder);

    public void AddOrReplace(IExifValue entry)
    {
        int index = this.Entries.FindIndex(t => t.Tag == entry.Tag);
        if (index >= 0)
        {
            this.Entries[index] = entry;
        }
        else
        {
            this.Entries.Add(entry);
        }
    }

    private void Add(IExifValue entry) => this.Entries.Add(entry);

    private abstract class BaseProcessor
    {
        public BaseProcessor(TiffEncoderEntriesCollector collector) => this.Collector = collector;

        protected TiffEncoderEntriesCollector Collector { get; }
    }

    private class MetadataProcessor : BaseProcessor
    {
        public MetadataProcessor(TiffEncoderEntriesCollector collector)
            : base(collector)
        {
        }

        public void Process(ImageFrame frame, ImageMetadata imageMetadata)
        {
            ImageFrameMetadata frameMetadata = frame.Metadata;
            ExifProfile frameExifProfile = frameMetadata.ExifProfile ?? new ExifProfile();
            XmpProfile frameXmpProfile = frameMetadata.XmpProfile;

            // The frame's own profiles take precedence over those of the image.
            // This keeps edits to a decoded frame's profiles even after a clone, which copies the frame and image profiles separately.
            IccProfile iccProfile = frameMetadata.IccProfile ?? imageMetadata?.IccProfile;
            IptcProfile iptcProfile = frameMetadata.IptcProfile ?? imageMetadata?.IptcProfile;

            this.ProcessProfiles(frameExifProfile, frameXmpProfile, iccProfile, iptcProfile);
            this.ProcessMetadata(frameExifProfile);

            if (!this.Collector.Entries.Exists(t => t.Tag == ExifTag.Software))
            {
                this.Collector.Add(new ExifString(ExifTagValue.Software)
                {
                    Value = SoftwareValue
                });
            }
        }

        private static bool IsPureMetadata(ExifTag tag)
        {
            switch ((ExifTagValue)(ushort)tag)
            {
                case ExifTagValue.DocumentName:
                case ExifTagValue.ImageDescription:
                case ExifTagValue.Make:
                case ExifTagValue.Model:
                case ExifTagValue.Software:
                case ExifTagValue.DateTime:
                case ExifTagValue.Artist:
                case ExifTagValue.HostComputer:
                case ExifTagValue.TargetPrinter:
                case ExifTagValue.XMP:
                case ExifTagValue.Rating:
                case ExifTagValue.RatingPercent:
                case ExifTagValue.ImageID:
                case ExifTagValue.Copyright:
                case ExifTagValue.MDLabName:
                case ExifTagValue.MDSampleInfo:
                case ExifTagValue.MDPrepDate:
                case ExifTagValue.MDPrepTime:
                case ExifTagValue.MDFileUnits:
                case ExifTagValue.SEMInfo:
                case ExifTagValue.XPTitle:
                case ExifTagValue.XPComment:
                case ExifTagValue.XPAuthor:
                case ExifTagValue.XPKeywords:
                case ExifTagValue.XPSubject:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the tag describes how the image data is laid out or encoded.
        /// The encoder writes these tags for the data it writes, so they are never copied from the source profile.
        /// </summary>
        /// <param name="tag">The tag.</param>
        /// <returns><see langword="true"/> if the tag describes the image data.</returns>
        private static bool IsImageDataTag(ExifTag tag)
        {
            switch ((ExifTagValue)(ushort)tag)
            {
                // Image structure (TIFF 6.0 sections 3 to 8 and 18 to 19).
                case ExifTagValue.ImageWidth:
                case ExifTagValue.ImageLength:
                case ExifTagValue.BitsPerSample:
                case ExifTagValue.Compression:
                case ExifTagValue.PhotometricInterpretation:
                case ExifTagValue.FillOrder:
                case ExifTagValue.StripOffsets:
                case ExifTagValue.SamplesPerPixel:
                case ExifTagValue.RowsPerStrip:
                case ExifTagValue.StripByteCounts:
                case ExifTagValue.StripRowCounts:
                case ExifTagValue.MinSampleValue:
                case ExifTagValue.MaxSampleValue:
                case ExifTagValue.PlanarConfiguration:
                case ExifTagValue.FreeOffsets:
                case ExifTagValue.FreeByteCounts:
                case ExifTagValue.Predictor:
                case ExifTagValue.ColorMap:
                case ExifTagValue.Indexed:
                case ExifTagValue.ExtraSamples:
                case ExifTagValue.SampleFormat:
                case ExifTagValue.SMinSampleValue:
                case ExifTagValue.SMaxSampleValue:
                case ExifTagValue.Decode:

                // Tiles (section 15).
                case ExifTagValue.TileWidth:
                case ExifTagValue.TileLength:
                case ExifTagValue.TileOffsets:
                case ExifTagValue.TileByteCounts:

                // Compression options (sections 11 and 22, TIFF Technical Note 2, TIFF-FX).
                case ExifTagValue.T4Options:
                case ExifTagValue.T6Options:
                case ExifTagValue.T82ptions:
                case ExifTagValue.JPEGTables:
                case ExifTagValue.JPEGProc:
                case ExifTagValue.JPEGInterchangeFormat:
                case ExifTagValue.JPEGInterchangeFormatLength:
                case ExifTagValue.JPEGRestartInterval:
                case ExifTagValue.JPEGLosslessPredictors:
                case ExifTagValue.JPEGPointTransforms:
                case ExifTagValue.JPEGQTables:
                case ExifTagValue.JPEGDCTables:
                case ExifTagValue.JPEGACTables:

                // YCbCr and CMYK sample encoding (sections 16 and 21).
                case ExifTagValue.YCbCrCoefficients:
                case ExifTagValue.YCbCrSubsampling:
                case ExifTagValue.YCbCrPositioning:
                case ExifTagValue.ReferenceBlackWhite:
                case ExifTagValue.InkSet:
                case ExifTagValue.InkNames:
                case ExifTagValue.NumberOfInks:
                case ExifTagValue.DotRange:

                // Color filter array samples (TIFF/EP) and TIFF-FX fax profiles, coding methods and layers.
                case ExifTagValue.CFARepeatPatternDim:
                case ExifTagValue.CFAPattern2:
                case ExifTagValue.FaxProfile:
                case ExifTagValue.CodingMethods:
                case ExifTagValue.VersionYear:
                case ExifTagValue.ModeNumber:
                case ExifTagValue.DefaultImageColor:
                case ExifTagValue.ImageLayer:

                // Their number of values depends on BitsPerSample.
                case ExifTagValue.GrayResponseUnit:
                case ExifTagValue.GrayResponseCurve:
                case ExifTagValue.TransferFunction:
                case ExifTagValue.TransferRange:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the field type can be written to a TIFF file.
        /// The BigTIFF types Long8, SignedLong8 and Ifd8 are not TIFF 6.0 types, and IFD offsets would point into the source file.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <returns><see langword="true"/> if the entry can be copied with its field type.</returns>
        private static bool HasCopyableDataType(IExifValue entry)
            => entry.DataType is not (ExifDataType.Ifd or ExifDataType.Long8 or ExifDataType.SignedLong8 or ExifDataType.Ifd8);

        private void ProcessMetadata(ExifProfile exifProfile)
        {
            foreach (IExifValue entry in exifProfile.Values)
            {
                // todo: skip subIfd
                if (!HasCopyableDataType(entry))
                {
                    continue;
                }

                switch ((ExifTagValue)(ushort)entry.Tag)
                {
                    case ExifTagValue.SubIFDOffset:
                    case ExifTagValue.GPSIFDOffset:
                    case ExifTagValue.SubIFDs:
                    case ExifTagValue.XMP:
                    case ExifTagValue.IPTC:
                    case ExifTagValue.IccProfile:
                        continue;
                }

                switch (ExifTags.GetPart(entry.Tag))
                {
                    case ExifParts.ExifTags:
                    case ExifParts.GpsTags:
                        break;

                    case ExifParts.IfdTags:
                        if (!IsPureMetadata(entry.Tag))
                        {
                            continue;
                        }

                        break;
                }

                if (!this.Collector.Entries.Exists(t => t.Tag == entry.Tag))
                {
                    this.Collector.AddOrReplace(entry.DeepClone());
                }
            }
        }

        private void ProcessProfiles(ExifProfile exifProfile, XmpProfile xmpProfile, IccProfile iccProfile, IptcProfile iptcProfile)
        {
            if (exifProfile != null && exifProfile.Parts != ExifParts.None)
            {
                foreach (IExifValue entry in exifProfile.Values)
                {
                    if (IsImageDataTag(entry.Tag) || !HasCopyableDataType(entry))
                    {
                        continue;
                    }

                    if (!this.Collector.Entries.Exists(t => t.Tag == entry.Tag) && entry.GetValue() != null)
                    {
                        ExifParts entryPart = ExifTags.GetPart(entry.Tag);
                        if (entryPart != ExifParts.None && exifProfile.Parts.HasFlag(entryPart))
                        {
                            this.Collector.AddOrReplace(entry.DeepClone());
                        }
                    }
                }
            }
            else
            {
                exifProfile.RemoveValue(ExifTag.SubIFDOffset);
            }

            if (iptcProfile != null)
            {
                iptcProfile.UpdateData();
                var iptc = new ExifByteArray(ExifTagValue.IPTC, ExifDataType.Byte)
                {
                    Value = iptcProfile.Data
                };

                this.Collector.AddOrReplace(iptc);
            }
            else
            {
                exifProfile.RemoveValue(ExifTag.IPTC);
            }

            if (iccProfile != null)
            {
                var icc = new ExifByteArray(ExifTagValue.IccProfile, ExifDataType.Undefined)
                {
                    Value = iccProfile.ToByteArray()
                };

                this.Collector.AddOrReplace(icc);
            }
            else
            {
                exifProfile.RemoveValue(ExifTag.IccProfile);
            }

            if (xmpProfile != null)
            {
                var xmp = new ExifByteArray(ExifTagValue.XMP, ExifDataType.Byte)
                {
                    Value = xmpProfile.Data
                };

                this.Collector.AddOrReplace(xmp);
            }
            else
            {
                exifProfile.RemoveValue(ExifTag.XMP);
            }
        }
    }

    private class FrameInfoProcessor : BaseProcessor
    {
        public FrameInfoProcessor(TiffEncoderEntriesCollector collector)
            : base(collector)
        {
        }

        public void Process(ImageFrame frame, ImageMetadata imageMetadata)
        {
            this.Collector.AddOrReplace(new ExifLong(ExifTagValue.ImageWidth)
            {
                Value = (uint)frame.Width
            });

            this.Collector.AddOrReplace(new ExifLong(ExifTagValue.ImageLength)
            {
                Value = (uint)frame.Height
            });

            this.ProcessResolution(imageMetadata);
        }

        private void ProcessResolution(ImageMetadata imageMetadata)
        {
            ExifResolutionValues resolution = UnitConverter.GetExifResolutionValues(
                imageMetadata.ResolutionUnits,
                imageMetadata.HorizontalResolution,
                imageMetadata.VerticalResolution);

            this.Collector.AddOrReplace(new ExifShort(ExifTagValue.ResolutionUnit)
            {
                Value = resolution.ResolutionUnit
            });

            if (resolution.VerticalResolution.HasValue && resolution.HorizontalResolution.HasValue)
            {
                this.Collector.AddOrReplace(new ExifRational(ExifTagValue.XResolution)
                {
                    Value = new Rational(resolution.HorizontalResolution.Value)
                });

                this.Collector.AddOrReplace(new ExifRational(ExifTagValue.YResolution)
                {
                    Value = new Rational(resolution.VerticalResolution.Value)
                });
            }
        }
    }

    private class ImageFormatProcessor : BaseProcessor
    {
        public ImageFormatProcessor(TiffEncoderEntriesCollector collector)
            : base(collector)
        {
        }

        public void Process(TiffEncoderCore encoder)
        {
            var planarConfig = new ExifShort(ExifTagValue.PlanarConfiguration)
            {
                Value = (ushort)TiffPlanarConfiguration.Chunky
            };

            var samplesPerPixel = new ExifShort(ExifTagValue.SamplesPerPixel)
            {
                Value = GetSamplesPerPixel(encoder)
            };

            ushort[] bitsPerSampleValue = GetBitsPerSampleValue(encoder);
            var bitPerSample = new ExifShortArray(ExifTagValue.BitsPerSample)
            {
                Value = bitsPerSampleValue
            };

            ushort compressionType = GetCompressionType(encoder);
            var compression = new ExifShort(ExifTagValue.Compression)
            {
                Value = compressionType
            };

            var photometricInterpretation = new ExifShort(ExifTagValue.PhotometricInterpretation)
            {
                Value = (ushort)encoder.PhotometricInterpretation
            };

            this.Collector.AddOrReplace(planarConfig);
            this.Collector.AddOrReplace(samplesPerPixel);
            this.Collector.AddOrReplace(bitPerSample);
            this.Collector.AddOrReplace(compression);
            this.Collector.AddOrReplace(photometricInterpretation);

            if (encoder.HorizontalPredictor == TiffPredictor.Horizontal)
            {
                if (encoder.PhotometricInterpretation == TiffPhotometricInterpretation.Rgb ||
                    encoder.PhotometricInterpretation == TiffPhotometricInterpretation.PaletteColor ||
                    encoder.PhotometricInterpretation == TiffPhotometricInterpretation.BlackIsZero ||
                    encoder.PhotometricInterpretation == TiffPhotometricInterpretation.WhiteIsZero)
                {
                    var predictor = new ExifShort(ExifTagValue.Predictor) { Value = (ushort)TiffPredictor.Horizontal };

                    this.Collector.AddOrReplace(predictor);
                }
            }
        }

        private static ushort GetSamplesPerPixel(TiffEncoderCore encoder)
        {
            switch (encoder.PhotometricInterpretation)
            {
                case TiffPhotometricInterpretation.PaletteColor:
                case TiffPhotometricInterpretation.BlackIsZero:
                case TiffPhotometricInterpretation.WhiteIsZero:
                    return 1;
                case TiffPhotometricInterpretation.Rgb:
                default:
                    return 3;
            }
        }

        private static ushort[] GetBitsPerSampleValue(TiffEncoderCore encoder)
        {
            switch (encoder.PhotometricInterpretation)
            {
                case TiffPhotometricInterpretation.PaletteColor:
                    if (encoder.BitsPerPixel == TiffBitsPerPixel.Bit4)
                    {
                        return TiffConstants.BitsPerSample4Bit.ToArray();
                    }
                    else
                    {
                        return TiffConstants.BitsPerSample8Bit.ToArray();
                    }

                case TiffPhotometricInterpretation.Rgb:
                    return TiffConstants.BitsPerSampleRgb8Bit.ToArray();

                case TiffPhotometricInterpretation.WhiteIsZero:
                    if (encoder.BitsPerPixel == TiffBitsPerPixel.Bit1)
                    {
                        return TiffConstants.BitsPerSample1Bit.ToArray();
                    }

                    return TiffConstants.BitsPerSample8Bit.ToArray();

                case TiffPhotometricInterpretation.BlackIsZero:
                    if (encoder.BitsPerPixel == TiffBitsPerPixel.Bit1)
                    {
                        return TiffConstants.BitsPerSample1Bit.ToArray();
                    }

                    return TiffConstants.BitsPerSample8Bit.ToArray();

                default:
                    return TiffConstants.BitsPerSampleRgb8Bit.ToArray();
            }
        }

        private static ushort GetCompressionType(TiffEncoderCore encoder)
        {
            switch (encoder.CompressionType)
            {
                case TiffCompression.Deflate:
                    // Deflate is allowed for all modes.
                    return (ushort)TiffCompression.Deflate;
                case TiffCompression.PackBits:
                    // PackBits is allowed for all modes.
                    return (ushort)TiffCompression.PackBits;
                case TiffCompression.Lzw:
                    if (encoder.PhotometricInterpretation == TiffPhotometricInterpretation.Rgb ||
                        encoder.PhotometricInterpretation == TiffPhotometricInterpretation.PaletteColor ||
                        encoder.PhotometricInterpretation == TiffPhotometricInterpretation.BlackIsZero ||
                        encoder.PhotometricInterpretation == TiffPhotometricInterpretation.WhiteIsZero)
                    {
                        return (ushort)TiffCompression.Lzw;
                    }

                    break;

                case TiffCompression.CcittGroup3Fax:
                    return (ushort)TiffCompression.CcittGroup3Fax;

                case TiffCompression.CcittGroup4Fax:
                    return (ushort)TiffCompression.CcittGroup4Fax;

                case TiffCompression.Ccitt1D:
                    return (ushort)TiffCompression.Ccitt1D;

                case TiffCompression.Jpeg:
                    return (ushort)TiffCompression.Jpeg;
            }

            return (ushort)TiffCompression.None;
        }
    }
}
