// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using PinataLabs.ImagePastel.Formats.Pbm;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using Xunit;
using static PinataLabs.ImagePastel.Tests.TestImages.Pbm;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests.Formats.Pbm;

[Collection("RunSerial")]
[Trait("Format", "Pbm")]
public class PbmEncoderTests
{
    public static readonly TheoryData<PbmColorType> ColorType =
        new()
        {
            PbmColorType.BlackAndWhite,
            PbmColorType.Grayscale,
            PbmColorType.Rgb
        };

    public static readonly TheoryData<string, PbmColorType> PbmColorTypeFiles =
        new()
        {
            { BlackAndWhiteBinary, PbmColorType.BlackAndWhite },
            { BlackAndWhitePlain, PbmColorType.BlackAndWhite },
            { GrayscaleBinary, PbmColorType.Grayscale },
            { GrayscaleBinaryWide, PbmColorType.Grayscale },
            { GrayscalePlain, PbmColorType.Grayscale },
            { RgbBinary, PbmColorType.Rgb },
            { RgbPlain, PbmColorType.Rgb },
        };

    public static readonly TheoryData<int> BlackAndWhiteWidths = new() { 1, 7, 8, 9, 10, 13, 17 };

    [Theory]
    [MemberData(nameof(PbmColorTypeFiles))]
    public void PbmEncoder_PreserveColorType(string imagePath, PbmColorType pbmColorType)
    {
        var options = new PbmEncoder();

        var testFile = TestFile.Create(imagePath);
        using (Image<Rgba32> input = testFile.CreateRgba32Image())
        {
            using (var memStream = new MemoryStream())
            {
                input.Save(memStream, options);
                memStream.Position = 0;
                using (var output = Image.Load<Rgba32>(memStream))
                {
                    PbmMetadata meta = output.Metadata.GetPbmMetadata();
                    Assert.Equal(pbmColorType, meta.ColorType);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(PbmColorTypeFiles))]
    public void PbmEncoder_WithPlainEncoding_PreserveBitsPerPixel(string imagePath, PbmColorType pbmColorType)
    {
        var options = new PbmEncoder()
        {
            Encoding = PbmEncoding.Plain
        };

        var testFile = TestFile.Create(imagePath);
        using (Image<Rgba32> input = testFile.CreateRgba32Image())
        {
            using (var memStream = new MemoryStream())
            {
                input.Save(memStream, options);

                // EOF indicator for plain is a Space.
                memStream.Seek(-1, SeekOrigin.End);
                int lastByte = memStream.ReadByte();
                Assert.Equal(0x20, lastByte);

                memStream.Seek(0, SeekOrigin.Begin);
                using (var output = Image.Load<Rgba32>(memStream))
                {
                    PbmMetadata meta = output.Metadata.GetPbmMetadata();
                    Assert.Equal(pbmColorType, meta.ColorType);
                }
            }
        }
    }

    [Fact]
    public void PbmEncoder_P5_WritesWideSamplesBigEndian()
    {
        using Image<L16> image = new(2, 1);
        image[0, 0] = new L16(0x0100);
        image[1, 0] = new L16(0x1234);
        var encoder = new PbmEncoder { ColorType = PbmColorType.Grayscale, ComponentType = PbmComponentType.Short, Encoding = PbmEncoding.Binary };

        using var memStream = new MemoryStream();
        image.Save(memStream, encoder);
        byte[] data = memStream.ToArray();

        Assert.Equal(new byte[] { 0x01, 0x00, 0x12, 0x34 }, data[^4..]);
    }

    [Fact]
    public void PbmEncoder_P6_WritesWideSamplesBigEndian()
    {
        using Image<Rgb48> image = new(1, 1);
        image[0, 0] = new Rgb48(0x0102, 0x0304, 0x0506);
        var encoder = new PbmEncoder { ColorType = PbmColorType.Rgb, ComponentType = PbmComponentType.Short, Encoding = PbmEncoding.Binary };

        using var memStream = new MemoryStream();
        image.Save(memStream, encoder);
        byte[] data = memStream.ToArray();

        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 }, data[^6..]);
    }

    [Theory]
    [MemberData(nameof(BlackAndWhiteWidths))]
    public void PbmEncoder_P4_PadsEachRowToByteBoundary(int width)
    {
        const int height = 3;
        using Image<L8> image = new(width, height, new L8(0));
        var encoder = new PbmEncoder
        {
            ColorType = PbmColorType.BlackAndWhite,
            ComponentType = PbmComponentType.Byte,
            Encoding = PbmEncoding.Binary
        };

        using var memStream = new MemoryStream();
        image.Save(memStream, encoder);
        byte[] data = memStream.ToArray();

        int headerLength = $"P4\n{width} {height}\n".Length;
        int bytesPerRow = (width + 7) / 8;
        Assert.Equal(headerLength + (bytesPerRow * height), data.Length);

        // Every pixel is black, so every used bit is set and every padding bit must be zero.
        int usedBitsInLastByte = width - ((bytesPerRow - 1) * 8);
        byte expectedLastByte = (byte)(0xFF << (8 - usedBitsInLastByte));
        for (int y = 0; y < height; y++)
        {
            int rowStart = headerLength + (y * bytesPerRow);
            for (int i = 0; i < bytesPerRow - 1; i++)
            {
                Assert.Equal(0xFF, data[rowStart + i]);
            }

            Assert.Equal(expectedLastByte, data[rowStart + bytesPerRow - 1]);
        }
    }

    [Theory]
    [WithFile(BlackAndWhitePlain, PixelTypes.Rgb24)]
    public void PbmEncoder_P1_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestPbmEncoderCore(provider, PbmColorType.BlackAndWhite, PbmEncoding.Plain);

    [Theory]
    [WithFile(BlackAndWhiteBinary, PixelTypes.Rgb24)]
    public void PbmEncoder_P4_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestPbmEncoderCore(provider, PbmColorType.BlackAndWhite, PbmEncoding.Binary);

    [Theory]
    [WithFile(GrayscalePlainMagick, PixelTypes.Rgb24)]
    public void PbmEncoder_P2_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestPbmEncoderCore(provider, PbmColorType.Grayscale, PbmEncoding.Plain);

    [Theory]
    [WithFile(GrayscaleBinary, PixelTypes.Rgb24)]
    public void PbmEncoder_P5_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestPbmEncoderCore(provider, PbmColorType.Grayscale, PbmEncoding.Binary);

    [Theory]
    [WithFile(RgbPlainMagick, PixelTypes.Rgb24)]
    public void PbmEncoder_P3_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestPbmEncoderCore(provider, PbmColorType.Rgb, PbmEncoding.Plain);

    [Theory]
    [WithFile(RgbBinary, PixelTypes.Rgb24)]
    public void PbmEncoder_P6_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel> => TestPbmEncoderCore(provider, PbmColorType.Rgb, PbmEncoding.Binary);

    private static void TestPbmEncoderCore<TPixel>(
        TestImageProvider<TPixel> provider,
        PbmColorType colorType,
        PbmEncoding encoding,
        bool useExactComparer = true,
        float compareTolerance = 0.01f)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        {
            var encoder = new PbmEncoder { ColorType = colorType, Encoding = encoding };

            using (var memStream = new MemoryStream())
            {
                image.Save(memStream, encoder);
                memStream.Position = 0;
                using (var encodedImage = (Image<TPixel>)Image.Load(memStream))
                {
                    ImageComparingUtils.CompareWithReferenceDecoder(provider, encodedImage, useExactComparer, compareTolerance);
                }
            }
        }
    }
}
