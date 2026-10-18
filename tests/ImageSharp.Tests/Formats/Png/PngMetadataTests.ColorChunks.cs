// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Tests.ColorManagement;
using Xunit;
using B = SixLabors.ImageSharp.Tests.Formats.Png.PngTestFileBuilder;

namespace SixLabors.ImageSharp.Tests.Formats.Png;

/// <summary>
/// The pixel profile of PNG files that describe their colour with <c>cICP</c>, <c>sRGB</c>, <c>cHRM</c> and
/// <c>gAMA</c> instead of, or as well as, <c>iCCP</c>. Built profiles are compared with the profiles Little
/// CMS writes for the same spaces (<c>tests/Images/Input/Icc/README.md</c>), or, where Little CMS has none,
/// with <see cref="IccProfileFactory"/> (itself checked against Little CMS), within two s15Fixed16 steps.
/// Precedence follows the PNG specification, third edition, section 4.3: cICP, iCCP, sRGB, cHRM/gAMA.
/// </summary>
public partial class PngMetadataTests
{
    // Adobe RGB (1998): D65 white and its primaries, and the gAMA (100000 / gamma) of its 563/256 gamma.
    private static readonly byte[] AdobeRgbChrm = B.Chromaticities(31270, 32900, 64000, 33000, 21000, 71000, 15000, 6000);

    private static readonly byte[] AdobeRgbGama = B.Gamma(45471);

    // D50 white (Little CMS's cmsD50_xyY) and ProPhoto primaries.
    private static readonly byte[] ProPhotoChrm = B.Chromaticities(34570, 35854, 73470, 26530, 15960, 84040, 3660, 10);

    // D65 white and the BT.709 (sRGB) primaries.
    private static readonly byte[] Rec709Chrm = B.Chromaticities(31270, 32900, 64000, 33000, 30000, 60000, 15000, 6000);

    // 1 / 2.2.
    private static readonly byte[] Gama22 = B.Gamma(45455);

    private static readonly RgbPrimariesChromaticityCoordinates Rec709Primaries = new(
        new CieXyChromaticityCoordinates(0.64F, 0.33F),
        new CieXyChromaticityCoordinates(0.30F, 0.60F),
        new CieXyChromaticityCoordinates(0.15F, 0.06F));

    private static readonly CieXyChromaticityCoordinates D65 = new(0.3127F, 0.3290F);

    // The four sources of colour information in precedence order, for the precedence tests.
    private static readonly string[] Sources = { "cICP", "iCCP", "sRGB", "cHRM+gAMA" };

