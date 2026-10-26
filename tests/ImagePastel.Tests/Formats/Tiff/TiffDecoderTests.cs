// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

// ReSharper disable InconsistentNaming
using System;
using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Tests.TestUtilities.ReferenceCodecs;
using Xunit;
using static SixLabors.ImageSharp.Tests.TestImages.Tiff;

namespace SixLabors.ImageSharp.Tests.Formats.Tiff;

[Trait("Format", "Tiff")]
[ValidateDisposedMemoryAllocations]
public class TiffDecoderTests : TiffDecoderBaseTester
{
    public static readonly string[] MultiframeTestImages = Multiframes;

    /// <summary>
    /// Data in the stream before the TIFF. The odd length keeps the TIFF off a word boundary.
    /// </summary>
    private static readonly byte[] StreamPrefix = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 };

    [Theory]
    [WithFile(RgbUncompressedTiled, PixelTypes.Rgba32)]
    [WithFile(MultiframeDifferentSize, PixelTypes.Rgba32)]
    [WithFile(MultiframeDifferentVariants, PixelTypes.Rgba32)]
    [WithFile(InkSetNotCmyk, PixelTypes.Rgba32)]
    [WithFile(CmykJpegCompressed, PixelTypes.Rgba32)]
    public void ThrowsNotSupported<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => Assert.Throws<NotSupportedException>(() => provider.GetImage(TiffDecoder));

    [Theory]
    [InlineData(RgbUncompressed, 24, 256, 256, 300, 300, PixelResolutionUnit.PixelsPerInch)]
    [InlineData(SmallRgbDeflate, 24, 32, 32, 96, 96, PixelResolutionUnit.PixelsPerInch)]
    [InlineData(Calliphora_GrayscaleUncompressed, 8, 200, 298, 96, 96, PixelResolutionUnit.PixelsPerInch)]
    [InlineData(Flower4BitPalette, 4, 73, 43, 72, 72, PixelResolutionUnit.PixelsPerInch)]
    public void Identify(string imagePath, int expectedPixelSize, int expectedWidth, int expectedHeight, double expectedHResolution, double expectedVResolution, PixelResolutionUnit expectedResolutionUnit)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            IImageInfo info = Image.Identify(stream);

            Assert.Equal(expectedPixelSize, info.PixelType?.BitsPerPixel);
            Assert.Equal(expectedWidth, info.Width);
            Assert.Equal(expectedHeight, info.Height);
            Assert.NotNull(info.Metadata);
            Assert.Equal(expectedHResolution, info.Metadata.HorizontalResolution);
            Assert.Equal(expectedVResolution, info.Metadata.VerticalResolution);
            Assert.Equal(expectedResolutionUnit, info.Metadata.ResolutionUnits);
        }
    }

    [Theory]
    [InlineData(RgbLzwNoPredictorMultistrip, ImageSharp.ByteOrder.LittleEndian)]
    [InlineData(RgbLzwNoPredictorMultistripMotorola, ImageSharp.ByteOrder.BigEndian)]
    public void ByteOrder(string imagePath, ByteOrder expectedByteOrder)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            IImageInfo info = Image.Identify(stream);

            Assert.NotNull(info.Metadata);
            Assert.Equal(expectedByteOrder, info.Metadata.GetTiffMetadata().ByteOrder);

            stream.Seek(0, SeekOrigin.Begin);

            using var img = Image.Load(stream);
            Assert.Equal(expectedByteOrder, img.Metadata.GetTiffMetadata().ByteOrder);
        }
    }

    [Theory]
    [WithFile(RgbUncompressed, PixelTypes.Rgba32)]
    [WithFile(Calliphora_GrayscaleUncompressed, PixelTypes.Rgba32)]
    [WithFile(Calliphora_RgbUncompressed, PixelTypes.Rgba32)]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_Uncompressed<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    // The reference is Magick.NET with the profiles removed, which converts CMYK with the same formula as the JPEG decoder.
    [Theory]
    [WithFile(Cmyk8BitContiguous, PixelTypes.Rgba32)]
    [WithFile(Cmyk8BitPlanar, PixelTypes.Rgba32)]
    [WithFile(Cmyk8BitDeflatePredictor, PixelTypes.Rgba32)]
    [WithFile(Cmyk8BitPlanarDeflatePredictor, PixelTypes.Rgba32)]
    [WithFile(Cmyk8BitWithIccProfile, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_Cmyk_8Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider, MagickReferenceDecoder.CmykToSrgbInstance, useExactComparer: false);

    [Theory]
    [WithFile(Cmyk16BitContiguousLittleEndian, PixelTypes.Rgba32 | PixelTypes.Rgba64)]
    [WithFile(Cmyk16BitContiguousBigEndian, PixelTypes.Rgba32 | PixelTypes.Rgba64)]
    [WithFile(Cmyk16BitPlanarLittleEndian, PixelTypes.Rgba32 | PixelTypes.Rgba64)]
    [WithFile(Cmyk16BitPlanarBigEndian, PixelTypes.Rgba32 | PixelTypes.Rgba64)]
    [WithFile(Cmyk16BitDeflatePredictorBigEndian, PixelTypes.Rgba64)]
    [WithFile(Cmyk16BitPlanarDeflatePredictorBigEndian, PixelTypes.Rgba64)]
    public void TiffDecoder_CanDecode_Cmyk_16Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider, MagickReferenceDecoder.CmykToSrgbInstance, useExactComparer: false);

    [Fact]
    public void TiffDecoder_Cmyk_ExposesEmbeddedIccProfile()
    {
        byte[] expected = TestFile.Create(CmykTestIccProfile).Bytes;

        using Image<Rgba32> image = Image.Load<Rgba32>(TestFile.Create(Cmyk8BitWithIccProfile).Bytes);

        // The CMYK profile does not describe the formula RGB pixels, so it is not the pixel profile.
        Assert.Null(image.Metadata.IccProfile);
        Assert.Null(image.Frames.RootFrame.Metadata.IccProfile);
        Assert.Equal(expected, image.Metadata.GetTiffMetadata().EmbeddedIccProfile?.ToByteArray());
        Assert.Equal(expected, image.Frames.RootFrame.Metadata.GetTiffMetadata().EmbeddedIccProfile?.ToByteArray());
    }

    [Theory]
    [InlineData(Cmyk8BitContiguous, 32)]
    [InlineData(Cmyk16BitPlanarBigEndian, 64)]
    public void Identify_Cmyk_ReportsBitsPerPixel(string imagePath, int expectedPixelSize)
    {
        IImageInfo info = Image.Identify(TestFile.Create(imagePath).Bytes);

        Assert.Equal(expectedPixelSize, info.PixelType?.BitsPerPixel);
        Assert.Equal(48, info.Width);
        Assert.Equal(32, info.Height);
    }

    [Theory]
    [WithFile(FlowerRgb888Planar6Strips, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb888Planar15Strips, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_Planar<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba8BitPlanarUnassociatedAlpha, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_Planar_32Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba16BitPlanarUnassociatedAlphaLittleEndian, PixelTypes.Rgba32)]
    [WithFile(Rgba16BitPlanarUnassociatedAlphaBigEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_Planar_64Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba24BitPlanarUnassociatedAlphaLittleEndian, PixelTypes.Rgba32)]
    [WithFile(Rgba24BitPlanarUnassociatedAlphaBigEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_Planar_96Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Note: because the MagickReferenceDecoder fails to load the image, we only debug save them.
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);
    }

    [Theory]
    [WithFile(Rgba32BitPlanarUnassociatedAlphaLittleEndian, PixelTypes.Rgba32)]
    [WithFile(Rgba32BitPlanarUnassociatedAlphaBigEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_Planar_128Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Note: because the MagickReferenceDecoder fails to load the image, we only debug save them.
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);
    }

    [Theory]
    [WithFile(Calliphora_PaletteUncompressed, PixelTypes.Rgba32)]
    [WithFile(PaletteDeflateMultistrip, PixelTypes.Rgba32)]
    [WithFile(RgbPalette, PixelTypes.Rgba32)]
    [WithFile(RgbPaletteDeflate, PixelTypes.Rgba32)]
    [WithFile(PaletteUncompressed, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_WithPalette<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgb4BitPalette, PixelTypes.Rgba32)]
    [WithFile(Flower4BitPalette, PixelTypes.Rgba32)]
    [WithFile(Flower4BitPaletteGray, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_4Bit_WithPalette<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider, ReferenceDecoder, useExactComparer: false, 0.01f);

    [Theory]
    [WithFile(Flower2BitPalette, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_2Bit_WithPalette<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider, ReferenceDecoder, useExactComparer: false, 0.01f);

    [Theory]
    [WithFile(Flower2BitGray, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_2Bit_Gray<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(FlowerRgb222Contiguous, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb222Planar, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_6Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Flower6BitGray, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_6Bit_Gray<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Flower8BitGray, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_8Bit_Gray<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba2BitUnassociatedAlpha, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_8Bit_WithUnassociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(FLowerRgb3Bit, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_9Bit_WithUnassociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Flower10BitGray, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_10Bit_Gray<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(FlowerRgb444Contiguous, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb444Planar, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_12Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Flower12BitGray, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_12Bit_Gray<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba3BitUnassociatedAlpha, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_12Bit_WithUnassociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba3BitAssociatedAlpha, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_12Bit_WithAssociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (TestEnvironment.IsMacOS)
        {
            // Only debug save on OSX: For some reason the reference image has a difference of 50%. The imagesharp output file looks ok though.
            using Image<TPixel> image = provider.GetImage(TiffDecoder);
            image.DebugSave(provider);
            return;
        }

        // Note: Using tolerant comparer here, because there is a small difference to the reference decoder probably due to floating point rounding issues.
        TestTiffDecoder(provider, useExactComparer: false);
    }

    [Theory]
    [WithFile(Flower14BitGray, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_14Bit_Gray<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(FLowerRgb5Bit, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_15Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Flower16BitGrayLittleEndian, PixelTypes.Rgba32)]
    [WithFile(Flower16BitGray, PixelTypes.Rgba32)]
    [WithFile(Flower16BitGrayMinIsWhiteLittleEndian, PixelTypes.Rgba32)]
    [WithFile(Flower16BitGrayMinIsWhiteBigEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_16Bit_Gray<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Flower16BitGrayPredictorBigEndian, PixelTypes.Rgba32)]
    [WithFile(Flower16BitGrayPredictorLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_16Bit_Gray_WithPredictor<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba4BitUnassociatedAlpha, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_16Bit_WithUnassociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(FLowerRgb6Bit, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_18Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba5BitUnassociatedAlpha, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_20Bit_WithUnassociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba5BitAssociatedAlpha, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_20Bit_WithAssociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)

        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (TestEnvironment.IsMacOS)
        {
            // Only debug save on OSX: For some reason the reference image has a difference of 50%. The imagesharp output file looks ok though.
            using Image<TPixel> image = provider.GetImage(TiffDecoder);
            image.DebugSave(provider);
            return;
        }

        // Note: Using tolerant comparer here, because there is a small difference to the reference decoder probably due to floating point rounding issues.
        TestTiffDecoder(provider, useExactComparer: false);
    }

    [Theory]
    [WithFile(FlowerRgb888Contiguous, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_24Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba6BitUnassociatedAlpha, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_24Bit_WithUnassociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba6BitAssociatedAlpha, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_24Bit_WithAssociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (TestEnvironment.IsMacOS)
        {
            // Only debug save on OSX: For some reason the reference image has a difference of 50%. The imagesharp output file looks ok though.
            using Image<TPixel> image = provider.GetImage(TiffDecoder);
            image.DebugSave(provider);
            return;
        }

        // Note: Using tolerant comparer here, because there is a small difference to the reference decoder probably due to floating point rounding issues.
        TestTiffDecoder(provider, useExactComparer: false);
    }

    [Theory]
    [WithFile(Flower24BitGray, PixelTypes.Rgba32)]
    [WithFile(Flower24BitGrayLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_24Bit_Gray<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Note: because the MagickReferenceDecoder fails to load the image, we only debug save them.
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);
    }

    [Theory]
    [WithFile(FlowerYCbCr888Contiguous, PixelTypes.Rgba32)]
    [WithFile(FlowerYCbCr888Planar, PixelTypes.Rgba32)]
    [WithFile(RgbYCbCr888Contiguoush2v2, PixelTypes.Rgba32)]
    [WithFile(RgbYCbCr888Contiguoush4v4, PixelTypes.Rgba32)]
    [WithFile(FlowerYCbCr888Contiguoush2v1, PixelTypes.Rgba32)]
    [WithFile(FlowerYCbCr888Contiguoush2v2, PixelTypes.Rgba32)]
    [WithFile(FlowerYCbCr888Contiguoush4v4, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_YCbCr_24Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Note: The image from MagickReferenceDecoder does not look right, maybe we are doing something wrong
        // converting the pixel data from Magick.NET to our format with YCbCr?
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);

        image.CompareToReferenceOutput(ImageComparer.Exact, provider);
    }

    [Theory]
    [WithFile(FlowerRgb101010Contiguous, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb101010Planar, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_30Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Flower32BitGray, PixelTypes.Rgba32)]
    [WithFile(Flower32BitGrayLittleEndian, PixelTypes.Rgba32)]
    [WithFile(Flower32BitGrayMinIsWhite, PixelTypes.Rgba32)]
    [WithFile(Flower32BitGrayMinIsWhiteLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_32Bit_Gray<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Note: because the MagickReferenceDecoder fails to load the image, we only debug save them.
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);
    }

    [Theory]
    [WithFile(Rgba8BitUnassociatedAlpha, PixelTypes.Rgba32)]
    [WithFile(Rgba8BitUnassociatedAlphaWithPredictor, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_32Bit_WithUnassociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Note: because the MagickReferenceDecoder fails to load the image, we only debug save them.
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);
    }

    [Theory]
    [WithFile(Rgba8BitAssociatedAlpha, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_32Bit_WithAssociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (TestEnvironment.IsMacOS)
        {
            // Only debug save on OSX: For some reason the reference image has a difference of 50%. The imagesharp output file looks ok though.
            using Image<TPixel> image = provider.GetImage(TiffDecoder);
            image.DebugSave(provider);
            return;
        }

        // Note: Using tolerant comparer here, because there is a small difference to the reference decoder probably due to floating point rounding issues.
        TestTiffDecoder(provider, useExactComparer: false, compareTolerance: 0.004F);
    }

    [Theory]
    [WithFile(Flower32BitGrayPredictorBigEndian, PixelTypes.Rgba32)]
    [WithFile(Flower32BitGrayPredictorLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_32Bit_Gray_WithPredictor<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Note: because the MagickReferenceDecoder fails to load the image, we only debug save them.
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);
    }

    [Theory]
    [WithFile(FlowerRgb121212Contiguous, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_36Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba10BitUnassociatedAlphaBigEndian, PixelTypes.Rgba32)]
    [WithFile(Rgba10BitUnassociatedAlphaLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_40Bit_WithUnassociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba10BitAssociatedAlphaBigEndian, PixelTypes.Rgba32)]
    [WithFile(Rgba10BitAssociatedAlphaLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_40Bit_WithAssociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (TestEnvironment.IsMacOS)
        {
            // Only debug save on OSX: For some reason the reference image has a difference of 50%. The imagesharp output file looks ok though.
            using Image<TPixel> image = provider.GetImage(TiffDecoder);
            image.DebugSave(provider);
            return;
        }

        // Note: Using tolerant comparer here, because there is a small difference to the reference decoder probably due to floating point rounding issues.
        TestTiffDecoder(provider, useExactComparer: false);
    }

    [Theory]
    [WithFile(FlowerRgb141414Contiguous, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb141414Planar, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_42Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(FlowerRgb161616Contiguous, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb161616ContiguousLittleEndian, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb161616Planar, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb161616PlanarLittleEndian, PixelTypes.Rgba32)]
    [WithFile(Issues1716Rgb161616BitLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_48Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba12BitUnassociatedAlphaBigEndian, PixelTypes.Rgba32)]
    [WithFile(Rgba12BitUnassociatedAlphaLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_48Bit_WithUnassociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba12BitAssociatedAlphaBigEndian, PixelTypes.Rgba32)]
    [WithFile(Rgba12BitAssociatedAlphaLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_48Bit_WithAssociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (TestEnvironment.IsMacOS)
        {
            // Only debug save on OSX: For some reason the reference image has a difference of 50%. The imagesharp output file looks ok though.
            using Image<TPixel> image = provider.GetImage(TiffDecoder);
            image.DebugSave(provider);
            return;
        }

        // Note: Using tolerant comparer here, because there is a small difference to the reference decoder probably due to floating point rounding issues.
        TestTiffDecoder(provider, useExactComparer: false, compareTolerance: 0.0002f);
    }

    [Theory]
    [WithFile(FlowerRgb161616PredictorBigEndian, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb161616PredictorLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_48Bit_WithPredictor<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba14BitUnassociatedAlphaBigEndian, PixelTypes.Rgba32)]
    [WithFile(Rgba14BitUnassociatedAlphaLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_56Bit_WithUnassociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba14BitAssociatedAlphaBigEndian, PixelTypes.Rgba32)]
    [WithFile(Rgba14BitAssociatedAlphaLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_56Bit_WithAssociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (TestEnvironment.IsMacOS)
        {
            // Only debug save on OSX: For some reason the reference image has a difference of 50%. The imagesharp output file looks ok though.
            using Image<TPixel> image = provider.GetImage(TiffDecoder);
            image.DebugSave(provider);
            return;
        }

        // Note: Using tolerant comparer here, because there is a small difference to the reference decoder probably due to floating point rounding issues.
        TestTiffDecoder(provider, useExactComparer: false, compareTolerance: 0.0002f);
    }

    [Theory]
    [WithFile(FlowerRgb242424Contiguous, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb242424ContiguousLittleEndian, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb242424Planar, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb242424PlanarLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_72Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Note: because the MagickReferenceDecoder fails to load the image, we only debug save them.
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);
    }

    [Theory]
    [WithFile(FlowerRgb323232Contiguous, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb323232ContiguousLittleEndian, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb323232Planar, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb323232PlanarLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_96Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Note: because the MagickReferenceDecoder fails to load the image, we only debug save them.
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);
    }

    [Theory]
    [WithFile(Rgba24BitUnassociatedAlphaBigEndian, PixelTypes.Rgba32)]
    [WithFile(Rgba24BitUnassociatedAlphaLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_96Bit_WithUnassociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Note: because the MagickReferenceDecoder fails to load the image, we only debug save them.
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);
    }

    [Theory]
    [WithFile(FlowerRgbFloat323232, PixelTypes.Rgba32)]
    [WithFile(FlowerRgbFloat323232LittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_Float_96Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Note: because the MagickReferenceDecoder fails to load the image, we only debug save them.
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);
    }

    [Theory]
    [WithFile(FlowerRgb323232PredictorBigEndian, PixelTypes.Rgba32)]
    [WithFile(FlowerRgb323232PredictorLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_96Bit_WithPredictor<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Note: because the MagickReferenceDecoder fails to load the image, we only debug save them.
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);
    }

    [Theory]
    [WithFile(Flower32BitFloatGray, PixelTypes.Rgba32)]
    [WithFile(Flower32BitFloatGrayLittleEndian, PixelTypes.Rgba32)]
    [WithFile(Flower32BitFloatGrayMinIsWhite, PixelTypes.Rgba32)]
    [WithFile(Flower32BitFloatGrayMinIsWhiteLittleEndian, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_Float_96Bit_Gray<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Note: because the MagickReferenceDecoder fails to load the image, we only debug save them.
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);
    }

    [Theory]
    [WithFile(Rgba16BitUnassociatedAlphaBigEndian, PixelTypes.Rgba32)]
    [WithFile(Rgba16BitUnassociatedAlphaLittleEndian, PixelTypes.Rgba32)]
    [WithFile(Rgba16BitUnassociatedAlphaBigEndianWithPredictor, PixelTypes.Rgba32)]
    [WithFile(Rgba16BitUnassociatedAlphaLittleEndianWithPredictor, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_128Bit_UnassociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Rgba32BitUnassociatedAlphaBigEndian, PixelTypes.Rgba32)]
    [WithFile(Rgba32BitUnassociatedAlphaLittleEndian, PixelTypes.Rgba32)]
    [WithFile(Rgba32BitUnassociatedAlphaBigEndianWithPredictor, PixelTypes.Rgba32)]
    [WithFile(Rgba32BitUnassociatedAlphaLittleEndianWithPredictor, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_128Bit_WithUnassociatedAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Note: because the MagickReferenceDecoder fails to load the image, we only debug save them.
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);
    }

    [Theory]
    [WithFile(GrayscaleDeflateMultistrip, PixelTypes.Rgba32)]
    [WithFile(RgbDeflateMultistrip, PixelTypes.Rgba32)]
    [WithFile(Calliphora_GrayscaleDeflate, PixelTypes.Rgba32)]
    [WithFile(Calliphora_GrayscaleDeflate_Predictor, PixelTypes.Rgba32)]
    [WithFile(Calliphora_RgbDeflate_Predictor, PixelTypes.Rgba32)]
    [WithFile(RgbDeflate, PixelTypes.Rgba32)]
    [WithFile(RgbDeflatePredictor, PixelTypes.Rgba32)]
    [WithFile(SmallRgbDeflate, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_DeflateCompressed<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(RgbLzwPredictor, PixelTypes.Rgba32)]
    [WithFile(RgbLzwNoPredictor, PixelTypes.Rgba32)]
    [WithFile(RgbLzwNoPredictorSinglestripMotorola, PixelTypes.Rgba32)]
    [WithFile(RgbLzwNoPredictorMultistripMotorola, PixelTypes.Rgba32)]
    [WithFile(RgbLzwMultistripPredictor, PixelTypes.Rgba32)]
    [WithFile(Calliphora_RgbPaletteLzw_Predictor, PixelTypes.Rgba32)]
    [WithFile(Calliphora_RgbLzwPredictor, PixelTypes.Rgba32)]
    [WithFile(Calliphora_GrayscaleLzw_Predictor, PixelTypes.Rgba32)]
    [WithFile(SmallRgbLzw, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_LzwCompressed<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(HuffmanRleAllTermCodes, PixelTypes.Rgba32)]
    [WithFile(HuffmanRleAllMakeupCodes, PixelTypes.Rgba32)]
    [WithFile(HuffmanRle_basi3p02, PixelTypes.Rgba32)]
    [WithFile(Calliphora_HuffmanCompressed, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_HuffmanCompressed<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(CcittFax3AllTermCodes, PixelTypes.Rgba32)]
    [WithFile(CcittFax3AllMakeupCodes, PixelTypes.Rgba32)]
    [WithFile(Calliphora_Fax3Compressed, PixelTypes.Rgba32)]
    [WithFile(Calliphora_Fax3Compressed_WithEolPadding, PixelTypes.Rgba32)]
    [WithFile(Fax3Uncompressed, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_Fax3Compressed<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Fax4Compressed, PixelTypes.Rgba32)]
    [WithFile(Fax4CompressedLowerOrderBitsFirst, PixelTypes.Rgba32)]
    [WithFile(Calliphora_Fax4Compressed, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_Fax4Compressed<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(CcittFax3LowerOrderBitsFirst, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_Compressed_LowerOrderBitsFirst<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(Calliphora_RgbPackbits, PixelTypes.Rgba32)]
    [WithFile(RgbPackbits, PixelTypes.Rgba32)]
    [WithFile(RgbPackbitsMultistrip, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_PackBitsCompressed<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider);

    [Theory]
    [WithFile(MultiFrameMipMap, PixelTypes.Rgba32)]
    public void CanDecodeJustOneFrame<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(new TiffDecoder() { DecodingMode = FrameDecodingMode.First }))
        {
            Assert.Equal(1, image.Frames.Count);
        }
    }

    [Theory]
    [WithFile(RgbJpegCompressed, PixelTypes.Rgba32)]
    [WithFile(RgbJpegCompressed2, PixelTypes.Rgba32)]
    [WithFile(RgbWithStripsJpegCompressed, PixelTypes.Rgba32)]
    [WithFile(YCbCrJpegCompressed, PixelTypes.Rgba32)]
    [WithFile(YCbCrJpegCompressed2, PixelTypes.Rgba32)]
    [WithFile(RgbJpegCompressedNoJpegTable, PixelTypes.Rgba32)]
    [WithFile(GrayscaleJpegCompressed, PixelTypes.Rgba32)]
    [WithFile(Issues2123, PixelTypes.Rgba32)]
    public void TiffDecoder_CanDecode_JpegCompressed<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffDecoder(provider, useExactComparer: false);

    // https://github.com/SixLabors/ImageSharp/issues/1891
    [Theory]
    [WithFile(Issues1891, PixelTypes.Rgba32)]
    public void TiffDecoder_ThrowsException_WithTooManyDirectories<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => Assert.Throws<ImageFormatException>(
            () =>
            {
                using (provider.GetImage(TiffDecoder))
                {
                }
            });

    [Theory]
    [WithFile(JpegCompressedGray0000539558, PixelTypes.Rgba32)]
    public void TiffDecoder_ThrowsException_WithCircular_IFD_Offsets<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => Assert.Throws<ImageFormatException>(
            () =>
            {
                using (provider.GetImage(TiffDecoder))
                {
                }
            });

    [Theory]
    [WithFileCollection(nameof(MultiframeTestImages), PixelTypes.Rgba32)]
    public void DecodeMultiframe<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage(TiffDecoder);
        Assert.True(image.Frames.Count > 1);

        image.DebugSave(provider);
        image.CompareToOriginal(provider, ImageComparer.Exact, ReferenceDecoder);

        image.DebugSaveMultiFrame(provider);
        image.CompareToOriginalMultiFrame(provider, ImageComparer.Exact, ReferenceDecoder);
    }

    [Theory(Timeout = 30000)]
    [InlineData(SampleMetadata)]
    [InlineData(MultiframeLzwPredictor)]
    [InlineData(MultiframeDeflateWithPreview)]
    [InlineData(Flower4BitPalette)]
    [InlineData(FlowerRgb888Planar6Strips)]
    [InlineData(RgbJpegCompressed)]
    [InlineData(CcittFax3AllTermCodes)]
    [InlineData(TestImages.BigTiff.BigTIFF)]
    [InlineData(TestImages.BigTiff.BigTIFFMotorola)]
    [InlineData(TestImages.BigTiff.BigTIFFSubIFD8)]
    [InlineData(TestImages.BigTiff.Indexed8_LZW)]
    public async Task TiffDecoder_AtNonZeroStreamPosition_MatchesDecodingAtPositionZero(string imagePath)
    {
        byte[] tiff = TestFile.Create(imagePath).Bytes;
        using Image<Rgba32> expected = Image.Load<Rgba32>(tiff);

        using (MemoryStream stream = CreatePrefixedStream(tiff))
        {
            using Image<Rgba32> actual = Image.Load<Rgba32>(stream);
            AssertSameImage(expected, actual);
        }

        using (MemoryStream stream = CreatePrefixedStream(tiff))
        {
            using Image<Rgba32> actual = await Image.LoadAsync<Rgba32>(stream, TestContext.Current.CancellationToken);
            AssertSameImage(expected, actual);
        }

        using (MemoryStream stream = CreatePrefixedStream(tiff))
        {
            IImageInfo info = Image.Identify(stream);
            AssertSameInfo(expected, info);
        }

        using (MemoryStream stream = CreatePrefixedStream(tiff))
        {
            IImageInfo info = await Image.IdentifyAsync(stream, TestContext.Current.CancellationToken);
            AssertSameInfo(expected, info);
        }
    }

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TiffDecoder_LoadsTiffSavedAfterOtherDataInSameStream(bool async)
    {
        using Image<Rgba32> expected = Image.Load<Rgba32>(TestFile.Create(MultiframeLzwPredictor).Bytes);
        using var stream = new MemoryStream();
        stream.Write(StreamPrefix);
        expected.Save(stream, new TiffEncoder());
        stream.Position = StreamPrefix.Length;

        using Image<Rgba32> actual = async
            ? await Image.LoadAsync<Rgba32>(stream, TestContext.Current.CancellationToken)
            : Image.Load<Rgba32>(stream);

        Assert.True(actual.Frames.Count > 1);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    /// <summary>
    /// Creates a stream holding other data before the TIFF, positioned at the start of the TIFF.
    /// </summary>
    private static MemoryStream CreatePrefixedStream(byte[] tiff)
    {
        var stream = new MemoryStream();
        stream.Write(StreamPrefix);
        stream.Write(tiff);
        stream.Position = StreamPrefix.Length;
        return stream;
    }

    private static void AssertSameImage(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        ImageComparer.Exact.VerifySimilarity(expected, actual);
        AssertSameMetadata(expected.Metadata, actual.Metadata);
        for (int i = 0; i < expected.Frames.Count; i++)
        {
            ImageFrameMetadata expectedFrame = expected.Frames[i].Metadata;
            ImageFrameMetadata actualFrame = actual.Frames[i].Metadata;
            AssertSameExif(expectedFrame.ExifProfile, actualFrame.ExifProfile);
            Assert.Equal(expectedFrame.XmpProfile?.ToByteArray(), actualFrame.XmpProfile?.ToByteArray());
            Assert.Equal(expectedFrame.IptcProfile?.Data, actualFrame.IptcProfile?.Data);
            Assert.Equal(expectedFrame.IccProfile?.ToByteArray(), actualFrame.IccProfile?.ToByteArray());
        }
    }

    private static void AssertSameInfo(Image<Rgba32> expected, IImageInfo actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        AssertSameMetadata(expected.Metadata, actual.Metadata);
    }

    private static void AssertSameMetadata(ImageMetadata expected, ImageMetadata actual)
    {
        Assert.Equal(expected.HorizontalResolution, actual.HorizontalResolution);
        Assert.Equal(expected.VerticalResolution, actual.VerticalResolution);
        Assert.Equal(expected.ResolutionUnits, actual.ResolutionUnits);
        Assert.Equal(expected.GetTiffMetadata().ByteOrder, actual.GetTiffMetadata().ByteOrder);
        Assert.Equal(expected.GetTiffMetadata().FormatType, actual.GetTiffMetadata().FormatType);
        AssertSameExif(expected.ExifProfile, actual.ExifProfile);
        Assert.Equal(expected.XmpProfile?.ToByteArray(), actual.XmpProfile?.ToByteArray());
        Assert.Equal(expected.IptcProfile?.Data, actual.IptcProfile?.Data);
        Assert.Equal(expected.IccProfile?.ToByteArray(), actual.IccProfile?.ToByteArray());
    }

    // Compares the values one by one: ExifProfile.ToByteArray cannot write the 64-bit values of a BigTIFF.
    private static void AssertSameExif(ExifProfile expected, ExifProfile actual)
    {
        Assert.Equal(expected is null, actual is null);
        if (expected is null)
        {
            return;
        }

        Assert.Equal(expected.Values.Count, actual.Values.Count);
        for (int i = 0; i < expected.Values.Count; i++)
        {
            IExifValue expectedValue = expected.Values[i];
            IExifValue actualValue = actual.Values[i];
            Assert.Equal(expectedValue.Tag, actualValue.Tag);
            Assert.Equal(expectedValue.DataType, actualValue.DataType);
            Assert.Equal(expectedValue.GetValue(), actualValue.GetValue());
        }
    }
}
