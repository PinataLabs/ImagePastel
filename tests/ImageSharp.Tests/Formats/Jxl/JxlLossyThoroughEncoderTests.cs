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

    // libjxl's choices at Squirrel, read back through the decoder's parse: XYB VarDCT, Gaborish above d0.5,
    // near-lossless DC in every LF group, and every block covered once by a strategy of Hare's set. The
    // remaining checks are the temporary departures, each of which a later ticket flips: the X
    // quantisation scale from the distance alone and a B scale of 2 (E5f), and an EPF sharpness of 4 in
    // every block (E5h). The Hare set itself grows with E5b (DCT4X8, DCT8X4) and E5c (AFV).
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

        // Every block is covered once by one of Hare's strategies (no DCT4X8, DCT8X4 or AFV until E5b and
        // E5c), and the tracer still merges into larger blocks.
        byte[] strategies = JxlEncAcStrategyTests.StrategiesOf(frame);
        TestContext.Current.TestOutputHelper?.WriteLine($"{sizeCase} d{distance}: [{JxlEncAcStrategyTests.CoverageOf(strategies)}]");
        foreach (byte b in strategies)
        {
            JxlAcStrategyType strategy = (JxlAcStrategyType)(b >> 1);
            Assert.True(Array.IndexOf(JxlLossyDefaultEncoderTests.HareStrategies, strategy) >= 0, $"{strategy} is not in Hare's search");
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

    // E5e: from Squirrel down libjxl fits a first chroma-from-luma map of each tile on DCT8 blocks before
    // the strategy search, which reads it, and fits the final map by Newton's method rather than least
    // squares. Read back through the decoder's parse, the flower crop's map differs from Default's.
    [Fact]
    public void Encode_SaturatedPhoto_FitsAChromaFromLumaMapOtherThanDefaults()
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("flower-384x244");

        using JxlDecodedDcGroups thorough = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(1.0f)));
        using JxlDecodedDcGroups @default = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, JxlLossyDefaultEncoderTests.Lossy(1.0f)));

        JxlColorCorrelation t = thorough.State.Cmap;
        JxlColorCorrelation d = @default.State.Cmap;
        TestContext.Current.TestOutputHelper?.WriteLine($"Thorough YtoX [{string.Join(", ", t.YtoXMap)}], YtoB [{string.Join(", ", t.YtoBMap)}]");
        TestContext.Current.TestOutputHelper?.WriteLine($"Default  YtoX [{string.Join(", ", d.YtoXMap)}], YtoB [{string.Join(", ", d.YtoBMap)}]");
        Assert.Equal(d.YtoBMap.Length, t.YtoBMap.Length);
        Assert.Contains(t.YtoBMap, f => f != 0);
        Assert.False(t.YtoXMap.AsSpan().SequenceEqual(d.YtoXMap) && t.YtoBMap.AsSpan().SequenceEqual(d.YtoBMap), "Thorough's CfL map is Default's");
    }

    // A grey image has no X (red-green) signal in XYB, so neither the pre-pass nor the Newton fit predicts
    // X from Y in any tile.
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

    // The CfL pre-pass and the Newton fit leave the blue channel of the flower crop at d1 no worse than
    // E5a's tracer, which wrote Default's file here: 24,935 bytes and a blue RMSE of 9.81 8-bit steps (our
    // decoder). E5e's value is in the E5e handoff.
    [Fact]
    public async Task Encode_SaturatedPhoto_BlueErrorDoesNotGrowAgainstTheTracer()
    {
        const double tracerBlueRmse = 9.8110135790738;
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("flower-384x244");

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(1.0f));

        using Image<Rgb24> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgb24>(JxlEncoderRoundTrip.Reader.Ours, encoded, "LossyThoroughEncoder", "flower_cfl_d1");
        JxlLossyEncoderTests.ChannelErrors errors = JxlLossyEncoderTests.ChannelErrors.Measure(image, decoded);
        TestContext.Current.TestOutputHelper?.WriteLine($"{encoded.Length} bytes: {errors}; blue RMSE {errors.Rmse[2]:R}");
        Assert.True(errors.Rmse[2] <= tracerBlueRmse, $"blue RMSE {errors.Rmse[2]:R} > the tracer's {tracerBlueRmse:R}");
    }

    // SHA-256 of this encoder's lossy Thorough output: the one table of Thorough pins. It uses no FMA and no
    // C runtime maths on a path that reaches the bytes, so every leg gives the same file. Every E5 ticket
    // changes these on purpose; tickets that run in parallel regenerate the table when they merge
    // (e5a-handoff.md says how). Until E5e (the CfL pre-pass and Newton fit) the rows without alpha equalled
    // the Default pins; flower-17x23 and dot-1x1 still do.
    [Theory]
    [InlineData("flower-384x244", "", 0.5f, "CDEEF285950682548ECE1EB07D73C43675845437105013C600619B46245DA259")]
    [InlineData("flower-384x244", "", 1.0f, "1B1E88411D63FFFBA8891365309E532059FC3550A4F6CCFD478730282CE72BFF")]
    [InlineData("flower-384x244", "", 2.0f, "2C26CD368C0EA520FFE0D1A793758F9E903CE49F57823A9080E12771BBD0AF04")]
    [InlineData("flower-384x244", "", 4.0f, "8E3B2FF3118AB9969218CCE7AEA62141CC90A7049F23DA39AD394FC5833D3DA3")]
    [InlineData("flower-17x23", "", 1.0f, "02082DB03AA3368FC88DE37F300D21A0A18EB78671F873D84F1DCF36C9841568")]
    [InlineData("dot-1x1", "", 1.0f, "0E0D22FC449A4D7D33375B82BD608513BED6874DF06D79A8F7DA55DE2F61CC42")]
    [InlineData("bike-582x453", "", 1.0f, "512E1DE28CBDF9C7D3B99EA4FFD6E2A032E6DE475EFA1FA4BE8BDC64EA570AAA")]
    [InlineData("gradient-2049x9", "", 2.0f, "77B37ED07A5E9D7E0DDAE4CCDA66DE6B79EA1690DFEB1ECB0EDC451C06592C96")]
    [InlineData("flower-256x244", "soft", 1.0f, "2FEE1D78A627A7E9651C0788467D3251E54BCD5A3B01DAC7991EF1AED20241D6")]
    [InlineData("bike-582x453", "hard", 1.0f, "DABF4FFD5E317B3DC53B8ADD81187C7DE7B59E5A651DF639F771975BC404804C")]
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
