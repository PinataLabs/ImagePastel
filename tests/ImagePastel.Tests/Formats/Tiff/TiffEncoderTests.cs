// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Tiff.Constants;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Dithering;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;

using Xunit;
using static SixLabors.ImageSharp.Tests.TestImages.Tiff;

namespace SixLabors.ImageSharp.Tests.Formats.Tiff;

[Trait("Format", "Tiff")]
public class TiffEncoderTests : TiffEncoderBaseTester
{
    /// <summary>
    /// Data already in the stream before the TIFF is written. The odd length keeps the TIFF off a word boundary.
    /// </summary>
    private static readonly byte[] StreamPrefix = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 };

    /// <summary>
    /// The field types TIFF 6.0 allows for the baseline and extension tags the encoder can write.
    /// </summary>
    private static readonly Dictionary<ExifTagValue, ExifDataType[]> BaselineFieldTypes = new()
    {
        { ExifTagValue.SubfileType, new[] { ExifDataType.Long } },
        { ExifTagValue.OldSubfileType, new[] { ExifDataType.Short } },
        { ExifTagValue.ImageWidth, new[] { ExifDataType.Short, ExifDataType.Long } },
        { ExifTagValue.ImageLength, new[] { ExifDataType.Short, ExifDataType.Long } },
        { ExifTagValue.BitsPerSample, new[] { ExifDataType.Short } },
        { ExifTagValue.Compression, new[] { ExifDataType.Short } },
        { ExifTagValue.PhotometricInterpretation, new[] { ExifDataType.Short } },
        { ExifTagValue.Thresholding, new[] { ExifDataType.Short } },
        { ExifTagValue.FillOrder, new[] { ExifDataType.Short } },
        { ExifTagValue.DocumentName, new[] { ExifDataType.Ascii } },
        { ExifTagValue.ImageDescription, new[] { ExifDataType.Ascii } },
        { ExifTagValue.Make, new[] { ExifDataType.Ascii } },
        { ExifTagValue.Model, new[] { ExifDataType.Ascii } },
        { ExifTagValue.StripOffsets, new[] { ExifDataType.Short, ExifDataType.Long } },
        { ExifTagValue.Orientation, new[] { ExifDataType.Short } },
        { ExifTagValue.SamplesPerPixel, new[] { ExifDataType.Short } },
        { ExifTagValue.RowsPerStrip, new[] { ExifDataType.Short, ExifDataType.Long } },
        { ExifTagValue.StripByteCounts, new[] { ExifDataType.Short, ExifDataType.Long } },
        { ExifTagValue.XResolution, new[] { ExifDataType.Rational } },
        { ExifTagValue.YResolution, new[] { ExifDataType.Rational } },
        { ExifTagValue.PlanarConfiguration, new[] { ExifDataType.Short } },
        { ExifTagValue.ResolutionUnit, new[] { ExifDataType.Short } },
        { ExifTagValue.Software, new[] { ExifDataType.Ascii } },
        { ExifTagValue.DateTime, new[] { ExifDataType.Ascii } },
        { ExifTagValue.Artist, new[] { ExifDataType.Ascii } },
        { ExifTagValue.HostComputer, new[] { ExifDataType.Ascii } },
        { ExifTagValue.Predictor, new[] { ExifDataType.Short } },
        { ExifTagValue.ColorMap, new[] { ExifDataType.Short } },
        { ExifTagValue.ExtraSamples, new[] { ExifDataType.Short } },
    };

    [Theory]
    [InlineData(null, TiffBitsPerPixel.Bit24)]
    [InlineData(TiffPhotometricInterpretation.Rgb, TiffBitsPerPixel.Bit24)]
    [InlineData(TiffPhotometricInterpretation.PaletteColor, TiffBitsPerPixel.Bit8)]
    [InlineData(TiffPhotometricInterpretation.BlackIsZero, TiffBitsPerPixel.Bit8)]
    [InlineData(TiffPhotometricInterpretation.WhiteIsZero, TiffBitsPerPixel.Bit8)]
    //// Unsupported TiffPhotometricInterpretation should default to 24 bits
    [InlineData(TiffPhotometricInterpretation.CieLab, TiffBitsPerPixel.Bit24)]
    [InlineData(TiffPhotometricInterpretation.ColorFilterArray, TiffBitsPerPixel.Bit24)]
    [InlineData(TiffPhotometricInterpretation.ItuLab, TiffBitsPerPixel.Bit24)]
    [InlineData(TiffPhotometricInterpretation.LinearRaw, TiffBitsPerPixel.Bit24)]
    [InlineData(TiffPhotometricInterpretation.Separated, TiffBitsPerPixel.Bit24)]
    [InlineData(TiffPhotometricInterpretation.TransparencyMask, TiffBitsPerPixel.Bit24)]
    public void EncoderOptions_SetPhotometricInterpretation_Works(TiffPhotometricInterpretation? photometricInterpretation, TiffBitsPerPixel expectedBitsPerPixel)
    {
        // arrange
        var tiffEncoder = new TiffEncoder { PhotometricInterpretation = photometricInterpretation };
        using Image input = new Image<Rgb24>(10, 10);
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, tiffEncoder);

        // assert
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        TiffFrameMetadata frameMetaData = output.Frames.RootFrame.Metadata.GetTiffMetadata();
        Assert.Equal(expectedBitsPerPixel, frameMetaData.BitsPerPixel);
        Assert.Equal(TiffCompression.None, frameMetaData.Compression);
    }

    [Theory]
    [InlineData(TiffBitsPerPixel.Bit24)]
    [InlineData(TiffBitsPerPixel.Bit8)]
    [InlineData(TiffBitsPerPixel.Bit4)]
    [InlineData(TiffBitsPerPixel.Bit1)]
    public void EncoderOptions_SetBitPerPixel_Works(TiffBitsPerPixel bitsPerPixel)
    {
        // arrange
        var tiffEncoder = new TiffEncoder { BitsPerPixel = bitsPerPixel };
        using Image input = new Image<Rgb24>(10, 10);
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, tiffEncoder);

        // assert
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);

        TiffFrameMetadata frameMetaData = output.Frames.RootFrame.Metadata.GetTiffMetadata();
        Assert.Equal(bitsPerPixel, frameMetaData.BitsPerPixel);
        Assert.Equal(TiffCompression.None, frameMetaData.Compression);
    }

    [Theory]
    [InlineData(TiffBitsPerPixel.Bit48)]
    [InlineData(TiffBitsPerPixel.Bit42)]
    [InlineData(TiffBitsPerPixel.Bit36)]
    [InlineData(TiffBitsPerPixel.Bit30)]
    [InlineData(TiffBitsPerPixel.Bit12)]
    [InlineData(TiffBitsPerPixel.Bit10)]
    [InlineData(TiffBitsPerPixel.Bit6)]
    public void EncoderOptions_UnsupportedBitPerPixel_DefaultTo24Bits(TiffBitsPerPixel bitsPerPixel)
    {
        // arrange
        var tiffEncoder = new TiffEncoder { BitsPerPixel = bitsPerPixel };
        using Image input = new Image<Rgb24>(10, 10);
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, tiffEncoder);

        // assert
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);

        TiffFrameMetadata frameMetaData = output.Frames.RootFrame.Metadata.GetTiffMetadata();
        Assert.Equal(TiffBitsPerPixel.Bit24, frameMetaData.BitsPerPixel);
    }

    [Theory]
    [InlineData(null, TiffCompression.Deflate, TiffBitsPerPixel.Bit24, TiffCompression.Deflate)]
    [InlineData(TiffPhotometricInterpretation.Rgb, TiffCompression.Deflate, TiffBitsPerPixel.Bit24, TiffCompression.Deflate)]
    [InlineData(TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Deflate, TiffBitsPerPixel.Bit8, TiffCompression.Deflate)]
    [InlineData(TiffPhotometricInterpretation.PaletteColor, TiffCompression.Deflate, TiffBitsPerPixel.Bit8, TiffCompression.Deflate)]
    [InlineData(null, TiffCompression.PackBits, TiffBitsPerPixel.Bit24, TiffCompression.PackBits)]
    [InlineData(TiffPhotometricInterpretation.Rgb, TiffCompression.PackBits, TiffBitsPerPixel.Bit24, TiffCompression.PackBits)]
    [InlineData(TiffPhotometricInterpretation.PaletteColor, TiffCompression.PackBits, TiffBitsPerPixel.Bit8, TiffCompression.PackBits)]
    [InlineData(TiffPhotometricInterpretation.BlackIsZero, TiffCompression.PackBits, TiffBitsPerPixel.Bit8, TiffCompression.PackBits)]
    [InlineData(null, TiffCompression.Lzw, TiffBitsPerPixel.Bit24, TiffCompression.Lzw)]
    [InlineData(TiffPhotometricInterpretation.Rgb, TiffCompression.Lzw, TiffBitsPerPixel.Bit24, TiffCompression.Lzw)]
    [InlineData(TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Lzw, TiffBitsPerPixel.Bit8, TiffCompression.Lzw)]
    [InlineData(TiffPhotometricInterpretation.PaletteColor, TiffCompression.Lzw, TiffBitsPerPixel.Bit8, TiffCompression.Lzw)]
    [InlineData(TiffPhotometricInterpretation.BlackIsZero, TiffCompression.CcittGroup3Fax, TiffBitsPerPixel.Bit1, TiffCompression.CcittGroup3Fax)]
    [InlineData(TiffPhotometricInterpretation.BlackIsZero, TiffCompression.CcittGroup4Fax, TiffBitsPerPixel.Bit1, TiffCompression.CcittGroup4Fax)]
    [InlineData(TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Ccitt1D, TiffBitsPerPixel.Bit1, TiffCompression.Ccitt1D)]
    [InlineData(TiffPhotometricInterpretation.Rgb, TiffCompression.ItuTRecT43, TiffBitsPerPixel.Bit24, TiffCompression.None)]
    [InlineData(TiffPhotometricInterpretation.Rgb, TiffCompression.ItuTRecT82, TiffBitsPerPixel.Bit24, TiffCompression.None)]
    [InlineData(TiffPhotometricInterpretation.Rgb, TiffCompression.Jpeg, TiffBitsPerPixel.Bit24, TiffCompression.Jpeg)]
    [InlineData(TiffPhotometricInterpretation.Rgb, TiffCompression.OldDeflate, TiffBitsPerPixel.Bit24, TiffCompression.None)]
    [InlineData(TiffPhotometricInterpretation.Rgb, TiffCompression.OldJpeg, TiffBitsPerPixel.Bit24, TiffCompression.None)]
    public void EncoderOptions_SetPhotometricInterpretationAndCompression_Works(
        TiffPhotometricInterpretation? photometricInterpretation,
        TiffCompression compression,
        TiffBitsPerPixel expectedBitsPerPixel,
        TiffCompression expectedCompression)
    {
        // arrange
        var tiffEncoder = new TiffEncoder { PhotometricInterpretation = photometricInterpretation, Compression = compression };
        using Image input = new Image<Rgb24>(10, 10);
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, tiffEncoder);

        // assert
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        TiffFrameMetadata rootFrameMetaData = output.Frames.RootFrame.Metadata.GetTiffMetadata();
        Assert.Equal(expectedBitsPerPixel, rootFrameMetaData.BitsPerPixel);
        Assert.Equal(expectedCompression, rootFrameMetaData.Compression);
    }

    [Theory]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.Rgba32, TiffBitsPerPixel.Bit1)]
    [WithFile(GrayscaleUncompressed, PixelTypes.Rgba32, TiffBitsPerPixel.Bit8)]
    [WithFile(RgbUncompressed, PixelTypes.Rgba32, TiffBitsPerPixel.Bit24)]
    [WithFile(Rgb4BitPalette, PixelTypes.Rgba32, TiffBitsPerPixel.Bit4)]
    [WithFile(RgbPalette, PixelTypes.Rgba32, TiffBitsPerPixel.Bit8)]
    [WithFile(Calliphora_PaletteUncompressed, PixelTypes.Rgba32, TiffBitsPerPixel.Bit8)]
    public void TiffEncoder_PreservesBitsPerPixel<TPixel>(TestImageProvider<TPixel> provider, TiffBitsPerPixel expectedBitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // arrange
        var tiffEncoder = new TiffEncoder();
        using Image<TPixel> input = provider.GetImage();
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, tiffEncoder);

        // assert
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        TiffFrameMetadata frameMetaData = output.Frames.RootFrame.Metadata.GetTiffMetadata();
        Assert.Equal(expectedBitsPerPixel, frameMetaData.BitsPerPixel);
    }

    [Fact]
    public void TiffEncoder_PreservesBitsPerPixel_WhenInputIsL8()
    {
        // arrange
        var tiffEncoder = new TiffEncoder();
        using Image input = new Image<L8>(10, 10);
        using var memStream = new MemoryStream();
        TiffBitsPerPixel expectedBitsPerPixel = TiffBitsPerPixel.Bit8;

        // act
        input.Save(memStream, tiffEncoder);

        // assert
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        TiffFrameMetadata frameMetaData = output.Frames.RootFrame.Metadata.GetTiffMetadata();
        Assert.Equal(expectedBitsPerPixel, frameMetaData.BitsPerPixel);
    }

    [Theory]
    [WithFile(RgbUncompressed, PixelTypes.Rgba32, TiffCompression.None)]
    [WithFile(RgbLzwNoPredictor, PixelTypes.Rgba32, TiffCompression.Lzw)]
    [WithFile(RgbDeflate, PixelTypes.Rgba32, TiffCompression.Deflate)]
    [WithFile(RgbPackbits, PixelTypes.Rgba32, TiffCompression.PackBits)]
    public void TiffEncoder_PreservesCompression<TPixel>(TestImageProvider<TPixel> provider, TiffCompression expectedCompression)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // arrange
        var tiffEncoder = new TiffEncoder();
        using Image<TPixel> input = provider.GetImage();
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, tiffEncoder);

        // assert
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        Assert.Equal(expectedCompression, output.Frames.RootFrame.Metadata.GetTiffMetadata().Compression);
    }

    [Theory]
    [WithFile(RgbLzwNoPredictor, PixelTypes.Rgba32, null)]
    [WithFile(RgbLzwPredictor, PixelTypes.Rgba32, TiffPredictor.Horizontal)]
    [WithFile(RgbDeflate, PixelTypes.Rgba32, null)]
    [WithFile(RgbDeflatePredictor, PixelTypes.Rgba32, TiffPredictor.Horizontal)]
    public void TiffEncoder_PreservesPredictor<TPixel>(TestImageProvider<TPixel> provider, TiffPredictor? expectedPredictor)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // arrange
        var tiffEncoder = new TiffEncoder();
        using Image<TPixel> input = provider.GetImage();
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, tiffEncoder);

        // assert
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        TiffFrameMetadata frameMetadata = output.Frames.RootFrame.Metadata.GetTiffMetadata();
        Assert.Equal(expectedPredictor, frameMetadata.Predictor);
    }

    [Theory]
    [WithFile(RgbLzwPredictor, PixelTypes.Rgba32, TiffCompression.None)]
    [WithFile(RgbLzwPredictor, PixelTypes.Rgba32, TiffCompression.PackBits)]
    [WithFile(RgbDeflatePredictor, PixelTypes.Rgba32, TiffCompression.Jpeg)]
    public void TiffEncoder_PredictorFromMetadata_IsNotWrittenForOtherCompression<TPixel>(TestImageProvider<TPixel> provider, TiffCompression compression)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // arrange
        using Image<TPixel> input = provider.GetImage();
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, new TiffEncoder { Compression = compression });

        // assert
        byte[] tiff = memStream.ToArray();
        Assert.Equal((int)compression, ReadFirstIfdValue(tiff, ExifTagValue.Compression));
        Assert.False(ReadFirstIfdFieldTypes(tiff).ContainsKey(ExifTagValue.Predictor), "The strips are not predicted, but a Predictor tag is written.");
    }

    [Theory]
    [WithFile(RgbUncompressed, PixelTypes.Rgba32, TiffCompression.CcittGroup3Fax, TiffCompression.CcittGroup3Fax)]
    [WithFile(RgbUncompressed, PixelTypes.Rgba32, TiffCompression.CcittGroup4Fax, TiffCompression.CcittGroup4Fax)]
    [WithFile(RgbUncompressed, PixelTypes.Rgba32, TiffCompression.Ccitt1D, TiffCompression.Ccitt1D)]
    [WithFile(GrayscaleUncompressed, PixelTypes.L8, TiffCompression.CcittGroup3Fax, TiffCompression.CcittGroup3Fax)]
    [WithFile(GrayscaleUncompressed, PixelTypes.L8, TiffCompression.CcittGroup4Fax, TiffCompression.CcittGroup4Fax)]
    [WithFile(PaletteDeflateMultistrip, PixelTypes.L8, TiffCompression.Ccitt1D, TiffCompression.Ccitt1D)]
    public void TiffEncoder_EncodesWithCorrectBiColorModeCompression<TPixel>(TestImageProvider<TPixel> provider, TiffCompression compression, TiffCompression expectedCompression)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // arrange
        var encoder = new TiffEncoder() { Compression = compression, BitsPerPixel = TiffBitsPerPixel.Bit1 };
        using Image<TPixel> input = provider.GetImage();
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, encoder);

        // assert
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        TiffFrameMetadata frameMetaData = output.Frames.RootFrame.Metadata.GetTiffMetadata();
        Assert.Equal(TiffBitsPerPixel.Bit1, frameMetaData.BitsPerPixel);
        Assert.Equal(expectedCompression, frameMetaData.Compression);
    }

    [Theory]
    [InlineData(TiffCompression.Ccitt1D)]
    [InlineData(TiffCompression.CcittGroup3Fax)]
    [InlineData(TiffCompression.CcittGroup4Fax)]
    public void TiffEncoder_CcittCompression_WithoutBitsPerPixel_EncodesBiColor(TiffCompression compression)
    {
        // arrange
        var encoder = new TiffEncoder { Compression = compression };
        using Image<Rgba32> input = CreateBlackDiagonalOnWhite(64, 32);
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, encoder);

        // assert
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        TiffFrameMetadata frameMetaData = output.Frames.RootFrame.Metadata.GetTiffMetadata();
        Assert.Equal(TiffBitsPerPixel.Bit1, frameMetaData.BitsPerPixel);
        Assert.Equal(TiffPhotometricInterpretation.WhiteIsZero, frameMetaData.PhotometricInterpretation);
        Assert.Equal(compression, frameMetaData.Compression);
        AssertBlackDiagonalOnWhite(output);
    }

    [Theory]
    [InlineData(TiffCompression.Ccitt1D)]
    [InlineData(TiffCompression.CcittGroup3Fax)]
    [InlineData(TiffCompression.CcittGroup4Fax)]
    public void TiffEncoder_CcittCompression_WithoutBitsPerPixel_MatchesUncompressedBiColor(TiffCompression compression)
    {
        // arrange
        using Image<Rgba32> input = CreateRandomColorImage(400, 300);
        using var expectedStream = new MemoryStream();
        input.Save(expectedStream, new TiffEncoder { BitsPerPixel = TiffBitsPerPixel.Bit1 });
        using var actualStream = new MemoryStream();

        // act
        input.Save(actualStream, new TiffEncoder { Compression = compression });

        // assert
        expectedStream.Position = 0;
        actualStream.Position = 0;
        using var expected = Image.Load<Rgba32>(expectedStream);
        using var actual = Image.Load<Rgba32>(actualStream);
        Assert.Equal(TiffBitsPerPixel.Bit1, actual.Frames.RootFrame.Metadata.GetTiffMetadata().BitsPerPixel);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    // The CCITT encoders wrote into a buffer of one byte per pixel, which a narrow image outgrows: the codes
    // for one row cost more than the row's width in bytes (GHSA-j9gm-c75j-xc9q, GHSA-jjfr-hcj7-qf5w).
    [Theory]
    [InlineData(TiffCompression.CcittGroup3Fax, 1, 2000, false)]
    [InlineData(TiffCompression.CcittGroup3Fax, 1, 1, false)]
    [InlineData(TiffCompression.CcittGroup3Fax, 1, 1, true)]
    [InlineData(TiffCompression.CcittGroup3Fax, 2, 1000, true)]
    [InlineData(TiffCompression.CcittGroup3Fax, 3, 700, false)]
    [InlineData(TiffCompression.CcittGroup4Fax, 1, 2000, false)]
    [InlineData(TiffCompression.CcittGroup4Fax, 1, 1, false)]
    [InlineData(TiffCompression.CcittGroup4Fax, 1, 1, true)]
    [InlineData(TiffCompression.CcittGroup4Fax, 2, 1000, true)]
    [InlineData(TiffCompression.CcittGroup4Fax, 3, 700, false)]
    [InlineData(TiffCompression.Ccitt1D, 1, 2000, false)]
    [InlineData(TiffCompression.Ccitt1D, 1, 1, false)]
    [InlineData(TiffCompression.Ccitt1D, 2, 1000, true)]
    public void TiffEncoder_CcittCompression_NarrowImage_RoundTrips(TiffCompression compression, int width, int height, bool startBlack)
    {
        // Alternate black and white along every row and every column, the most codes an image can need.
        using var input = new Image<L8>(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool black = ((x + y) % 2 == 0) == startBlack;
                input[x, y] = new L8(black ? (byte)0 : (byte)255);
            }
        }

        using var encoded = new MemoryStream();
        input.Save(encoded, new TiffEncoder { BitsPerPixel = TiffBitsPerPixel.Bit1, Compression = compression });
        encoded.Position = 0;
        using var decoded = Image.Load<L8>(encoded);
        Assert.Equal(compression, decoded.Frames.RootFrame.Metadata.GetTiffMetadata().Compression);
        ImageComparer.Exact.VerifySimilarity(input, decoded);

        // Saving a decoded image with the default encoder keeps its compression and bit depth.
        using var reencoded = new MemoryStream();
        decoded.Save(reencoded, new TiffEncoder());
        reencoded.Position = 0;
        using var redecoded = Image.Load<L8>(reencoded);
        Assert.Equal(compression, redecoded.Frames.RootFrame.Metadata.GetTiffMetadata().Compression);
        ImageComparer.Exact.VerifySimilarity(input, redecoded);
    }

    [Theory]
    [InlineData(TiffCompression.Ccitt1D, TiffBitsPerPixel.Bit4, null)]
    [InlineData(TiffCompression.Ccitt1D, TiffBitsPerPixel.Bit8, null)]
    [InlineData(TiffCompression.Ccitt1D, TiffBitsPerPixel.Bit24, null)]
    [InlineData(TiffCompression.Ccitt1D, TiffBitsPerPixel.Bit16, null)]
    [InlineData(TiffCompression.Ccitt1D, null, TiffPhotometricInterpretation.Rgb)]
    [InlineData(TiffCompression.Ccitt1D, null, TiffPhotometricInterpretation.PaletteColor)]
    [InlineData(TiffCompression.Ccitt1D, null, TiffPhotometricInterpretation.WhiteIsZero)]
    [InlineData(TiffCompression.CcittGroup3Fax, TiffBitsPerPixel.Bit4, null)]
    [InlineData(TiffCompression.CcittGroup3Fax, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero)]
    [InlineData(TiffCompression.CcittGroup3Fax, TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb)]
    [InlineData(TiffCompression.CcittGroup3Fax, null, TiffPhotometricInterpretation.Rgb)]
    [InlineData(TiffCompression.CcittGroup4Fax, TiffBitsPerPixel.Bit8, null)]
    [InlineData(TiffCompression.CcittGroup4Fax, TiffBitsPerPixel.Bit24, null)]
    [InlineData(TiffCompression.CcittGroup4Fax, TiffBitsPerPixel.Bit48, null)]
    [InlineData(TiffCompression.CcittGroup4Fax, null, TiffPhotometricInterpretation.PaletteColor)]
    [InlineData(TiffCompression.CcittGroup4Fax, null, TiffPhotometricInterpretation.BlackIsZero)]
    public void TiffEncoder_CcittCompression_WithOtherBitDepthOrPhotometric_EncodesBiColor(
        TiffCompression compression,
        TiffBitsPerPixel? bitsPerPixel,
        TiffPhotometricInterpretation? photometricInterpretation)
    {
        // arrange
        var encoder = new TiffEncoder
        {
            Compression = compression,
            BitsPerPixel = bitsPerPixel,
            PhotometricInterpretation = photometricInterpretation
        };
        using Image<Rgba32> input = CreateBlackDiagonalOnWhite(64, 32);
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, encoder);

        // assert
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        TiffFrameMetadata frameMetaData = output.Frames.RootFrame.Metadata.GetTiffMetadata();
        Assert.Equal(TiffBitsPerPixel.Bit1, frameMetaData.BitsPerPixel);
        Assert.Equal(TiffPhotometricInterpretation.WhiteIsZero, frameMetaData.PhotometricInterpretation);
        Assert.Equal(compression, frameMetaData.Compression);
        AssertBlackDiagonalOnWhite(output);
    }

    [Theory]
    [InlineData(TiffBitsPerPixel.Bit8)]
    [InlineData(TiffBitsPerPixel.Bit24)]
    public void TiffEncoder_CcittCompressionFromMetadata_WithExplicitBitsPerPixel_DropsCompression(TiffBitsPerPixel bitsPerPixel)
    {
        // arrange
        using Image<Rgba32> source = CreateBlackDiagonalOnWhite(64, 32);
        using var ccittStream = new MemoryStream();
        source.Save(ccittStream, new TiffEncoder { Compression = TiffCompression.CcittGroup4Fax });
        ccittStream.Position = 0;
        using var input = Image.Load<Rgba32>(ccittStream);
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, new TiffEncoder { BitsPerPixel = bitsPerPixel });

        // assert
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        TiffFrameMetadata frameMetaData = output.Frames.RootFrame.Metadata.GetTiffMetadata();
        Assert.Equal(bitsPerPixel, frameMetaData.BitsPerPixel);
        Assert.Equal(TiffCompression.None, frameMetaData.Compression);
        AssertBlackDiagonalOnWhite(output);
    }

    [Theory]
    [InlineData(TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.None)]
    [InlineData(TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.Lzw)]
    [InlineData(TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.Deflate)]
    [InlineData(TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.PackBits)]
    [InlineData(TiffPhotometricInterpretation.BlackIsZero, TiffCompression.None)]
    [InlineData(TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Lzw)]
    public void TiffEncoder_Gray8_RoundTripsWithPhotometricInterpretation(TiffPhotometricInterpretation photometricInterpretation, TiffCompression compression)
    {
        // arrange
        using Image<Rgba32> input = CreateBlackDiagonalOnWhite(16, 8);
        using var memStream = new MemoryStream();
        var encoder = new TiffEncoder
        {
            BitsPerPixel = TiffBitsPerPixel.Bit8,
            PhotometricInterpretation = photometricInterpretation,
            Compression = compression,
            HorizontalPredictor = compression is TiffCompression.Lzw or TiffCompression.Deflate ? TiffPredictor.Horizontal : null
        };

        // act
        input.Save(memStream, encoder);

        // assert
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        Assert.Equal(photometricInterpretation, output.Frames.RootFrame.Metadata.GetTiffMetadata().PhotometricInterpretation);
        AssertBlackDiagonalOnWhite(output);
    }

    [Theory]
    [InlineData(TiffPhotometricInterpretation.WhiteIsZero, 0x00)]
    [InlineData(TiffPhotometricInterpretation.BlackIsZero, 0xFF)]
    public void TiffEncoder_Gray8_WritesWhiteSampleForPhotometricInterpretation(TiffPhotometricInterpretation photometricInterpretation, byte expectedWhiteSample)
    {
        // arrange
        using var input = new Image<Rgba32>(4, 1, Color.White);
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, new TiffEncoder { BitsPerPixel = TiffBitsPerPixel.Bit8, PhotometricInterpretation = photometricInterpretation, Compression = TiffCompression.None });

        // assert
        byte[] tiff = memStream.ToArray();
        int stripOffset = ReadFirstIfdValue(tiff, ExifTagValue.StripOffsets);
        Assert.Equal(new[] { expectedWhiteSample, expectedWhiteSample, expectedWhiteSample, expectedWhiteSample }, tiff.AsSpan(stripOffset, 4).ToArray());
    }

    [Theory]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.None, null, false)]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.PackBits, null, false)]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Deflate, null, false)]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Lzw, null, false)]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Lzw, TiffPredictor.Horizontal, false)]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.None, null, false)]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.PackBits, null, false)]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.Deflate, null, false)]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.Lzw, null, false)]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.Ccitt1D, null, false)]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.CcittGroup3Fax, null, false)]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.CcittGroup4Fax, null, false)]
    [InlineData(TiffBitsPerPixel.Bit4, TiffPhotometricInterpretation.PaletteColor, TiffCompression.None, null, false)]
    [InlineData(TiffBitsPerPixel.Bit4, TiffPhotometricInterpretation.PaletteColor, TiffCompression.PackBits, null, false)]
    [InlineData(TiffBitsPerPixel.Bit4, TiffPhotometricInterpretation.PaletteColor, TiffCompression.Deflate, null, false)]
    [InlineData(TiffBitsPerPixel.Bit4, TiffPhotometricInterpretation.PaletteColor, TiffCompression.Lzw, null, false)]
    [InlineData(TiffBitsPerPixel.Bit4, TiffPhotometricInterpretation.PaletteColor, TiffCompression.Lzw, TiffPredictor.Horizontal, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.None, null, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.None, TiffPredictor.Horizontal, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.PackBits, null, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Deflate, null, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Deflate, TiffPredictor.Horizontal, true)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Lzw, null, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Lzw, TiffPredictor.Horizontal, true)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Jpeg, null, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.None, null, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.PackBits, null, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.PackBits, TiffPredictor.Horizontal, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.Deflate, null, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.Deflate, TiffPredictor.Horizontal, true)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.Lzw, null, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.Lzw, TiffPredictor.Horizontal, true)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.Jpeg, null, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor, TiffCompression.None, null, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor, TiffCompression.PackBits, null, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor, TiffCompression.Deflate, null, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor, TiffCompression.Deflate, TiffPredictor.Horizontal, true)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor, TiffCompression.Lzw, null, false)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor, TiffCompression.Lzw, TiffPredictor.Horizontal, true)]
    [InlineData(TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.None, null, false)]
    [InlineData(TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.None, TiffPredictor.Horizontal, false)]
    [InlineData(TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.PackBits, null, false)]
    [InlineData(TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.Deflate, null, false)]
    [InlineData(TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.Deflate, TiffPredictor.Horizontal, true)]
    [InlineData(TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.Lzw, null, false)]
    [InlineData(TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.Lzw, TiffPredictor.Horizontal, true)]
    [InlineData(TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.Jpeg, null, false)]
    [InlineData(TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.Jpeg, TiffPredictor.Horizontal, false)]
    public void TiffEncoder_CompressionAndPredictorTagsMatchStripData(
        TiffBitsPerPixel bitsPerPixel,
        TiffPhotometricInterpretation photometricInterpretation,
        TiffCompression compression,
        TiffPredictor? predictor,
        bool expectPredictorTag)
    {
        // arrange
        using Image<Rgba32> input = CreateRoundTripInput(bitsPerPixel, photometricInterpretation);
        using var memStream = new MemoryStream();
        var encoder = new TiffEncoder
        {
            BitsPerPixel = bitsPerPixel,
            PhotometricInterpretation = photometricInterpretation,
            Compression = compression,
            HorizontalPredictor = predictor
        };

        // act
        input.Save(memStream, encoder);

        // assert
        byte[] tiff = memStream.ToArray();
        Assert.Equal((int)compression, ReadFirstIfdValue(tiff, ExifTagValue.Compression));
        if (expectPredictorTag)
        {
            Assert.Equal((int)TiffPredictor.Horizontal, ReadFirstIfdValue(tiff, ExifTagValue.Predictor));
        }
        else
        {
            Assert.False(ReadFirstIfdFieldTypes(tiff).ContainsKey(ExifTagValue.Predictor), "The strips are not predicted, but a Predictor tag is written.");
        }

        // The pixels only round trip if the tags describe the strip data.
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        ImageComparer comparer = compression == TiffCompression.Jpeg ? ImageComparer.TolerantPercentage(1F) : ImageComparer.Exact;
        comparer.VerifySimilarity(input, output);
    }

    [Theory]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.BlackIsZero, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero)]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.WhiteIsZero, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.WhiteIsZero)]
    [InlineData(TiffBitsPerPixel.Bit4, TiffPhotometricInterpretation.PaletteColor, TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor, TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb)]
    public void TiffEncoder_JpegCompressionOption_WithBilevelOrPaletteData_EncodesContinuousToneJpeg(
        TiffBitsPerPixel bitsPerPixel,
        TiffPhotometricInterpretation photometricInterpretation,
        TiffBitsPerPixel expectedBitsPerPixel,
        TiffPhotometricInterpretation expectedPhotometricInterpretation)
    {
        // arrange
        using Image<Rgba32> input = CreateRoundTripInput(bitsPerPixel, photometricInterpretation);
        using var memStream = new MemoryStream();
        var encoder = new TiffEncoder
        {
            BitsPerPixel = bitsPerPixel,
            PhotometricInterpretation = photometricInterpretation,
            Compression = TiffCompression.Jpeg
        };

        // act
        input.Save(memStream, encoder);

        // assert
        Assert.Equal((int)TiffCompression.Jpeg, ReadFirstIfdValue(memStream.ToArray(), ExifTagValue.Compression));
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        TiffFrameMetadata frameMetadata = output.Frames.RootFrame.Metadata.GetTiffMetadata();
        Assert.Equal(expectedBitsPerPixel, frameMetadata.BitsPerPixel);
        Assert.Equal(expectedPhotometricInterpretation, frameMetadata.PhotometricInterpretation);
        ImageComparer.TolerantPercentage(1F).VerifySimilarity(input, output);
    }

    [Theory]
    [InlineData(TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.BlackIsZero)]
    [InlineData(TiffBitsPerPixel.Bit4, TiffPhotometricInterpretation.PaletteColor)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor)]
    public void TiffEncoder_JpegCompressionFromMetadata_WithBilevelOrPaletteData_WritesUncompressed(
        TiffBitsPerPixel bitsPerPixel,
        TiffPhotometricInterpretation photometricInterpretation)
    {
        // arrange
        using Image<Rgba32> input = CreateRoundTripInput(bitsPerPixel, photometricInterpretation);
        TiffFrameMetadata inputMetadata = input.Frames.RootFrame.Metadata.GetTiffMetadata();
        inputMetadata.BitsPerPixel = bitsPerPixel;
        inputMetadata.PhotometricInterpretation = photometricInterpretation;
        inputMetadata.Compression = TiffCompression.Jpeg;
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, new TiffEncoder());

        // assert
        Assert.Equal((int)TiffCompression.None, ReadFirstIfdValue(memStream.ToArray(), ExifTagValue.Compression));
        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        Assert.Equal(bitsPerPixel, output.Frames.RootFrame.Metadata.GetTiffMetadata().BitsPerPixel);
        ImageComparer.Exact.VerifySimilarity(input, output);
    }

    [Theory]
    [InlineData(TiffBitsPerPixel.Bit1, null, TiffCompression.None)]
    [InlineData(TiffBitsPerPixel.Bit1, null, TiffCompression.CcittGroup4Fax)]
    [InlineData(TiffBitsPerPixel.Bit4, null, TiffCompression.None)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.None)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.None)]
    [InlineData(TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor, TiffCompression.Lzw)]
    [InlineData(TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.Deflate)]
    [InlineData(null, null, TiffCompression.None)]
    public void TiffEncoder_WritesBaselineTagsWithSpecFieldTypes(
        TiffBitsPerPixel? bitsPerPixel,
        TiffPhotometricInterpretation? photometricInterpretation,
        TiffCompression compression)
    {
        // arrange
        var encoder = new TiffEncoder
        {
            BitsPerPixel = bitsPerPixel,
            PhotometricInterpretation = photometricInterpretation,
            Compression = compression,
            HorizontalPredictor = compression is TiffCompression.Lzw or TiffCompression.Deflate ? TiffPredictor.Horizontal : null
        };
        using Image<Rgba32> input = CreateRandomColorImage(40, 30);
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, encoder);

        // assert
        Dictionary<ExifTagValue, ExifDataType> fieldTypes = ReadFirstIfdFieldTypes(memStream.ToArray());
        AssertBaselineFieldTypes(fieldTypes);
    }

    [Theory]
    [WithFile(CcittFax3LowerOrderBitsFirst, PixelTypes.Rgba32)]
    [WithFile(Fax4CompressedLowerOrderBitsFirst, PixelTypes.Rgba32)]
    [WithFile(HuffmanRleLowerOrderBitsFirst, PixelTypes.Rgba32)]
    public void TiffEncoder_WithMetadataFromDecodedTiff_DoesNotCopyFillOrder<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // arrange
        using Image<TPixel> input = provider.GetImage(new TiffDecoder());
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, new TiffEncoder());

        // assert
        Dictionary<ExifTagValue, ExifDataType> fieldTypes = ReadFirstIfdFieldTypes(memStream.ToArray());
        Assert.False(fieldTypes.ContainsKey(ExifTagValue.FillOrder), "The strips are written most significant bit first, but FillOrder is copied from the source.");
        AssertBaselineFieldTypes(fieldTypes);
        memStream.Position = 0;
        using var output = Image.Load<TPixel>(memStream);
        ImageComparer.Exact.VerifySimilarity(input, output);
    }

    [Theory]
    [WithFile(FlowerYCbCr888Contiguoush2v2, PixelTypes.Rgba32)]
    [WithFile(YCbCrJpegCompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_WithMetadataFromDecodedYCbCrTiff_DoesNotCopyYCbCrOrJpegTags<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // arrange
        using Image<TPixel> input = provider.GetImage(new TiffDecoder());
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, new TiffEncoder());

        // assert
        Dictionary<ExifTagValue, ExifDataType> fieldTypes = ReadFirstIfdFieldTypes(memStream.ToArray());
        Assert.Equal((int)TiffPhotometricInterpretation.Rgb, ReadFirstIfdValue(memStream.ToArray(), ExifTagValue.PhotometricInterpretation));
        Assert.False(fieldTypes.ContainsKey(ExifTagValue.YCbCrSubsampling), "YCbCrSubsampling is copied to RGB data.");
        Assert.False(fieldTypes.ContainsKey(ExifTagValue.YCbCrCoefficients), "YCbCrCoefficients is copied to RGB data.");
        Assert.False(fieldTypes.ContainsKey(ExifTagValue.YCbCrPositioning), "YCbCrPositioning is copied to RGB data.");
        Assert.False(fieldTypes.ContainsKey(ExifTagValue.ReferenceBlackWhite), "ReferenceBlackWhite is copied to full-range data.");
        Assert.False(fieldTypes.ContainsKey(ExifTagValue.JPEGTables), "JPEGTables is copied from the source.");
    }

    [Fact]
    public void TiffEncoder_WithImageDataTagsInExifProfile_WritesOnlyTheTagsOfTheWrittenData()
    {
        // arrange
        using Image<Rgba32> input = CreateRoundTripInput(TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb);
        var exifProfile = new ExifProfile();
        exifProfile.SetValue(ExifTag.FillOrder, (ushort)2);
        exifProfile.SetValue(ExifTag.T4Options, 1u);
        exifProfile.SetValue(ExifTag.TileWidth, 16);
        exifProfile.SetValue(ExifTag.TileLength, 16);
        exifProfile.SetValue(ExifTag.TileOffsets, new uint[] { 8, 16 });
        exifProfile.SetValue(ExifTag.TileByteCounts, new Number[] { 8, 8 });
        exifProfile.SetValue(ExifTag.StripRowCounts, new uint[] { 16 });
        exifProfile.SetValue(ExifTag.ExtraSamples, new ushort[] { 2 });
        exifProfile.SetValue(ExifTag.SampleFormat, new ushort[] { 3, 3, 3 });
        exifProfile.SetValue(ExifTag.SMinSampleValue, new uint[] { 0, 0, 0 });
        exifProfile.SetValue(ExifTag.ColorMap, new ushort[6]);
        exifProfile.SetValue(ExifTag.TransferFunction, new ushort[6]);
        exifProfile.SetValue(ExifTag.YCbCrSubsampling, new ushort[] { 2, 2 });
        exifProfile.SetValue(ExifTag.ReferenceBlackWhite, new Rational[] { new(0), new(255), new(128), new(255), new(128), new(255) });
        exifProfile.SetValue(ExifTag.JPEGTables, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        exifProfile.SetValue(ExifTag.FreeOffsets, new uint[] { 8 });
        exifProfile.SetValue(ExifTag.InkNames, "Cyan");
        exifProfile.SetValue(ExifTag.CFARepeatPatternDim, new ushort[] { 2, 2 });
        exifProfile.SetValue(ExifTag.CodingMethods, 2u);
        exifProfile.SetValue(ExifTag.DefaultImageColor, new ushort[] { 255, 255, 255 });
        exifProfile.SetValue(ExifTag.Predictor, (ushort)TiffPredictor.Horizontal);
        exifProfile.SetValue(ExifTag.Compression, (ushort)TiffCompression.Lzw);
        exifProfile.SetValue(ExifTag.PlanarConfiguration, (ushort)TiffPlanarConfiguration.Planar);
        exifProfile.SetValue(ExifTag.SamplesPerPixel, (ushort)4);
        exifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        exifProfile.SetValue(ExifTag.ImageDescription, "Image description");
        input.Frames.RootFrame.Metadata.ExifProfile = exifProfile;
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, new TiffEncoder());

        // assert
        byte[] tiff = memStream.ToArray();
        Dictionary<ExifTagValue, ExifDataType> fieldTypes = ReadFirstIfdFieldTypes(tiff);
        ExifTagValue[] imageDataTags =
        {
            ExifTagValue.FillOrder, ExifTagValue.T4Options, ExifTagValue.TileWidth, ExifTagValue.TileLength, ExifTagValue.TileOffsets,
            ExifTagValue.TileByteCounts, ExifTagValue.StripRowCounts, ExifTagValue.ExtraSamples, ExifTagValue.SampleFormat,
            ExifTagValue.SMinSampleValue, ExifTagValue.ColorMap, ExifTagValue.TransferFunction, ExifTagValue.YCbCrSubsampling,
            ExifTagValue.ReferenceBlackWhite, ExifTagValue.JPEGTables, ExifTagValue.FreeOffsets, ExifTagValue.InkNames,
            ExifTagValue.CFARepeatPatternDim, ExifTagValue.CodingMethods, ExifTagValue.DefaultImageColor, ExifTagValue.Predictor
        };
        foreach (ExifTagValue tag in imageDataTags)
        {
            Assert.False(fieldTypes.ContainsKey(tag), $"Tag {tag} is copied from the source profile.");
        }

        Assert.Equal((int)TiffCompression.None, ReadFirstIfdValue(tiff, ExifTagValue.Compression));
        Assert.Equal((int)TiffPlanarConfiguration.Chunky, ReadFirstIfdValue(tiff, ExifTagValue.PlanarConfiguration));
        Assert.Equal(3, ReadFirstIfdValue(tiff, ExifTagValue.SamplesPerPixel));
        Assert.Equal(6, ReadFirstIfdValue(tiff, ExifTagValue.Orientation));
        Assert.True(fieldTypes.ContainsKey(ExifTagValue.ImageDescription));
        AssertBaselineFieldTypes(fieldTypes);

        memStream.Position = 0;
        using var output = Image.Load<Rgba32>(memStream);
        ImageComparer.Exact.VerifySimilarity(input, output);
    }

    [Fact]
    public void TiffEncoder_WithBigTiffFieldTypesInExifProfile_WritesOnlyClassicTiffFieldTypes()
    {
        // arrange
        using var input = new Image<Rgba32>(4, 4);
        var values = new List<IExifValue>();
        foreach ((ExifTagValue tag, ExifDataType dataType, object value) in new (ExifTagValue, ExifDataType, object)[]
        {
            ((ExifTagValue)0xdd01, ExifDataType.Long8, 1UL),
            ((ExifTagValue)0xdd02, ExifDataType.SignedLong8, -1L),
            ((ExifTagValue)0xdd03, ExifDataType.Long, 1U),
        })
        {
            ExifValue exifValue = ExifValues.Create(tag, dataType, false);
            Assert.True(exifValue.TrySetValue(value));
            values.Add(exifValue);
        }

        input.Frames.RootFrame.Metadata.ExifProfile = new ExifProfile(values, Array.Empty<ExifTag>());
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, new TiffEncoder());

        // assert
        Dictionary<ExifTagValue, ExifDataType> fieldTypes = ReadFirstIfdFieldTypes(memStream.ToArray());
        foreach (KeyValuePair<ExifTagValue, ExifDataType> entry in fieldTypes)
        {
            Assert.True(entry.Value <= ExifDataType.Ifd, $"Tag {entry.Key} has field type {entry.Value}, which only BigTIFF defines.");
        }

        Assert.Equal(ExifDataType.Long, fieldTypes[(ExifTagValue)0xdd03]);
    }

    [Theory]
    [WithFile(SampleMetadata, PixelTypes.Rgba32)]
    public void TiffEncoder_WithMetadataFromDecodedTiff_WritesEachTagOnceWithSpecFieldTypes<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // arrange
        using Image<TPixel> input = provider.GetImage(new TiffDecoder());
        using var memStream = new MemoryStream();

        // act
        input.Save(memStream, new TiffEncoder());

        // assert
        // ReadFirstIfdFieldTypes throws on a repeated tag.
        Dictionary<ExifTagValue, ExifDataType> fieldTypes = ReadFirstIfdFieldTypes(memStream.ToArray());
        Assert.True(fieldTypes.ContainsKey(ExifTagValue.XMP));
        AssertBaselineFieldTypes(fieldTypes);
    }

    [Theory]
    [WithFile(MultiframeLzwPredictor, PixelTypes.Rgba32)]
    [WithFile(SampleMetadata, PixelTypes.Rgba32)]
    public void TiffEncoder_ToNonSeekableStream_MatchesSeekableStream<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // arrange
        using Image<TPixel> input = provider.GetImage(new TiffDecoder());
        var encoder = new TiffEncoder();
        using var seekableStream = new MemoryStream();
        input.Save(seekableStream, encoder);
        using var memStream = new MemoryStream();

        // act
        input.Save(new NonSeekableStream(memStream), encoder);

        // assert
        Assert.Equal(seekableStream.ToArray(), memStream.ToArray());
    }

    [Theory]
    [InlineData(TiffCompression.None, 1)]
    [InlineData(TiffCompression.None, 3)]
    [InlineData(TiffCompression.Deflate, 1)]
    [InlineData(TiffCompression.Deflate, 3)]
    [InlineData(TiffCompression.Lzw, 1)]
    [InlineData(TiffCompression.Lzw, 3)]
    [InlineData(TiffCompression.PackBits, 1)]
    [InlineData(TiffCompression.PackBits, 3)]
    [InlineData(TiffCompression.Jpeg, 1)]
    [InlineData(TiffCompression.Jpeg, 3)]
    [InlineData(TiffCompression.Ccitt1D, 1)]
    [InlineData(TiffCompression.Ccitt1D, 3)]
    [InlineData(TiffCompression.CcittGroup3Fax, 1)]
    [InlineData(TiffCompression.CcittGroup3Fax, 3)]
    [InlineData(TiffCompression.CcittGroup4Fax, 1)]
    [InlineData(TiffCompression.CcittGroup4Fax, 3)]
    public void TiffEncoder_AtNonZeroStreamPosition_MatchesEncodingAtPositionZero(TiffCompression compression, int frameCount)
    {
        // arrange
        using Image<Rgba32> input = CreateRandomColorImage(64, 80, frameCount);
        var encoder = new TiffEncoder { Compression = compression };
        byte[] expected = EncodeAtPositionZero(input, encoder);
        using var memStream = new MemoryStream();
        memStream.Write(StreamPrefix);

        // act
        input.Save(memStream, encoder);

        // assert
        AssertPrefixedTiff(expected, memStream.ToArray(), frameCount);
    }

    [Fact]
    public async Task TiffEncoder_AtNonZeroStreamPosition_Async_MatchesEncodingAtPositionZero()
    {
        // arrange
        using Image<Rgba32> input = CreateRandomColorImage(64, 80, 3);
        var encoder = new TiffEncoder { Compression = TiffCompression.Lzw };
        byte[] expected = EncodeAtPositionZero(input, encoder);
        using var memStream = new MemoryStream();
        memStream.Write(StreamPrefix);

        // act
        await input.SaveAsync(memStream, encoder, TestContext.Current.CancellationToken);

        // assert
        AssertPrefixedTiff(expected, memStream.ToArray(), 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TiffEncoder_ToNonSeekableStreamWithData_MatchesEncodingAtPositionZero(bool async)
    {
        // arrange
        using Image<Rgba32> input = CreateRandomColorImage(64, 80, 3);
        var encoder = new TiffEncoder { Compression = TiffCompression.Deflate };
        byte[] expected = EncodeAtPositionZero(input, encoder);
        using var memStream = new MemoryStream();
        memStream.Write(StreamPrefix);
        var nonSeekableStream = new NonSeekableStream(memStream);

        // act
        if (async)
        {
            await input.SaveAsync(nonSeekableStream, encoder, TestContext.Current.CancellationToken);
        }
        else
        {
            input.Save(nonSeekableStream, encoder);
        }

        // assert
        AssertPrefixedTiff(expected, memStream.ToArray(), 3);
    }

    // This makes sure, that when decoding a planar tiff, the planar configuration is not carried over to the encoded image.
    [Theory]
    [WithFile(FlowerRgb444Planar, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodePlanar_AndReload_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, imageDecoder: new TiffDecoder());

    [Theory]
    [WithFile(Calliphora_RgbUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeRgb_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb);

    [Theory]
    [WithFile(Calliphora_RgbUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeRgb_WithDeflateCompression_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.Deflate);

    [Theory]
    [WithFile(Calliphora_RgbUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeRgb_WithDeflateCompressionAndPredictor_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.Deflate, TiffPredictor.Horizontal);

    [Theory]
    [WithFile(Calliphora_RgbUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeRgb_WithLzwCompression_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.Lzw);

    [Theory]
    [WithFile(Calliphora_RgbUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeRgb_WithLzwCompressionAndPredictor_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.Lzw, TiffPredictor.Horizontal);

    [Theory]
    [WithFile(Calliphora_RgbUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeRgb_WithPackBitsCompression_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.PackBits);

    [Theory]
    [WithFile(Calliphora_RgbUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeRgb_WithJpegCompression_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit24, TiffPhotometricInterpretation.Rgb, TiffCompression.Jpeg, useExactComparer: false, compareTolerance: 0.012f);

    [Theory]
    [WithFile(Calliphora_GrayscaleUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeGray_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero);

    [Theory]
    [WithFile(Calliphora_GrayscaleUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeGray_WithDeflateCompression_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Deflate);

    [Theory]
    [WithFile(Calliphora_GrayscaleUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeGray_WithDeflateCompressionAndPredictor_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Deflate, TiffPredictor.Horizontal);

    [Theory]
    [WithFile(Calliphora_GrayscaleUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeGray_WithLzwCompression_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Lzw);

    [Theory]
    [WithFile(Calliphora_GrayscaleUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeGray_WithLzwCompressionAndPredictor_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Lzw, TiffPredictor.Horizontal);

    [Theory]
    [WithFile(Calliphora_GrayscaleUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeGray_WithPackBitsCompression_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.PackBits);

    [Theory]
    [WithFile(Calliphora_PaletteUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeColorPalette_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> =>
        TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor, useExactComparer: false, compareTolerance: 0.001f);

    [Theory]
    [WithFile(Rgb4BitPalette, PixelTypes.Rgba32)]
    [WithFile(Flower4BitPalette, PixelTypes.Rgba32)]
    [WithFile(Flower4BitPaletteGray, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeColorPalette_With4Bit_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> =>
        TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit4, TiffPhotometricInterpretation.PaletteColor, useExactComparer: false, compareTolerance: 0.003f);

    [Theory]
    [WithFile(Calliphora_PaletteUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeColorPalette_WithPackBitsCompression_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> =>
        TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor, TiffCompression.PackBits, useExactComparer: false, compareTolerance: 0.001f);

    [Theory]
    [WithFile(Calliphora_PaletteUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeColorPalette_WithDeflateCompression_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> =>
        TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor, TiffCompression.Deflate, useExactComparer: false, compareTolerance: 0.001f);

    [Theory]
    [WithFile(Calliphora_PaletteUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeColorPalette_WithDeflateCompressionAndPredictor_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> =>
        TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor, TiffCompression.Deflate, TiffPredictor.Horizontal, useExactComparer: false, compareTolerance: 0.001f);

    [Theory]
    [WithFile(Calliphora_PaletteUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeColorPalette_WithLzwCompression_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> =>
        TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor, TiffCompression.Lzw, useExactComparer: false, compareTolerance: 0.001f);

    [Theory]
    [WithFile(Calliphora_PaletteUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeColorPalette_WithLzwCompressionAndPredictor_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> =>
        TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit8, TiffPhotometricInterpretation.PaletteColor, TiffCompression.Lzw, TiffPredictor.Horizontal, useExactComparer: false, compareTolerance: 0.001f);

    [Theory]
    [InlineData(TiffBitsPerPixel.Bit4, false)]
    [InlineData(TiffBitsPerPixel.Bit8, false)]
    [InlineData(TiffBitsPerPixel.Bit4, true)]
    [InlineData(TiffBitsPerPixel.Bit8, true)]
    public void TiffEncoder_EncodeColorPalette_WithoutDither_KeepsGradientRowsIdenticalAndMonotonic(TiffBitsPerPixel bitsPerPixel, bool zeroDitherScale)
    {
        // A dither scale of zero adds no error to the neighbouring pixels, so it must match no dither.
        QuantizerOptions options = zeroDitherScale
            ? new QuantizerOptions { Dither = KnownDitherings.FloydSteinberg, DitherScale = 0 }
            : new QuantizerOptions { Dither = null };

        using Image<Rgba32> image = CreateGrayGradient();
        var encoder = new TiffEncoder
        {
            BitsPerPixel = bitsPerPixel,
            PhotometricInterpretation = TiffPhotometricInterpretation.PaletteColor,
            Quantizer = new PaletteQuantizer(CreateGrayPalette(), options)
        };

        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        stream.Position = 0;
        using Image<Rgba32> decoded = Image.Load<Rgba32>(stream);

        decoded.ProcessPixelRows(accessor =>
        {
            Span<Rgba32> firstRow = accessor.GetRowSpan(0);
            Assert.Equal(new Rgba32(0, 0, 0), firstRow[0]);
            Assert.Equal(new Rgba32(255, 255, 255), firstRow[^1]);
            for (int x = 1; x < firstRow.Length; x++)
            {
                Assert.True(firstRow[x].R >= firstRow[x - 1].R, $"Row 0 decreases at x={x}.");
            }

            for (int y = 1; y < accessor.Height; y++)
            {
                Assert.True(firstRow.SequenceEqual(accessor.GetRowSpan(y)), $"Row {y} differs from row 0.");
            }
        });
    }

    [Theory]
    [InlineData(TiffBitsPerPixel.Bit4, "Bayer8x8", 1F)]
    [InlineData(TiffBitsPerPixel.Bit8, "Bayer8x8", 1F)]
    [InlineData(TiffBitsPerPixel.Bit4, "FloydSteinberg", 0.5F)]
    [InlineData(TiffBitsPerPixel.Bit8, "FloydSteinberg", 0.5F)]
    public void TiffEncoder_EncodeColorPalette_MatchesQuantizeWithSameDither(TiffBitsPerPixel bitsPerPixel, string ditherName, float ditherScale)
    {
        IDither dither = ditherName == "Bayer8x8" ? KnownDitherings.Bayer8x8 : KnownDitherings.FloydSteinberg;
        var quantizer = new PaletteQuantizer(CreateGrayPalette(), new QuantizerOptions { Dither = dither, DitherScale = ditherScale });

        // The palette has 16 entries, so the color limit of either bit depth does not change the result.
        using Image<Rgba32> image = CreateGrayGradient();
        using Image<Rgba32> expected = image.Clone(x => x.Quantize(quantizer));
        var encoder = new TiffEncoder
        {
            BitsPerPixel = bitsPerPixel,
            PhotometricInterpretation = TiffPhotometricInterpretation.PaletteColor,
            Quantizer = quantizer
        };

        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        stream.Position = 0;
        using Image<Rgba32> decoded = Image.Load<Rgba32>(stream);

        ImageComparer.Exact.VerifySimilarity(expected, decoded);
    }

    [Theory]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeBiColor_BlackIsZero_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.BlackIsZero);

    [Theory]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeBiColor_WhiteIsZero_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.WhiteIsZero);

    [Theory]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeBiColor_WithDeflateCompression_BlackIsZero_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Deflate);

    [Theory]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeBiColor_WithDeflateCompression_WhiteIsZero_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.Deflate);

    [Theory]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeBiColor_WithPackBitsCompression_BlackIsZero_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.PackBits);

    [Theory]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeBiColor_WithPackBitsCompression_WhiteIsZero_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.PackBits);

    [Theory]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeBiColor_WithCcittGroup3FaxCompression_WhiteIsZero_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.CcittGroup3Fax);

    [Theory]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeBiColor_WithCcittGroup3FaxCompression_BlackIsZero_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.CcittGroup3Fax);

    [Theory]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeBiColor_WithCcittGroup4FaxCompression_WhiteIsZero_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.CcittGroup4Fax);

    [Theory]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeBiColor_WithCcittGroup4FaxCompression_BlackIsZero_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.CcittGroup4Fax);

    [Theory]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeBiColor_WithModifiedHuffmanCompression_WhiteIsZero_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.WhiteIsZero, TiffCompression.Ccitt1D);

    [Theory]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.Rgba32)]
    public void TiffEncoder_EncodeBiColor_WithModifiedHuffmanCompression_BlackIsZero_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestTiffEncoderCore(provider, TiffBitsPerPixel.Bit1, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.Ccitt1D);

    [Theory]
    [WithFile(GrayscaleUncompressed, PixelTypes.L8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.PackBits)]
    [WithFile(RgbUncompressed, PixelTypes.Rgba32, TiffPhotometricInterpretation.Rgb, TiffCompression.Deflate)]
    [WithFile(RgbUncompressed, PixelTypes.Rgb24, TiffPhotometricInterpretation.Rgb, TiffCompression.None)]
    [WithFile(RgbUncompressed, PixelTypes.Rgba32, TiffPhotometricInterpretation.Rgb, TiffCompression.None)]
    [WithFile(RgbUncompressed, PixelTypes.Rgb48, TiffPhotometricInterpretation.Rgb, TiffCompression.None)]
    public void TiffEncoder_StripLength<TPixel>(TestImageProvider<TPixel> provider, TiffPhotometricInterpretation photometricInterpretation, TiffCompression compression)
        where TPixel : unmanaged, IPixel<TPixel> =>
        TestStripLength(provider, photometricInterpretation, compression);

    [Theory]
    [WithFile(PaletteDeflateMultistrip, PixelTypes.L8, TiffPhotometricInterpretation.PaletteColor, TiffCompression.Lzw)]
    public void TiffEncoder_StripLength_WithPalette<TPixel>(TestImageProvider<TPixel> provider, TiffPhotometricInterpretation photometricInterpretation, TiffCompression compression)
        where TPixel : unmanaged, IPixel<TPixel> =>
        TestStripLength(provider, photometricInterpretation, compression, false, 0.01f);

    [Theory]
    [WithFile(Calliphora_BiColorUncompressed, PixelTypes.L8, TiffPhotometricInterpretation.BlackIsZero, TiffCompression.CcittGroup3Fax)]
    public void TiffEncoder_StripLength_OutOfBounds<TPixel>(TestImageProvider<TPixel> provider, TiffPhotometricInterpretation photometricInterpretation, TiffCompression compression)
        where TPixel : unmanaged, IPixel<TPixel> =>
        //// CcittGroup3Fax compressed data length can be larger than the original length.
        Assert.Throws<Xunit.Sdk.TrueException>(() => TestStripLength(provider, photometricInterpretation, compression));

    [Theory]
    [WithTestPatternImages(287, 321, PixelTypes.Rgba32, TiffPhotometricInterpretation.Rgb)]
    [WithTestPatternImages(287, 321, PixelTypes.Rgba32, TiffPhotometricInterpretation.PaletteColor)]
    [WithTestPatternImages(287, 321, PixelTypes.Rgba32, TiffPhotometricInterpretation.BlackIsZero)]
    public void TiffEncode_WorksWithDiscontiguousBuffers<TPixel>(TestImageProvider<TPixel> provider, TiffPhotometricInterpretation photometricInterpretation)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        provider.LimitAllocatorBufferCapacity().InPixelsSqrt(200);
        using Image<TPixel> image = provider.GetImage();

        var encoder = new TiffEncoder { PhotometricInterpretation = photometricInterpretation };
        image.DebugSave(provider, encoder);
    }

    /// <summary>
    /// Creates a 64x16 image whose rows run from black to white.
    /// </summary>
    private static Image<Rgba32> CreateGrayGradient()
    {
        var image = new Image<Rgba32>(64, 16);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    byte value = (byte)(x * 255 / (row.Length - 1));
                    row[x] = new Rgba32(value, value, value);
                }
            }
        });

        return image;
    }

    /// <summary>
    /// Creates a palette of 16 evenly spaced grays, which fits both the 4 bit and the 8 bit color map.
    /// </summary>
    private static Color[] CreateGrayPalette()
    {
        var palette = new Color[16];
        for (int i = 0; i < palette.Length; i++)
        {
            byte value = (byte)(i * 255 / (palette.Length - 1));
            palette[i] = Color.FromRgb(value, value, value);
        }

        return palette;
    }

    /// <summary>
    /// Reads the tag and field type of each entry in the first IFD of a TIFF file.
    /// </summary>
    private static Dictionary<ExifTagValue, ExifDataType> ReadFirstIfdFieldTypes(byte[] data)
    {
        bool littleEndian = data[0] == 0x49;
        ushort ReadUInt16(int offset) => littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset))
            : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
        int ifdOffset = (int)(littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4))
            : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)));

        var fieldTypes = new Dictionary<ExifTagValue, ExifDataType>();
        int count = ReadUInt16(ifdOffset);
        for (int i = 0; i < count; i++)
        {
            int entryOffset = ifdOffset + 2 + (i * 12);
            fieldTypes.Add((ExifTagValue)ReadUInt16(entryOffset), (ExifDataType)ReadUInt16(entryOffset + 2));
        }

        return fieldTypes;
    }

    private static int ReadFirstIfdValue(byte[] data, ExifTagValue tag)
    {
        Assert.Equal(0x49, data[0]);
        int ifdOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(ifdOffset));
        for (int i = 0; i < count; i++)
        {
            int entryOffset = ifdOffset + 2 + (i * 12);
            if (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryOffset)) == (ushort)tag)
            {
                var type = (ExifDataType)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryOffset + 2));
                return type == ExifDataType.Short
                    ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryOffset + 8))
                    : (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entryOffset + 8));
            }
        }

        throw new InvalidOperationException($"Tag {tag} not found.");
    }

    private static void AssertBaselineFieldTypes(Dictionary<ExifTagValue, ExifDataType> fieldTypes)
    {
        Assert.Equal(ExifDataType.Short, fieldTypes[ExifTagValue.SamplesPerPixel]);
        foreach (KeyValuePair<ExifTagValue, ExifDataType> entry in fieldTypes)
        {
            if (BaselineFieldTypes.TryGetValue(entry.Key, out ExifDataType[] allowedTypes))
            {
                Assert.True(Array.IndexOf(allowedTypes, entry.Value) >= 0, $"Tag {entry.Key} has field type {entry.Value}.");
            }
        }
    }

    private static Image<Rgba32> CreateBlackDiagonalOnWhite(int width, int height)
    {
        var image = new Image<Rgba32>(width, height, Color.White);
        for (int y = 0; y < height; y++)
        {
            image[y * width / height, y] = Color.Black;
        }

        return image;
    }

    private static void AssertBlackDiagonalOnWhite(Image<Rgba32> image)
    {
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 expected = x == y * image.Width / image.Height ? Color.Black : Color.White;
                Assert.True(expected == image[x, y], $"Pixel ({x},{y}) is {image[x, y]}, expected {expected}.");
            }
        }
    }

    private static byte[] EncodeAtPositionZero(Image<Rgba32> image, TiffEncoder encoder)
    {
        using var memStream = new MemoryStream();
        image.Save(memStream, encoder);
        return memStream.ToArray();
    }

    private static void AssertPrefixedTiff(byte[] expected, byte[] actual, int expectedFrameCount)
    {
        Assert.Equal(StreamPrefix, actual.AsSpan(0, StreamPrefix.Length).ToArray());
        byte[] tiff = actual.AsSpan(StreamPrefix.Length).ToArray();
        Assert.Equal(expected, tiff);

        using Image<Rgba32> decoded = Image.Load<Rgba32>(tiff);
        Assert.Equal(expectedFrameCount, decoded.Frames.Count);
    }

    private static Image<Rgba32> CreateRandomColorImage(int width, int height, int frameCount)
    {
        Image<Rgba32> image = CreateRandomColorImage(width, height);
        for (int i = 1; i < frameCount; i++)
        {
            using Image<Rgba32> frame = CreateRandomColorImage(width, height);
            image.Frames.AddFrame(frame.Frames.RootFrame);
        }

        return image;
    }

    /// <summary>
    /// Creates an image that the given bit depth and photometric interpretation store without loss.
    /// </summary>
    private static Image<Rgba32> CreateRoundTripInput(TiffBitsPerPixel bitsPerPixel, TiffPhotometricInterpretation photometricInterpretation)
    {
        const int width = 32;
        const int height = 16;
        if (bitsPerPixel == TiffBitsPerPixel.Bit1)
        {
            return CreateBlackDiagonalOnWhite(width, height);
        }

        var image = new Image<Rgba32>(width, height);
        Rgba32[] palette = { Color.Black, Color.White, Color.Red, Color.Blue };
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte gray = (byte)((x * 8) + y);
                image[x, y] = photometricInterpretation switch
                {
                    TiffPhotometricInterpretation.PaletteColor => palette[((x / 4) + (y / 4)) % palette.Length],
                    TiffPhotometricInterpretation.Rgb => new Rgba32((byte)(x * 8), (byte)(y * 16), 128),
                    _ => new Rgba32(gray, gray, gray),
                };
            }
        }

        return image;
    }

    private static Image<Rgba32> CreateRandomColorImage(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        var random = new Random(42);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image[x, y] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            }
        }

        return image;
    }
}
