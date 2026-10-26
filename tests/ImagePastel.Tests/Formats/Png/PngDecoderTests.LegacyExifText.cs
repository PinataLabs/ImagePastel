// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Text;
using Xunit;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Tests.Formats.Png;

public partial class PngDecoderTests
{
    [Theory]
    [InlineData("ab")]
    [InlineData("exif")]
    [InlineData("\nexif\n  12")]
    [InlineData("\nexif\n  abc\n457869660000")]
    [InlineData("\nexif\n  -1\n457869660000")]
    [InlineData("\nexif\n  99999999999\n457869660000")]
    [InlineData("\nexif\n  2000000000\n457869660000")]
    [InlineData("\nexif\n  10\n457869660000")]
    [InlineData("\nexif\n  8\n4578696600")]
    public void Decode_MalformedLegacyExifText_IsIgnored(string value)
    {
        byte[] text = Encoding.Latin1.GetBytes("Raw profile type exif\0" + value);
        byte[] png = BuildGrayscalePng(PngTestFileBuilder.Chunk("tEXt", text));

        using (Image image = Image.Load(png))
        {
            Assert.Equal(new Size(1, 1), image.Size());
            Assert.Null(image.Metadata.ExifProfile);
        }

        IImageInfo info = Image.Identify(png);
        Assert.Equal(1, info.Width);
        Assert.Null(info.Metadata.ExifProfile);
    }

    [Theory]
    [InlineData("\nexif\n  20\n45786966000049492A0008000000000000000000\n")]
    [InlineData("\nexif\n  20\r\n45786966000049492A0008000000000000000000\n")]
    [InlineData("\nexif\n  20  \n45786966000049492A0008000000000000000000\n")]
    public void Decode_LegacyExifTextWithWhiteSpaceAfterLength_ReadsProfile(string value)
    {
        byte[] text = Encoding.Latin1.GetBytes("Raw profile type exif\0" + value);
        byte[] png = BuildGrayscalePng(PngTestFileBuilder.Chunk("tEXt", text));

        using Image image = Image.Load(png);
        Assert.NotNull(image.Metadata.ExifProfile);
    }
}
