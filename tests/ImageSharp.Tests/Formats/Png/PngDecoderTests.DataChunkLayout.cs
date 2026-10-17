// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Tests.Formats.Png;

public partial class PngDecoderTests
{
    public enum DataChunkLayout
    {
        SingleChunk,
        LeadingEmptyChunk,
        FirstChunkOneByte,
        FirstChunkTwoBytes,
        EveryChunkOneByte,
        EmptyChunkBetween,
        TrailingEmptyChunk,
    }

    [Theory]
    [InlineData(40, 30, DataChunkLayout.SingleChunk)]
    [InlineData(40, 30, DataChunkLayout.LeadingEmptyChunk)]
    [InlineData(40, 30, DataChunkLayout.FirstChunkOneByte)]
    [InlineData(40, 30, DataChunkLayout.FirstChunkTwoBytes)]
    [InlineData(40, 30, DataChunkLayout.EveryChunkOneByte)]
    [InlineData(40, 30, DataChunkLayout.EmptyChunkBetween)]
    [InlineData(40, 30, DataChunkLayout.TrailingEmptyChunk)]
    [InlineData(128, 128, DataChunkLayout.EveryChunkOneByte)]
    public void Decode_ZlibStreamSplitAcrossDataChunks_DecodesPixelExact(int width, int height, DataChunkLayout layout)
    {
        using Image<Rgba32> expected = CreateNoiseImage(width, height);
        byte[] encoded = EncodeWithDataChunkLayout(expected, layout);

        using Image<Rgba32> actual = Image.Load<Rgba32>(encoded);

        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    [Fact]
    public void Decode_InvalidZlibMethodSplitAcrossDataChunks_ThrowsImageFormatException()
    {
        // CMF 0x79 names compression method 9, which RFC 1950 does not define.
        byte[] png = BuildRgbaPng(new byte[] { 0x79 }, new byte[] { 0xDA, 0x01, 0x02, 0x03 });

        Assert.ThrowsAny<ImageFormatException>(() => Image.Load<Rgba32>(png));
    }

    [Theory]
    [InlineData(new byte[] { 0x78 })]
    [InlineData(new byte[] { 0x78, 0xBB })]
    [InlineData(new byte[] { 0x78, 0xBB, 0x01 })]
    [InlineData(new byte[] { 0x78, 0xDA, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    public void Decode_TruncatedZlibHeaderOrCorruptData_ThrowsImageFormatException(byte[] zlibData)
    {
        // Each case is decoded as one data chunk and as one data chunk per byte.
        Assert.ThrowsAny<ImageFormatException>(() => Image.Load<Rgba32>(BuildRgbaPng(zlibData)));
        Assert.ThrowsAny<ImageFormatException>(() => Image.Load<Rgba32>(BuildRgbaPng(zlibData.Select(b => new[] { b }).ToArray())));
    }

    [Theory]
    [InlineData(new byte[] { 0x78, 0xDA })]
    [InlineData(new byte[] { 0x78, 0xBB, 0x01, 0x02, 0x03, 0x04 })]
    public void Decode_ZlibDataTruncatedAfterHeader_DecodesPartialImage(byte[] zlibData)
    {
        // As with other truncated image data, the pixels decoded so far are kept.
        using Image<Rgba32> single = Image.Load<Rgba32>(BuildRgbaPng(zlibData));
        using Image<Rgba32> split = Image.Load<Rgba32>(BuildRgbaPng(zlibData.Select(b => new[] { b }).ToArray()));

        Assert.Equal(new Size(4, 4), single.Size());
        Assert.Equal(new Size(4, 4), split.Size());
    }

    private static Image<Rgba32> CreateNoiseImage(int width, int height)
    {
        var random = new Random(width * height);
        var image = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image[x, y] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            }
        }

        return image;
    }

    private static byte[] EncodeWithDataChunkLayout(Image<Rgba32> image, DataChunkLayout layout)
    {
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder { ColorType = PngColorType.RgbWithAlpha, BitDepth = PngBitDepth.Bit8 });
        List<(string Type, byte[] Data)> chunks = PngTestFileBuilder.ReadChunks(stream.ToArray());

        byte[] zlib = chunks.Where(c => c.Type == "IDAT").SelectMany(c => c.Data).ToArray();
        byte[][] segments = layout switch
        {
            DataChunkLayout.SingleChunk => new[] { zlib },
            DataChunkLayout.LeadingEmptyChunk => new[] { Array.Empty<byte>(), zlib },
            DataChunkLayout.FirstChunkOneByte => new[] { zlib[..1], zlib[1..] },
            DataChunkLayout.FirstChunkTwoBytes => new[] { zlib[..2], zlib[2..] },
            DataChunkLayout.EveryChunkOneByte => zlib.Select(b => new[] { b }).ToArray(),
            DataChunkLayout.EmptyChunkBetween => new[] { zlib[..(zlib.Length / 2)], Array.Empty<byte>(), zlib[(zlib.Length / 2)..] },
            DataChunkLayout.TrailingEmptyChunk => new[] { zlib, Array.Empty<byte>() },
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };

        var output = new List<byte[]>();
        foreach ((string type, byte[] data) in chunks)
        {
            if (type == "IDAT")
            {
                continue;
            }

            if (type == "IEND")
            {
                output.AddRange(segments.Select(s => PngTestFileBuilder.Chunk("IDAT", s)));
            }

            output.Add(PngTestFileBuilder.Chunk(type, data));
        }

        return PngTestFileBuilder.Build(output.ToArray());
    }

    private static byte[] BuildRgbaPng(params byte[][] dataSegments)
    {
        var chunks = new List<byte[]> { PngTestFileBuilder.Header(4, 4, 8, PngColorType.RgbWithAlpha) };
        chunks.AddRange(dataSegments.Select(s => PngTestFileBuilder.Chunk("IDAT", s)));
        chunks.Add(PngTestFileBuilder.End());
        return PngTestFileBuilder.Build(chunks.ToArray());
    }
}
