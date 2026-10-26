// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using PinataLabs.ImagePastel.Formats;
using PinataLabs.ImagePastel.Formats.Bmp;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Quantization;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using PinataLabs.ImagePastel.Tests.TestUtilities.ReferenceCodecs;

using Xunit;
using static PinataLabs.ImagePastel.Tests.TestImages.Bmp;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests.Formats.Bmp;

[Trait("Format", "Bmp")]
public class BmpEncoderTests
{
    public static readonly TheoryData<BmpBitsPerPixel> BitsPerPixel =
    new()
    {
        BmpBitsPerPixel.Pixel24,
        BmpBitsPerPixel.Pixel32
    };

    public static readonly TheoryData<string, int, int, PixelResolutionUnit> RatioFiles =
    new()
    {
        { Car, 3780, 3780, PixelResolutionUnit.PixelsPerMeter },
        { V5Header, 3780, 3780, PixelResolutionUnit.PixelsPerMeter },
        { RLE8, 2835, 2835, PixelResolutionUnit.PixelsPerMeter }
    };

    public static readonly TheoryData<string, BmpBitsPerPixel> BmpBitsPerPixelFiles =
    new()
    {
        { Bit1, BmpBitsPerPixel.Pixel1 },
        { Bit4, BmpBitsPerPixel.Pixel4 },
        { Bit8, BmpBitsPerPixel.Pixel8 },
        { Rgb16, BmpBitsPerPixel.Pixel16 },
        { Car, BmpBitsPerPixel.Pixel24 },
        { Bit32Rgb, BmpBitsPerPixel.Pixel32 }
    };

    public static readonly TheoryData<int> OneBitWidths = new()
    {
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
        18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33
    };

