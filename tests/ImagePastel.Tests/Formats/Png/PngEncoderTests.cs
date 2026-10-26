// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

// ReSharper disable InconsistentNaming
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats;
using PinataLabs.ImagePastel.Formats.Png;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing.Processors.Quantization;
using PinataLabs.ImagePastel.Tests.TestUtilities;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Png;

[Trait("Format", "Png")]
public partial class PngEncoderTests
{
    private static PngEncoder PngEncoder => new();

    public static readonly TheoryData<string, PngBitDepth> PngBitDepthFiles =
    new()
    {
        { TestImages.Png.Rgb48Bpp, PngBitDepth.Bit16 },
        { TestImages.Png.Bpp1, PngBitDepth.Bit1 }
    };

    public static readonly TheoryData<string, PngBitDepth, PngColorType> PngTrnsFiles =
    new()
    {
        { TestImages.Png.Gray1BitTrans, PngBitDepth.Bit1, PngColorType.Grayscale },
        { TestImages.Png.Gray2BitTrans, PngBitDepth.Bit2, PngColorType.Grayscale },
        { TestImages.Png.Gray4BitTrans, PngBitDepth.Bit4, PngColorType.Grayscale },
        { TestImages.Png.L8BitTrans, PngBitDepth.Bit8, PngColorType.Grayscale },
        { TestImages.Png.GrayTrns16BitInterlaced, PngBitDepth.Bit16, PngColorType.Grayscale },
        { TestImages.Png.Rgb24BppTrans, PngBitDepth.Bit8, PngColorType.Rgb },
        { TestImages.Png.Rgb48BppTrans, PngBitDepth.Bit16, PngColorType.Rgb }
    };

    /// <summary>
    /// All types except Palette
    /// </summary>
    public static readonly TheoryData<PngColorType> PngColorTypes = new()
    {
        PngColorType.RgbWithAlpha,
        PngColorType.Rgb,
        PngColorType.Grayscale,
        PngColorType.GrayscaleWithAlpha,
    };

    public static readonly TheoryData<PngFilterMethod> PngFilterMethods = new()
    {
        PngFilterMethod.None,
        PngFilterMethod.Sub,
        PngFilterMethod.Up,
        PngFilterMethod.Average,
        PngFilterMethod.Paeth,
        PngFilterMethod.Adaptive
    };

    /// <summary>
    /// All types except Palette
    /// </summary>
    public static readonly TheoryData<PngCompressionLevel> CompressionLevels
    = new()
    {
        PngCompressionLevel.Level0,
        PngCompressionLevel.Level1,
        PngCompressionLevel.Level2,
        PngCompressionLevel.Level3,
        PngCompressionLevel.Level4,
        PngCompressionLevel.Level5,
        PngCompressionLevel.Level6,
        PngCompressionLevel.Level7,
        PngCompressionLevel.Level8,
        PngCompressionLevel.Level9,
    };

    public static readonly TheoryData<int> PaletteSizes = new()
    {
        30, 55, 100, 201, 255
    };

    public static readonly TheoryData<int> PaletteLargeOnly = new()
    {
        80, 100, 120, 230
    };

    public static readonly PngInterlaceMode[] InterlaceMode = new[]
    {
        PngInterlaceMode.None,
        PngInterlaceMode.Adam7
    };

    public static readonly TheoryData<string, int, int, PixelResolutionUnit> RatioFiles =
    new()
    {
        { TestImages.Png.Splash, 11810, 11810, PixelResolutionUnit.PixelsPerMeter },
        { TestImages.Png.Ratio1x4, 1, 4, PixelResolutionUnit.AspectRatio },
        { TestImages.Png.Ratio4x1, 4, 1, PixelResolutionUnit.AspectRatio }
    };

