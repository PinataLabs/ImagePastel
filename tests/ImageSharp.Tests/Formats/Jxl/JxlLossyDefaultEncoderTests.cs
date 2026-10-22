// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Lossy encoding at Default (libjxl effort 5, <c>kHare</c>), slice E4. E4a is the tracer: Gaborish,
/// near-lossless DC, the <c>kACMeta</c> tree for the AC metadata and a learned tree for alpha are libjxl's
/// Hare stages, and the heuristics that later tickets port keep their Fastest form for now (a constant
/// quant field, DCT8 everywhere, the default chroma-from-luma map). E4e chooses the block context map.
/// </summary>
/// <remarks>
/// The colour is checked against E3a's interim bound (<see cref="JxlLossyEncoderTests"/>): each channel's
/// RMSE against the source is at most 20 8-bit steps.
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlLossyDefaultEncoderTests
{
    public static TheoryData<string, float> PhotoReaderCases { get; } = CreatePhotoReaderCases();

    public static TheoryData<string, string, float> AlphaReaderCases { get; } = CreateAlphaReaderCases();

    public static TheoryData<string, string> SizeReaderCases { get; } = CreateSizeReaderCases();

    [Theory]
    [MemberData(nameof(PhotoReaderCases))]
    public async Task Encode_Photo_DecodesThroughEachReaderWithinTheInterimBound(string reader, float distance)
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));

        using Image<Rgb24> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgb24>(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, "LossyDefaultEncoder", $"photo_d{distance}");
        JxlLossyEncoderTests.AssertWithinInterimBound(image, decoded, $"d{distance}, {encoded.Length} bytes");
    }

    // An RGBA photo: the colour is lossy, and the alpha is coded losslessly in Modular with a tree learned
    // at Hare (kLearn, Gradient predictor, a local palette per group). Bike with a hard mask spreads the
    // alpha over its six groups, as a two-colour channel palette; the soft mask has every level.
    [Theory]
    [MemberData(nameof(AlphaReaderCases))]
    public async Task Encode_RgbaPhoto_AlphaIsExactAndColourWithinTheInterimBound(string reader, string mask, float distance)
    {
        using Image<Rgba32> image = JxlLossyAlphaEncoderTests.CreateMaskedImage("bike-582x453", mask);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));

        using Image<Rgba32> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgba32>(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, "LossyDefaultEncoder", $"{mask}-bike_d{distance}");
        string label = $"{mask} d{distance}, {encoded.Length} bytes";
        JxlLossyAlphaEncoderTests.AssertAlphaExact(image, decoded, label);
        JxlLossyAlphaEncoderTests.AssertVisibleColourWithinInterimBound(image, decoded, label);
    }

    // Odd sizes, one block, and frames of several groups and LF groups (Bike: six groups; the gradient:
    // nine groups over two LF groups), through each reader.
    [Theory]
    [MemberData(nameof(SizeReaderCases))]
    public async Task Encode_Size_RoundTripsWithinTheInterimBound(string reader, string sizeCase)
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase(sizeCase);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(1.0f));

        using Image<Rgb24> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgb24>(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, "LossyDefaultEncoder", sizeCase);
        JxlLossyEncoderTests.AssertWithinInterimBound(image, decoded, $"{sizeCase}, {encoded.Length} bytes");
    }

    // libjxl's choices at Hare, read back through the decoder's parse: XYB VarDCT; Gaborish above d0.5
    // (LoopFilterFromParams); the EPF iterations and X quantisation scale as at Fastest; near-lossless DC,
    // so one bit of extra DC precision in every LF group (the gradient has two). E4a's temporary departures
    // are visible too: every block DCT8, one quant field value, and the default chroma-from-luma map.
    [Theory]
    [InlineData("flower-384x244", 0.5f, false, 0u, 3u)]
    [InlineData("flower-384x244", 1.0f, true, 1u, 3u)]
    [InlineData("flower-384x244", 2.0f, true, 2u, 3u)]
    [InlineData("flower-384x244", 4.0f, true, 3u, 4u)]
    [InlineData("gradient-2049x9", 1.0f, true, 1u, 3u)]
    public void Encode_Photo_IsTheHareFrameOfTheTracer(string sizeCase, float distance, bool expectedGab, uint expectedEpfIters, uint expectedXQmScale)
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase(sizeCase);

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(distance)));

        JxlFrameHeader header = frame.FrameHeader;
        Assert.Equal(JxlFrameEncoding.VarDct, header.Encoding);
        Assert.True(header.NonserializedMetadata.M.XybEncoded);
        Assert.Equal(JxlColorTransform.Xyb, header.ColorTransform);
        Assert.Equal(expectedGab, header.LoopFilter.Gab);
        Assert.False(header.LoopFilter.GabCustom);
        Assert.Equal(expectedEpfIters, header.LoopFilter.EpfIters);
        Assert.Equal(expectedXQmScale, header.XQmScale);
        Assert.Equal(2u, header.BQmScale);
        Assert.Equal(header.ToFrameDimensions().NumDcGroups, frame.ExtraDcPrecision.Length);
        Assert.All(frame.ExtraDcPrecision, p => Assert.Equal(1, p));

        // Temporary departures until E4g (strategies) and E4b (quant field).
        for (int by = 0; by < frame.State.YSizeBlocks; by++)
        {
            for (int bx = 0; bx < frame.State.XSizeBlocks; bx++)
            {
                JxlAcStrategy acs = frame.State.AcStrategy.Get(bx, by);
                Assert.True(acs.Strategy == JxlAcStrategyType.Dct && acs.IsFirstBlock, $"Block ({bx}, {by}) is {acs.Strategy}");
            }
        }

        (int min, int max) = frame.QuantFieldRange();
        Assert.Equal(min, max);

        // Temporary departure until E4d (chroma-from-luma).
        JxlColorCorrelation defaultCmap = new(header.ToFrameDimensions().XSize, header.ToFrameDimensions().YSize);
        Assert.All(frame.State.Cmap.YtoXMap, f => Assert.Equal(0, f));
        Assert.All(frame.State.Cmap.YtoBMap, f => Assert.Equal(0, f));
        Assert.Equal(defaultCmap.DcFactors.ToArray(), frame.State.Cmap.DcFactors.ToArray());

        // The block context map (E4e) is chosen for frames of at least 1024·d blocks: the flower's 48x31
        // blocks at d0.5 and d1; the gradient's 257x2 and the flower at d2 and d4 keep the default.
        bool chosen = sizeCase == "flower-384x244" && distance <= 1.0f;
        Assert.Equal(!chosen, frame.State.BlockCtxMap.IsDefault);
    }

    // libjxl's FindBestBlockEntropyModel through the decoder's parse: Bike's 73x57 blocks at d1 get a map of
    // one quant field segment (fewer than 8192·d blocks) with two luma clusters and one chroma cluster
    // (4161 / 1024 / 2 and / 3, clamped), so three contexts. Below 1024·d blocks the map keeps its default
    // bit: Bike at d8, text (40x25 blocks) and bike_alpha at d1.
    [Theory]
    [InlineData("bike", 1.0f, false, 3)]
    [InlineData("bike", 8.0f, true, 15)]
    [InlineData("text", 1.0f, true, 15)]
    [InlineData("bike_alpha", 1.0f, true, 15)]
    public void Encode_CalibrationImage_ChoosesTheBlockContextMapOfLargeFrames(string name, float distance, bool expectedDefault, int expectedNumCtxs)
    {
        string path = Quality.JxlCalibrationTable.Instance.Images.Single(i => i.Name == name).FullPath;
        byte[] encoded;
        if (name == "bike_alpha")
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(path);
            encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));
        }
        else
        {
            using Image<Rgb24> image = Image.Load<Rgb24>(path);
            encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));
        }

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(encoded);

        JxlBlockCtxMap map = frame.State.BlockCtxMap;
        TestContext.Current.TestOutputHelper?.WriteLine($"{name} d{distance}: {frame.State.XSizeBlocks}x{frame.State.YSizeBlocks} blocks, QfThresholds [{string.Join(",", map.QfThresholds)}], NumCtxs {map.NumCtxs}, CtxMap [{string.Join(",", map.CtxMap)}]");
        Assert.Equal(expectedDefault, map.IsDefault);
        Assert.Empty(map.QfThresholds);
        Assert.Equal(expectedNumCtxs, map.NumCtxs);
        Assert.Equal(1, map.NumDcCtxs);
    }

    [Fact]
    public void Encode_LargerDistance_GivesASmallerFile()
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();
        float[] distances = { 0.5f, 1, 2, 4, 8, 16, 25 };

        int[] sizes = Array.ConvertAll(distances, d => JxlEncoderRoundTrip.Encode(image, Lossy(d)).Length);

        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(", ", Array.ConvertAll(distances, d => $"d{d}: {sizes[Array.IndexOf(distances, d)]} B")));
        for (int i = 1; i < sizes.Length; i++)
        {
            Assert.True(sizes[i] < sizes[i - 1], $"d{distances[i]} gives {sizes[i]} bytes, d{distances[i - 1]} gives {sizes[i - 1]}");
        }
    }

    // SHA-256 of this encoder's lossy Default output: the one table of Default pins. It uses no FMA and no
    // C runtime maths on a path that reaches the bytes, so every leg gives the same file. Every E4 ticket
    // changes these on purpose; tickets that run in parallel regenerate the table when they merge.
    [Theory]
    [InlineData("flower-384x244", "", 0.5f, "B30280F9DB48F7791E08797AE2EAC70D654D595AB0581A763D0F7D3273C47B95")]
    [InlineData("flower-384x244", "", 1.0f, "7600459F3894782BC52B9E9F49710FCEB81BA7745A338D00A59CFFAF6E2330B9")]
    [InlineData("flower-384x244", "", 2.0f, "389A786B664E1E110F12CB6BC81E3050AD780811593EBBCF92F501637B629D9B")]
    [InlineData("flower-384x244", "", 4.0f, "17CD37A73A106C4382C8D5D748F8CE817A342ACB61054D55C342CE0E2856FA24")]
    [InlineData("flower-17x23", "", 1.0f, "B398A6CA525E189365B160BA0CCEFA9AF80C2C626004CAC1B37EA12BBBABC803")]
    [InlineData("dot-1x1", "", 1.0f, "325ADA26DA3104663BA288B60DFDD7942D3533BEFD809FA43594FD851860C1C8")]
    [InlineData("bike-582x453", "", 1.0f, "3A13142A2E307BD64E12DAFBBC7D848AC0B5C5034087A3AF588FD86B4A4DF436")]
    [InlineData("gradient-2049x9", "", 2.0f, "A58E35BF3F749154E9B08B44CEFC7CC0BAE07A2299E4E04C6FF17D241DF9FFFA")]
    [InlineData("flower-256x244", "soft", 1.0f, "566F2BFFDB2D22E82B71DFBCC0505973A9547E94EEC8528C24508E2FDCFECC38")]
    [InlineData("bike-582x453", "hard", 1.0f, "9AD1B5036B8525C9D29C93370DFB87BD43F2F7D20C468215BD35D7F260F67546")]
    public void Encode_LossyDefault_IsByteIdenticalToItsPin(string sizeCase, string mask, float distance, string expectedSha256)
    {
        byte[] encoded;
        if (mask.Length == 0)
        {
            using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase(sizeCase);
            encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));
        }
        else
        {
            using Image<Rgba32> image = JxlLossyAlphaEncoderTests.CreateMaskedImage(sizeCase, mask);
            encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));
        }

        // Logged in full, so that a ticket that changes the output on purpose can regenerate the table.
        string actual = Convert.ToHexString(SHA256.HashData(encoded));
        TestContext.Current.TestOutputHelper?.WriteLine($"[InlineData(\"{sizeCase}\", \"{mask}\", {distance:0.0#}f, \"{actual}\")]");
        Assert.Equal(expectedSha256, actual);
    }

    internal static JxlEncoder Lossy(float distance) => new() { Distance = distance, Effort = JxlEffort.Default };

    private static TheoryData<string, float> CreatePhotoReaderCases()
    {
        TheoryData<string, float> cases = new();
        foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
        {
            foreach (float distance in new[] { 1.0f, 2.0f, 4.0f })
            {
                cases.Add(reader.ToString(), distance);
            }
        }

        return cases;
    }

    private static TheoryData<string, string, float> CreateAlphaReaderCases()
    {
        TheoryData<string, string, float> cases = new();
        foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
        {
            foreach (float distance in new[] { 1.0f, 2.0f, 4.0f })
            {
                cases.Add(reader.ToString(), "hard", distance);
            }

            cases.Add(reader.ToString(), "soft", 1.0f);
        }

        return cases;
    }

    private static TheoryData<string, string> CreateSizeReaderCases()
    {
        TheoryData<string, string> cases = new();
        foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
        {
            foreach (string sizeCase in new[] { "flower-17x23", "dot-1x1", "bike-582x453", "gradient-2049x9" })
            {
                cases.Add(reader.ToString(), sizeCase);
            }
        }

        return cases;
    }
}
