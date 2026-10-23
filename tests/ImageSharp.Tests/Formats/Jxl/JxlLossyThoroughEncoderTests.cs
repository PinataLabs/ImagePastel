// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Lossy encoding at Thorough (libjxl effort 7, <c>kSquirrel</c>), slice E5. E5a is the tracer: each
/// Squirrel stage that a later ticket ports still runs as at Hare.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlLossyThoroughEncoderTests
{
    public static TheoryData<string, float> PhotoReaderCases { get; } = CreateReaderCases();

    [Theory]
    [MemberData(nameof(PhotoReaderCases))]
    public async Task Encode_Photo_DecodesThroughEachReaderWithinTheInterimBound(string reader, float distance)
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));

        using Image<Rgb24> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgb24>(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, "LossyThoroughEncoder", $"photo_d{distance}");
        JxlLossyEncoderTests.AssertWithinInterimBound(image, decoded, $"d{distance}, {encoded.Length} bytes");
    }

    // An RGBA photo: the colour is lossy, and the alpha is coded losslessly in Modular with a tree learned
    // with Squirrel's parameters (7 properties, 96 values). Bike with a hard mask spreads the alpha over its
    // six groups.
    [Theory]
    [MemberData(nameof(PhotoReaderCases))]
    public async Task Encode_RgbaPhoto_AlphaIsExactAndColourWithinTheInterimBound(string reader, float distance)
    {
        using Image<Rgba32> image = JxlLossyAlphaEncoderTests.CreateMaskedImage("bike-582x453", "hard");

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));

        using Image<Rgba32> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgba32>(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, "LossyThoroughEncoder", $"hard-bike_d{distance}");
        string label = $"hard d{distance}, {encoded.Length} bytes";
        JxlLossyAlphaEncoderTests.AssertAlphaExact(image, decoded, label);
        JxlLossyAlphaEncoderTests.AssertVisibleColourWithinInterimBound(image, decoded, label);
    }

    // The strategies Squirrel's search chooses so far: Hare's, and DCT4X8, DCT8X4 (E5b) and AFV0 to AFV3
    // (E5c) at the 8x8 level. E5d adds the non-aligned merges' DCT16X16 and DCT32X32 (already in it).
    // Default's tracer test keeps Hare's set.
    internal static readonly JxlAcStrategyType[] SquirrelStrategies =
    {
        JxlAcStrategyType.Dct, JxlAcStrategyType.Identity, JxlAcStrategyType.Dct2X2, JxlAcStrategyType.Dct4X4,
        JxlAcStrategyType.Dct4X8, JxlAcStrategyType.Dct8X4,
        JxlAcStrategyType.Afv0, JxlAcStrategyType.Afv1, JxlAcStrategyType.Afv2, JxlAcStrategyType.Afv3,
        JxlAcStrategyType.Dct16X8, JxlAcStrategyType.Dct8X16, JxlAcStrategyType.Dct16X16,
        JxlAcStrategyType.Dct32X16, JxlAcStrategyType.Dct16X32, JxlAcStrategyType.Dct32X32,
        JxlAcStrategyType.Dct64X32, JxlAcStrategyType.Dct32X64, JxlAcStrategyType.Dct64X64,
    };

    // libjxl's choices at Squirrel, read back through the decoder's parse: XYB VarDCT, Gaborish above d0.5,
    // near-lossless DC in every LF group, and every block covered once by a strategy of Squirrel's set so
    // far. The remaining checks are the temporary departures, each of which a later ticket flips: the X
    // quantisation scale from the distance alone and a B scale of 2 (E5f), and an EPF sharpness of 4 in
    // every block (E5h).
    [Theory]
    [InlineData("flower-384x244", 0.5f, false, 0u, 3u)]
    [InlineData("flower-384x244", 1.0f, true, 1u, 3u)]
    [InlineData("flower-384x244", 2.0f, true, 2u, 3u)]
    [InlineData("flower-384x244", 4.0f, true, 3u, 4u)]
    [InlineData("bike-582x453", 1.0f, true, 1u, 3u)]
    [InlineData("gradient-2049x9", 1.0f, true, 1u, 3u)]
    public void Encode_Photo_IsTheSquirrelFrameOfTheTracer(string sizeCase, float distance, bool expectedGab, uint expectedEpfIters, uint expectedXQmScale)
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
        Assert.Equal(JxlFrameFlags.None, header.Flags);
        Assert.All(frame.ExtraDcPrecision, p => Assert.Equal(1, p));

        // Every block is covered once by one of Squirrel's strategies so far, and the search still merges
        // into larger blocks.
        byte[] strategies = JxlEncAcStrategyTests.StrategiesOf(frame);
        TestContext.Current.TestOutputHelper?.WriteLine($"{sizeCase} d{distance}: [{JxlEncAcStrategyTests.CoverageOf(strategies)}]");
        foreach (byte b in strategies)
        {
            JxlAcStrategyType strategy = (JxlAcStrategyType)(b >> 1);
            Assert.True(Array.IndexOf(SquirrelStrategies, strategy) >= 0, $"{strategy} is not in Squirrel's search so far");
        }

        Assert.Contains(strategies, b => JxlAcStrategy.FromRawStrategy(b >> 1).CoveredBlocksX * JxlAcStrategy.FromRawStrategy(b >> 1).CoveredBlocksY > 1);

        // Temporary departure (E5f): the scales of the distance alone.
        Assert.Equal(expectedXQmScale, header.XQmScale);
        Assert.Equal(2u, header.BQmScale);

        // Temporary departure (E5h): the constant sharpness of 4 in every block. libjxl writes 4 too where
        // the frame has no EPF (d0.5) or the distance is below 0.5, and E5h keeps those.
        for (int by = 0; by < frame.State.YSizeBlocks; by++)
        {
            Assert.All(frame.State.EpfSharpness.DangerousGetRowSpan(by).ToArray(), s => Assert.Equal(4, s));
        }
    }

    // E5b: below Hare FindBest8x8Transform also tries DCT4X8 and DCT8X4 (enc_ac_strategy.cc kTransforms8x8,
    // tier limit 4), and Bike at d1 takes some of each, read back through the decoder's parse. The
    // histogram is how many 8x8 blocks each raw strategy covers (12 DCT4X8, 13 DCT8X4); Default's is in
    // JxlEncAcStrategyTests.Encode_Bike_ChoosesLibjxlsStrategyForEachBlock.
    [Fact]
    public void Encode_Bike_TriesDct4X8AndDct8X4AtTheEightByEightLevel()
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("bike-582x453");

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(1.0f)));

        byte[] strategies = JxlEncAcStrategyTests.StrategiesOf(frame);
        TestContext.Current.TestOutputHelper?.WriteLine($"bike d1: {JxlEncAcStrategyTests.CoverageOf(strategies)}");
        Assert.Contains(strategies, b => (JxlAcStrategyType)(b >> 1) is JxlAcStrategyType.Dct4X8 or JxlAcStrategyType.Dct8X4);
    }

    // E5c: from Wombat down the 8x8 level tries AFV0 to AFV3, and Bike at d1 keeps some of them through the
    // merges. Read back through the decoder's parse; the histogram (raw strategy: blocks covered) is logged.
    // At E5c, before DCT4X8 and DCT8X4 (E5b) compete for the same blocks, it was 0:1578 1:37 2:255 4:152
    // 5:304 6:138 7:130 10:128 11:224 14:44 15:40 16:37 17:38 18:768 19:128 20:160 (Default's is the same
    // without the AFV kinds 14 to 17, and with 100 more DCT8, 10 more IDENTITY, 47 more DCT2X2 and 2 more
    // DCT8X16 blocks).
    [Fact]
    public void Encode_BikeAtD1_ChoosesAfvBlocks()
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("bike-582x453");

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(1.0f)));

        byte[] strategies = JxlEncAcStrategyTests.StrategiesOf(frame);
        TestContext.Current.TestOutputHelper?.WriteLine($"bike d1: [{JxlEncAcStrategyTests.CoverageOf(strategies)}]");
        Assert.Contains(strategies, b => (JxlAcStrategyType)(b >> 1) is JxlAcStrategyType.Afv0 or JxlAcStrategyType.Afv1 or JxlAcStrategyType.Afv2 or JxlAcStrategyType.Afv3);
    }

    // E5a's record, as E5b and E5c leave it: with alpha the file differs from Default's, as the alpha tree
    // is learned with Squirrel's parameters (7 properties and 96 values against Hare's 4 and 48). Without
    // alpha it differs where the 8x8 level picks DCT4X8, DCT8X4 (E5b) or AFV (E5c): the flower and Bike.
    // The gradient, 9 pixels high, takes none of them and is still Default's file. Each later E5 ticket
    // changes this test when it breaks another row's equality.
    [Theory]
    [InlineData("flower-384x244", "", 1.0f, false)]
    [InlineData("bike-582x453", "", 1.0f, false)]
    [InlineData("bike-582x453", "", 4.0f, false)]
    [InlineData("gradient-2049x9", "", 2.0f, true)]
    [InlineData("bike-582x453", "hard", 1.0f, false)]
    [InlineData("flower-256x244", "soft", 1.0f, false)]
    public void Encode_AtThorough_IsDefaultsFileOnlyWhereNoSquirrelStageChoosesOtherwise(string sizeCase, string mask, float distance, bool expectedEqual)
    {
        byte[] thorough = EncodeCase(sizeCase, mask, Lossy(distance));
        byte[] @default = EncodeCase(sizeCase, mask, JxlLossyDefaultEncoderTests.Lossy(distance));

        TestContext.Current.TestOutputHelper?.WriteLine($"{sizeCase} {mask} d{distance}: Thorough {thorough.Length} B, Default {@default.Length} B");
        Assert.Equal(expectedEqual, thorough.AsSpan().SequenceEqual(@default));
    }

    // SHA-256 of this encoder's lossy Thorough output: the one table of Thorough pins. It uses no FMA and no
    // C runtime maths on a path that reaches the bytes, so every leg gives the same file. Every E5 ticket
    // changes these on purpose; tickets that run in parallel regenerate the table when they merge
    // (e5a-handoff.md says how). Since E5b (DCT4X8, DCT8X4) and E5c (AFV) only the three small rows without
    // alpha (the 17x23 flower, the dot and the gradient) still equal their Default pins.
    [Theory]
    [InlineData("flower-384x244", "", 0.5f, "2AC005B35DA01FA8BAC5F020103DBDCB362758559099DAF48E0B9880263F9647")]
    [InlineData("flower-384x244", "", 1.0f, "935DAF95601A7723899977B59B23D639D02B9C885F5541F02DE1C9A580BE9B2A")]
    [InlineData("flower-384x244", "", 2.0f, "8438B31EFB42F1063A190A207DD1453881AE50D5CE71374F7E28B78243696F89")]
    [InlineData("flower-384x244", "", 4.0f, "C8A054C225CAF644492D07DEBFB47D67DA5EA6DF6029A89EA12C11C5A8B402B7")]
    [InlineData("flower-17x23", "", 1.0f, "02082DB03AA3368FC88DE37F300D21A0A18EB78671F873D84F1DCF36C9841568")]
    [InlineData("dot-1x1", "", 1.0f, "0E0D22FC449A4D7D33375B82BD608513BED6874DF06D79A8F7DA55DE2F61CC42")]
    [InlineData("bike-582x453", "", 1.0f, "83E7893EF71BD82D5A9915BD9E4319954A50E3607CB2235141B5589A4C400A9E")]
    [InlineData("gradient-2049x9", "", 2.0f, "BB61F5256C35D906407A2F732AD242BDA4A47FF75CD40C16D6A83CA0CCC1E834")]
    [InlineData("flower-256x244", "soft", 1.0f, "9BBF5E020570316421AD795ED265755BDD5152BAF75C5F46F55B5D6781828F05")]
    [InlineData("bike-582x453", "hard", 1.0f, "D7E11037814C8CA17294CEA54179833CF04C950EF945254D56110C6C889E9FE5")]
    public void Encode_LossyThorough_IsByteIdenticalToItsPin(string sizeCase, string mask, float distance, string expectedSha256)
    {
        byte[] encoded = EncodeCase(sizeCase, mask, Lossy(distance));

        // Logged in full, so that a ticket that changes the output on purpose can regenerate the table.
        string actual = Convert.ToHexString(SHA256.HashData(encoded));
        TestContext.Current.TestOutputHelper?.WriteLine($"[InlineData(\"{sizeCase}\", \"{mask}\", {distance:0.0#}f, \"{actual}\")]");
        Assert.Equal(expectedSha256, actual);
    }

    internal static JxlEncoder Lossy(float distance) => new() { Distance = distance, Effort = JxlEffort.Thorough };

    // A size case as RGB, or Bike or a flower crop with a synthetic alpha mask.
    private static byte[] EncodeCase(string sizeCase, string mask, JxlEncoder encoder)
    {
        if (mask.Length == 0)
        {
            using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase(sizeCase);
            return JxlEncoderRoundTrip.Encode(image, encoder);
        }

        using Image<Rgba32> rgba = JxlLossyAlphaEncoderTests.CreateMaskedImage(sizeCase, mask);
        return JxlEncoderRoundTrip.Encode(rgba, encoder);
    }

    private static TheoryData<string, float> CreateReaderCases()
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
}
