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
    // are visible too: every block DCT8, and a quant field built on one constant value (which E4c's
    // AdjustQuantBlockAC raises).
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

        // Temporary departure until E4g (strategies).
        for (int by = 0; by < frame.State.YSizeBlocks; by++)
        {
            for (int bx = 0; bx < frame.State.XSizeBlocks; bx++)
            {
                JxlAcStrategy acs = frame.State.AcStrategy.Get(bx, by);
                Assert.True(acs.Strategy == JxlAcStrategyType.Dct && acs.IsFirstBlock, $"Block ({bx}, {by}) is {acs.Strategy}");
            }
        }

        // Temporary departure until E4b (quant field): before AdjustQuantBlockAC every block has the raw
        // value of the constant 0.79 / d. AdjustQuantBlockAC (E4c) then raises the quant of flat blocks.
        (int raised, _, _) = frame.QuantFieldAround(frame.State.Quantizer.RawQuantField((float)(0.79 / distance)));
        Assert.True(raised > 0, "AdjustQuantBlockAC raises no block's quant");

        // The AC chroma-from-luma map is fitted per tile (Encode_SaturatedPhoto_FitsAChromaFromLumaMap); the DC
        // factors keep their default, as libjxl never fits them.
        JxlColorCorrelation defaultCmap = new(header.ToFrameDimensions().XSize, header.ToFrameDimensions().YSize);
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

    // At Hare libjxl fits the AC chroma-from-luma map of each 64x64 tile by least squares (the fast
    // FindBestMultiplier), read back through the decoder's parse. The flower crop is saturated, so its tiles
    // predict B from Y with non-zero multipliers; its X multipliers all shrink to 0, as cjxl -e 5 -d 1 gives
    // too. Bike has tiles with non-zero multipliers in both maps (at d1 with E4d's DCT8 blocks and constant
    // field, 73 of its 80 X and 60 of its 80 B multipliers equal cjxl's).
    [Theory]
    [InlineData("flower-384x244", 1.0f, 24, false)]
    [InlineData("flower-384x244", 2.0f, 24, false)]
    [InlineData("flower-384x244", 4.0f, 24, false)]
    [InlineData("bike-582x453", 1.0f, 80, true)]
    public void Encode_SaturatedPhoto_FitsAChromaFromLumaMap(string sizeCase, float distance, int tiles, bool expectYtoX)
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase(sizeCase);

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(distance)));

        JxlColorCorrelation cmap = frame.State.Cmap;
        TestContext.Current.TestOutputHelper?.WriteLine($"{sizeCase} d{distance}: YtoX [{string.Join(", ", cmap.YtoXMap)}], YtoB [{string.Join(", ", cmap.YtoBMap)}]");
        Assert.Equal(tiles, cmap.YtoXMap.Length);
        Assert.Contains(cmap.YtoBMap, f => f != 0);
        Assert.Equal(expectYtoX, Array.Exists(cmap.YtoXMap, f => f != 0));
    }

    // A grey image has no X (red-green) signal in XYB, so no tile predicts X from Y.
    [Fact]
    public void Encode_GreyImage_KeepsTheYtoXMapZero()
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("flower-384x244");
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                foreach (ref Rgb24 p in accessor.GetRowSpan(y))
                {
                    byte grey = (byte)(((p.R * 77) + (p.G * 150) + (p.B * 29)) >> 8);
                    p = new Rgb24(grey, grey, grey);
                }
            }
        });

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(1.0f)));

        TestContext.Current.TestOutputHelper?.WriteLine($"YtoX [{string.Join(", ", frame.State.Cmap.YtoXMap)}], YtoB [{string.Join(", ", frame.State.Cmap.YtoBMap)}]");
        Assert.All(frame.State.Cmap.YtoXMap, f => Assert.Equal(0, f));
    }

    // The chroma-from-luma map takes the part of B that Y predicts out before B is quantised, so the blue
    // channel of the flower crop at d1 is no worse than with E4a's default map (our decoder): E4a gave 31,333
    // bytes and a blue RMSE of 7.92 8-bit steps.
    [Fact]
    public async Task Encode_SaturatedPhoto_BlueErrorDoesNotGrowAgainstTheDefaultMap()
    {
        const double e4aBlueRmse = 7.923919958812128;
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("flower-384x244");

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(1.0f));

        using Image<Rgb24> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgb24>(JxlEncoderRoundTrip.Reader.Ours, encoded, "LossyDefaultEncoder", "flower_cfl_d1");
        JxlLossyEncoderTests.ChannelErrors errors = JxlLossyEncoderTests.ChannelErrors.Measure(image, decoded);
        TestContext.Current.TestOutputHelper?.WriteLine($"{encoded.Length} bytes: {errors}; blue RMSE {errors.Rmse[2]:R}");
        Assert.True(errors.Rmse[2] <= e4aBlueRmse, $"blue RMSE {errors.Rmse[2]:R} > E4a's {e4aBlueRmse:R}");
    }

    // AdjustQuantBlockAC (E4c), read back through the decoder's parse of the raw quant field. Each block
    // takes the largest of the quants its Y, X and B coefficients ask for: flat blocks and lone high
    // frequencies raise it, and busy blocks lower it when all three channels are busy. Until E4b every
    // block starts from the raw value of the constant 0.79 / d. Shares on the flower crop (E4c):
    // d0.5 base 7, 65.0% raised and 6.0% lowered; d1 base 6, 79.4% and 4.8%; d2 base 5, 92.7% and 0.4%.
    // At d4 (base 5) no block is lowered: the floor max(4, quant / 2) leaves one step, and the flat X or
    // B channel of nearly every block asks for a raise.
    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    [InlineData(2.0f)]
    public void Encode_Photo_AdjustsTheQuantOfEachBlockBothWays(float distance)
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("flower-384x244");

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(distance)));

        int constantQuant = frame.State.Quantizer.RawQuantField((float)(0.79 / distance));
        (int raised, int lowered, int blocks) = frame.QuantFieldAround(constantQuant);
        TestContext.Current.TestOutputHelper?.WriteLine($"d{distance}: base {constantQuant}, raised {raised} ({100.0 * raised / blocks:F1}%), lowered {lowered} ({100.0 * lowered / blocks:F1}%) of {blocks} blocks; range {frame.QuantFieldRange()}");
        Assert.True(raised > 0, "AdjustQuantBlockAC raises no block's quant");
        Assert.True(lowered > 0, "AdjustQuantBlockAC lowers no block's quant");
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
    [InlineData("flower-384x244", "", 0.5f, "2EA24D572C14D5F6021EB4D5F5B255DFAA5FC93EAF7AE2E3B8A4490D017055A3")]
    [InlineData("flower-384x244", "", 1.0f, "440791D3FA512487E9855B83B0698BE1C3C0CE6AC2E6B42EFAFAF0E3ABCE47E7")]
    [InlineData("flower-384x244", "", 2.0f, "5FFCB9420C9C48A5B36428AF14C4DCF2E0AC732721AF75FA95DBFB9C740AB370")]
    [InlineData("flower-384x244", "", 4.0f, "90C4F7D175A119FCFE92127C0A63B6D9139699B78BE15F99CA3D416EF2BDE7B1")]
    [InlineData("flower-17x23", "", 1.0f, "943248258212CB24293BE736E41DAB0D094C64FF79FFEA6222DA5605A09DDC52")]
    [InlineData("dot-1x1", "", 1.0f, "01C0A1CA1E665AF496DAAB8785E5231EE0A38F0A399EF6FC36B7272233892718")]
    [InlineData("bike-582x453", "", 1.0f, "1654A9D17BC0078729847D085C65E38AF64B4D0575C6BF008B3556E70365DC63")]
    [InlineData("gradient-2049x9", "", 2.0f, "D9BC5EF966CF433037A1B2C6FD980AC9EFA3364541C98EE04CDA7A4DE3A7B3E0")]
    [InlineData("flower-256x244", "soft", 1.0f, "69B3DEEB64473ED35ADC2071DF38B77A16BD5BB3F7ADC199B68D393222C16220")]
    [InlineData("bike-582x453", "hard", 1.0f, "957B763AC4A11FBA7B2250C02E127AB39BDDB66FA82D57C02198ED39CA78AC11")]
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
