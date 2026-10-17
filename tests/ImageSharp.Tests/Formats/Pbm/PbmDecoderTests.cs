// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Text;
using SixLabors.ImageSharp.Formats.Pbm;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.TestUtilities;
using Xunit;
using static SixLabors.ImageSharp.Tests.TestImages.Pbm;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Tests.Formats.Pbm;

[Trait("Format", "Pbm")]
[ValidateDisposedMemoryAllocations]
public class PbmDecoderTests
{
    // Each image is 2x1 with samples at half of maxval and at maxval.
    // The binary 16-bit samples (257 = 0x0101, 514 = 0x0202) have equal high and low bytes,
    // so these tests do not depend on 16-bit byte order.
    public static readonly TheoryData<string, byte[]> GrayscaleWithUnusualMaxValue =
        new()
        {
            { "P2\n2 1\n100\n50 100\n", Array.Empty<byte>() },
            { "P5\n2 1\n100\n", new byte[] { 50, 100 } },
            { "P2\n2 1\n1000\n500 1000\n", Array.Empty<byte>() },
            { "P5\n2 1\n514\n", new byte[] { 0x01, 0x01, 0x02, 0x02 } },
        };

    // Each image is 2x1 with pixels (half, max, 0) and (max, 0, half).
    public static readonly TheoryData<string, byte[]> RgbWithUnusualMaxValue =
        new()
        {
            { "P3\n2 1\n100\n50 100 0 100 0 50\n", Array.Empty<byte>() },
            { "P6\n2 1\n100\n", new byte[] { 50, 100, 0, 100, 0, 50 } },
            { "P3\n2 1\n1000\n500 1000 0 1000 0 500\n", Array.Empty<byte>() },
            { "P6\n2 1\n514\n", new byte[] { 0x01, 0x01, 0x02, 0x02, 0x00, 0x00, 0x02, 0x02, 0x00, 0x00, 0x01, 0x01 } },
        };

    [Theory]
    [InlineData(BlackAndWhitePlain, PbmColorType.BlackAndWhite, PbmComponentType.Bit)]
    [InlineData(BlackAndWhiteBinary, PbmColorType.BlackAndWhite, PbmComponentType.Bit)]
    [InlineData(GrayscalePlain, PbmColorType.Grayscale, PbmComponentType.Byte)]
    [InlineData(GrayscalePlainMagick, PbmColorType.Grayscale, PbmComponentType.Byte)]
    [InlineData(GrayscaleBinary, PbmColorType.Grayscale, PbmComponentType.Byte)]
    [InlineData(GrayscaleBinaryWide, PbmColorType.Grayscale, PbmComponentType.Short)]
    [InlineData(RgbPlain, PbmColorType.Rgb, PbmComponentType.Byte)]
    [InlineData(RgbPlainMagick, PbmColorType.Rgb, PbmComponentType.Byte)]
    [InlineData(RgbBinary, PbmColorType.Rgb, PbmComponentType.Byte)]
    public void ImageLoadCanDecode(string imagePath, PbmColorType expectedColorType, PbmComponentType expectedComponentType)
    {
        // Arrange
        var testFile = TestFile.Create(imagePath);
        using var stream = new MemoryStream(testFile.Bytes, false);

        // Act
        using var image = Image.Load(stream);

        // Assert
        Assert.NotNull(image);
        PbmMetadata metadata = image.Metadata.GetPbmMetadata();
        Assert.NotNull(metadata);
        Assert.Equal(expectedColorType, metadata.ColorType);
        Assert.Equal(expectedComponentType, metadata.ComponentType);
    }

    [Theory]
    [InlineData(BlackAndWhitePlain)]
    [InlineData(BlackAndWhiteBinary)]
    [InlineData(GrayscalePlain)]
    [InlineData(GrayscalePlainMagick)]
    [InlineData(GrayscaleBinary)]
    [InlineData(GrayscaleBinaryWide)]
    public void ImageLoadL8CanDecode(string imagePath)
    {
        // Arrange
        var testFile = TestFile.Create(imagePath);
        using var stream = new MemoryStream(testFile.Bytes, false);

        // Act
        using var image = Image.Load<L8>(stream);

        // Assert
        Assert.NotNull(image);
    }

    [Theory]
    [InlineData(RgbPlain)]
    [InlineData(RgbPlainMagick)]
    [InlineData(RgbBinary)]
    public void ImageLoadRgb24CanDecode(string imagePath)
    {
        // Arrange
        var testFile = TestFile.Create(imagePath);
        using var stream = new MemoryStream(testFile.Bytes, false);

        // Act
        using var image = Image.Load<Rgb24>(stream);

        // Assert
        Assert.NotNull(image);
    }

