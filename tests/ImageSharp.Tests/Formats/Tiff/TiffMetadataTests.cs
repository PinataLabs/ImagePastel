// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp.Common.Helpers;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Tiff.Constants;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Tiff.Utils;
using Xunit;
using static SixLabors.ImageSharp.Tests.TestImages.Tiff;

namespace SixLabors.ImageSharp.Tests.Formats.Tiff;

[Trait("Format", "Tiff")]
public class TiffMetadataTests
{
    private static TiffDecoder TiffDecoder => new();

    private class NumberComparer : IEqualityComparer<Number>
    {
        public bool Equals(Number x, Number y) => x.Equals(y);

        public int GetHashCode(Number obj) => obj.GetHashCode();
    }

    [Fact]
    public void TiffMetadata_CloneIsDeep()
    {
        var meta = new TiffMetadata
        {
            ByteOrder = ByteOrder.BigEndian,
        };

        var clone = (TiffMetadata)meta.DeepClone();

        clone.ByteOrder = ByteOrder.LittleEndian;

        Assert.False(meta.ByteOrder == clone.ByteOrder);
    }

    [Theory]
    [WithFile(SampleMetadata, PixelTypes.Rgba32)]
    public void TiffFrameMetadata_CloneIsDeep<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(TiffDecoder))
        {
            TiffFrameMetadata meta = image.Frames.RootFrame.Metadata.GetTiffMetadata();
            var cloneSameAsSampleMetaData = (TiffFrameMetadata)meta.DeepClone();
            VerifyExpectedTiffFrameMetaDataIsPresent(cloneSameAsSampleMetaData);

            var clone = (TiffFrameMetadata)meta.DeepClone();

            clone.BitsPerPixel = TiffBitsPerPixel.Bit8;
            clone.Compression = TiffCompression.None;
            clone.PhotometricInterpretation = TiffPhotometricInterpretation.CieLab;
            clone.Predictor = TiffPredictor.Horizontal;

            Assert.False(meta.BitsPerPixel == clone.BitsPerPixel);
            Assert.False(meta.Compression == clone.Compression);
            Assert.False(meta.PhotometricInterpretation == clone.PhotometricInterpretation);
            Assert.False(meta.Predictor == clone.Predictor);
        }
    }

    private static void VerifyExpectedTiffFrameMetaDataIsPresent(TiffFrameMetadata frameMetaData)
    {
        Assert.NotNull(frameMetaData);
        Assert.NotNull(frameMetaData.BitsPerPixel);
        Assert.Equal(TiffBitsPerPixel.Bit4, frameMetaData.BitsPerPixel);
        Assert.Equal(TiffCompression.Lzw, frameMetaData.Compression);
        Assert.Equal(TiffPhotometricInterpretation.PaletteColor, frameMetaData.PhotometricInterpretation);
        Assert.Equal(TiffPredictor.None, frameMetaData.Predictor);
    }

    [Theory]
    [InlineData(Calliphora_BiColorUncompressed, 1)]
    [InlineData(GrayscaleUncompressed, 8)]
    [InlineData(RgbUncompressed, 24)]
    public void Identify_DetectsCorrectBitPerPixel(string imagePath, int expectedBitsPerPixel)
    {
        var testFile = TestFile.Create(imagePath);
        using var stream = new MemoryStream(testFile.Bytes, false);

        IImageInfo imageInfo = Image.Identify(stream);

        Assert.NotNull(imageInfo);
        TiffMetadata tiffMetadata = imageInfo.Metadata.GetTiffMetadata();
        Assert.NotNull(tiffMetadata);
        Assert.Equal(expectedBitsPerPixel, imageInfo.PixelType.BitsPerPixel);
    }

    [Theory]
    [InlineData(GrayscaleUncompressed, ByteOrder.BigEndian)]
    [InlineData(LittleEndianByteOrder, ByteOrder.LittleEndian)]
    public void Identify_DetectsCorrectByteOrder(string imagePath, ByteOrder expectedByteOrder)
    {
        var testFile = TestFile.Create(imagePath);
        using var stream = new MemoryStream(testFile.Bytes, false);

        IImageInfo imageInfo = Image.Identify(stream);

        Assert.NotNull(imageInfo);
        TiffMetadata tiffMetadata = imageInfo.Metadata.GetTiffMetadata();
        Assert.NotNull(tiffMetadata);
        Assert.Equal(expectedByteOrder, tiffMetadata.ByteOrder);
    }

    [Theory]
    [WithFile(SampleMetadata, PixelTypes.Rgba32, false)]
    [WithFile(SampleMetadata, PixelTypes.Rgba32, true)]
    public void MetadataProfiles<TPixel>(TestImageProvider<TPixel> provider, bool ignoreMetadata)
      where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(new TiffDecoder() { IgnoreMetadata = ignoreMetadata }))
        {
            TiffMetadata meta = image.Metadata.GetTiffMetadata();
            ImageFrameMetadata rootFrameMetaData = image.Frames.RootFrame.Metadata;
            Assert.NotNull(meta);
            if (ignoreMetadata)
            {
                Assert.Null(rootFrameMetaData.XmpProfile);
                Assert.Null(rootFrameMetaData.ExifProfile);
            }
            else
            {
                Assert.NotNull(rootFrameMetaData.XmpProfile);
                Assert.NotNull(rootFrameMetaData.ExifProfile);
                Assert.Equal(2599, rootFrameMetaData.XmpProfile.Data.Length);
                Assert.Equal(26, rootFrameMetaData.ExifProfile.Values.Count);
            }
        }
    }

    [Theory]
    [WithFile(InvalidIptcData, PixelTypes.Rgba32)]
    public void CanDecodeImage_WithIptcDataAsLong<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage(TiffDecoder);

        IptcProfile iptcProfile = image.Frames.RootFrame.Metadata.IptcProfile;
        Assert.NotNull(iptcProfile);
        IptcValue byline = iptcProfile.Values.FirstOrDefault(data => data.Tag == IptcTag.Byline);
        Assert.NotNull(byline);
        Assert.Equal("Studio Mantyniemi", byline.Value);
    }

    [Theory]
    [WithFile(SampleMetadata, PixelTypes.Rgba32)]
    public void BaselineTags<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(TiffDecoder))
        {
            ImageFrame<TPixel> rootFrame = image.Frames.RootFrame;
            Assert.Equal(32, rootFrame.Width);
            Assert.Equal(32, rootFrame.Height);
            Assert.NotNull(rootFrame.Metadata.XmpProfile);
            Assert.Equal(2599, rootFrame.Metadata.XmpProfile.Data.Length);

            ExifProfile exifProfile = rootFrame.Metadata.ExifProfile;
            TiffFrameMetadata tiffFrameMetadata = rootFrame.Metadata.GetTiffMetadata();
            Assert.NotNull(exifProfile);

            // The original exifProfile has 30 values, but 4 of those values will be stored in the TiffFrameMetaData
            // and removed from the profile on decode.
            Assert.Equal(26, exifProfile.Values.Count);
            Assert.Equal(TiffBitsPerPixel.Bit4, tiffFrameMetadata.BitsPerPixel);
            Assert.Equal(TiffCompression.Lzw, tiffFrameMetadata.Compression);
            Assert.Equal("This is Название", exifProfile.GetValue(ExifTag.ImageDescription).Value);
            Assert.Equal("This is Изготовитель камеры", exifProfile.GetValue(ExifTag.Make).Value);
            Assert.Equal("This is Модель камеры", exifProfile.GetValue(ExifTag.Model).Value);
            Assert.Equal("IrfanView", exifProfile.GetValue(ExifTag.Software).Value);
            Assert.Null(exifProfile.GetValue(ExifTag.DateTime)?.Value);
            Assert.Equal("This is author1;Author2", exifProfile.GetValue(ExifTag.Artist).Value);
            Assert.Null(exifProfile.GetValue(ExifTag.HostComputer)?.Value);
            Assert.Equal("This is Авторские права", exifProfile.GetValue(ExifTag.Copyright).Value);
            Assert.Equal(4, exifProfile.GetValue(ExifTag.Rating).Value);
            Assert.Equal(75, exifProfile.GetValue(ExifTag.RatingPercent).Value);
            var expectedResolution = new Rational(10000, 1000, simplify: false);
            Assert.Equal(expectedResolution, exifProfile.GetValue(ExifTag.XResolution).Value);
            Assert.Equal(expectedResolution, exifProfile.GetValue(ExifTag.YResolution).Value);
            Assert.Equal(new Number[] { 8u }, exifProfile.GetValue(ExifTag.StripOffsets)?.Value, new NumberComparer());
            Assert.Equal(new Number[] { 297u }, exifProfile.GetValue(ExifTag.StripByteCounts)?.Value, new NumberComparer());
            Assert.Null(exifProfile.GetValue(ExifTag.ExtraSamples)?.Value);
            Assert.Equal(32u, exifProfile.GetValue(ExifTag.RowsPerStrip).Value);
            Assert.Null(exifProfile.GetValue(ExifTag.SampleFormat));
            Assert.Equal(TiffPredictor.None, tiffFrameMetadata.Predictor);
            Assert.Equal(PixelResolutionUnit.PixelsPerInch, UnitConverter.ExifProfileToResolutionUnit(exifProfile));
            ushort[] colorMap = exifProfile.GetValue(ExifTag.ColorMap)?.Value;
            Assert.NotNull(colorMap);
            Assert.Equal(48, colorMap.Length);
            Assert.Equal(10537, colorMap[0]);
            Assert.Equal(14392, colorMap[1]);
            Assert.Equal(58596, colorMap[46]);
            Assert.Equal(3855, colorMap[47]);
            Assert.Equal(TiffPhotometricInterpretation.PaletteColor, tiffFrameMetadata.PhotometricInterpretation);
            Assert.Equal(1u, exifProfile.GetValue(ExifTag.SamplesPerPixel).Value);

            ImageMetadata imageMetaData = image.Metadata;
            Assert.NotNull(imageMetaData);
            Assert.Equal(PixelResolutionUnit.PixelsPerInch, imageMetaData.ResolutionUnits);
            Assert.Equal(10, imageMetaData.HorizontalResolution);
            Assert.Equal(10, imageMetaData.VerticalResolution);

            TiffMetadata tiffMetaData = image.Metadata.GetTiffMetadata();
            Assert.NotNull(tiffMetaData);
            Assert.Equal(ByteOrder.LittleEndian, tiffMetaData.ByteOrder);
        }
    }

    [Theory]
    [WithFile(MultiframeDeflateWithPreview, PixelTypes.Rgba32)]
    public void SubfileType<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(TiffDecoder))
        {
            TiffMetadata meta = image.Metadata.GetTiffMetadata();
            Assert.NotNull(meta);

            Assert.Equal(2, image.Frames.Count);

            ExifProfile frame0Exif = image.Frames[0].Metadata.ExifProfile;
            Assert.Equal(TiffNewSubfileType.FullImage, (TiffNewSubfileType)frame0Exif.GetValue(ExifTag.SubfileType).Value);
            Assert.Equal(255, image.Frames[0].Width);
            Assert.Equal(255, image.Frames[0].Height);

            ExifProfile frame1Exif = image.Frames[1].Metadata.ExifProfile;
            Assert.Equal(TiffNewSubfileType.Preview, (TiffNewSubfileType)frame1Exif.GetValue(ExifTag.SubfileType).Value);
            Assert.Equal(255, image.Frames[1].Width);
            Assert.Equal(255, image.Frames[1].Height);
        }
    }

    [Theory]
    [WithFile(SampleMetadata, PixelTypes.Rgba32)]
    public void Encode_PreservesMetadata<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Load Tiff image
        using Image<TPixel> image = provider.GetImage(new TiffDecoder() { IgnoreMetadata = false });

        ImageMetadata inputMetaData = image.Metadata;
        ImageFrame<TPixel> rootFrameInput = image.Frames.RootFrame;
        TiffFrameMetadata frameMetaInput = rootFrameInput.Metadata.GetTiffMetadata();
        XmpProfile xmpProfileInput = rootFrameInput.Metadata.XmpProfile;
        ExifProfile exifProfileInput = rootFrameInput.Metadata.ExifProfile;

        Assert.Equal(TiffCompression.Lzw, frameMetaInput.Compression);
        Assert.Equal(TiffBitsPerPixel.Bit4, frameMetaInput.BitsPerPixel);

        // Save to Tiff
        var tiffEncoder = new TiffEncoder() { PhotometricInterpretation = TiffPhotometricInterpretation.Rgb };
        using var ms = new MemoryStream();
        image.Save(ms, tiffEncoder);

        // Assert
        ms.Position = 0;
        using var encodedImage = Image.Load<Rgba32>(ms);

        ImageMetadata encodedImageMetaData = encodedImage.Metadata;
        ImageFrame<Rgba32> rootFrameEncodedImage = encodedImage.Frames.RootFrame;
        TiffFrameMetadata tiffMetaDataEncodedRootFrame = rootFrameEncodedImage.Metadata.GetTiffMetadata();
        ExifProfile encodedImageExifProfile = rootFrameEncodedImage.Metadata.ExifProfile;
        XmpProfile encodedImageXmpProfile = rootFrameEncodedImage.Metadata.XmpProfile;

        Assert.Equal(TiffBitsPerPixel.Bit4, tiffMetaDataEncodedRootFrame.BitsPerPixel);
        Assert.Equal(TiffCompression.Lzw, tiffMetaDataEncodedRootFrame.Compression);

        Assert.Equal(inputMetaData.HorizontalResolution, encodedImageMetaData.HorizontalResolution);
        Assert.Equal(inputMetaData.VerticalResolution, encodedImageMetaData.VerticalResolution);
        Assert.Equal(inputMetaData.ResolutionUnits, encodedImageMetaData.ResolutionUnits);

        Assert.Equal(rootFrameInput.Width, rootFrameEncodedImage.Width);
        Assert.Equal(rootFrameInput.Height, rootFrameEncodedImage.Height);

        PixelResolutionUnit resolutionUnitInput = UnitConverter.ExifProfileToResolutionUnit(exifProfileInput);
        PixelResolutionUnit resolutionUnitEncoded = UnitConverter.ExifProfileToResolutionUnit(encodedImageExifProfile);
        Assert.Equal(resolutionUnitInput, resolutionUnitEncoded);
        Assert.Equal(exifProfileInput.GetValue(ExifTag.XResolution).Value.ToDouble(), encodedImageExifProfile.GetValue(ExifTag.XResolution).Value.ToDouble());
        Assert.Equal(exifProfileInput.GetValue(ExifTag.YResolution).Value.ToDouble(), encodedImageExifProfile.GetValue(ExifTag.YResolution).Value.ToDouble());

        Assert.NotNull(xmpProfileInput);
        Assert.NotNull(encodedImageXmpProfile);
        Assert.Equal(xmpProfileInput.Data, encodedImageXmpProfile.Data);

        Assert.Equal("IrfanView", exifProfileInput.GetValue(ExifTag.Software).Value);
        Assert.Equal("This is Название", exifProfileInput.GetValue(ExifTag.ImageDescription).Value);
        Assert.Equal("This is Изготовитель камеры", exifProfileInput.GetValue(ExifTag.Make).Value);
        Assert.Equal("This is Авторские права", exifProfileInput.GetValue(ExifTag.Copyright).Value);

        Assert.Equal(exifProfileInput.GetValue(ExifTag.ImageDescription).Value, encodedImageExifProfile.GetValue(ExifTag.ImageDescription).Value);
        Assert.Equal(exifProfileInput.GetValue(ExifTag.Make).Value, encodedImageExifProfile.GetValue(ExifTag.Make).Value);
        Assert.Equal(exifProfileInput.GetValue(ExifTag.Copyright).Value, encodedImageExifProfile.GetValue(ExifTag.Copyright).Value);

        // Note that the encoded profile has PlanarConfiguration explicitly set, which is missing in the original image profile.
        Assert.Equal((ushort)TiffPlanarConfiguration.Chunky, encodedImageExifProfile.GetValue(ExifTag.PlanarConfiguration)?.Value);
        Assert.Equal(exifProfileInput.Values.Count + 1, encodedImageExifProfile.Values.Count);
    }

    [Fact]
    public void Decode_WithIccProfile_ExposesProfileOnImageAndRootFrame()
    {
        byte[] iccBytes = GetJpegIccProfileBytes();

        using Image<Rgba32> source = CreateImageWithIccProfile(iccBytes);

        using Image<Rgba32> image = Image.Load<Rgba32>(EncodeTiff(source));

        Assert.Equal(iccBytes, image.Metadata.IccProfile?.ToByteArray());
        Assert.Equal(iccBytes, image.Frames.RootFrame.Metadata.IccProfile?.ToByteArray());
    }

    [Fact]
    public void Decode_WithIccProfile_ImageProfileIsRootFrameProfile()
    {
        using Image<Rgba32> source = CreateImageWithIccProfile(GetJpegIccProfileBytes());

        using Image<Rgba32> image = Image.Load<Rgba32>(EncodeTiff(source));

        Assert.NotNull(image.Metadata.IccProfile);
        Assert.Same(image.Frames.RootFrame.Metadata.IccProfile, image.Metadata.IccProfile);
    }

    [Fact]
    public void Decode_WithIptcProfile_ImageProfileIsRootFrameProfile()
    {
        // Load the file directly: the test image provider returns clones, and cloning does not keep shared instances.
        using Image<Rgba32> image = Image.Load<Rgba32>(TestFile.Create(IptcData).Bytes);

        Assert.NotNull(image.Metadata.IptcProfile);
        Assert.Same(image.Frames.RootFrame.Metadata.IptcProfile, image.Metadata.IptcProfile);
    }

    [Fact]
    public void Identify_WithIccProfile_ExposesProfileOnImage()
    {
        byte[] iccBytes = GetJpegIccProfileBytes();
        using Image<Rgba32> source = CreateImageWithIccProfile(iccBytes);

        IImageInfo info = Image.Identify(EncodeTiff(source));

        Assert.Equal(iccBytes, info.Metadata.IccProfile?.ToByteArray());
    }

    [Theory]
    [InlineData(IptcData, "author")]
    [InlineData(InvalidIptcData, "Studio Mantyniemi")]
    public void Identify_WithIptcProfile_ExposesProfileOnImage(string imagePath, string expectedByline)
    {
        IImageInfo info = Image.Identify(TestFile.Create(imagePath).Bytes);

        Assert.Equal(expectedByline, info.Metadata.IptcProfile?.GetValues(IptcTag.Byline).Single().Value);
    }

    [Fact]
    public void Identify_IgnoreMetadata_ExposesNoProfiles()
    {
        using Image<Rgba32> source = CreateImageWithIccProfile(GetJpegIccProfileBytes());
        source.Metadata.IptcProfile = new IptcProfile();
        source.Metadata.IptcProfile.SetValue(IptcTag.Byline, "image author");
        using var stream = new MemoryStream(EncodeTiff(source));

        IImageInfo info = new TiffDecoder { IgnoreMetadata = true }.Identify(Configuration.Default, stream, TestContext.Current.CancellationToken);

        Assert.Null(info.Metadata.IccProfile);
        Assert.Null(info.Metadata.IptcProfile);
    }

    [Theory]
    [InlineData(ExifDataType.Byte, new byte[] { 0x1c })]
    [InlineData(ExifDataType.Undefined, new byte[] { 0x1c })]
    [InlineData(ExifDataType.Long, new byte[] { 0x1c, 0, 0, 0 })]
    public void IdentifyAndDecode_SingleValueIptcEntry_ExposesNoIptcProfile(ExifDataType type, byte[] value)
    {
        // A single value is too short to hold an IPTC record.
        var builder = new TiffTestFileBuilder();
        AddOnePixelRgbPage(builder, GetJpegIccProfileBytes());
        byte[] tiff = builder.AddEntry(ExifTagValue.IPTC, type, 1, value).Build();

        IImageInfo info = Image.Identify(tiff);
        using Image<Rgba32> image = Image.Load<Rgba32>(tiff);

        Assert.Null(info.Metadata.IptcProfile);
        Assert.Null(image.Frames.RootFrame.Metadata.IptcProfile);
        Assert.NotNull(image.Frames.RootFrame.Metadata.IccProfile);
    }

    [Theory]
    [InlineData(SampleMetadata)]
    [InlineData(IptcData)]
    [InlineData(InvalidIptcData)]
    [InlineData(RgbUncompressed)]
    [InlineData(LittleEndianByteOrder)]
    [InlineData(MultiframeDeflateWithPreview)]
    [InlineData(TestImages.BigTiff.BigTIFF)]
    public void Identify_ImageMetadata_MatchesDecode(string imagePath)
    {
        byte[] bytes = TestFile.Create(imagePath).Bytes;

        IImageInfo info = Image.Identify(bytes);
        using Image<Rgba32> image = Image.Load<Rgba32>(bytes);

        ImageMetadata expected = image.Metadata;
        ImageMetadata actual = info.Metadata;
        Assert.Equal(expected.IccProfile?.ToByteArray(), actual.IccProfile?.ToByteArray());
        Assert.Equal(GetIptcValues(expected.IptcProfile), GetIptcValues(actual.IptcProfile));
        Assert.Equal(expected.ExifProfile?.ToByteArray(), actual.ExifProfile?.ToByteArray());
        Assert.Equal(expected.XmpProfile?.ToByteArray(), actual.XmpProfile?.ToByteArray());
        Assert.Equal(expected.HorizontalResolution, actual.HorizontalResolution);
        Assert.Equal(expected.VerticalResolution, actual.VerticalResolution);
        Assert.Equal(expected.ResolutionUnits, actual.ResolutionUnits);
        Assert.Equal(expected.GetTiffMetadata().ByteOrder, actual.GetTiffMetadata().ByteOrder);
        Assert.Equal(expected.GetTiffMetadata().FormatType, actual.GetTiffMetadata().FormatType);
    }

    [Fact]
    public void Encode_DecodedTiffWithIccProfile_KeepsProfile()
    {
        byte[] iccBytes = GetJpegIccProfileBytes();
        using Image<Rgba32> source = CreateImageWithIccProfile(iccBytes);
        using Image<Rgba32> decoded = Image.Load<Rgba32>(EncodeTiff(source));

        using Image<Rgba32> image = Image.Load<Rgba32>(EncodeTiff(decoded));

        Assert.Equal(iccBytes, image.Metadata.IccProfile?.ToByteArray());
        Assert.Equal(iccBytes, image.Frames.RootFrame.Metadata.IccProfile?.ToByteArray());
    }

    [Fact]
    public void Encode_DecodedTiffWithIccProfileAsJpeg_KeepsProfile()
    {
        byte[] iccBytes = GetJpegIccProfileBytes();
        using Image<Rgba32> source = CreateImageWithIccProfile(iccBytes);
        using Image<Rgba32> decoded = Image.Load<Rgba32>(EncodeTiff(source));

        using var stream = new MemoryStream();
        decoded.SaveAsJpeg(stream);
        using Image<Rgba32> image = Image.Load<Rgba32>(stream.ToArray());

        Assert.Equal(iccBytes, image.Metadata.IccProfile?.ToByteArray());
    }

    [Fact]
    public void Decode_MultiframeWithIccProfiles_KeepsEachProfileOnItsFrame()
    {
        byte[] rootIccBytes = GetJpegIccProfileBytes();
        byte[] secondIccBytes = IccTestDataProfiles.Profile_Random_Array;
        var builder = new TiffTestFileBuilder();
        AddOnePixelRgbPage(builder, rootIccBytes);
        AddOnePixelRgbPage(builder, secondIccBytes);
        byte[] tiff = builder.Build();

        using Image<Rgba32> image = Image.Load<Rgba32>(tiff);

        Assert.Equal(2, image.Frames.Count);
        Assert.Equal(rootIccBytes, image.Metadata.IccProfile?.ToByteArray());
        Assert.Equal(rootIccBytes, image.Frames[0].Metadata.IccProfile?.ToByteArray());
        Assert.Equal(secondIccBytes, image.Frames[1].Metadata.IccProfile?.ToByteArray());
    }

    [Theory]
    [WithFile(IptcData, PixelTypes.Rgba32)]
    public void Encode_DecodedTiffWithIptcProfile_KeepsProfile<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> decoded = provider.GetImage(TiffDecoder);
        List<(IptcTag Tag, string Value)> expected = GetIptcValues(decoded.Frames.RootFrame.Metadata.IptcProfile);
        Assert.NotEmpty(expected);
        Assert.Equal(expected, GetIptcValues(decoded.Metadata.IptcProfile));

        using Image<Rgba32> image = Image.Load<Rgba32>(EncodeTiff(decoded));

        Assert.Equal(expected, GetIptcValues(image.Metadata.IptcProfile));
        Assert.Equal(expected, GetIptcValues(image.Frames.RootFrame.Metadata.IptcProfile));
    }

    [Theory]
    [WithFile(IptcData, PixelTypes.Rgba32)]
    public void Encode_EditedRootFrameIptcProfile_WritesEdit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> decoded = provider.GetImage(TiffDecoder);
        decoded.Frames.RootFrame.Metadata.IptcProfile.SetValue(IptcTag.Caption, "edited caption");

        using Image<Rgba32> image = Image.Load<Rgba32>(EncodeTiff(decoded));

        IptcProfile iptcProfile = image.Frames.RootFrame.Metadata.IptcProfile;
        Assert.Equal("edited caption", iptcProfile.GetValues(IptcTag.Caption).Single().Value);
        Assert.Equal("author", iptcProfile.GetValues(IptcTag.Byline).Single().Value);
    }

    [Theory]
    [WithFile(IptcData, PixelTypes.Rgba32)]
    public void Encode_ReplacedRootFrameIptcProfile_WritesReplacement<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> decoded = provider.GetImage(TiffDecoder);
        var replacement = new IptcProfile();
        replacement.SetValue(IptcTag.Byline, "replaced author");
        decoded.Frames.RootFrame.Metadata.IptcProfile = replacement;

        using Image<Rgba32> image = Image.Load<Rgba32>(EncodeTiff(decoded));

        IptcProfile iptcProfile = image.Frames.RootFrame.Metadata.IptcProfile;
        Assert.Equal("replaced author", iptcProfile.GetValues(IptcTag.Byline).Single().Value);
        Assert.Empty(iptcProfile.GetValues(IptcTag.Caption));
    }

    [Fact]
    public void Encode_RootFrameProfilesWithoutImageProfiles_AreWritten()
    {
        byte[] iccBytes = GetJpegIccProfileBytes();
        using var source = new Image<Rgba32>(8, 8);
        source.Frames.RootFrame.Metadata.IccProfile = new IccProfile(iccBytes);
        source.Frames.RootFrame.Metadata.IptcProfile = new IptcProfile();
        source.Frames.RootFrame.Metadata.IptcProfile.SetValue(IptcTag.Byline, "frame author");

        using Image<Rgba32> image = Image.Load<Rgba32>(EncodeTiff(source));

        Assert.Equal(iccBytes, image.Frames.RootFrame.Metadata.IccProfile?.ToByteArray());
        Assert.Equal("frame author", image.Frames.RootFrame.Metadata.IptcProfile?.GetValues(IptcTag.Byline).Single().Value);
    }

    [Fact]
    public void Encode_ImageLevelProfiles_AreWritten()
    {
        byte[] iccBytes = GetJpegIccProfileBytes();
        using Image<Rgba32> source = CreateImageWithIccProfile(iccBytes);
        source.Metadata.IptcProfile = new IptcProfile();
        source.Metadata.IptcProfile.SetValue(IptcTag.Byline, "image author");

        using Image<Rgba32> image = Image.Load<Rgba32>(EncodeTiff(source));

        Assert.Equal(iccBytes, image.Frames.RootFrame.Metadata.IccProfile?.ToByteArray());
        Assert.Equal("image author", image.Frames.RootFrame.Metadata.IptcProfile?.GetValues(IptcTag.Byline).Single().Value);
    }

    [Fact]
    public void Encode_DecodedMultiframeWithIccProfiles_KeepsEachProfileOnItsFrame()
    {
        byte[] rootIccBytes = GetJpegIccProfileBytes();
        byte[] secondIccBytes = IccTestDataProfiles.Profile_Random_Array;
        var builder = new TiffTestFileBuilder();
        AddOnePixelRgbPage(builder, rootIccBytes);
        AddOnePixelRgbPage(builder, secondIccBytes);
        using Image<Rgba32> decoded = Image.Load<Rgba32>(builder.Build());

        using Image<Rgba32> image = Image.Load<Rgba32>(EncodeTiff(decoded));

        Assert.Equal(2, image.Frames.Count);
        Assert.Equal(rootIccBytes, image.Frames[0].Metadata.IccProfile?.ToByteArray());
        Assert.Equal(secondIccBytes, image.Frames[1].Metadata.IccProfile?.ToByteArray());
    }

    [Fact]
    public void Encode_MultiframeWithFrameProfiles_WritesEachFrameProfilesToItsOwnIfd()
    {
        byte[] rootIccBytes = GetJpegIccProfileBytes();
        byte[] secondIccBytes = IccTestDataProfiles.Profile_Random_Array;
        using var source = new Image<Rgba32>(8, 8);
        source.Frames.CreateFrame();
        source.Frames.CreateFrame();
        SetFrameProfiles(source.Frames[0].Metadata, rootIccBytes, "page 1");
        SetFrameProfiles(source.Frames[1].Metadata, secondIccBytes, "page 2");
        source.Metadata.IccProfile = new IccProfile(rootIccBytes);
        source.Metadata.IptcProfile = new IptcProfile();
        source.Metadata.IptcProfile.SetValue(IptcTag.Byline, "image author");

        using Image<Rgba32> image = Image.Load<Rgba32>(EncodeTiff(source));

        Assert.Equal(3, image.Frames.Count);
        AssertFrameProfiles(image.Frames[0].Metadata, rootIccBytes, "page 1");
        AssertFrameProfiles(image.Frames[1].Metadata, secondIccBytes, "page 2");

        // A frame without profiles of its own does not take those of another frame or of the image.
        ImageFrameMetadata thirdFrameMetadata = image.Frames[2].Metadata;
        Assert.Null(thirdFrameMetadata.IccProfile);
        Assert.Null(thirdFrameMetadata.IptcProfile);
        Assert.Null(thirdFrameMetadata.XmpProfile);
        Assert.Null(thirdFrameMetadata.ExifProfile.GetValue(ExifTag.ImageDescription));
    }

    // The expected hashes were captured from the encoder output before the fix for #90,
    // which writes the metadata of every frame. Single-page output must not change.
    // A change that is meant to alter single-page output must check the new output and recapture these hashes.
    [Theory]
    [InlineData(false, "D2B1045E53C83749186F1CD6E419885D2173A7F0D4BD5EC416B898857E24F524")]
    [InlineData(true, "E07EDC36FDF3BC8DE5E9BAFD8252F418F21E38AD6D2B6E6048C6AB5E6073780C")]
    public void Encode_SinglePage_OutputIsUnchanged(bool withProfiles, string expectedSha256)
    {
        using var source = new Image<Rgba32>(5, 3);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                source[x, y] = new Rgba32((byte)(x * 50), (byte)(y * 100), (byte)((x + y) * 30), 255);
            }
        }

        if (withProfiles)
        {
            source.Metadata.HorizontalResolution = 300;
            source.Metadata.VerticalResolution = 300;
            SetFrameProfiles(source.Frames.RootFrame.Metadata, GetJpegIccProfileBytes(), "single page");
        }

        string actualSha256 = Convert.ToHexString(SHA256.HashData(EncodeTiff(source)));

        Assert.Equal(expectedSha256, actualSha256);
    }

    private static void SetFrameProfiles(ImageFrameMetadata metadata, byte[] iccBytes, string text)
    {
        metadata.IccProfile = new IccProfile(iccBytes);
        metadata.IptcProfile = new IptcProfile();
        metadata.IptcProfile.SetValue(IptcTag.Byline, text);
        metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes($"<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">{text}</x:xmpmeta>"));
        metadata.ExifProfile = new ExifProfile();
        metadata.ExifProfile.SetValue(ExifTag.ImageDescription, text);
    }

    private static void AssertFrameProfiles(ImageFrameMetadata metadata, byte[] iccBytes, string text)
    {
        Assert.Equal(iccBytes, metadata.IccProfile?.ToByteArray());
        Assert.Equal(text, metadata.IptcProfile?.GetValues(IptcTag.Byline).Single().Value);
        Assert.Equal(Encoding.UTF8.GetBytes($"<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">{text}</x:xmpmeta>"), metadata.XmpProfile?.Data);
        Assert.Equal(text, metadata.ExifProfile?.GetValue(ExifTag.ImageDescription)?.Value);
    }

    private static byte[] GetJpegIccProfileBytes()
    {
        using Image<Rgba32> jpeg = Image.Load<Rgba32>(TestFile.Create(TestImages.Jpeg.Baseline.Snake).Bytes);
        return jpeg.Metadata.IccProfile.ToByteArray();
    }

    private static void AddOnePixelRgbPage(TiffTestFileBuilder builder, byte[] iccBytes)
        => builder
            .AddPage(new byte[] { 1, 2, 3 })
            .AddLong(ExifTagValue.ImageWidth, 1)
            .AddLong(ExifTagValue.ImageLength, 1)
            .AddShort(ExifTagValue.BitsPerSample, 8, 8, 8)
            .AddShort(ExifTagValue.PhotometricInterpretation, (ushort)TiffPhotometricInterpretation.Rgb)
            .AddShort(ExifTagValue.SamplesPerPixel, 3)
            .AddLong(ExifTagValue.RowsPerStrip, 1)
            .AddEntry(ExifTagValue.IccProfile, ExifDataType.Undefined, (uint)iccBytes.Length, iccBytes);

    private static Image<Rgba32> CreateImageWithIccProfile(byte[] iccBytes)
    {
        var image = new Image<Rgba32>(8, 8);
        image.Metadata.IccProfile = new IccProfile(iccBytes);
        return image;
    }

    private static byte[] EncodeTiff(Image image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, new TiffEncoder());
        return stream.ToArray();
    }

    private static List<(IptcTag Tag, string Value)> GetIptcValues(IptcProfile profile)
        => profile?.Values.Select(v => (v.Tag, v.Value)).ToList();
}
