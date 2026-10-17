// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata;
using Xunit;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Tests.Formats.Png;

public partial class PngDecoderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task Decode_TruncatedPhysicalChunk_IsIgnored(int length)
    {
        // A pHYs chunk needs 9 bytes; fill the truncated data with a resolution that would be visible if read.
        byte[] data = new byte[length];
        data.AsSpan().Fill(0x01);
        byte[] png = BuildGrayscalePng(PngTestFileBuilder.Chunk("pHYs", data));
        var expected = new ImageMetadata();

        using (Image image = Image.Load(png))
        {
            AssertDefaultResolution(expected, image.Metadata);
        }

        AssertDefaultResolution(expected, Image.Identify(png).Metadata);

        using (Image image = await Image.LoadAsync(new MemoryStream(png), TestContext.Current.CancellationToken))
        {
            AssertDefaultResolution(expected, image.Metadata);
        }

        IImageInfo info = await Image.IdentifyAsync(new MemoryStream(png), TestContext.Current.CancellationToken);
        AssertDefaultResolution(expected, info.Metadata);
    }

    [Theory]
    [InlineData("zTXt", "abc\0")]
    [InlineData("iTXt", "abc\0")]
    [InlineData("iTXt", "abc\0\0")]
    [InlineData("iTXt", "abc\0\0\0en\0")]
    [InlineData("iTXt", "abc\0\0\0en\0xyz")]
    public void Decode_TruncatedTextChunk_IsSkipped(string chunkType, string chunkData)
    {
        byte[] png = BuildGrayscalePng(PngTestFileBuilder.Chunk(chunkType, Encoding.Latin1.GetBytes(chunkData)));

        using (Image image = Image.Load(png))
        {
            Assert.Equal(new Size(1, 1), image.Size());
            Assert.Empty(image.Metadata.GetPngMetadata().TextData);
        }

        IImageInfo info = Image.Identify(png);
        Assert.Equal(1, info.Width);
        Assert.Empty(info.Metadata.GetPngMetadata().TextData);
    }

    [Fact]
    public void Decode_WellFormedTextChunks_AreRead()
    {
        byte[] compressedText = Encoding.Latin1.GetBytes("zkey\0\0").Concat(PngTestFileBuilder.Zlib(Encoding.Latin1.GetBytes("zvalue"))).ToArray();
        byte[] png = BuildGrayscalePng(
            PngTestFileBuilder.Chunk("zTXt", compressedText),
            PngTestFileBuilder.Chunk("iTXt", Encoding.UTF8.GetBytes("ikey\0\0\0en\0translated\0ivalue")));

        using Image image = Image.Load(png);
        IList<PngTextData> text = image.Metadata.GetPngMetadata().TextData;

        Assert.Equal(2, text.Count);
        Assert.Equal(new PngTextData("zkey", "zvalue", string.Empty, string.Empty), text[0]);
        Assert.Equal(new PngTextData("ikey", "ivalue", "en", "translated"), text[1]);
    }

    private static void AssertDefaultResolution(ImageMetadata expected, ImageMetadata actual)
    {
        Assert.Equal(expected.ResolutionUnits, actual.ResolutionUnits);
        Assert.Equal(expected.HorizontalResolution, actual.HorizontalResolution);
        Assert.Equal(expected.VerticalResolution, actual.VerticalResolution);
    }

    /// <summary>
    /// Builds a valid 1x1 8-bit grayscale PNG with the given chunks placed before the image data.
    /// </summary>
    private static byte[] BuildGrayscalePng(params byte[][] ancillaryChunks)
    {
        var chunks = new List<byte[]> { PngTestFileBuilder.Header(1, 1, 8, PngColorType.Grayscale) };
        chunks.AddRange(ancillaryChunks);
        chunks.Add(PngTestFileBuilder.Chunk("IDAT", PngTestFileBuilder.Zlib(new byte[] { 0, 0x80 })));
        chunks.Add(PngTestFileBuilder.End());
        return PngTestFileBuilder.Build(chunks.ToArray());
    }
}