    [Theory]
    [WithFile(BlackAndWhitePlain, PixelTypes.L8, "pbm")]
    [WithFile(BlackAndWhiteBinary, PixelTypes.L8, "pbm")]
    [WithFile(GrayscalePlain, PixelTypes.L8, "pgm")]
    [WithFile(GrayscalePlainNormalized, PixelTypes.L8, "pgm")]
    [WithFile(GrayscaleBinary, PixelTypes.L8, "pgm")]
    [WithFile(GrayscaleBinaryWide, PixelTypes.L16, "pgm")]
    [WithFile(RgbPlain, PixelTypes.Rgb24, "ppm")]
    [WithFile(RgbPlainNormalized, PixelTypes.Rgb24, "ppm")]
    [WithFile(RgbBinary, PixelTypes.Rgb24, "ppm")]
    public void DecodeReferenceImage<TPixel>(TestImageProvider<TPixel> provider, string extension)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider, extension: extension);

        bool isGrayscale = extension is "pgm" or "pbm";
        image.CompareToReferenceOutput(provider, grayscale: isGrayscale);
    }

    [Theory]
    [InlineData("P2\n1 1\n0\n0\n")]
    [InlineData("P3\n1 1\n0\n0 0 0\n")]
    [InlineData("P5\n1 1\n0\n\0")]
    [InlineData("P6\n1 1\n0\n\0\0\0")]
    [InlineData("P2\n1 1\n65536\n0\n")]
    [InlineData("P3\n1 1\n70000\n0 0 0\n")]
    [InlineData("P5\n1 1\n65536\n\0\0")]
    [InlineData("P6\n1 1\n99999\n\0\0\0\0\0\0")]
    [InlineData("P2\n1 1\n4294967396\n0\n")]
    public void Decode_InvalidMaxValue_ThrowsInvalidImageContentException(string data)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(data);

        Assert.Throws<InvalidImageContentException>(() => Image.Load<L8>(bytes));
    }

    [Theory]
    [InlineData("P1\n0 1\n")]
    [InlineData("P2\n0 1\n255\n")]
    [InlineData("P2\n1 0\n255\n")]
    [InlineData("P3\n0 0\n255\n")]
    [InlineData("P4\n0 1\n")]
    [InlineData("P5\n1 0\n255\n")]
    [InlineData("P6\n0 1\n255\n")]
    public void Decode_ZeroWidthOrHeight_ThrowsInvalidImageContentException(string data)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(data);

        Assert.Throws<InvalidImageContentException>(() => Image.Load<L8>(bytes));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(bytes));
    }

    [Theory]
    [InlineData("P6\n400000 1\n255\n")]
    [InlineData("P5\n600000 1\n65535\n")]
    [InlineData("P3\n400000 1\n255\n")]
    public void Decode_PixelProcessingFails_DisposesImage(string header)
    {
        // The L8 image needs less than 1 MB, but the row buffer needs more, so its allocation fails after
        // the image is allocated. The class attribute checks that the image is disposed.
        Configuration configuration = Configuration.Default.Clone();
        configuration.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions { AllocationLimitMegabytes = 1 });
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(header));

        Assert.Throws<InvalidImageContentException>(() => Image.Load<L8>(configuration, stream));
    }

    [Theory]
    [MemberData(nameof(GrayscaleWithUnusualMaxValue))]
    public void Decode_Grayscale_ScalesMaxValueToFullIntensity(string header, byte[] samples)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(header).Concat(samples).ToArray();

        using Image<L8> image = Image.Load<L8>(bytes);

        Assert.InRange(image[0, 0].PackedValue, 127, 128);
        Assert.Equal(255, image[1, 0].PackedValue);
    }

    [Theory]
    [MemberData(nameof(RgbWithUnusualMaxValue))]
    public void Decode_Rgb_ScalesMaxValueToFullIntensity(string header, byte[] samples)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(header).Concat(samples).ToArray();

        using Image<Rgb24> image = Image.Load<Rgb24>(bytes);

        Rgb24 first = image[0, 0];
        Rgb24 second = image[1, 0];
        Assert.InRange(first.R, 127, 128);
        Assert.Equal(255, first.G);
        Assert.Equal(0, first.B);
        Assert.Equal(255, second.R);
        Assert.Equal(0, second.G);
        Assert.InRange(second.B, 127, 128);
    }

    [Fact]
    public void Decode_Binary16BitGrayscale_ReadsSamplesBigEndian()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("P5\n2 1\n65535\n").Concat(new byte[] { 0x01, 0x00, 0x12, 0x34 }).ToArray();

        using Image<L16> image = Image.Load<L16>(bytes);

        Assert.Equal(0x0100, image[0, 0].PackedValue);
        Assert.Equal(0x1234, image[1, 0].PackedValue);
    }

    [Fact]
    public void Decode_Binary16BitRgb_ReadsSamplesBigEndian()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("P6\n1 1\n65535\n").Concat(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 }).ToArray();

        using Image<Rgb48> image = Image.Load<Rgb48>(bytes);

        Assert.Equal(new Rgb48(0x0102, 0x0304, 0x0506), image[0, 0]);
    }

    [Fact]
    public void Decode_Binary16BitWithMaxValue1000_ScalesBigEndianSamples()
    {
        // 500 = 0x01F4 and 1000 = 0x03E8, stored big-endian.
        byte[] gray = Encoding.ASCII.GetBytes("P5\n2 1\n1000\n").Concat(new byte[] { 0x01, 0xF4, 0x03, 0xE8 }).ToArray();
        byte[] rgb = Encoding.ASCII.GetBytes("P6\n1 1\n1000\n").Concat(new byte[] { 0x01, 0xF4, 0x03, 0xE8, 0x00, 0x00 }).ToArray();

        using Image<L16> grayImage = Image.Load<L16>(gray);
        using Image<Rgb48> rgbImage = Image.Load<Rgb48>(rgb);

        Assert.Equal(32768, grayImage[0, 0].PackedValue);
        Assert.Equal(65535, grayImage[1, 0].PackedValue);
        Assert.Equal(new Rgb48(32768, 65535, 0), rgbImage[0, 0]);
    }

    [Theory]
    [InlineData("P5\n2 1\n255\n", new byte[] { 0x20, 0x80 }, 0x20)]
    [InlineData("P5\n2 1\n255\n", new byte[] { 0x0A, 0x80 }, 0x0A)]
    [InlineData("P5\n2 1\n255\n", new byte[] { 0x23, 0x80 }, 0x23)]
    [InlineData("P5 2 1 255 ", new byte[] { 0x09, 0x80 }, 0x09)]
    public void Decode_BinaryGrayscaleRasterStartingWithWhitespaceOrHash_ReadsFirstByteAsPixel(string header, byte[] raster, byte expected)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(header).Concat(raster).ToArray();

        using Image<L8> image = Image.Load<L8>(bytes);

        Assert.Equal(expected, image[0, 0].PackedValue);
        Assert.Equal(0x80, image[1, 0].PackedValue);
    }

    [Fact]
    public void Decode_BinaryRgbRasterStartingWithWhitespace_ReadsFirstByteAsSample()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("P6\n1 1\n255\n").Concat(new byte[] { 0x0D, 0x20, 0x0A }).ToArray();

        using Image<Rgb24> image = Image.Load<Rgb24>(bytes);

        Assert.Equal(new Rgb24(0x0D, 0x20, 0x0A), image[0, 0]);
    }

    [Theory]
    [InlineData(0x0A)]
    [InlineData(0x20)]
    [InlineData(0x23)]
    public void Decode_BinaryBlackAndWhiteRasterStartingWithWhitespaceOrHash_ReadsFirstByteAsPixels(byte raster)
    {
        byte[] bytes = Encoding.ASCII.GetBytes("P4\n8 1\n").Concat(new[] { raster }).ToArray();

        using Image<L8> image = Image.Load<L8>(bytes);

        for (int x = 0; x < 8; x++)
        {
            bool black = (raster & (0x80 >> x)) != 0;
            Assert.Equal(black ? 0 : 255, image[x, 0].PackedValue);
        }
    }

    [Theory]
    [InlineData("P1\n4 2\n0101\n1100\n")]
    [InlineData("P1\n4 2\n0 1 0 1\n1 1 0 0\n")]
    [InlineData("P1\n4 2\n01 0\n1110\n0")]
    [InlineData("P1\n4 2\n01# comment\n0111 00")]
    public void Decode_PlainBlackAndWhite_ReadsEachBitAsOneCharacter(string data)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(data);

        using Image<L8> image = Image.Load<L8>(bytes);

        byte[] expected = { 255, 0, 255, 0, 0, 0, 255, 255 };
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], image[i % 4, i / 4].PackedValue);
        }
    }

    [Fact]
    public void Decode_PlainHeaderFollowedByWhitespaceAndComments_SkipsThem()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("P2\n2 1\n255\n\n# comment\n  7 8\n");

        using Image<L8> image = Image.Load<L8>(bytes);

        Assert.Equal(7, image[0, 0].PackedValue);
        Assert.Equal(8, image[1, 0].PackedValue);
    }

    [Fact]
    public void PlainText_PrematureEof()
    {
        byte[] bytes = Encoding.ASCII.GetBytes($"P1\n100 100\n1 0 1 0 1 0");
        using EofHitCounter eofHitCounter = EofHitCounter.RunDecoder(bytes);

        Assert.True(eofHitCounter.EofHitCount <= 2);
        Assert.Equal(new Size(100, 100), eofHitCounter.Image.Size());
    }

    [Fact]
    public void Binary_PrematureEof()
    {
        using EofHitCounter eofHitCounter = EofHitCounter.RunDecoder(RgbBinaryPrematureEof);

        Assert.True(eofHitCounter.EofHitCount <= 2);
        Assert.Equal(new Size(29, 30), eofHitCounter.Image.Size());
    }
}