    private static readonly Dictionary<string, (PngColorType ColorType, byte[][] Chunks, Func<IccProfile> Expected)> SingleChunkCases = new()
    {
        ["cHRM+gAMA"] = (PngColorType.Rgb, new[] { AdobeRgbChrm, AdobeRgbGama }, () => Lcms(TestImages.Icc.LittleCmsAdobeRgbV4)),
        ["gAMA+cHRM, gAMA first"] = (PngColorType.Rgb, new[] { AdobeRgbGama, AdobeRgbChrm }, () => Lcms(TestImages.Icc.LittleCmsAdobeRgbV4)),
        ["cHRM+gAMA, palette"] = (PngColorType.Palette, new[] { AdobeRgbChrm, AdobeRgbGama }, () => Lcms(TestImages.Icc.LittleCmsAdobeRgbV4)),
        ["cHRM+gAMA, gray"] = (PngColorType.Grayscale, new[] { ProPhotoChrm, Gama22 }, () => Lcms(TestImages.Icc.LittleCmsGray22V4)),
        ["cHRM+gAMA 1"] = (PngColorType.Rgb, new[] { Rec709Chrm, B.Gamma(100000) }, () => Lcms(TestImages.Icc.LittleCmsLinearSrgbV4)),

        // gAMA without cHRM does not say which primaries, so it builds no profile; PngMetadata.Gamma keeps it.
        ["gAMA, gray"] = (PngColorType.Grayscale, new[] { Gama22 }, () => null),
        ["gAMA"] = (PngColorType.Rgb, new[] { Gama22 }, () => null),
        ["gAMA 1"] = (PngColorType.Rgb, new[] { B.Gamma(100000) }, () => null),
        ["gAMA 0"] = (PngColorType.Rgb, new[] { B.Gamma(0) }, () => null),
        ["gAMA, short"] = (PngColorType.Rgb, new[] { B.Chunk("gAMA", new byte[] { 0, 0, 0xB1 }) }, () => null),
        ["cHRM"] = (PngColorType.Rgb, new[] { AdobeRgbChrm }, () => null),
        ["cHRM, short"] = (PngColorType.Rgb, new[] { B.Chunk("cHRM", new byte[31]), Gama22 }, () => null),
        ["cHRM, white y 0"] = (PngColorType.Rgb, new[] { B.Chromaticities(31270, 0, 64000, 33000, 30000, 60000, 15000, 6000), Gama22 }, () => null),
        ["cHRM+gAMA, BT.709"] = (PngColorType.Rgb, new[] { Rec709Chrm, Gama22 }, () => Lcms(TestImages.Icc.LittleCmsRec709Gamma22V4)),
        ["sRGB"] = (PngColorType.Rgb, new[] { B.StandardRgb() }, () => null),
        ["sRGB, gray"] = (PngColorType.Grayscale, new[] { B.StandardRgb() }, () => null),
        ["cICP sRGB"] = (PngColorType.Rgb, new[] { B.Cicp(1, 13) }, () => null),
        ["cICP linear sRGB"] = (PngColorType.Rgb, new[] { B.Cicp(1, 8) }, () => Lcms(TestImages.Icc.LittleCmsLinearSrgbV4)),
        ["cICP Display P3"] = (PngColorType.Rgb, new[] { B.Cicp(12, 13) }, () => Lcms(TestImages.Icc.LittleCmsDisplayP3V4)),
        ["cICP BT.2020"] = (PngColorType.Rgb, new[] { B.Cicp(9, 1) }, () => Lcms(TestImages.Icc.LittleCmsRec2020V4)),
        ["cICP BT.2020 10-bit"] = (PngColorType.RgbWithAlpha, new[] { B.Cicp(9, 14) }, () => Lcms(TestImages.Icc.LittleCmsRec2020V4)),
        ["cICP BT.2020 12-bit"] = (PngColorType.Rgb, new[] { B.Cicp(9, 15) }, () => Lcms(TestImages.Icc.LittleCmsRec2020V4)),
        ["cICP BT.709"] = (PngColorType.Rgb, new[] { B.Cicp(1, 1) }, () => IccProfileFactory.CreateRgb(Rec709Primaries, D65, IccToneCurve.Rec709)),
        ["cICP BT.601 curve"] = (PngColorType.Rgb, new[] { B.Cicp(1, 6) }, () => IccProfileFactory.CreateRgb(Rec709Primaries, D65, IccToneCurve.Rec709)),
        ["cICP linear, gray"] = (PngColorType.Grayscale, new[] { B.Cicp(1, 8) }, () => Lcms(TestImages.Icc.LittleCmsGrayLinearV4)),
        ["cICP Display P3, gray"] = (PngColorType.GrayscaleWithAlpha, new[] { B.Cicp(12, 13) }, () => null),
        ["cICP BT.2020, gray"] = (PngColorType.Grayscale, new[] { B.Cicp(9, 1) }, () => IccProfileFactory.CreateGray(IccToneCurve.Rec709)),
    };

    public static TheoryData<string> SingleChunkCaseNames { get; } = new(SingleChunkCases.Keys);

    // cICP code points this decoder does not build a profile for: unspecified, PQ, HLG, DCI-P3 (DCI white),
    // gamma 2.2, non-RGB matrix coefficients, narrow range, and a truncated chunk.
    public static TheoryData<string> UnsupportedCicpChunks { get; } = new()
    {
        "2 2 0 1",
        "9 16 0 1",
        "9 18 0 1",
        "11 13 0 1",
        "1 4 0 1",
        "1 13 1 1",
        "1 8 0 0",
        "12 13 0 2",
        "1 8 0",
    };

