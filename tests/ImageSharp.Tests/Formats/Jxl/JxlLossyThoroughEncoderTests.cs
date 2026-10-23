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

    // E5a's record: while every temporary departure is in place, Squirrel runs Hare's VarDCT choices, so an
    // image with no alpha is the same file at Thorough as at Default. With alpha it is not: the alpha tree
    // is learned with Squirrel's parameters (7 properties and 96 values against Hare's 4 and 48). Each later
    // E5 ticket breaks the equality on purpose, and changes this test when it does. E5c (AFV at the 8x8
    // level) broke it for the flower and Bike; the gradient, 9 pixels high, chooses no AFV block.
    // E5f's pixel-statistics scales leave these RGB photos at Hare's scales; they change text and line_art
    // (Encode_CalibrationImage_HasLibjxlsQmScales).
    [Theory]
    [InlineData("flower-384x244", "", 1.0f, false)]
    [InlineData("bike-582x453", "", 1.0f, false)]
    [InlineData("bike-582x453", "", 4.0f, false)]
    [InlineData("gradient-2049x9", "", 2.0f, true)]
    [InlineData("bike-582x453", "hard", 1.0f, false)]
    [InlineData("flower-256x244", "soft", 1.0f, false)]
    public void Encode_WithEveryTemporaryDeparture_IsDefaultsFileOnlyWithoutAlpha(string sizeCase, string mask, float distance, bool expectedEqual)
    {
        byte[] thorough = EncodeCase(sizeCase, mask, Lossy(distance));
        byte[] @default = EncodeCase(sizeCase, mask, JxlLossyDefaultEncoderTests.Lossy(distance));

        TestContext.Current.TestOutputHelper?.WriteLine($"{sizeCase} {mask} d{distance}: Thorough {thorough.Length} B, Default {@default.Length} B");
        Assert.Equal(expectedEqual, thorough.AsSpan().SequenceEqual(@default));
    }

    // SHA-256 of this encoder's lossy Thorough output: the one table of Thorough pins. It uses no FMA and no
    // C runtime maths on a path that reaches the bytes, so every leg gives the same file. Every E5 ticket
    // changes these on purpose; tickets that run in parallel regenerate the table when they merge
    // (e5a-handoff.md says how). Since E5c (AFV) only the three small rows without alpha (the 17x23 flower,
    // the dot and the gradient) still equal their Default pins.
    [Theory]
    [InlineData("flower-384x244", "", 0.5f, "25948546CC332E02E03CC592ABD42DC86D46DBBDDB42D49307E895C17B8FAE5B")]
    [InlineData("flower-384x244", "", 1.0f, "5A0127CDC6E36DF41BFE9B1D338B326F05B6AD2BAB0F0A914139A3F095C99055")]
    [InlineData("flower-384x244", "", 2.0f, "59BFFE0FAA00A94512F711C3F8982A95BDFD87E5074A29E01CF37D97FDA05CB6")]
    [InlineData("flower-384x244", "", 4.0f, "4518705E39DA2580EE2F1362320107B7AAB908E843834EE96D4A3D7F3D33ED48")]
    [InlineData("flower-17x23", "", 1.0f, "02082DB03AA3368FC88DE37F300D21A0A18EB78671F873D84F1DCF36C9841568")]
    [InlineData("dot-1x1", "", 1.0f, "0E0D22FC449A4D7D33375B82BD608513BED6874DF06D79A8F7DA55DE2F61CC42")]
    [InlineData("bike-582x453", "", 1.0f, "96EDAAAD1D1AACD095ED688498624F2F28DCC94F44D5884C31B9A60E0FE6040C")]
    [InlineData("gradient-2049x9", "", 2.0f, "BB61F5256C35D906407A2F732AD242BDA4A47FF75CD40C16D6A83CA0CCC1E834")]
    [InlineData("flower-256x244", "soft", 1.0f, "31F2FE28924D952B8EEACF40D0C98F8FBAC551731A566FE2DBA06BDB07D8C3E5")]
    [InlineData("bike-582x453", "hard", 1.0f, "DDEF0C10375EE234AECE81C35EF0B49A8916A0A920499B8819B4B601CE373FA9")]
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
