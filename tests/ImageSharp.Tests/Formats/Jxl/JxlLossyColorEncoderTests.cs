// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Icc;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// E3e: the colour rules of #115 for lossy encodes. The pixel profile is the only colour input: a profile
/// that <see cref="JxlColorEncodingHeader.TryFromIccProfile"/> recognises is written as its colour encoding (not
/// the ICC), and the pixels reach XYB through its transfer function and primaries; a profile it does not
/// recognise throws; a profile whose colour space does not match the pixels is ignored. The intensity
/// target follows the transfer function, unless <see cref="JxlMetadata.IntensityTarget"/> sets it.
/// The Display P3 checks also run at Default (slice E4, #297 story 6).
/// </summary>
/// <remarks>
/// The decoders give the pixels in the image's own colour space (ours attaches a profile it builds for the
/// encoding, #105), so the decoded samples are compared with the input samples, within the interim bound
/// of <see cref="JxlLossyEncoderTests"/>.
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlLossyColorEncoderTests
{
    private const string UnrecognisedMessage = "lossy JPEG XL needs an ICC profile the encoder can describe; use Distance = 0 or convert to sRGB first";

    public static TheoryData<string> Readers { get; } = new()
    {
        nameof(JxlEncoderRoundTrip.Reader.Ours),
        nameof(JxlEncoderRoundTrip.Reader.Magick),
        nameof(JxlEncoderRoundTrip.Reader.Djxl),
    };

    // Each reader at Fastest and at Default (#297 story 6: a wide-gamut input works at Default as at
    // Fastest).
    public static TheoryData<string, string> ReaderEfforts { get; } = CreateReaderEfforts();

    [Theory]
    [InlineData(nameof(JxlEffort.Fastest))]
    [InlineData(nameof(JxlEffort.Default))]
    public void Encode_DisplayP3Lossy_WritesTheP3EnumAndNoIcc(string effort)
    {
        using Image<Rgb24> image = LoadPhoto(TestImages.Icc.CompactDisplayP3V4);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(effort, 1.0f));

        JxlMetadata metadata = Identify(encoded);
        Assert.True(metadata.IsXybEncoded);
        Assert.Null(metadata.EmbeddedIccProfile);
        JxlColorEncodingFromIccTests.AssertSameEncoding(JxlColorEncodingFromIccTests.Parse("RGB_D65_DCI_Per_SRG"), JxlCodestreamFrames.ReadColorEncoding(encoded));
    }

    [Theory]
    [MemberData(nameof(ReaderEfforts))]
    public async Task Encode_DisplayP3Lossy_DecodesThroughEachReaderToTheP3SamplesWithinTheInterimBound(string reader, string effort)
    {
        using Image<Rgb24> image = LoadPhoto(TestImages.Icc.CompactDisplayP3V4);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(effort, 1.0f));

        using Image<Rgb24> decoded = await JxlLossyEncoderTests.DecodeAsync(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, $"color_p3_{effort}_d1");
        JxlLossyEncoderTests.AssertWithinInterimBound(image, decoded, $"Display P3 d1 at {effort} through {reader}, {encoded.Length} bytes");
    }

    // A LUT-based profile (lut16 with an XYZ connection space, lutAToB) and one with a Lab connection space
    // (lut8): the encoder cannot describe them, so lossy throws with #115's message, and lossless embeds
    // them verbatim, never recognising them.
    [Theory]
    [InlineData(TestImages.Icc.LittleCmsRgbLut16V2)]
    [InlineData(TestImages.Icc.LittleCmsRgbLutAToBV4)]
    [InlineData(TestImages.Icc.LittleCmsRgbLut8V2)]
    public void Encode_LutOrLabProfile_ThrowsForLossyAndEmbedsVerbatimForLossless(string path)
    {
        using Image<Rgb24> image = new(8, 8);
        byte[] profile = File.ReadAllBytes(TestFile.GetInputFileFullPath(path));
        image.Metadata.IccProfile = new IccProfile(profile);
        using MemoryStream stream = new();

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => image.Save(stream, JxlLossyEncoderTests.Lossy(1.0f)));

        Assert.Equal(UnrecognisedMessage, ex.Message);
        Assert.Equal(0, stream.Length);
        JxlMetadata lossless = Identify(JxlEncoderRoundTrip.Encode(image));
        Assert.Null(lossless.ColorEncoding);
        Assert.Equal(profile, lossless.EmbeddedIccProfile.ToByteArray());
    }

    // The CMYK profile the JPEG decoder leaves on its RGB output, and a grey profile on RGB pixels, do not
    // describe the pixels: they are treated as no profile, so the file has the sRGB enum and no ICC.
    [Theory]
    [InlineData(TestImages.Icc.LittleCmsFakeCmykV4)]
    [InlineData(TestImages.Icc.LittleCmsGrayGamma22V4)]
    public void Encode_ProfileOfAnotherColourSpace_IsIgnored(string path)
    {
        using Image<Rgb24> image = LoadPhoto(path);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, JxlLossyEncoderTests.Lossy(1.0f));

        JxlMetadata metadata = Identify(encoded);
        Assert.Null(metadata.EmbeddedIccProfile);
        JxlColorEncodingFromIccTests.AssertSameEncoding(JxlColorEncodingFromIccTests.Parse("RGB_D65_SRG_Rel_SRG"), JxlCodestreamFrames.ReadColorEncoding(encoded));
    }

    // libjxl's SetIntensityTarget: 10,000 nits for PQ, 1,000 for HLG and 255 otherwise, unless the caller
    // sets one (JxlEncoderSetBasicInfo). The PQ and HLG profiles are the cicp-only ones.
    [Theory]
    [InlineData("none", 0f, 255f)]
    [InlineData("srgb", 0f, 255f)]
    [InlineData("pq", 0f, 10000f)]
    [InlineData("hlg", 0f, 1000f)]
    [InlineData("none", 400f, 400f)]
    [InlineData("pq", 4000f, 4000f)]
    public void Encode_Lossy_WritesTheIntensityTargetOfItsTransferFunctionOrTheMetadata(string profile, float metadataTarget, float expected)
    {
        using Image<Rgb24> image = new(8, 8);
        image.Metadata.IccProfile = profile switch
        {
            "srgb" => new IccProfile(File.ReadAllBytes(TestFile.GetInputFileFullPath(TestImages.Icc.LittleCmsSrgbV4))),
            "pq" => JxlColorEncodingFromIccTests.CicpOnlyProfile(9, 16, 0, 1),
            "hlg" => JxlColorEncodingFromIccTests.CicpOnlyProfile(9, 18, 0, 1),
            _ => null,
        };
        image.Metadata.GetFormatMetadata(JxlFormat.Instance).IntensityTarget = metadataTarget;

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, JxlLossyEncoderTests.Lossy(1.0f));

        Assert.Equal(expected, Identify(encoded).IntensityTarget);
    }

    // The header stores the intensity target as a half float, which has to be positive.
    [Theory]
    [InlineData(-1f)]
    [InlineData(70000f)]
    [InlineData(float.NaN)]
    public void Encode_IntensityTargetOutOfRange_Throws(float target)
    {
        using Image<Rgb24> image = new(8, 8);
        image.Metadata.GetFormatMetadata(JxlFormat.Instance).IntensityTarget = target;

        Assert.Throws<ImageFormatException>(() => JxlEncoderRoundTrip.Encode(image, JxlLossyEncoderTests.Lossy(1.0f)));
    }

    // #115's round trip: a decoded image carries the profile the decoder built for its colour encoding (or
    // none for sRGB) and its intensity target, and a lossy re-encode gives back the same colour encoding and
    // intensity target, never an ICC.
    [Theory]
    [InlineData("RGB_D65_DCI_Rel_SRG")]
    [InlineData("RGB_D65_202_Per_PeQ")]
    [InlineData("RGB_D65_202_Rel_HLG")]
    [InlineData("RGB_D65_SRG_Rel_g0.4545455")]
    [InlineData("Gra_D65_Rel_709")]
    public void Encode_DecodedImage_KeepsItsColourEncodingAndIntensityTarget(string description)
    {
        JxlColorEncodingHeader expected = JxlColorEncodingFromIccTests.Parse(description);
        using Image<Rgb24> patches = CreatePatches(expected.IsGray);
        using Image source = expected.IsGray ? patches.CloneAs<L8>() : patches.Clone();
        source.Metadata.IccProfile = JxlIccBuilder.Create(expected);
        byte[] first = Encode(source);
        using Image decoded = Image.Load(JxlEncoderRoundTrip.CreateConfiguration(), first);
        Assert.NotNull(decoded.Metadata.IccProfile);

        byte[] second = Encode(decoded);

        JxlMetadata metadata = Identify(second);
        Assert.Null(metadata.EmbeddedIccProfile);
        JxlColorEncodingFromIccTests.AssertSameEncoding(expected, JxlCodestreamFrames.ReadColorEncoding(second));
        Assert.Equal(Identify(first).IntensityTarget, metadata.IntensityTarget);

        static byte[] Encode(Image image)
        {
            using MemoryStream stream = new();
            image.Save(stream, JxlLossyEncoderTests.Lossy(1.0f));
            return stream.ToArray();
        }
    }

    /// <summary>
    /// Gets the recognised encodings whose pixel path the patch tests check, with the worst patch error of
    /// cjxl v0.12.0's file (<c>-e 3 -d 1</c>, from a PNG with the same profile) through djxl. The encoding is
    /// a profile file, or one in the description form of <see cref="JxlColorEncodingFromIccTests"/> whose
    /// profile the decoder's builder makes. Between them they take every transfer function, a gamma, wide and
    /// custom primaries, a D50 and a DCI white, and grey.
    /// </summary>
    /// <remarks>
    /// The errors are large for the deepest colours of the wide gamuts (ProPhoto green, Adobe RGB red, PQ
    /// yellow): XYB clamps negative cone responses, which colours far outside sRGB have, and cjxl's files lose
    /// them the same way.
    /// </remarks>
    public static TheoryData<string, double> PatchCases { get; } = new()
    {
        { TestImages.Icc.CompactDisplayP3V4, 7.23 },
        { TestImages.Icc.LittleCmsAdobeRgbV4, 16.00 },
        { TestImages.Icc.LittleCmsProPhoto18V4, 19.72 },
        { TestImages.Icc.LittleCmsRec2020V4, 5.31 },
        { TestImages.Icc.LittleCmsLinearSrgbV4, 1.19 },
        { TestImages.Icc.LittleCmsGrayGamma22V4, 0.17 },
        { "RGB_D65_202_Rel_PeQ", 67.56 },
        { "RGB_D65_202_Rel_HLG", 12.81 },
        { "RGB_DCI_DCI_Rel_DCI", 29.36 },
        { "Gra_D65_Rel_HLG", 0.11 },
    };

    // Flat patches of saturated colours: the deep red, green and blue of the encoding's own gamut, which a
    // wrong primaries matrix or curve moves by tens of steps, and their mixes, white, black and greys. Our
    // decoder gives the samples in the image's colour space, so each patch must come back as its colour, as
    // well as cjxl's file does through djxl: the mean error of each patch's inner 8 x 8 pixels, in 8-bit
    // steps, is within one step of cjxl's worst. It needs no tools, so it runs under coverage too.
    [Theory]
    [MemberData(nameof(PatchCases))]
    public void Encode_RecognisedProfile_DecodesSaturatedPatchesAsWellAsCjxl(string profile, double cjxlWorst)
    {
        using Image<Rgb24> expected = CreatePatches(IsGrey(profile));

        byte[] encoded = EncodePatches(expected, profile);

        using Image<Rgb24> decoded = Image.Load<Rgb24>(JxlEncoderRoundTrip.CreateConfiguration(), encoded);
        double worst = WorstPatchError(expected, decoded, out string where);
        TestContext.Current.TestOutputHelper?.WriteLine($"{profile}: worst patch mean error {worst:F2} steps ({where}), cjxl {cjxlWorst:F2}, {encoded.Length} bytes");
        Assert.True(worst <= cjxlWorst + 1, $"{profile}: patch {where} is {worst:F2} steps off; cjxl's worst is {cjxlWorst:F2}");
    }

    // The same patches through cjxl v0.12.0 at effort 3 and through this encoder, both decoded by djxl. cjxl
    // converts to XYB with its CMS through the profile's own curves and colorants, and this encoder
    // analytically from the recognised enum, so the samples can differ a little. Measured: identical for
    // P3, linear sRGB, grey, PQ, HLG and DCI; Adobe RGB peak 1; ProPhoto RMSE 0.74 / 0.52 / 0.14, peak 3;
    // Rec. 2020 RMSE 0.19 / 0.26 / 1.11, peak 5. It also checks the recorded cjxl errors.
    [Theory]
    [MemberData(nameof(PatchCases))]
    public async Task Encode_RecognisedProfile_DecodesThroughDjxlToThePixelsOfCjxl(string profile, double cjxlWorst)
    {
        JxlTools.GetToolPath("cjxl");
        using Image<Rgb24> expected = CreatePatches(IsGrey(profile));
        string name = "color_patches_" + Path.GetFileNameWithoutExtension(profile).Replace(';', '-').Replace('.', '-');
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", "LossyEncoder");
        string pngPath = Path.Combine(directory, name + ".png");
        string cjxlPath = Path.Combine(directory, name + ".cjxl.jxl");
        byte[] encoded = EncodePatches(expected, profile, pngPath);
        await JxlTools.RunAsync("cjxl", TestContext.Current.CancellationToken, pngPath, cjxlPath, "-e", "3", "-d", "1");

        using Image<Rgb24> ours = await JxlLossyEncoderTests.DecodeAsync(JxlEncoderRoundTrip.Reader.Djxl, encoded, name + ".ours");
        using Image<Rgb24> theirs = await JxlLossyEncoderTests.DecodeAsync(JxlEncoderRoundTrip.Reader.Djxl, await File.ReadAllBytesAsync(cjxlPath, TestContext.Current.CancellationToken), name + ".cjxl");

        JxlLossyEncoderTests.ChannelErrors errors = JxlLossyEncoderTests.ChannelErrors.Measure(theirs, ours);
        double theirWorst = WorstPatchError(expected, theirs, out string where);
        TestContext.Current.TestOutputHelper?.WriteLine($"{profile} against cjxl -e 3: {errors}; cjxl's worst patch {theirWorst:F2} ({where})");
        Assert.True(Math.Abs(theirWorst - cjxlWorst) <= 0.5, $"{profile}: cjxl's worst patch is {theirWorst:F2}, recorded as {cjxlWorst:F2}");
        for (int c = 0; c < 3; c++)
        {
            Assert.True(errors.Rmse[c] <= 1.5 && errors.Peak[c] <= 6, $"{profile}: channel {c} differs from cjxl's: {errors}");
        }
    }

    private const int PatchSize = 16;

    private static readonly Rgb24[] PatchColours =
    {
        new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(255, 255, 0),
        new(0, 255, 255), new(255, 0, 255), new(255, 255, 255), new(0, 0, 0),
        new(128, 128, 128), new(64, 64, 64), new(192, 32, 32), new(32, 160, 64),
        new(40, 60, 200), new(230, 180, 120), new(90, 200, 220), new(200, 100, 240),
    };

    private static bool IsGrey(string profile) => profile.StartsWith("Gra", StringComparison.Ordinal) || profile.Contains("Gray", StringComparison.Ordinal);

    // Encodes the patches (as L8 for a grey profile) with the profile, and saves them as a PNG with it.
    private static byte[] EncodePatches(Image<Rgb24> patches, string profile, string pngPath = null)
    {
        IccProfile iccProfile = profile.Contains(".icc", StringComparison.Ordinal)
            ? new IccProfile(File.ReadAllBytes(TestFile.GetInputFileFullPath(profile)))
            : JxlIccBuilder.Create(JxlColorEncodingFromIccTests.Parse(profile));
        using Image image = IsGrey(profile) ? patches.CloneAs<L8>() : patches.Clone();
        image.Metadata.IccProfile = iccProfile;
        if (pngPath is not null)
        {
            image.SaveAsPng(pngPath);
        }

        using MemoryStream stream = new();
        image.Save(stream, JxlLossyEncoderTests.Lossy(1.0f));
        return stream.ToArray();
    }

    internal static Image<Rgb24> CreatePatches(bool grey)
    {
        Image<Rgb24> image = new(PatchSize * 4, PatchSize * 4);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgb24 c = PatchColours[((y / PatchSize) * 4) + (x / PatchSize)];
                image[x, y] = grey ? new Rgb24(c.G, c.G, c.G) : c;
            }
        }

        return image;
    }

    internal static double WorstPatchError(Image<Rgb24> expected, Image<Rgb24> actual, out string where)
    {
        double worst = 0;
        where = string.Empty;
        for (int p = 0; p < PatchColours.Length; p++)
        {
            int x0 = ((p % 4) * PatchSize) + 4;
            int y0 = ((p / 4) * PatchSize) + 4;
            double[] sums = new double[3];
            for (int y = y0; y < y0 + PatchSize - 8; y++)
            {
                for (int x = x0; x < x0 + PatchSize - 8; x++)
                {
                    Rgb24 e = expected[x, y];
                    Rgb24 a = actual[x, y];
                    sums[0] += a.R - e.R;
                    sums[1] += a.G - e.G;
                    sums[2] += a.B - e.B;
                }
            }

            for (int c = 0; c < 3; c++)
            {
                double mean = Math.Abs(sums[c]) / ((PatchSize - 8) * (PatchSize - 8));
                if (mean > worst)
                {
                    worst = mean;
                    where = $"{expected[x0, y0]} channel {c}";
                }
            }
        }

        return worst;
    }

    private static Image<Rgb24> LoadPhoto(string profilePath)
    {
        Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();
        image.Metadata.IccProfile = new IccProfile(File.ReadAllBytes(TestFile.GetInputFileFullPath(profilePath)));
        return image;
    }

    private static JxlEncoder Lossy(string effort, float distance) => new() { Distance = distance, Effort = Enum.Parse<JxlEffort>(effort) };

    private static TheoryData<string, string> CreateReaderEfforts()
    {
        TheoryData<string, string> data = new();
        foreach (string effort in new[] { nameof(JxlEffort.Fastest), nameof(JxlEffort.Default) })
        {
            foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
            {
                data.Add(reader.ToString(), effort);
            }
        }

        return data;
    }

    private static JxlMetadata Identify(byte[] encoded)
        => Image.Identify(JxlEncoderRoundTrip.CreateConfiguration(), new MemoryStream(encoded)).Metadata.GetFormatMetadata(JxlFormat.Instance);
}