    // Every combination of two or more sources, in precedence order and in reverse order in the file.
    public static TheoryData<int, bool> SourceCombinations
    {
        get
        {
            TheoryData<int, bool> data = new();
            for (int mask = 0; mask < 1 << Sources.Length; mask++)
            {
                if (CountBits(mask) >= 2)
                {
                    data.Add(mask, false);
                    data.Add(mask, true);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SingleChunkCaseNames))]
    public void Decode_ColorSpaceChunk_DescribesPixelProfile(string name)
    {
        (PngColorType colorType, byte[][] chunks, Func<IccProfile> expected) = SingleChunkCases[name];

        AssertPixelProfile(CreatePng(colorType, chunks), expected());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 6)]
    [InlineData(1, 8)]
    [InlineData(9, 1)]
    [InlineData(9, 14)]
    [InlineData(9, 8)]
    [InlineData(12, 13)]
    [InlineData(12, 15)]
    public void Decode_Cicp_BuiltProfileCarriesTheCodePoints(byte primaries, byte transfer)
    {
        using Image image = Image.Load(CreatePng(PngColorType.Rgb, B.Cicp(primaries, transfer)));

        Assert.True(image.Metadata.IccProfile.TryGetTag(IccProfileTag.Cicp, out IccCicpTagDataEntry cicp));
        Assert.Equal((primaries, transfer, (byte)0, (byte)1), (cicp.ColorPrimaries, cicp.TransferCharacteristics, cicp.MatrixCoefficients, cicp.VideoFullRangeFlag));
    }

    [Theory]
    [MemberData(nameof(SourceCombinations))]
    public void Decode_SeveralColorSpaceChunks_UsesTheOneWithPrecedence(int sources, bool reverseOrder)
    {
        IEnumerable<int> present = Enumerable.Range(0, Sources.Length).Where(i => (sources & (1 << i)) != 0);
        byte[][] chunks = (reverseOrder ? present.Reverse() : present).SelectMany(SourceChunks).ToArray();
        byte[] png = CreatePng(PngColorType.Rgb, chunks);

        switch (Sources[present.First()])
        {
            case "cICP":
                AssertPixelProfile(png, Lcms(TestImages.Icc.LittleCmsDisplayP3V4));
                break;
            case "iCCP":
                AssertEmbeddedProfile(png, ProPhotoProfileBytes());
                break;
            case "sRGB":
                AssertPixelProfile(png, null);
                break;
            default:
                throw new InvalidOperationException("A combination has two or more sources.");
        }
    }

    [Fact]
    public void Decode_SrgbAndGamaOnly_HasNoPixelProfile()
        => AssertPixelProfile(CreatePng(PngColorType.Rgb, B.StandardRgb(), Gama22), null);

    [Fact]
    public void Decode_IccpAndGamaOnly_UsesIccp()
        => AssertEmbeddedProfile(CreatePng(PngColorType.Rgb, Gama22, B.EmbeddedProfile(ProPhotoProfileBytes())), ProPhotoProfileBytes());

    [Theory]
    [MemberData(nameof(UnsupportedCicpChunks))]
    public void Decode_UnsupportedCicp_BuildsNoProfile(string codePoints)
    {
        byte[] cicp = B.Chunk("cICP", codePoints.Split(' ').Select(byte.Parse).ToArray());

        AssertPixelProfile(CreatePng(PngColorType.Rgb, cicp), null);

        // The chunk is ignored, so the next source in precedence order describes the image.
        AssertEmbeddedProfile(CreatePng(PngColorType.Rgb, cicp, B.EmbeddedProfile(ProPhotoProfileBytes())), ProPhotoProfileBytes());
        AssertPixelProfile(CreatePng(PngColorType.Rgb, cicp, AdobeRgbChrm, AdobeRgbGama), Lcms(TestImages.Icc.LittleCmsAdobeRgbV4));
    }

    [Fact]
    public void Decode_SecondCicp_IsIgnored()
        => AssertPixelProfile(CreatePng(PngColorType.Rgb, B.Cicp(12, 13), B.Cicp(1, 8)), Lcms(TestImages.Icc.LittleCmsDisplayP3V4));

    [Fact]
    public void Decode_ColorSpaceChunksAfterImageData_AreIgnored()
    {
        byte[] png = CreatePng(PngColorType.Rgb);
        List<(string Type, byte[] Data)> chunks = B.ReadChunks(png);
        byte[] late = B.Build(
            B.Chunk(chunks[0].Type, chunks[0].Data),
            B.Chunk(chunks[1].Type, chunks[1].Data),
            B.Cicp(12, 13),
            AdobeRgbChrm,
            AdobeRgbGama,
            B.End());

        AssertPixelProfile(late, null);
    }