    [Theory]
    [WithFile(TestImages.Png.Palette8Bpp, nameof(PngColorTypes), PixelTypes.Rgba32)]
    [WithTestPatternImages(nameof(PngColorTypes), 48, 24, PixelTypes.Rgba32)]
    [WithTestPatternImages(nameof(PngColorTypes), 47, 8, PixelTypes.Rgba32)]
    [WithTestPatternImages(nameof(PngColorTypes), 49, 7, PixelTypes.Rgba32)]
    [WithSolidFilledImages(nameof(PngColorTypes), 1, 1, 255, 100, 50, 255, PixelTypes.Rgba32)]
    [WithTestPatternImages(nameof(PngColorTypes), 7, 5, PixelTypes.Rgba32)]
    public void WorksWithDifferentSizes<TPixel>(TestImageProvider<TPixel> provider, PngColorType pngColorType)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        TestPngEncoderCore(
            provider,
            pngColorType,
            PngFilterMethod.Adaptive,
            PngBitDepth.Bit8,
            PngInterlaceMode.None,
            appendPngColorType: true);
    }

    [Theory]
    [WithTestPatternImages(nameof(PngColorTypes), 24, 24, PixelTypes.Rgba32 | PixelTypes.Bgra32 | PixelTypes.Rgb24)]
    public void IsNotBoundToSinglePixelType<TPixel>(TestImageProvider<TPixel> provider, PngColorType pngColorType)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        foreach (PngInterlaceMode interlaceMode in InterlaceMode)
        {
            TestPngEncoderCore(
            provider,
            pngColorType,
            PngFilterMethod.Adaptive,
            PngBitDepth.Bit8,
            interlaceMode,
            appendPixelType: true,
            appendPngColorType: true);
        }
    }

    [Theory]
    [WithTestPatternImages(nameof(PngFilterMethods), 24, 24, PixelTypes.Rgba32)]
    public void WorksWithAllFilterMethods<TPixel>(TestImageProvider<TPixel> provider, PngFilterMethod pngFilterMethod)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        foreach (PngInterlaceMode interlaceMode in InterlaceMode)
        {
            TestPngEncoderCore(
            provider,
            PngColorType.RgbWithAlpha,
            pngFilterMethod,
            PngBitDepth.Bit8,
            interlaceMode,
            appendPngFilterMethod: true);
        }
    }

    [Theory]
    [WithTestPatternImages(nameof(CompressionLevels), 24, 24, PixelTypes.Rgba32)]
    public void WorksWithAllCompressionLevels<TPixel>(TestImageProvider<TPixel> provider, PngCompressionLevel compressionLevel)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        foreach (PngInterlaceMode interlaceMode in InterlaceMode)
        {
            TestPngEncoderCore(
            provider,
            PngColorType.RgbWithAlpha,
            PngFilterMethod.Adaptive,
            PngBitDepth.Bit8,
            interlaceMode,
            compressionLevel,
            appendCompressionLevel: true);
        }
    }

    [Theory]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba32, PngColorType.Rgb, PngBitDepth.Bit8)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba64, PngColorType.Rgb, PngBitDepth.Bit16)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba32, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba64, PngColorType.RgbWithAlpha, PngBitDepth.Bit16)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba32, PngColorType.Palette, PngBitDepth.Bit1)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba32, PngColorType.Palette, PngBitDepth.Bit2)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba32, PngColorType.Palette, PngBitDepth.Bit4)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba32, PngColorType.Palette, PngBitDepth.Bit8)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgb24, PngColorType.Grayscale, PngBitDepth.Bit1)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgb24, PngColorType.Grayscale, PngBitDepth.Bit2)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgb24, PngColorType.Grayscale, PngBitDepth.Bit4)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgb24, PngColorType.Grayscale, PngBitDepth.Bit8)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgb48, PngColorType.Grayscale, PngBitDepth.Bit16)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba32, PngColorType.GrayscaleWithAlpha, PngBitDepth.Bit8)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba64, PngColorType.GrayscaleWithAlpha, PngBitDepth.Bit16)]
    public void WorksWithAllBitDepths<TPixel>(TestImageProvider<TPixel> provider, PngColorType pngColorType, PngBitDepth pngBitDepth)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // TODO: Investigate WuQuantizer to see if we can reduce memory pressure.
        if (TestEnvironment.RunsOnCI && !TestEnvironment.Is64BitProcess)
        {
            return;
        }

        foreach (PngFilterMethod filterMethod in PngFilterMethods)
        {
            foreach (PngInterlaceMode interlaceMode in InterlaceMode)
            {
                TestPngEncoderCore(
                provider,
                pngColorType,
                filterMethod,
                pngBitDepth,
                interlaceMode,
                appendPngColorType: true,
                appendPixelType: true,
                appendPngBitDepth: true);
            }
        }
    }

    [Theory]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba32, PngColorType.Rgb, PngBitDepth.Bit8)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba64, PngColorType.Rgb, PngBitDepth.Bit16)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba32, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba64, PngColorType.RgbWithAlpha, PngBitDepth.Bit16)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba32, PngColorType.Palette, PngBitDepth.Bit1)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba32, PngColorType.Palette, PngBitDepth.Bit2)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba32, PngColorType.Palette, PngBitDepth.Bit4)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba32, PngColorType.Palette, PngBitDepth.Bit8)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgb24, PngColorType.Grayscale, PngBitDepth.Bit1)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgb24, PngColorType.Grayscale, PngBitDepth.Bit2)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgb24, PngColorType.Grayscale, PngBitDepth.Bit4)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgb24, PngColorType.Grayscale, PngBitDepth.Bit8)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgb48, PngColorType.Grayscale, PngBitDepth.Bit16)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba32, PngColorType.GrayscaleWithAlpha, PngBitDepth.Bit8)]
    [WithTestPatternImages(24, 24, PixelTypes.Rgba64, PngColorType.GrayscaleWithAlpha, PngBitDepth.Bit16)]
    public void WorksWithAllBitDepthsAndExcludeAllFilter<TPixel>(TestImageProvider<TPixel> provider, PngColorType pngColorType, PngBitDepth pngBitDepth)
      where TPixel : unmanaged, IPixel<TPixel>
    {
        foreach (PngFilterMethod filterMethod in PngFilterMethods)
        {
            foreach (PngInterlaceMode interlaceMode in InterlaceMode)
            {
                TestPngEncoderCore(
                provider,
                pngColorType,
                filterMethod,
                pngBitDepth,
                interlaceMode,
                appendPngColorType: true,
                appendPixelType: true,
                appendPngBitDepth: true,
                optimizeMethod: PngChunkFilter.ExcludeAll);
            }
        }
    }

    // Each case covers every size, so an encoder or decoder that hangs ties up one thread per format rather than one per size.
    // The palette case quantizes 162 images; with the default quantizer it took over 120 s on the coverage runner.
    [Theory(Timeout = 120_000)]
    [InlineData(PngColorType.Grayscale, PngBitDepth.Bit1)]
    [InlineData(PngColorType.Grayscale, PngBitDepth.Bit2)]
    [InlineData(PngColorType.Grayscale, PngBitDepth.Bit4)]
    [InlineData(PngColorType.Grayscale, PngBitDepth.Bit8)]
    [InlineData(PngColorType.Grayscale, PngBitDepth.Bit16)]
    [InlineData(PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [InlineData(PngColorType.RgbWithAlpha, PngBitDepth.Bit16)]
    [InlineData(PngColorType.Palette, PngBitDepth.Bit8)]
    public async Task Encode_InterlacedSmallImages_WriteEveryPassAndMatchNonInterlaced(PngColorType colorType, PngBitDepth bitDepth)
    {
        await Task.Run(
            () =>
            {
                var failures = new List<string>();
                for (int width = 1; width <= 9; width++)
                {
                    for (int height = 1; height <= 9; height++)
                    {
                        string failure = EncodeInterlacedSmallImage(width, height, colorType, bitDepth);
                        if (failure is not null)
                        {
                            failures.Add($"{width}x{height}: {failure}");
                        }
                    }
                }

                Assert.Empty(failures);
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Encodes an image with and without interlacing, and returns how the interlaced file is wrong, or <see langword="null"/>.
    /// </summary>
    private static string EncodeInterlacedSmallImage(int width, int height, PngColorType colorType, PngBitDepth bitDepth)
    {
        using var source = new Image<Rgba64>(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                uint hash = (uint)((x * 73) + (y * 151) + 11) * 2654435761u;
                source[x, y] = new Rgba64((ushort)hash, (ushort)(hash >> 7), (ushort)(hash >> 13), (ushort)(hash >> 16));
            }
        }

        // The default Wu quantizer clears tables sized for any image, about 100 ms an image however small
        // it is. A fixed palette gives both encodes the same indices for a fraction of that.
        IQuantizer quantizer = colorType == PngColorType.Palette ? new WebSafePaletteQuantizer() : null;
        byte[] interlaced = EncodeToBytes(source, new PngEncoder { ColorType = colorType, BitDepth = bitDepth, InterlaceMethod = PngInterlaceMode.Adam7, Quantizer = quantizer });
        byte[] nonInterlaced = EncodeToBytes(source, new PngEncoder { ColorType = colorType, BitDepth = bitDepth, InterlaceMethod = PngInterlaceMode.None, Quantizer = quantizer });

        // The image data holds one scanline per row of each pass with columns, and nothing for empty passes.
        byte[] header = PngTestFileBuilder.ReadChunks(interlaced).First(c => c.Type == "IHDR").Data;
        int bitsPerPixel = header[8] * (colorType == PngColorType.RgbWithAlpha ? 4 : 1);
        int expectedLength = 0;
        for (int pass = 0; pass < 7; pass++)
        {
            int columns = Adam7.ComputeColumns(width, pass);
            if (columns > 0)
            {
                expectedLength += Adam7.ComputeBlockHeight(height, pass) * (1 + (((columns * bitsPerPixel) + 7) / 8));
            }
        }

        int length = InflateImageData(interlaced).Length;
        if (length != expectedLength)
        {
            return $"image data is {length} bytes, expected {expectedLength}";
        }

        using Image<Rgba64> expected = Image.Load<Rgba64>(nonInterlaced);
        using Image<Rgba64> actual = Image.Load<Rgba64>(interlaced);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (expected[x, y] != actual[x, y])
                {
                    return $"pixel ({x}, {y}) is {actual[x, y]}, expected {expected[x, y]}";
                }
            }
        }

        return null;
    }

    private static byte[] InflateImageData(byte[] png)
    {
        byte[] zlib = PngTestFileBuilder.ReadChunks(png).Where(c => c.Type == "IDAT").SelectMany(c => c.Data).ToArray();
        using var output = new MemoryStream();
        using (var inflate = new ZLibStream(new MemoryStream(zlib), CompressionMode.Decompress))
        {
            inflate.CopyTo(output);
        }

        return output.ToArray();
    }

    [Theory]
    [WithBlankImages(1, 1, PixelTypes.A8, PngColorType.GrayscaleWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.Argb32, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.Bgr565, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.Bgra4444, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.Byte4, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.HalfSingle, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.HalfVector2, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.HalfVector4, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.NormalizedByte2, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.NormalizedByte4, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.NormalizedShort4, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.Rg32, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.Rgba1010102, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.Rgba32, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.RgbaVector, PngColorType.RgbWithAlpha, PngBitDepth.Bit16)]
    [WithBlankImages(1, 1, PixelTypes.Short2, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.Short4, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.Rgb24, PngColorType.Rgb, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.Bgr24, PngColorType.Rgb, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.Bgra32, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.Rgb48, PngColorType.Rgb, PngBitDepth.Bit16)]
    [WithBlankImages(1, 1, PixelTypes.Rgba64, PngColorType.RgbWithAlpha, PngBitDepth.Bit16)]
    [WithBlankImages(1, 1, PixelTypes.Bgra5551, PngColorType.RgbWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.L8, PngColorType.Grayscale, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.L16, PngColorType.Grayscale, PngBitDepth.Bit16)]
    [WithBlankImages(1, 1, PixelTypes.La16, PngColorType.GrayscaleWithAlpha, PngBitDepth.Bit8)]
    [WithBlankImages(1, 1, PixelTypes.La32, PngColorType.GrayscaleWithAlpha, PngBitDepth.Bit16)]
    public void InfersColorTypeAndBitDepth<TPixel>(TestImageProvider<TPixel> provider, PngColorType pngColorType, PngBitDepth pngBitDepth)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Stream stream = new MemoryStream())
        {
            PngEncoder.Encode(provider.GetImage(), stream);

            stream.Seek(0, SeekOrigin.Begin);

            var decoder = new PngDecoder();

            Image image = decoder.Decode(Configuration.Default, stream, TestContext.Current.CancellationToken);

            PngMetadata metadata = image.Metadata.GetPngMetadata();
            Assert.Equal(pngColorType, metadata.ColorType);
            Assert.Equal(pngBitDepth, metadata.BitDepth);
        }
    }

    [Theory]
    [WithFile(TestImages.Png.Palette8Bpp, nameof(PaletteLargeOnly), PixelTypes.Rgba32)]
    public void PaletteColorType_WuQuantizer<TPixel>(TestImageProvider<TPixel> provider, int paletteSize)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // TODO: Investigate WuQuantizer to see if we can reduce memory pressure.
        if (TestEnvironment.RunsOnCI && !TestEnvironment.Is64BitProcess)
        {
            return;
        }

        foreach (PngInterlaceMode interlaceMode in InterlaceMode)
        {
            TestPngEncoderCore(
            provider,
            PngColorType.Palette,
            PngFilterMethod.Adaptive,
            PngBitDepth.Bit8,
            interlaceMode,
            paletteSize: paletteSize,
            appendPaletteSize: true);
        }
    }

    [Theory]
    [WithBlankImages(1, 1, PixelTypes.Rgba32)]
    public void WritesFileMarker<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        using (var ms = new MemoryStream())
        {
            image.Save(ms, PngEncoder);

            byte[] data = ms.ToArray().Take(8).ToArray();
            byte[] expected =
            {
                0x89, // Set the high bit.
                0x50, // P
                0x4E, // N
                0x47, // G
                0x0D, // Line ending CRLF
                0x0A, // Line ending CRLF
                0x1A, // EOF
                0x0A // LF
            };

            Assert.Equal(expected, data);
        }
    }

    [Theory]
    [MemberData(nameof(RatioFiles))]
    public void Encode_PreserveRatio(string imagePath, int xResolution, int yResolution, PixelResolutionUnit resolutionUnit)
    {
        var testFile = TestFile.Create(imagePath);
        using (Image<Rgba32> input = testFile.CreateRgba32Image())
        {
            using (var memStream = new MemoryStream())
            {
                input.Save(memStream, PngEncoder);

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
    [MemberData(nameof(PngBitDepthFiles))]
    public void Encode_PreserveBits(string imagePath, PngBitDepth pngBitDepth)
    {
        var testFile = TestFile.Create(imagePath);
        using (Image<Rgba32> input = testFile.CreateRgba32Image())
        {
            using (var memStream = new MemoryStream())
            {
                input.Save(memStream, PngEncoder);

                memStream.Position = 0;
                using (var output = Image.Load<Rgba32>(memStream))
                {
                    PngMetadata meta = output.Metadata.GetPngMetadata();

                    Assert.Equal(pngBitDepth, meta.BitDepth);
                }
            }
        }
    }

    [Theory]
    [InlineData(PngColorType.Palette)]
    [InlineData(PngColorType.RgbWithAlpha)]
    [InlineData(PngColorType.GrayscaleWithAlpha)]
    public void Encode_WithPngTransparentColorBehaviorClear_Works(PngColorType colorType)
    {
        // arrange
        var image = new Image<Rgba32>(50, 50);
        var encoder = new PngEncoder()
        {
            TransparentColorMode = PngTransparentColorMode.Clear,
            ColorType = colorType
        };
        Rgba32 rgba32 = Color.Blue;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < image.Height; y++)
            {
                System.Span<Rgba32> rowSpan = accessor.GetRowSpan(y);

                // Half of the test image should be transparent.
                if (y > 25)
                {
                    rgba32.A = 0;
                }

                for (int x = 0; x < image.Width; x++)
                {
                    rowSpan[x].FromRgba32(rgba32);
                }
            }
        });

        // act
        using var memStream = new MemoryStream();
        image.Save(memStream, encoder);

        // assert
        memStream.Position = 0;
        using var actual = Image.Load<Rgba32>(memStream);
        Rgba32 expectedColor = Color.Blue;
        if (colorType == PngColorType.Grayscale || colorType == PngColorType.GrayscaleWithAlpha)
        {
            var luminance = ColorNumerics.Get8BitBT709Luminance(expectedColor.R, expectedColor.G, expectedColor.B);
            expectedColor = new Rgba32(luminance, luminance, luminance);
        }

        actual.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                System.Span<Rgba32> rowSpan = accessor.GetRowSpan(y);

                if (y > 25)
                {
                    expectedColor = Color.Transparent;
                }

                for (int x = 0; x < accessor.Width; x++)
                {
                    Assert.Equal(expectedColor, rowSpan[x]);
                }
            }
        });
    }

    [Theory]
    [MemberData(nameof(PngTrnsFiles))]
    public void Encode_PreserveTrns(string imagePath, PngBitDepth pngBitDepth, PngColorType pngColorType)
    {
        var testFile = TestFile.Create(imagePath);
        using (Image<Rgba32> input = testFile.CreateRgba32Image())
        {
            PngMetadata inMeta = input.Metadata.GetPngMetadata();
            Assert.True(inMeta.HasTransparency);

            using (var memStream = new MemoryStream())
            {
                input.Save(memStream, PngEncoder);
                memStream.Position = 0;
                using (var output = Image.Load<Rgba32>(memStream))
                {
                    PngMetadata outMeta = output.Metadata.GetPngMetadata();
                    Assert.True(outMeta.HasTransparency);

                    switch (pngColorType)
                    {
                        case PngColorType.Grayscale:
                            if (pngBitDepth.Equals(PngBitDepth.Bit16))
                            {
                                Assert.True(outMeta.TransparentL16.HasValue);
                                Assert.Equal(inMeta.TransparentL16, outMeta.TransparentL16);
                            }
                            else
                            {
                                Assert.True(outMeta.TransparentL8.HasValue);
                                Assert.Equal(inMeta.TransparentL8, outMeta.TransparentL8);
                            }

                            break;
                        case PngColorType.Rgb:
                            if (pngBitDepth.Equals(PngBitDepth.Bit16))
                            {
                                Assert.True(outMeta.TransparentRgb48.HasValue);
                                Assert.Equal(inMeta.TransparentRgb48, outMeta.TransparentRgb48);
                            }
                            else
                            {
                                Assert.True(outMeta.TransparentRgb24.HasValue);
                                Assert.Equal(inMeta.TransparentRgb24, outMeta.TransparentRgb24);
                            }

                            break;
                    }
                }
            }
        }
    }

    [Fact]
    public void Encode_16BitGrayscaleTransparency_IsWrittenBigEndian()
    {
        using var image = new Image<Rgba64>(2, 1);
        PngMetadata metadata = image.Metadata.GetPngMetadata();
        metadata.ColorType = PngColorType.Grayscale;
        metadata.BitDepth = PngBitDepth.Bit16;
        metadata.HasTransparency = true;
        metadata.TransparentL16 = new L16(0x0102);

        byte[] png = EncodeToBytes(image, new PngEncoder { ColorType = PngColorType.Grayscale, BitDepth = PngBitDepth.Bit16 });

        Assert.Equal(new byte[] { 0x01, 0x02 }, PngTestFileBuilder.ReadChunks(png).Single(c => c.Type == "tRNS").Data);
        Assert.Equal(new L16(0x0102), Image.Identify(png).Metadata.GetPngMetadata().TransparentL16);
    }

    [Fact]
    public void Encode_16BitRgbTransparency_IsWrittenBigEndian()
    {
        using var image = new Image<Rgba64>(2, 1);
        PngMetadata metadata = image.Metadata.GetPngMetadata();
        metadata.ColorType = PngColorType.Rgb;
        metadata.BitDepth = PngBitDepth.Bit16;
        metadata.HasTransparency = true;
        metadata.TransparentRgb48 = new Rgb48(0x0102, 0x0304, 0x0506);

        byte[] png = EncodeToBytes(image, new PngEncoder { ColorType = PngColorType.Rgb, BitDepth = PngBitDepth.Bit16 });

        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 }, PngTestFileBuilder.ReadChunks(png).Single(c => c.Type == "tRNS").Data);
        Assert.Equal(new Rgb48(0x0102, 0x0304, 0x0506), Image.Identify(png).Metadata.GetPngMetadata().TransparentRgb48);
    }

    [Theory]
    [InlineData(TestImages.Png.Rgb48BppTrans, PngColorType.Palette)]
    [InlineData(TestImages.Png.Rgb24BppTrans, PngColorType.Palette)]
    [InlineData(TestImages.Png.L8BitTrans, PngColorType.Palette)]
    [InlineData(TestImages.Png.Rgb48BppTrans, PngColorType.RgbWithAlpha)]
    [InlineData(TestImages.Png.Rgb24BppTrans, PngColorType.Grayscale)]
    [InlineData(TestImages.Png.L8BitTrans, PngColorType.GrayscaleWithAlpha)]
    [InlineData(TestImages.Png.L8BitTrans, PngColorType.Rgb)]
    public void Encode_TransparencyToOtherColorType_WritesOnlyMatchingTransparencyChunk(string imagePath, PngColorType colorType)
    {
        var testFile = TestFile.Create(imagePath);
        using Image<Rgba32> input = testFile.CreateRgba32Image();
        Assert.True(input.Metadata.GetPngMetadata().HasTransparency);

        byte[] png = EncodeToBytes(input, new PngEncoder { ColorType = colorType });
        List<(string Type, byte[] Data)> chunks = PngTestFileBuilder.ReadChunks(png);
        List<byte[]> transparency = chunks.Where(c => c.Type == "tRNS").Select(c => c.Data).ToList();

        if (colorType == PngColorType.Palette)
        {
            // A palette tRNS holds one alpha value per palette entry at most.
            byte[] transparencyData = Assert.Single(transparency);
            Assert.True(transparencyData.Length <= chunks.Single(c => c.Type == "PLTE").Data.Length / 3);
        }
        else
        {
            // The transparent color in the metadata belongs to another color type, so it cannot be written.
            Assert.Empty(transparency);
        }
    }

    [Theory]
    [WithTestPatternImages(587, 821, PixelTypes.Rgba32)]
    [WithTestPatternImages(677, 683, PixelTypes.Rgba32)]
    public void Encode_WorksWithDiscontiguousBuffers<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        provider.LimitAllocatorBufferCapacity().InPixelsSqrt(200);
        foreach (PngInterlaceMode interlaceMode in InterlaceMode)
        {
            TestPngEncoderCore(
                provider,
                PngColorType.Rgb,
                PngFilterMethod.Adaptive,
                PngBitDepth.Bit8,
                interlaceMode,
                appendPixelType: true,
                appendPngColorType: true);
        }
    }

    [Theory]
    [WithTestPatternImages(100, 100, PixelTypes.Rgba32)]
    public void EncodeWorksWithoutSsse3Intrinsics(TestImageProvider<Rgba32> provider)
    {
        static void RunTest(string serialized)
        {
            TestImageProvider<Rgba32> provider =
                FeatureTestRunner.DeserializeForXunit<TestImageProvider<Rgba32>>(serialized);

            foreach (PngInterlaceMode interlaceMode in InterlaceMode)
            {
                TestPngEncoderCore(
                    provider,
                    PngColorType.Rgb,
                    PngFilterMethod.Adaptive,
                    PngBitDepth.Bit8,
                    interlaceMode,
                    appendPixelType: true,
                    appendPngColorType: true);
            }
        }

        FeatureTestRunner.RunWithHwIntrinsicsFeature(
            RunTest,
            HwIntrinsics.DisableSSSE3,
            provider);
    }

    [Fact]
    public void EncodeFixesInvalidOptions()
    {
        // https://github.com/SixLabors/ImageSharp/issues/935
        using var ms = new MemoryStream();
        var testFile = TestFile.Create(TestImages.Png.Issue935);
        using Image<Rgba32> image = testFile.CreateRgba32Image(new PngDecoder());

        image.Save(ms, new PngEncoder { ColorType = PngColorType.RgbWithAlpha });
    }

    private static void TestPngEncoderCore<TPixel>(
        TestImageProvider<TPixel> provider,
        PngColorType pngColorType,
        PngFilterMethod pngFilterMethod,
        PngBitDepth bitDepth,
        PngInterlaceMode interlaceMode,
        PngCompressionLevel compressionLevel = PngCompressionLevel.DefaultCompression,
        int paletteSize = 255,
        bool appendPngColorType = false,
        bool appendPngFilterMethod = false,
        bool appendPixelType = false,
        bool appendCompressionLevel = false,
        bool appendPaletteSize = false,
        bool appendPngBitDepth = false,
        PngChunkFilter optimizeMethod = PngChunkFilter.None)
            where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        {
            var encoder = new PngEncoder
            {
                ColorType = pngColorType,
                FilterMethod = pngFilterMethod,
                CompressionLevel = compressionLevel,
                BitDepth = bitDepth,
                Quantizer = new WuQuantizer(new QuantizerOptions { MaxColors = paletteSize }),
                InterlaceMethod = interlaceMode,
                ChunkFilter = optimizeMethod,
            };

            string pngColorTypeInfo = appendPngColorType ? pngColorType.ToString() : string.Empty;
            string pngFilterMethodInfo = appendPngFilterMethod ? pngFilterMethod.ToString() : string.Empty;
            string compressionLevelInfo = appendCompressionLevel ? $"_C{compressionLevel}" : string.Empty;
            string paletteSizeInfo = appendPaletteSize ? $"_PaletteSize-{paletteSize}" : string.Empty;
            string pngBitDepthInfo = appendPngBitDepth ? bitDepth.ToString() : string.Empty;
            string pngInterlaceModeInfo = interlaceMode != PngInterlaceMode.None ? $"_{interlaceMode}" : string.Empty;

            string debugInfo = $"{pngColorTypeInfo}{pngFilterMethodInfo}{compressionLevelInfo}{paletteSizeInfo}{pngBitDepthInfo}{pngInterlaceModeInfo}";

            string actualOutputFile = provider.Utility.SaveTestOutputFile(image, "png", encoder, debugInfo, appendPixelType);

            // Compare to the Magick reference decoder.
            IImageDecoder referenceDecoder = TestEnvironment.GetReferenceDecoder(actualOutputFile);

            // We compare using both our decoder and the reference decoder as pixel transformation
            // occurs within the encoder itself leaving the input image unaffected.
            // This means we are benefiting from testing our decoder also.
            using (var imageSharpImage = Image.Load<TPixel>(actualOutputFile, new PngDecoder()))
            using (var referenceImage = Image.Load<TPixel>(actualOutputFile, referenceDecoder))
            {
                ImageComparer.Exact.VerifySimilarity(referenceImage, imageSharpImage);
            }
        }
    }

    private static byte[] EncodeToBytes<TPixel>(Image<TPixel> image, PngEncoder encoder)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }
}
