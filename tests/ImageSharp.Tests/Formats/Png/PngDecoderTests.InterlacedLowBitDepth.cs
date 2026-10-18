// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Tests.Formats.Png;

public partial class PngDecoderTests
{
    // Images this small decode in milliseconds, so a test that runs this long has hung.
    private const int InterlaceTimeoutMilliseconds = 30_000;

    [Theory]
    [InlineData(5, 9, 1)]
    [InlineData(9, 9, 1)]
    [InlineData(17, 5, 1)]
    [InlineData(33, 11, 1)]
    [InlineData(5, 9, 2)]
    [InlineData(9, 9, 2)]
    [InlineData(17, 5, 2)]
    [InlineData(5, 9, 4)]
    [InlineData(9, 9, 4)]
    [InlineData(17, 5, 4)]
    public void Decode_InterlacedLowBitDepthGrayscale_MatchesSamples(int width, int height, int bitDepth)
    {
        int maxSample = (1 << bitDepth) - 1;
        byte Sample(int x, int y) => (byte)(((x * 7) + (y * 3)) % (maxSample + 1));

        byte[] interlaced = BuildLowBitDepthPng(width, height, bitDepth, PngColorType.Grayscale, PngInterlaceMode.Adam7, Sample);
        byte[] nonInterlaced = BuildLowBitDepthPng(width, height, bitDepth, PngColorType.Grayscale, PngInterlaceMode.None, Sample);

        using Image<L8> expected = Image.Load<L8>(nonInterlaced);
        using Image<L8> actual = Image.Load<L8>(interlaced);

        ImageComparer.Exact.VerifySimilarity(expected, actual);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.Equal(Sample(x, y) * 255 / maxSample, actual[x, y].PackedValue);
            }
        }
    }

    [Theory]
    [InlineData(5, 9, 1)]
    [InlineData(17, 5, 1)]
    [InlineData(5, 9, 2)]
    [InlineData(17, 5, 2)]
    [InlineData(5, 9, 4)]
    [InlineData(17, 5, 4)]
    public void Decode_InterlacedLowBitDepthPalette_MatchesNonInterlaced(int width, int height, int bitDepth)
    {
        int maxIndex = (1 << bitDepth) - 1;
        byte Index(int x, int y) => (byte)(((x * 5) + y) % (maxIndex + 1));

        byte[] interlaced = BuildLowBitDepthPng(width, height, bitDepth, PngColorType.Palette, PngInterlaceMode.Adam7, Index);
        byte[] nonInterlaced = BuildLowBitDepthPng(width, height, bitDepth, PngColorType.Palette, PngInterlaceMode.None, Index);

        using Image<Rgba32> expected = Image.Load<Rgba32>(nonInterlaced);
        using Image<Rgba32> actual = Image.Load<Rgba32>(interlaced);

        ImageComparer.Exact.VerifySimilarity(expected, actual);
        Assert.Equal(new Rgba32(10, 20, 30, 255), actual[0, 0]);
    }

    /// <summary>
    /// Builds a 1, 2 or 4-bit grayscale or palette PNG. Palette entry k is (10 + 15k, 20 + 15k, 30 + 15k).
    /// </summary>
    private static byte[] BuildLowBitDepthPng(int width, int height, int bitDepth, PngColorType colorType, PngInterlaceMode interlaceMode, Func<int, int, byte> sample)
    {
        var chunks = new List<byte[]> { PngTestFileBuilder.Header(width, height, (byte)bitDepth, colorType, interlaceMode) };
        if (colorType == PngColorType.Palette)
        {
            int entries = 1 << bitDepth;
            byte[] palette = new byte[entries * 3];
            for (int i = 0; i < entries; i++)
            {
                palette[i * 3] = (byte)(10 + (15 * i));
                palette[(i * 3) + 1] = (byte)(20 + (15 * i));
                palette[(i * 3) + 2] = (byte)(30 + (15 * i));
            }

            chunks.Add(PngTestFileBuilder.Chunk("PLTE", palette));
        }

        byte[] scanlines = PngTestFileBuilder.Scanlines(width, height, interlaceMode == PngInterlaceMode.Adam7, sample, bitDepth);
        chunks.Add(PngTestFileBuilder.Chunk("IDAT", PngTestFileBuilder.Zlib(scanlines)));
        chunks.Add(PngTestFileBuilder.End());
        return PngTestFileBuilder.Build(chunks.ToArray());
    }

    // Each case covers every size, so a decoder that hangs ties up one thread per format rather than one per size.
    [Theory(Timeout = InterlaceTimeoutMilliseconds)]
    [InlineData(PngColorType.Grayscale, 1)]
    [InlineData(PngColorType.Grayscale, 2)]
    [InlineData(PngColorType.Grayscale, 4)]
    [InlineData(PngColorType.Grayscale, 8)]
    [InlineData(PngColorType.Grayscale, 16)]
    [InlineData(PngColorType.RgbWithAlpha, 8)]
    [InlineData(PngColorType.RgbWithAlpha, 16)]
    public async Task Decode_InterlacedSmallImages_MatchNonInterlaced(PngColorType colorType, int bitDepth)
    {
        await Task.Run(
            () =>
            {
                int samplesPerPixel = colorType == PngColorType.RgbWithAlpha ? 4 : 1;
                int Sample(int x, int y, int c) => InterlaceTestSample(x, y, c, bitDepth);

                var failures = new List<string>();
                for (int width = 1; width <= 9; width++)
                {
                    for (int height = 1; height <= 9; height++)
                    {
                        byte[] interlaced = BuildSamplePng(width, height, colorType, bitDepth, true, PngTestFileBuilder.Zlib(SampleScanlines(width, height, true, samplesPerPixel, bitDepth, Sample)));
                        byte[] nonInterlaced = BuildSamplePng(width, height, colorType, bitDepth, false, PngTestFileBuilder.Zlib(SampleScanlines(width, height, false, samplesPerPixel, bitDepth, Sample)));

                        using Image<Rgba64> expected = Image.Load<Rgba64>(nonInterlaced);
                        try
                        {
                            using Image<Rgba64> actual = Image.Load<Rgba64>(interlaced);
                            string mismatch = FirstPixelMismatch(expected, actual);
                            if (mismatch is not null)
                            {
                                failures.Add($"{width}x{height}: {mismatch}");
                            }
                        }
                        catch (ImageFormatException ex)
                        {
                            failures.Add($"{width}x{height}: {ex.Message}");
                        }
                    }
                }

                Assert.Empty(failures);
            },
            TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = InterlaceTimeoutMilliseconds)]
    [InlineData(1, 1, 1)]
    [InlineData(3, 2, 1)]
    [InlineData(4, 4, 1)]
    [InlineData(9, 9, 1)]
    [InlineData(1, 1, 8)]
    [InlineData(3, 5, 8)]
    [InlineData(4, 4, 8)]
    [InlineData(16, 16, 8)]
    public async Task Decode_InterlacedImageDataEndsEarly_KeepsCompleteRows(int width, int height, int bitDepth)
    {
        await Task.Run(
            () =>
            {
                int Sample(int x, int y, int c) => InterlaceTestSample(x, y, c, bitDepth);
                byte[] scanlines = SampleScanlines(width, height, true, 1, bitDepth, Sample);

                using Image<L16> complete = Image.Load<L16>(BuildSamplePng(width, height, PngColorType.Grayscale, bitDepth, true, PngTestFileBuilder.Zlib(scanlines)));

                // Start at one byte, as compressing no bytes writes no zlib header at all.
                for (int length = 1; length < scanlines.Length; length++)
                {
                    // A complete zlib stream that holds only the first scanline bytes, so the image data ends early.
                    byte[] png = BuildSamplePng(width, height, PngColorType.Grayscale, bitDepth, true, PngTestFileBuilder.Zlib(scanlines.AsSpan(0, length).ToArray()));

                    using Image<L16> image = Image.Load<L16>(png);
                    foreach ((int x, int y) in CompleteInterlacedPixels(width, height, bitDepth, length))
                    {
                        Assert.True(complete[x, y] == image[x, y], $"Length {length}, pixel ({x}, {y}): expected {complete[x, y]}, got {image[x, y]}.");
                    }
                }
            },
            TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = InterlaceTimeoutMilliseconds)]
    [InlineData(1)]
    [InlineData(8)]
    public async Task Decode_InterlacedImageDataEndsEarly_IgnoresLaterDataChunk(int bitDepth)
    {
        await Task.Run(
            () =>
            {
                const int size = 9;
                int Sample(int x, int y, int c) => InterlaceTestSample(x, y, c, bitDepth);
                byte[] scanlines = SampleScanlines(size, size, true, 1, bitDepth, Sample);
                byte[] header = PngTestFileBuilder.Header(size, size, (byte)bitDepth, PngColorType.Grayscale, PngInterlaceMode.Adam7);

                var failures = new List<string>();
                for (int length = 1; length < scanlines.Length; length++)
                {
                    // The image data ends early. A second zlib stream in a later data chunk cannot continue it,
                    // as the decoder has no way to know which pass and row that stream starts at.
                    byte[] first = PngTestFileBuilder.Zlib(scanlines.AsSpan(0, length).ToArray());
                    byte[] rest = PngTestFileBuilder.Zlib(scanlines.AsSpan(length).ToArray());
                    byte[] truncated = PngTestFileBuilder.Build(header, PngTestFileBuilder.Chunk("IDAT", first), PngTestFileBuilder.End());
                    byte[] split = PngTestFileBuilder.Build(
                        header,
                        PngTestFileBuilder.Chunk("IDAT", first),
                        PngTestFileBuilder.Chunk("tEXt", "Comment\0between the data chunks"u8),
                        PngTestFileBuilder.Chunk("IDAT", rest),
                        PngTestFileBuilder.End());

                    using Image<L16> expected = Image.Load<L16>(truncated);
                    using Image<L16> actual = Image.Load<L16>(split);
                    string mismatch = FirstPixelMismatch(expected, actual);
                    if (mismatch is not null)
                    {
                        failures.Add($"Length {length}: {mismatch}");
                    }
                }

                Assert.Empty(failures);
            },
            TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = InterlaceTimeoutMilliseconds)]
    [InlineData(1, 1, 1)]
    [InlineData(4, 4, 1)]
    [InlineData(9, 9, 8)]
    [InlineData(16, 16, 8)]
    public async Task Decode_InterlacedCompressedDataEndsEarly_DoesNotHang(int width, int height, int bitDepth)
    {
        await Task.Run(
            () =>
            {
                int Sample(int x, int y, int c) => InterlaceTestSample(x, y, c, bitDepth);
                byte[] zlib = PngTestFileBuilder.Zlib(SampleScanlines(width, height, true, 1, bitDepth, Sample));

                // Start after the two byte zlib header, so the cut falls in the compressed scanlines.
                for (int length = 2; length < zlib.Length; length++)
                {
                    byte[] png = BuildSamplePng(width, height, PngColorType.Grayscale, bitDepth, true, zlib.AsSpan(0, length).ToArray());

                    try
                    {
                        using Image<L16> image = Image.Load<L16>(png);
                        Assert.Equal(width, image.Width);
                        Assert.Equal(height, image.Height);
                    }
                    catch (InvalidImageContentException)
                    {
                        // Reporting the truncated data as invalid is also acceptable.
                    }
                }
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Returns the first pixel at which two images of the same size differ, or <see langword="null"/> if they match.
    /// </summary>
    private static string FirstPixelMismatch<TPixel>(Image<TPixel> expected, Image<TPixel> actual)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            return $"size {actual.Width}x{actual.Height}, expected {expected.Width}x{expected.Height}";
        }

        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                if (!expected[x, y].Equals(actual[x, y]))
                {
                    return $"pixel ({x}, {y}) is {actual[x, y]}, expected {expected[x, y]}";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a sample value that varies with the pixel position and channel and fits the bit depth.
    /// </summary>
    private static int InterlaceTestSample(int x, int y, int channel, int bitDepth)
        => (int)((((uint)((x * 73) + (y * 151) + (channel * 37) + 11) * 2654435761u) >> 8) & ((1u << bitDepth) - 1));

    /// <summary>
    /// Returns the pixels of an interlaced image whose scanlines lie wholly within the first bytes of the image data.
    /// </summary>
    private static IEnumerable<(int X, int Y)> CompleteInterlacedPixels(int width, int height, int bitDepth, int length)
    {
        int offset = 0;
        for (int pass = 0; pass < 7; pass++)
        {
            int columns = Adam7.ComputeColumns(width, pass);
            if (columns == 0)
            {
                continue;
            }

            int scanlineLength = 1 + (((columns * bitDepth) + 7) / 8);
            for (int y = Adam7.FirstRow[pass]; y < height; y += Adam7.RowIncrement[pass])
            {
                offset += scanlineLength;
                if (offset > length)
                {
                    yield break;
                }

                for (int x = Adam7.FirstColumn[pass]; x < width; x += Adam7.ColumnIncrement[pass])
                {
                    yield return (x, y);
                }
            }
        }
    }

    /// <summary>
    /// Builds a grayscale or RGBA PNG with the given compressed image data in one IDAT chunk.
    /// </summary>
    private static byte[] BuildSamplePng(int width, int height, PngColorType colorType, int bitDepth, bool interlaced, byte[] zlib)
        => PngTestFileBuilder.Build(
            PngTestFileBuilder.Header(width, height, (byte)bitDepth, colorType, interlaced ? PngInterlaceMode.Adam7 : PngInterlaceMode.None),
            PngTestFileBuilder.Chunk("IDAT", zlib),
            PngTestFileBuilder.End());

    /// <summary>
    /// Builds the unfiltered scanlines of an image, laid out as in the PNG specification, section 8.2.
    /// </summary>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <param name="interlaced">Whether to lay the scanlines out in the seven Adam7 passes.</param>
    /// <param name="samplesPerPixel">The number of samples in each pixel.</param>
    /// <param name="bitDepth">The bits per sample: 1, 2, 4, 8 or 16. Samples under 8 bits are packed from the high bits down.</param>
    /// <param name="sample">Returns the sample for channel c of the pixel at x, y.</param>
    private static byte[] SampleScanlines(int width, int height, bool interlaced, int samplesPerPixel, int bitDepth, Func<int, int, int, int> sample)
    {
        int[] firstColumn = interlaced ? new[] { 0, 4, 0, 2, 0, 1, 0 } : new[] { 0 };
        int[] firstRow = interlaced ? new[] { 0, 0, 4, 0, 2, 0, 1 } : new[] { 0 };
        int[] columnIncrement = interlaced ? new[] { 8, 8, 4, 4, 2, 2, 1 } : new[] { 1 };
        int[] rowIncrement = interlaced ? new[] { 8, 8, 8, 4, 4, 2, 2 } : new[] { 1 };

        var data = new List<byte>();
        for (int pass = 0; pass < firstColumn.Length; pass++)
        {
            if (firstColumn[pass] >= width)
            {
                // Passes with no columns contribute no scanlines, not even filter bytes.
                continue;
            }

            for (int y = firstRow[pass]; y < height; y += rowIncrement[pass])
            {
                data.Add(0); // Filter type None.
                int packed = 0;
                int packedBits = 0;
                for (int x = firstColumn[pass]; x < width; x += columnIncrement[pass])
                {
                    for (int c = 0; c < samplesPerPixel; c++)
                    {
                        int value = sample(x, y, c);
                        if (bitDepth == 16)
                        {
                            data.Add((byte)(value >> 8));
                            data.Add((byte)value);
                            continue;
                        }

                        packed = (packed << bitDepth) | value;
                        packedBits += bitDepth;
                        if (packedBits == 8)
                        {
                            data.Add((byte)packed);
                            packed = 0;
                            packedBits = 0;
                        }
                    }
                }

                if (packedBits > 0)
                {
                    // Pad the last byte of the row with zero bits.
                    data.Add((byte)(packed << (8 - packedBits)));
                }
            }
        }

        return data.ToArray();
    }
}
