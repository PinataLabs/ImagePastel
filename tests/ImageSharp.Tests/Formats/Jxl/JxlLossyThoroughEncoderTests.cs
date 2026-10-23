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
    // qm scale checks are a temporary departure that E5f flips: the X quantisation scale from the distance
    // alone and a B scale of 2. The EPF sharpness is E5h's search. The Hare set itself grows with E5b
    // (DCT4X8, DCT8X4) and E5c (AFV).
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

        // Dynamic EPF sharpness (E5h): searched over {0, 2, 7} where the frame has EPF; a constant 4 where it
        // has none (d0.5).
        int[] histogram = SharpnessHistogram(frame);
        TestContext.Current.TestOutputHelper?.WriteLine($"{sizeCase} d{distance} sharpness: {FormatHistogram(histogram)}");
        if (expectedEpfIters == 0)
        {
            Assert.Equal(frame.State.XSizeBlocks * frame.State.YSizeBlocks, histogram[4]);
        }
        else
        {
            AssertSearched(histogram, distance, $"{sizeCase} d{distance}");
        }
    }

    // Dynamic EPF sharpness (libjxl's ComputeARHeuristics), read back from the file's AC metadata: from
    // distance 0.7 (where EPF starts) each block takes one of {0, 2, 7}, or of {0, 4} above d4.5, and the
    // plane is not constant. At d0.6 the frame has no EPF, so the sharpness stays the constant 4.
    [Theory]
    [InlineData("bike", 0.6f)]
    [InlineData("bike", 1.0f)]
    [InlineData("bike", 2.0f)]
    [InlineData("bike", 5.0f)]
    [InlineData("line_art", 0.6f)]
    [InlineData("line_art", 1.0f)]
    [InlineData("line_art", 2.0f)]
    [InlineData("line_art", 5.0f)]
    public void Encode_CalibrationImage_SearchesTheEpfSharpness(string imageName, float distance)
    {
        using Image<Rgb24> image = Image.Load<Rgb24>(JxlCalibrationTable.Instance.Images.Single(i => i.Name == imageName).FullPath);

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(distance)));

        int[] histogram = SharpnessHistogram(frame);
        TestContext.Current.TestOutputHelper?.WriteLine($"{imageName} d{distance}: EPF iterations {frame.FrameHeader.LoopFilter.EpfIters}, sharpness {FormatHistogram(histogram)}");
        if (distance < 0.7f)
        {
            Assert.Equal(0u, frame.FrameHeader.LoopFilter.EpfIters);
            Assert.Equal(frame.State.XSizeBlocks * frame.State.YSizeBlocks, histogram[4]);
            return;
        }

        Assert.True(frame.FrameHeader.LoopFilter.EpfIters > 0);
        AssertSearched(histogram, distance, $"{imageName} d{distance}");
    }

    // E5a's record: while every temporary departure was in place, Squirrel ran Hare's VarDCT choices, so an
    // image with no alpha was the same file at Thorough as at Default; with alpha it was not, as the alpha
    // tree is learned with Squirrel's parameters (7 properties and 96 values against Hare's 4 and 48).
    // E5h's dynamic EPF sharpness broke the equality without alpha on purpose: every row here has EPF.
    [Theory]
    [InlineData("flower-384x244", "", 1.0f, false)]
    [InlineData("bike-582x453", "", 1.0f, false)]
    [InlineData("bike-582x453", "", 4.0f, false)]
    [InlineData("gradient-2049x9", "", 2.0f, false)]
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
    // (e5a-handoff.md says how). From E5h the rows with EPF differ from the Default pins.
    [Theory]
    [InlineData("flower-384x244", "", 0.5f, "88E6145ED0606797E70C0BEC85B59FF73562B4A68A4171AC5BA6F731082CDEB5")]
    [InlineData("flower-384x244", "", 1.0f, "C86B8AA29ED8983E4054AA18C912DA02566A23B8883C57BBE0D2C08ED6802D2A")]
    [InlineData("flower-384x244", "", 2.0f, "259C6E3219C6202C55F892E5B4A337563C75CF1D76427C9EDAC9EC4A4974DA4B")]
    [InlineData("flower-384x244", "", 4.0f, "A12A57BA61F8447AE4D799034C9E3115220545CFE8E3A67D8244E8D083F14D02")]
    [InlineData("flower-17x23", "", 1.0f, "FED88E8EE80F72986918E3BCCE5C1618AE86A3920766A671783EF90E525707EA")]
    [InlineData("dot-1x1", "", 1.0f, "A84E239F194B63933E2CF99F9088EB8661BE9E5BC3F0CD2DD4CB4F49E967B500")]
    [InlineData("bike-582x453", "", 1.0f, "9934FB423CC82EF2B93FD38A17276B79216C948A1B1C020F7A191CE2E27F34AC")]
    [InlineData("gradient-2049x9", "", 2.0f, "2809346538E74C6E37171A5E31DFD88AC08EECDA28B914D55508996D2C83871C")]
    [InlineData("flower-256x244", "soft", 1.0f, "20B2EF639264D6C0D44EF3FD57D91E9C61B57DCED37B416C4DF461A3930D2529")]
    [InlineData("bike-582x453", "hard", 1.0f, "D64D4130B34CD16311FEA6CEDECCE2EE9FCC92A6D764009854666CFD12CD38A2")]
    public void Encode_LossyThorough_IsByteIdenticalToItsPin(string sizeCase, string mask, float distance, string expectedSha256)
    {
        byte[] encoded = EncodeCase(sizeCase, mask, Lossy(distance));

        // Logged in full, so that a ticket that changes the output on purpose can regenerate the table.
        string actual = Convert.ToHexString(SHA256.HashData(encoded));
        TestContext.Current.TestOutputHelper?.WriteLine($"[InlineData(\"{sizeCase}\", \"{mask}\", {distance:0.0#}f, \"{actual}\")]");
        Assert.Equal(expectedSha256, actual);
    }

    internal static JxlEncoder Lossy(float distance) => new() { Distance = distance, Effort = JxlEffort.Thorough };

    // The number of blocks of each sharpness 0 to 7 in the parsed frame.
    private static int[] SharpnessHistogram(JxlDecodedDcGroups frame)
    {
        int[] histogram = new int[8];
        for (int by = 0; by < frame.State.YSizeBlocks; by++)
        {
            foreach (byte s in frame.State.EpfSharpness.DangerousGetRowSpan(by).Slice(0, frame.State.XSizeBlocks))
            {
                histogram[s]++;
            }
        }

        return histogram;
    }

    private static string FormatHistogram(int[] histogram)
        => string.Join(", ", histogram.Select((n, s) => (n, s)).Where(e => e.n > 0).Select(e => $"{e.s}: {e.n}"));

    // Only the search's steps occur, and at least two of them.
    private static void AssertSearched(int[] histogram, float distance, string label)
    {
        byte[] steps = distance > 4.5f ? new byte[] { 0, 4 } : new byte[] { 0, 2, 7 };
        for (int s = 0; s < 8; s++)
        {
            Assert.True(histogram[s] == 0 || Array.IndexOf(steps, (byte)s) >= 0, $"{label}: {histogram[s]} blocks of sharpness {s}");
        }

        Assert.True(histogram.Count(n => n > 0) > 1, $"{label}: the sharpness is constant");
    }

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