    [Theory]
    [MemberData(nameof(RatioFiles))]
    public void Encode_PreserveRatio(string imagePath, int xResolution, int yResolution, PixelResolutionUnit resolutionUnit)
    {
        var options = new BmpEncoder();

        var testFile = TestFile.Create(imagePath);
        using (Image<Rgba32> input = testFile.CreateRgba32Image())
        {
            using (var memStream = new MemoryStream())
            {
                input.Save(memStream, options);

                memStream.Position = 0;
                using (var output = Image.Load<Rgba32>(memStream))
                {
                    ImageMetadata meta = output.Metadata;
                    Assert.Equal(xResolution, meta.HorizontalResolution);
                    Assert.Equal(yResolution, meta.VerticalResolution);
                    Assert.Equal(resolutionUnit, meta.ResolutionUnits);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(BmpBitsPerPixelFiles))]
    public void Encode_PreserveBitsPerPixel(string imagePath, BmpBitsPerPixel bmpBitsPerPixel)
    {
        var options = new BmpEncoder();

        var testFile = TestFile.Create(imagePath);
        using (Image<Rgba32> input = testFile.CreateRgba32Image())
        {
            using (var memStream = new MemoryStream())
            {
                input.Save(memStream, options);

                memStream.Position = 0;
                using (var output = Image.Load<Rgba32>(memStream))
                {
                    BmpMetadata meta = output.Metadata.GetBmpMetadata();

                    Assert.Equal(bmpBitsPerPixel, meta.BitsPerPixel);
                }
            }
        }
    }

    [Theory]
    [WithTestPatternImages(nameof(BitsPerPixel), 24, 24, PixelTypes.Rgba32 | PixelTypes.Bgra32 | PixelTypes.Rgb24)]
    public void Encode_IsNotBoundToSinglePixelType<TPixel>(TestImageProvider<TPixel> provider, BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel> => TestBmpEncoderCore(provider, bitsPerPixel);

    [Theory]
    [WithTestPatternImages(nameof(BitsPerPixel), 48, 24, PixelTypes.Rgba32)]
    [WithTestPatternImages(nameof(BitsPerPixel), 47, 8, PixelTypes.Rgba32)]
    [WithTestPatternImages(nameof(BitsPerPixel), 49, 7, PixelTypes.Rgba32)]
    [WithSolidFilledImages(nameof(BitsPerPixel), 1, 1, 255, 100, 50, 255, PixelTypes.Rgba32)]
    [WithTestPatternImages(nameof(BitsPerPixel), 7, 5, PixelTypes.Rgba32)]
    public void Encode_WorksWithDifferentSizes<TPixel>(TestImageProvider<TPixel> provider, BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel> => TestBmpEncoderCore(provider, bitsPerPixel);

    [Theory]
    [WithFile(Bit32Rgb, PixelTypes.Rgba32 | PixelTypes.Rgb24, BmpBitsPerPixel.Pixel32)]
    [WithFile(Bit32Rgba, PixelTypes.Rgba32 | PixelTypes.Rgb24, BmpBitsPerPixel.Pixel32)]
    [WithFile(WinBmpv4, PixelTypes.Rgba32 | PixelTypes.Rgb24, BmpBitsPerPixel.Pixel32)]
    [WithFile(WinBmpv5, PixelTypes.Rgba32 | PixelTypes.Rgb24, BmpBitsPerPixel.Pixel32)]
    public void Encode_32Bit_WithV3Header_Works<TPixel>(TestImageProvider<TPixel> provider, BmpBitsPerPixel bitsPerPixel)

        // If supportTransparency is false, a v3 bitmap header will be written.
        where TPixel : unmanaged, IPixel<TPixel> => TestBmpEncoderCore(provider, bitsPerPixel, supportTransparency: false);

    [Theory]
    [WithFile(Bit32Rgb, PixelTypes.Rgba32 | PixelTypes.Rgb24, BmpBitsPerPixel.Pixel32)]
    [WithFile(Bit32Rgba, PixelTypes.Rgba32 | PixelTypes.Rgb24, BmpBitsPerPixel.Pixel32)]
    [WithFile(WinBmpv4, PixelTypes.Rgba32 | PixelTypes.Rgb24, BmpBitsPerPixel.Pixel32)]
    [WithFile(WinBmpv5, PixelTypes.Rgba32 | PixelTypes.Rgb24, BmpBitsPerPixel.Pixel32)]
    public void Encode_32Bit_WithV4Header_Works<TPixel>(TestImageProvider<TPixel> provider, BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel> => TestBmpEncoderCore(provider, bitsPerPixel, supportTransparency: true);

    [Theory]
    [WithFile(WinBmpv3, PixelTypes.Rgb24, BmpBitsPerPixel.Pixel24)] // WinBmpv3 is a 24 bits per pixel image.
    [WithFile(F, PixelTypes.Rgb24, BmpBitsPerPixel.Pixel24)]
    public void Encode_24Bit_WithV3Header_Works<TPixel>(TestImageProvider<TPixel> provider, BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel> => TestBmpEncoderCore(provider, bitsPerPixel, supportTransparency: false);

    [Theory]
    [WithFile(WinBmpv3, PixelTypes.Rgb24, BmpBitsPerPixel.Pixel24)]
    [WithFile(F, PixelTypes.Rgb24, BmpBitsPerPixel.Pixel24)]
    public void Encode_24Bit_WithV4Header_Works<TPixel>(TestImageProvider<TPixel> provider, BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel> => TestBmpEncoderCore(provider, bitsPerPixel, supportTransparency: true);

    [Theory]
    [WithFile(Rgb16, PixelTypes.Bgra5551, BmpBitsPerPixel.Pixel16)]
    [WithFile(Bit16, PixelTypes.Bgra5551, BmpBitsPerPixel.Pixel16)]
    public void Encode_16Bit_WithV3Header_Works<TPixel>(TestImageProvider<TPixel> provider, BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel> => TestBmpEncoderCore(provider, bitsPerPixel, supportTransparency: false);

    [Theory]
    [WithFile(Rgb16, PixelTypes.Bgra5551, BmpBitsPerPixel.Pixel16)]
    [WithFile(Bit16, PixelTypes.Bgra5551, BmpBitsPerPixel.Pixel16)]
    public void Encode_16Bit_WithV4Header_Works<TPixel>(TestImageProvider<TPixel> provider, BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel> => TestBmpEncoderCore(provider, bitsPerPixel, supportTransparency: true);

    [Theory]
    [WithFile(WinBmpv5, PixelTypes.Rgba32, BmpBitsPerPixel.Pixel8)]
    [WithFile(Bit8Palette4, PixelTypes.Rgba32, BmpBitsPerPixel.Pixel8)]
    public void Encode_8Bit_WithV3Header_Works<TPixel>(TestImageProvider<TPixel> provider, BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel> => TestBmpEncoderCore(provider, bitsPerPixel, supportTransparency: false);

    [Theory]
    [WithFile(WinBmpv5, PixelTypes.Rgba32, BmpBitsPerPixel.Pixel8)]
    [WithFile(Bit8Palette4, PixelTypes.Rgba32, BmpBitsPerPixel.Pixel8)]
    public void Encode_8Bit_WithV4Header_Works<TPixel>(TestImageProvider<TPixel> provider, BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel> => TestBmpEncoderCore(provider, bitsPerPixel, supportTransparency: true);

    [Theory]
    [WithFile(Bit8Gs, PixelTypes.L8, BmpBitsPerPixel.Pixel8)]
    public void Encode_8BitGray_WithV3Header_Works<TPixel>(TestImageProvider<TPixel> provider, BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel> =>
        TestBmpEncoderCore(
            provider,
            bitsPerPixel,
            supportTransparency: false);

    [Theory]
    [WithFile(Bit4, PixelTypes.Rgba32, BmpBitsPerPixel.Pixel4)]
    public void Encode_4Bit_WithV3Header_Works<TPixel>(
        TestImageProvider<TPixel> provider,
        BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Oddly the difference only happens locally but we'll not test for that.
        // I suspect the issue is with the reference codec.
        ImageComparer comparer = TestEnvironment.IsFramework
            ? ImageComparer.TolerantPercentage(0.0161F)
            : ImageComparer.Exact;

        TestBmpEncoderCore(provider, bitsPerPixel, supportTransparency: false, customComparer: comparer);
    }

    [Theory]
    [WithFile(Bit4, PixelTypes.Rgba32, BmpBitsPerPixel.Pixel4)]
    public void Encode_4Bit_WithV4Header_Works<TPixel>(
        TestImageProvider<TPixel> provider,
        BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Oddly the difference only happens locally but we'll not test for that.
        // I suspect the issue is with the reference codec.
        ImageComparer comparer = TestEnvironment.IsFramework
            ? ImageComparer.TolerantPercentage(0.0161F)
            : ImageComparer.Exact;

        TestBmpEncoderCore(provider, bitsPerPixel, supportTransparency: true, customComparer: comparer);
    }

    [Theory]
    [WithFile(Bit1, PixelTypes.Rgba32, BmpBitsPerPixel.Pixel1)]
    public void Encode_1Bit_WithV3Header_Works<TPixel>(
        TestImageProvider<TPixel> provider,
        BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel> => TestBmpEncoderCore(provider, bitsPerPixel, supportTransparency: false);

    [Theory]
    [WithFile(Bit1, PixelTypes.Rgba32, BmpBitsPerPixel.Pixel1)]
    public void Encode_1Bit_WithV4Header_Works<TPixel>(
        TestImageProvider<TPixel> provider,
        BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel> => TestBmpEncoderCore(provider, bitsPerPixel, supportTransparency: true);

    [Theory]
    [WithFile(Bit8Gs, PixelTypes.L8, BmpBitsPerPixel.Pixel8)]
    public void Encode_8BitGray_WithV4Header_Works<TPixel>(TestImageProvider<TPixel> provider, BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel> =>
        TestBmpEncoderCore(
            provider,
            bitsPerPixel,
            supportTransparency: true);

    [Theory]
    [WithFile(Bit32Rgb, PixelTypes.Rgba32)]
    public void Encode_8BitColor_WithWuQuantizer<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!TestEnvironment.Is64BitProcess)
        {
            return;
        }

        using (Image<TPixel> image = provider.GetImage())
        {
            var encoder = new BmpEncoder
            {
                BitsPerPixel = BmpBitsPerPixel.Pixel8,
                Quantizer = new WuQuantizer()
            };
            string actualOutputFile = provider.Utility.SaveTestOutputFile(image, "bmp", encoder, appendPixelTypeToFileName: false);

            // Use the default decoder to test our encoded image. This verifies the content.
            // We do not verify the reference image though as some are invalid.
            IImageDecoder referenceDecoder = TestEnvironment.GetReferenceDecoder(actualOutputFile);
            using (var referenceImage = Image.Load<TPixel>(actualOutputFile, referenceDecoder))
            {
                referenceImage.CompareToReferenceOutput(
                    ImageComparer.TolerantPercentage(0.01f),
                    provider,
                    extension: "bmp",
                    appendPixelTypeToFileName: false,
                    decoder: new MagickReferenceDecoder(false));
            }
        }
    }

    [Theory]
    [WithFile(Bit32Rgb, PixelTypes.Rgba32)]
    public void Encode_8BitColor_WithOctreeQuantizer<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!TestEnvironment.Is64BitProcess)
        {
            return;
        }

        using (Image<TPixel> image = provider.GetImage())
        {
            var encoder = new BmpEncoder
            {
                BitsPerPixel = BmpBitsPerPixel.Pixel8,
                Quantizer = new OctreeQuantizer()
            };
            string actualOutputFile = provider.Utility.SaveTestOutputFile(image, "bmp", encoder, appendPixelTypeToFileName: false);

            // Use the default decoder to test our encoded image. This verifies the content.
            // We do not verify the reference image though as some are invalid.
            IImageDecoder referenceDecoder = TestEnvironment.GetReferenceDecoder(actualOutputFile);
            using (var referenceImage = Image.Load<TPixel>(actualOutputFile, referenceDecoder))
            {
                referenceImage.CompareToReferenceOutput(
                    ImageComparer.TolerantPercentage(0.01f),
                    provider,
                    extension: "bmp",
                    appendPixelTypeToFileName: false,
                    decoder: new MagickReferenceDecoder(false));
            }
        }
    }

    [Theory]
    [WithFile(TestImages.Png.GrayAlpha2BitInterlaced, PixelTypes.Rgba32, BmpBitsPerPixel.Pixel32)]
    [WithFile(Bit32Rgba, PixelTypes.Rgba32, BmpBitsPerPixel.Pixel32)]
    public void Encode_PreservesAlpha<TPixel>(TestImageProvider<TPixel> provider, BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel> => TestBmpEncoderCore(provider, bitsPerPixel, supportTransparency: true);

    [Theory]
    [WithFile(Car, PixelTypes.Rgba32, BmpBitsPerPixel.Pixel32)]
    [WithFile(V5Header, PixelTypes.Rgba32, BmpBitsPerPixel.Pixel32)]
    public void Encode_WorksWithDiscontiguousBuffers<TPixel>(TestImageProvider<TPixel> provider, BmpBitsPerPixel bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        provider.LimitAllocatorBufferCapacity().InBytesSqrt(100);
        TestBmpEncoderCore(provider, bitsPerPixel);
    }

    [Theory]
    [InlineData(BmpBitsPerPixel.Pixel1, false)]
    [InlineData(BmpBitsPerPixel.Pixel4, false)]
    [InlineData(BmpBitsPerPixel.Pixel8, false)]
    [InlineData(BmpBitsPerPixel.Pixel1, true)]
    [InlineData(BmpBitsPerPixel.Pixel4, true)]
    [InlineData(BmpBitsPerPixel.Pixel8, true)]
    public void Encode_IndexedWithoutDither_KeepsGradientRowsIdenticalAndMonotonic(BmpBitsPerPixel bitsPerPixel, bool zeroDitherScale)
    {
        // A dither scale of zero adds no error to the neighbouring pixels, so it must match no dither.
        QuantizerOptions options = zeroDitherScale
            ? new QuantizerOptions { Dither = KnownDitherings.FloydSteinberg, DitherScale = 0 }
            : new QuantizerOptions { Dither = null };

        // A palette of evenly spaced grays that fits the bit depth.
        int paletteLength = bitsPerPixel == BmpBitsPerPixel.Pixel1 ? 2 : 16;
        var palette = new Color[paletteLength];
        for (int i = 0; i < paletteLength; i++)
        {
            byte value = (byte)(i * 255 / (paletteLength - 1));
            palette[i] = Color.FromRgb(value, value, value);
        }

        using var image = new Image<Rgba32>(64, 16);
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

        var encoder = new BmpEncoder
        {
            BitsPerPixel = bitsPerPixel,
            Quantizer = new PaletteQuantizer(palette, options)
        };

        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        stream.Position = 0;
        using Image<Rgba32> decoded = Image.Load<Rgba32>(stream);

        decoded.ProcessPixelRows(accessor =>
        {
            Span<Rgba32> firstRow = accessor.GetRowSpan(0);
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
    [MemberData(nameof(OneBitWidths))]
    public void Encode_1Bit_RoundTripIsExact(int width)
    {
        // A non-periodic black and white pattern, so every bit of each row is checked.
        var random = new Random(width);
        using var image = new Image<Rgba32>(width, 5);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = random.Next(2) == 0 ? Color.Black : Color.White;
                }
            }
        });

        using var stream = new MemoryStream();
        image.Save(stream, new BmpEncoder { BitsPerPixel = BmpBitsPerPixel.Pixel1 });
        stream.Position = 0;
        using Image<Rgba32> decoded = Image.Load<Rgba32>(stream);

        ImageComparer.Exact.VerifySimilarity(image, decoded);
    }

    [Fact]
    public void Encode_1Bit_PacksPixelsMostSignificantBitFirst()
    {
        // Bits per row, 1 = white: 10110010 01110001 | 11000000 01.
        string[] rows = { "1011001001110001", "1100000001" };
        var palette = new[] { Color.Black, Color.White };
        var encoder = new BmpEncoder
        {
            BitsPerPixel = BmpBitsPerPixel.Pixel1,
            Quantizer = new PaletteQuantizer(palette, new QuantizerOptions { Dither = null })
        };

        byte[][] expectedRows =
        {
            new byte[] { 0b1011_0010, 0b0111_0001, 0, 0 },
            new byte[] { 0b1100_0000, 0b0100_0000, 0, 0 }
        };

        for (int i = 0; i < rows.Length; i++)
        {
            string bits = rows[i];
            using var image = new Image<Rgba32>(bits.Length, 1);
            for (int x = 0; x < bits.Length; x++)
            {
                image[x, 0] = bits[x] == '1' ? Color.White : Color.Black;
            }

            using var stream = new MemoryStream();
            image.Save(stream, encoder);
            byte[] data = stream.ToArray();

            // The pixel data starts after the file header, the info header and the two entry palette.
            int pixelOffset = BitConverter.ToInt32(data, 10);
            Assert.Equal(expectedRows[i], data[pixelOffset..]);
        }
    }

    private static void TestBmpEncoderCore<TPixel>(
        TestImageProvider<TPixel> provider,
        BmpBitsPerPixel bitsPerPixel,
        bool supportTransparency = true, // if set to true, will write a V4 header, otherwise a V3 header.
        IQuantizer quantizer = null,
        ImageComparer customComparer = null)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        {
            // There is no alpha in bmp with less then 32 bits per pixels, so the reference image will be made opaque.
            if (bitsPerPixel != BmpBitsPerPixel.Pixel32)
            {
                image.Mutate(c => c.MakeOpaque());
            }

            var encoder = new BmpEncoder
            {
                BitsPerPixel = bitsPerPixel,
                SupportTransparency = supportTransparency,
                Quantizer = quantizer ?? KnownQuantizers.Octree
            };

            // Does DebugSave & load reference CompareToReferenceInput():
            image.VerifyEncoder(provider, "bmp", bitsPerPixel, encoder, customComparer);
        }
    }
}
