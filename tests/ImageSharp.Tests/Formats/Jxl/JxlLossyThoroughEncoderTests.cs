// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Quality;
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

    public static TheoryData<string, double> CalibrationCases => CreateCalibrationCases();

    public static TheoryData<string, string, double> RaisedScaleReaderCases { get; } = CreateRaisedScaleReaderCases();

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

    // The strategies Squirrel's search chooses so far: Hare's, and AFV0 to AFV3 at the 8x8 level (E5c).
    // E5b adds DCT4X8 and DCT8X4, and E5d the non-aligned merges' DCT16X16 and DCT32X32 (already in it).
    // Default's tracer test keeps Hare's set.
    internal static readonly JxlAcStrategyType[] SquirrelStrategies =
    {
        JxlAcStrategyType.Dct, JxlAcStrategyType.Identity, JxlAcStrategyType.Dct2X2, JxlAcStrategyType.Dct4X4,
        JxlAcStrategyType.Afv0, JxlAcStrategyType.Afv1, JxlAcStrategyType.Afv2, JxlAcStrategyType.Afv3,
        JxlAcStrategyType.Dct16X8, JxlAcStrategyType.Dct8X16, JxlAcStrategyType.Dct16X16,
        JxlAcStrategyType.Dct32X16, JxlAcStrategyType.Dct16X32, JxlAcStrategyType.Dct32X32,
        JxlAcStrategyType.Dct64X32, JxlAcStrategyType.Dct32X64, JxlAcStrategyType.Dct64X64,
    };

    // libjxl's choices at Squirrel, read back through the decoder's parse: XYB VarDCT, Gaborish above d0.5,
    // near-lossless DC in every LF group, every block covered once by a strategy of Squirrel's set so far,
    // and the pixel statistics leaving these photos at the distance's X scale and a B scale of 2. The
    // remaining check is a temporary departure, which a later ticket flips: an EPF sharpness of 4 in every
    // block (E5h).
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

        // Every block is covered once by one of Squirrel's strategies so far (no DCT4X8 or DCT8X4 until
        // E5b), and the search still merges into larger blocks.
        byte[] strategies = JxlEncAcStrategyTests.StrategiesOf(frame);
        TestContext.Current.TestOutputHelper?.WriteLine($"{sizeCase} d{distance}: [{JxlEncAcStrategyTests.CoverageOf(strategies)}]");
        foreach (byte b in strategies)
        {
            JxlAcStrategyType strategy = (JxlAcStrategyType)(b >> 1);
            Assert.True(Array.IndexOf(SquirrelStrategies, strategy) >= 0, $"{strategy} is not in Squirrel's search so far");
        }

        Assert.Contains(strategies, b => JxlAcStrategy.FromRawStrategy(b >> 1).CoveredBlocksX * JxlAcStrategy.FromRawStrategy(b >> 1).CoveredBlocksY > 1);

        // No pixel of these photos steps far enough to raise a scale (Encode_CalibrationImage_HasLibjxlsQmScales
        // has images that do).
        Assert.Equal(expectedXQmScale, header.XQmScale);
        Assert.Equal(2u, header.BQmScale);

        // Temporary departure (E5h): the constant sharpness of 4 in every block. libjxl writes 4 too where
        // the frame has no EPF (d0.5) or the distance is below 0.5, and E5h keeps those.
        for (int by = 0; by < frame.State.YSizeBlocks; by++)
        {
            Assert.All(frame.State.EpfSharpness.DangerousGetRowSpan(by).ToArray(), s => Assert.Equal(4, s));
        }
    }

    // The X and B quantisation scales of each calibration image at Thorough, through the decoder's parse.
    // Default (Hare) writes the distance's X scale (3, or 4 above d2.5) and a B scale of 2; from Squirrel
    // down libjxl raises them from the pixel statistics of the XYB image. The photos stay at Default's
    // scales; bike_alpha (the simplified invisible pixels), text and line_art are raised. Each value is
    // cjxl v0.12.0 -e 7's (Encode_CalibrationImage_QmScalesAreCjxlEffort7s checks it with the tool).
    [Theory]
    [InlineData("bike", 1.0, 3u, 2u)]
    [InlineData("bike", 2.0, 3u, 2u)]
    [InlineData("bike", 4.0, 4u, 2u)]
    [InlineData("calliphora", 1.0, 3u, 2u)]
    [InlineData("calliphora", 2.0, 3u, 2u)]
    [InlineData("calliphora", 4.0, 4u, 2u)]
    [InlineData("bike_alpha", 1.0, 5u, 4u)]
    [InlineData("bike_alpha", 2.0, 5u, 4u)]
    [InlineData("bike_alpha", 4.0, 5u, 4u)]
    [InlineData("gradient", 1.0, 3u, 2u)]
    [InlineData("gradient", 2.0, 3u, 2u)]
    [InlineData("gradient", 4.0, 4u, 2u)]
    [InlineData("text", 1.0, 4u, 3u)]
    [InlineData("text", 2.0, 4u, 3u)]
    [InlineData("text", 4.0, 4u, 3u)]
    [InlineData("line_art", 1.0, 3u, 5u)]
    [InlineData("line_art", 2.0, 3u, 5u)]
    [InlineData("line_art", 4.0, 4u, 5u)]
    public void Encode_CalibrationImage_HasLibjxlsQmScales(string image, double distance, uint expectedXQmScale, uint expectedBQmScale)
    {
        JxlCalibrationRow row = JxlCalibrationTable.Instance.Find(image, JxlEffort.Thorough, distance);

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(EncodeCalibrationImage(row));

        TestContext.Current.TestOutputHelper?.WriteLine($"{image} d{distance}: x {frame.FrameHeader.XQmScale} b {frame.FrameHeader.BQmScale}");
        Assert.Equal(expectedXQmScale, frame.FrameHeader.XQmScale);
        Assert.Equal(expectedBQmScale, frame.FrameHeader.BQmScale);
    }

    // The images whose scales Squirrel raises (B 5 for line_art, X 4 and B 3 for text) decode through each
    // reader within the interim bound: the decoders apply the raised scales' dequantisation. bike_alpha's
    // raised scales go through the readers in Encode_RgbaPhoto_AlphaIsExactAndColourWithinTheInterimBound
    // (hard-masked Bike's scales are raised too: of the pins, E5f moved only its row).
    [Theory]
    [MemberData(nameof(RaisedScaleReaderCases))]
    public async Task Encode_ImageWithRaisedQmScales_DecodesThroughEachReaderWithinTheInterimBound(string reader, string image, double distance)
    {
        JxlCalibrationRow row = JxlCalibrationTable.Instance.Find(image, JxlEffort.Thorough, distance);
        using Image<Rgb24> original = Image.Load<Rgb24>(row.Image.FullPath);

        byte[] encoded = JxlEncoderRoundTrip.Encode(original, Lossy((float)distance));

        using Image<Rgb24> decoded = await JxlEncoderRoundTrip.DecodeAsync<Rgb24>(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, "LossyThoroughEncoder", string.Create(CultureInfo.InvariantCulture, $"{image}_d{distance}"));
        JxlLossyEncoderTests.AssertWithinInterimBound(original, decoded, $"{image} d{distance}, {encoded.Length} bytes");
    }

    // With JXL_TOOLS_DIR: the X and B quantisation scales of each calibration image at Thorough are those
    // of cjxl v0.12.0 -e 7 (the table's arguments), both read back through the decoder's parse. From
    // Squirrel down libjxl raises them from the pixel statistics of the padded XYB image; the statistics
    // are maxima of float differences with no lane sums, so cjxl's SIMD target does not change them.
    [Theory]
    [MemberData(nameof(CalibrationCases))]
    public async Task Encode_CalibrationImage_QmScalesAreCjxlEffort7s(string image, double distance)
    {
        JxlTools.GetToolPath("cjxl");
        JxlCalibrationRow row = JxlCalibrationTable.Instance.Find(image, JxlEffort.Thorough, distance);
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", "LossyThoroughEncoder");
        string cjxlPath = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{image}_e7_d{distance}.jxl"));
        await JxlTools.RunAsync("cjxl", TestContext.Current.CancellationToken, [row.Image.FullPath, cjxlPath, .. JxlCalibrationTable.Instance.CjxlArguments(row), "--container=0"]);

        using JxlDecodedDcGroups ours = JxlDecodedDcGroups.Decode(EncodeCalibrationImage(row));

        // cjxl's patches (text, bike_alpha d4) come first as a Modular reference frame; its scales are
        // those of the last frame, the VarDCT image. libjxl computes them before it looks for patches.
        JxlFrameHeader theirs = JxlCodestreamFrames.Read(await File.ReadAllBytesAsync(cjxlPath, TestContext.Current.CancellationToken))[^1];
        Assert.Equal(JxlFrameEncoding.VarDct, theirs.Encoding);

        TestContext.Current.TestOutputHelper?.WriteLine($"{image} d{distance}: ours x {ours.FrameHeader.XQmScale} b {ours.FrameHeader.BQmScale}, cjxl -e 7 x {theirs.XQmScale} b {theirs.BQmScale}");
        Assert.Equal(theirs.XQmScale, ours.FrameHeader.XQmScale);
        Assert.Equal(theirs.BQmScale, ours.FrameHeader.BQmScale);
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
    // (e5a-handoff.md says how). Since E5c (AFV) and E5e (the CfL pre-pass and Newton fit), only
    // the two smallest rows (the 17x23 flower and the dot) still equal their Default pins.
    [Theory]
    [InlineData("flower-384x244", "", 0.5f, "5935C26B6DA263D6B80EDE7C0FF1F20F76D6162E1BA2B4B22447195FCF04B00F")]
    [InlineData("flower-384x244", "", 1.0f, "2E2A27BA2C7EBDF6A2A1B008F9CEE7A88584977A211F49901791ED7C54419C3E")]
    [InlineData("flower-384x244", "", 2.0f, "DE4316DF4D64840834AAD9ECE3656B4C4918BA93A6C20467262161368E6F9051")]
    [InlineData("flower-384x244", "", 4.0f, "8D7674B35177BD7F0568D1282B47E4C5B4A1F06CD8EFECFC25F70B06C89D509D")]
    [InlineData("flower-17x23", "", 1.0f, "02082DB03AA3368FC88DE37F300D21A0A18EB78671F873D84F1DCF36C9841568")]
    [InlineData("dot-1x1", "", 1.0f, "0E0D22FC449A4D7D33375B82BD608513BED6874DF06D79A8F7DA55DE2F61CC42")]
    [InlineData("bike-582x453", "", 1.0f, "3FB8F5CD7CDF65B804D3541F6A7143F3013D852478BC8C11E82A04B6E765B1E5")]
    [InlineData("gradient-2049x9", "", 2.0f, "77B37ED07A5E9D7E0DDAE4CCDA66DE6B79EA1690DFEB1ECB0EDC451C06592C96")]
    [InlineData("flower-256x244", "soft", 1.0f, "4C25836DBA0FE19ACFC8406027CEA58381943C28219E3EFFF4FFAFBBE5F82139")]
    [InlineData("bike-582x453", "hard", 1.0f, "F332187CA50E31E35C41539226FB56D3F84A1C88EE60E641B7ADC7874AFF1F58")]
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

    // A calibration image as its PNG stores it, as cjxl reads it: RGB, or RGBA for bike_alpha.
    private static byte[] EncodeCalibrationImage(JxlCalibrationRow row)
    {
        JxlEncoder encoder = Lossy((float)row.Distance);
        if (Image.Identify(row.Image.FullPath).Metadata.GetPngMetadata().ColorType is PngColorType.RgbWithAlpha)
        {
            using Image<Rgba32> rgba = Image.Load<Rgba32>(row.Image.FullPath);
            return JxlEncoderRoundTrip.Encode(rgba, encoder);
        }

        using Image<Rgb24> rgb = Image.Load<Rgb24>(row.Image.FullPath);
        return JxlEncoderRoundTrip.Encode(rgb, encoder);
    }

    private static TheoryData<string, double> CreateCalibrationCases()
    {
        TheoryData<string, double> cases = new();
        foreach (JxlCalibrationImage image in JxlCalibrationTable.Instance.Images)
        {
            foreach (double distance in new[] { 1.0, 2.0, 4.0 })
            {
                cases.Add(image.Name, distance);
            }
        }

        return cases;
    }

    private static TheoryData<string, string, double> CreateRaisedScaleReaderCases()
    {
        TheoryData<string, string, double> cases = new();
        foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
        {
            foreach ((string image, double distance) in new[] { ("line_art", 1.0), ("line_art", 2.0), ("line_art", 4.0), ("text", 2.0) })
            {
                cases.Add(reader.ToString(), image, distance);
            }
        }

        return cases;
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
