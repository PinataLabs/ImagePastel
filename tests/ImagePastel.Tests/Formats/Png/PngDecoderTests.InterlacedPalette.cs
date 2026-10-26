// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Png;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using Xunit;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests.Formats.Png;

public partial class PngDecoderTests
{
    [Fact]
    public void Decode_InterlacedPalette_IndexBeyondPalette_UsesLastPaletteEntry()
    {
        byte[] png = BuildPalettePng(9, 9, PngInterlaceMode.Adam7, paletteLength: 1, transparencyLength: 0, (_, _) => 255);

        using Image<Rgba32> image = Image.Load<Rgba32>(png);

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Assert.Equal(new Rgba32(10, 20, 30, 255), image[x, y]);
            }
        }
    }

    [Theory]
    [InlineData(9, 9, 1, 0, false)]
    [InlineData(16, 16, 1, 0, true)]
    [InlineData(6, 3, 2, 18, true)]
    [InlineData(16, 16, 4, 256, true)]
    public void Decode_InterlacedPalette_MatchesNonInterlaced(int width, int height, int paletteLength, int transparencyLength, bool sequentialIndices)
    {
        byte Index(int x, int y) => sequentialIndices ? (byte)((y * width) + x) : (byte)255;

        byte[] interlaced = BuildPalettePng(width, height, PngInterlaceMode.Adam7, paletteLength, transparencyLength, Index);
        byte[] nonInterlaced = BuildPalettePng(width, height, PngInterlaceMode.None, paletteLength, transparencyLength, Index);

        using Image<Rgba32> expected = Image.Load<Rgba32>(nonInterlaced);
        using Image<Rgba32> actual = Image.Load<Rgba32>(interlaced);

        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    [Theory]
    [InlineData(PngInterlaceMode.None)]
    [InlineData(PngInterlaceMode.Adam7)]
    public void Decode_PaletteWithoutPaletteChunk_ThrowsInvalidImageContentException(PngInterlaceMode interlaceMode)
    {
        byte[] png = BuildPalettePng(9, 9, interlaceMode, paletteLength: 0, transparencyLength: 0, (_, _) => 0);

        Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgba32>(png));
    }

    [Theory]
    [InlineData(PngInterlaceMode.None)]
    [InlineData(PngInterlaceMode.Adam7)]
    public void Decode_PaletteChunkShorterThanOneEntry_ThrowsInvalidImageContentException(PngInterlaceMode interlaceMode)
    {
        byte[] png = PngTestFileBuilder.Build(
            PngTestFileBuilder.Header(9, 9, 8, PngColorType.Palette, interlaceMode),
            PngTestFileBuilder.Chunk("PLTE", new byte[] { 10, 20 }),
            PngTestFileBuilder.Chunk("IDAT", PngTestFileBuilder.Zlib(PngTestFileBuilder.Scanlines(9, 9, interlaceMode == PngInterlaceMode.Adam7, (_, _) => 0))),
            PngTestFileBuilder.End());

        Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgba32>(png));
    }

    /// <summary>
    /// Builds an 8-bit palette PNG whose palette entry k is (10 + 20k, 20 + 20k, 30 + 20k) and whose tRNS entry k is 13k.
    /// A palette length of 0 omits the PLTE chunk.
    /// </summary>
    private static byte[] BuildPalettePng(int width, int height, PngInterlaceMode interlaceMode, int paletteLength, int transparencyLength, Func<int, int, byte> index)
    {
        var chunks = new List<byte[]> { PngTestFileBuilder.Header(width, height, 8, PngColorType.Palette, interlaceMode) };

        if (paletteLength > 0)
        {
            byte[] palette = new byte[paletteLength * 3];
            for (int k = 0; k < paletteLength; k++)
            {
                palette[k * 3] = (byte)(10 + (20 * k));
                palette[(k * 3) + 1] = (byte)(20 + (20 * k));
                palette[(k * 3) + 2] = (byte)(30 + (20 * k));
            }

            chunks.Add(PngTestFileBuilder.Chunk("PLTE", palette));
        }

        if (transparencyLength > 0)
        {
            byte[] alpha = new byte[transparencyLength];
            for (int k = 0; k < transparencyLength; k++)
            {
                alpha[k] = (byte)(13 * k);
            }

            chunks.Add(PngTestFileBuilder.Chunk("tRNS", alpha));
        }

        byte[] scanlines = PngTestFileBuilder.Scanlines(width, height, interlaceMode == PngInterlaceMode.Adam7, index);
        chunks.Add(PngTestFileBuilder.Chunk("IDAT", PngTestFileBuilder.Zlib(scanlines)));
        chunks.Add(PngTestFileBuilder.End());
        return PngTestFileBuilder.Build(chunks.ToArray());
    }
}
