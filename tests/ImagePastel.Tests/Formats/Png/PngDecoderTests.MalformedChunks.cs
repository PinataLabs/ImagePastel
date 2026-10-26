// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Png;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests.Formats.Png;

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

    [Fact]
    public void Decode_IccpChunk_ReadsProfile()
    {
        byte[] iccBytes = GetGrayIccProfileBytes();
        byte[] png = BuildGrayscalePng(PngTestFileBuilder.Chunk("iCCP", IccpChunkData("ICC Profile", 0, PngTestFileBuilder.Zlib(iccBytes))));

        using (Image image = Image.Load(png))
        {
            Assert.Equal(iccBytes, image.Metadata.IccProfile?.ToByteArray());
        }

        Assert.Equal(iccBytes, Image.Identify(png).Metadata.IccProfile?.ToByteArray());
    }

    [Fact]
    public void Decode_IccpChunk_IgnoreMetadata_DoesNotReadProfile()
    {
        byte[] iccBytes = GetGrayIccProfileBytes();
        byte[] png = BuildGrayscalePng(PngTestFileBuilder.Chunk("iCCP", IccpChunkData("ICC Profile", 0, PngTestFileBuilder.Zlib(iccBytes))));
        var decoder = new PngDecoder { IgnoreMetadata = true };

        using (Image image = Image.Load(png, decoder))
        {
            Assert.Null(image.Metadata.IccProfile);
        }

        IImageInfo info = decoder.Identify(Configuration.Default, new MemoryStream(png), TestContext.Current.CancellationToken);
        Assert.Null(info.Metadata.IccProfile);
    }

    [Theory]
    [InlineData("no separator")]
    [InlineData("empty name")]
    [InlineData("long name")]
    [InlineData("no compression method")]
    [InlineData("unknown compression method")]
    [InlineData("corrupt zlib header")]
    [InlineData("corrupt deflate data")]
    [InlineData("truncated zlib stream")]
    [InlineData("invalid profile")]
    [InlineData("zero-filled profile")]
    public void Decode_MalformedIccpChunk_IsIgnored(string defect)
    {
        byte[] compressed = PngTestFileBuilder.Zlib(GetGrayIccProfileBytes());
        byte[] data = defect switch
        {
            "no separator" => Encoding.Latin1.GetBytes("ICC Profile"),
            "empty name" => IccpChunkData(string.Empty, 0, compressed),
            "long name" => IccpChunkData(new string('a', 80), 0, compressed),
            "no compression method" => Encoding.Latin1.GetBytes("ICC Profile\0"),
            "unknown compression method" => IccpChunkData("ICC Profile", 1, compressed),
            "corrupt zlib header" => IccpChunkData("ICC Profile", 0, new byte[] { 0xFF, 0xFF, 1, 2, 3 }),
            "corrupt deflate data" => IccpChunkData("ICC Profile", 0, new byte[] { 0x78, 0x9C, 0xFF, 0xFF, 0xFF, 0xFF }),
            "truncated zlib stream" => IccpChunkData("ICC Profile", 0, compressed.Take(compressed.Length / 2).ToArray()),
            "invalid profile" => IccpChunkData("ICC Profile", 0, PngTestFileBuilder.Zlib(new byte[] { 1, 2, 3, 4 })),
            "zero-filled profile" => IccpChunkData("ICC Profile", 0, PngTestFileBuilder.Zlib(new byte[1_000_000])),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };

        byte[] png = BuildGrayscalePng(PngTestFileBuilder.Chunk("iCCP", data));

        using (Image<L8> image = Image.Load<L8>(png))
        {
            Assert.Null(image.Metadata.IccProfile);
            Assert.Equal(0x80, image[0, 0].PackedValue);
        }

        IImageInfo info = Image.Identify(png);
        Assert.Equal(1, info.Width);
        Assert.Null(info.Metadata.IccProfile);
    }

    [Fact]
    public void Decode_SecondIccpChunk_IsIgnored()
    {
        byte[] first = GetGrayIccProfileBytes();
        byte[] second = WithDataColorSpace(first, "RGB ");
        byte[] png = BuildGrayscalePng(
            PngTestFileBuilder.Chunk("iCCP", IccpChunkData("first", 0, PngTestFileBuilder.Zlib(first))),
            PngTestFileBuilder.Chunk("iCCP", IccpChunkData("second", 0, PngTestFileBuilder.Zlib(second))));

        using (Image image = Image.Load(png))
        {
            Assert.Equal(first, image.Metadata.IccProfile?.ToByteArray());
        }

        Assert.Equal(first, Image.Identify(png).Metadata.IccProfile?.ToByteArray());
    }

    [Fact]
    public void Decode_IccpChunkAfterImageData_IsIgnored()
    {
        byte[] png = PngTestFileBuilder.Build(
            PngTestFileBuilder.Header(1, 1, 8, PngColorType.Grayscale),
            PngTestFileBuilder.Chunk("IDAT", PngTestFileBuilder.Zlib(new byte[] { 0, 0x80 })),
            PngTestFileBuilder.Chunk("iCCP", IccpChunkData("ICC Profile", 0, PngTestFileBuilder.Zlib(GetGrayIccProfileBytes()))),
            PngTestFileBuilder.End());

        using (Image image = Image.Load(png))
        {
            Assert.Null(image.Metadata.IccProfile);
        }

        Assert.Null(Image.Identify(png).Metadata.IccProfile);
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

    private static byte[] IccpChunkData(string profileName, byte compressionMethod, byte[] compressedProfile)
        => Encoding.Latin1.GetBytes(profileName)
            .Append((byte)0)
            .Append(compressionMethod)
            .Concat(compressedProfile)
            .ToArray();

    /// <summary>
    /// Returns a copy of the profile with its data color space field (header offset 16) replaced.
    /// </summary>
    private static byte[] WithDataColorSpace(byte[] profile, string colorSpace)
    {
        byte[] copy = (byte[])profile.Clone();
        Encoding.ASCII.GetBytes(colorSpace, 0, 4, copy, 16);
        return copy;
    }

    // The test files are grayscale, so their iCCP profile must be a gray profile.
    private static byte[] GetGrayIccProfileBytes()
        => File.ReadAllBytes(TestFile.GetInputFileFullPath(TestImages.Icc.LittleCmsGray22V4));
}