    [Theory]
    [InlineData("cICP")]
    [InlineData("iCCP")]
    [InlineData("cHRM+gAMA")]
    [InlineData("all")]
    public void Decode_IgnoreMetadata_IgnoresColorSpaceChunks(string source)
    {
        byte[][] chunks = source == "all"
            ? Enumerable.Range(0, Sources.Length).SelectMany(SourceChunks).ToArray()
            : SourceChunks(Array.IndexOf(Sources, source)).ToArray();

        AssertPixelProfile(CreatePng(PngColorType.Rgb, chunks), null, new PngDecoder { IgnoreMetadata = true });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decode_Gama_KeepsRawGammaInPngMetadata(bool withCicp)
    {
        byte[] png = withCicp ? CreatePng(PngColorType.Rgb, B.Cicp(12, 13), AdobeRgbGama) : CreatePng(PngColorType.Rgb, AdobeRgbGama);

        using Image image = Image.Load(png);
        Assert.Equal(0.45471F, image.Metadata.GetPngMetadata().Gamma, 5);
        Assert.Equal(0.45471F, Image.Identify(png).Metadata.GetPngMetadata().Gamma, 5);
    }

    [Fact]
    public void Encode_BuiltPixelProfile_IsWrittenAsIccp()
    {
        using Image image = Image.Load(CreatePng(PngColorType.Rgb, B.Cicp(12, 13)));
        using MemoryStream stream = new();
        image.SaveAsPng(stream);
        byte[] png = stream.ToArray();

        List<(string Type, byte[] Data)> chunks = B.ReadChunks(png);
        Assert.Contains(chunks, c => c.Type == "iCCP");
        Assert.DoesNotContain(chunks, c => c.Type == "cICP");
        AssertPixelProfile(png, Lcms(TestImages.Icc.LittleCmsDisplayP3V4));
    }

    private static IEnumerable<byte[]> SourceChunks(int source) => Sources[source] switch
    {
        "cICP" => new[] { B.Cicp(12, 13) },
        "iCCP" => new[] { B.EmbeddedProfile(ProPhotoProfileBytes()) },
        "sRGB" => new[] { B.StandardRgb() },
        _ => new[] { AdobeRgbChrm, AdobeRgbGama },
    };

    private static int CountBits(int value)
    {
        int count = 0;
        for (; value != 0; value >>= 1)
        {
            count += value & 1;
        }

        return count;
    }

    private static IccProfile Lcms(string path) => IccProfileFactoryTests.Load(path);

    private static byte[] ProPhotoProfileBytes() => Lcms(TestImages.Icc.CompactProPhotoV4).ToByteArray();

    // Decodes and identifies the file, and checks both report the expected pixel profile (null for none).
    private static void AssertPixelProfile(byte[] png, IccProfile expected, PngDecoder decoder = null)
    {
        foreach (IccProfile actual in DecodeAndIdentify(png, decoder ?? new PngDecoder()))
        {
            if (expected is null)
            {
                Assert.Null(actual);
            }
            else
            {
                Assert.NotNull(actual);
                IccProfileFactoryTests.AssertSameColour(expected, actual);
            }
        }
    }

    private static void AssertEmbeddedProfile(byte[] png, byte[] expected)
    {
        foreach (IccProfile actual in DecodeAndIdentify(png, new PngDecoder()))
        {
            Assert.Equal(expected, actual?.ToByteArray());
        }
    }

    private static IccProfile[] DecodeAndIdentify(byte[] png, PngDecoder decoder)
    {
        using Image image = Image.Load(png, decoder);
        IImageInfo info = decoder.Identify(Configuration.Default, new MemoryStream(png), default);
        return new[] { image.Metadata.IccProfile, info.Metadata.IccProfile };
    }

    // A 2x1 8-bit PNG with the given chunks between IHDR and IDAT.
    private static byte[] CreatePng(PngColorType colorType, params byte[][] colorChunks)
    {
        byte[] scanline = colorType switch
        {
            PngColorType.Grayscale => new byte[] { 0, 128, 64 },
            PngColorType.GrayscaleWithAlpha => new byte[] { 0, 128, 255, 64, 255 },
            PngColorType.Palette => new byte[] { 0, 0, 0 },
            PngColorType.RgbWithAlpha => new byte[] { 0, 128, 64, 32, 255, 51, 102, 153, 255 },
            _ => new byte[] { 0, 128, 64, 32, 51, 102, 153 },
        };

        List<byte[]> chunks = new() { B.Header(2, 1, 8, colorType) };
        chunks.AddRange(colorChunks);
        if (colorType == PngColorType.Palette)
        {
            chunks.Add(B.Chunk("PLTE", new byte[] { 128, 64, 32 }));
        }

        chunks.Add(B.Chunk("IDAT", B.Zlib(scanline)));
        chunks.Add(B.End());
        return B.Build(chunks.ToArray());
    }
}
