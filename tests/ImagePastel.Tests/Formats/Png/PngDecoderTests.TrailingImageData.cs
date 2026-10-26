// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Linq;
using System.Text;
using PinataLabs.ImagePastel.Formats.Png;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests.Formats.Png;

public partial class PngDecoderTests
{
    // Longer than the buffer the deflate stream reads with, so the image is complete before the data is.
    private const int TrailingImageDataLength = 20000;

    [Theory]
    [InlineData(PngInterlaceMode.None, false)]
    [InlineData(PngInterlaceMode.None, true)]
    [InlineData(PngInterlaceMode.Adam7, false)]
    [InlineData(PngInterlaceMode.Adam7, true)]
    public void Decode_ImageDataAfterLastRow_IsSkipped(PngInterlaceMode interlaceMode, bool trailingDataInSeparateChunks)
    {
        byte Sample(int x, int y) => (byte)((y * 32) + x);
        byte[] zlib = PngTestFileBuilder.Zlib(PngTestFileBuilder.Scanlines(8, 8, interlaceMode == PngInterlaceMode.Adam7, Sample));
        byte[] trailing = new byte[TrailingImageDataLength];

        byte[][] dataChunks = trailingDataInSeparateChunks
            ? new[] { PngTestFileBuilder.Chunk("IDAT", zlib), PngTestFileBuilder.Chunk("IDAT", trailing), PngTestFileBuilder.Chunk("IDAT", trailing) }
            : new[] { PngTestFileBuilder.Chunk("IDAT", zlib.Concat(trailing).ToArray()) };

        byte[] png = PngTestFileBuilder.Build(
            new[] { PngTestFileBuilder.Header(8, 8, 8, PngColorType.Grayscale, interlaceMode) }
                .Concat(dataChunks)
                .Concat(new[]
                {
                    PngTestFileBuilder.Chunk("tEXt", Encoding.Latin1.GetBytes("Comment\0after the image data")),
                    PngTestFileBuilder.End(),
                })
                .ToArray());

        using Image<L8> image = Image.Load<L8>(png);

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                Assert.Equal(Sample(x, y), image[x, y].PackedValue);
            }
        }

        PngTextData text = Assert.Single(image.Metadata.GetPngMetadata().TextData);
        Assert.Equal(new PngTextData("Comment", "after the image data", string.Empty, string.Empty), text);
    }
}
