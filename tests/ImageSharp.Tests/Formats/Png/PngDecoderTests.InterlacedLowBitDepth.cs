// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Tests.Formats.Png;

public partial class PngDecoderTests
{
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
}
