// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ReferenceCodecs;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Lossy encoding at Fastest (libjxl effort 3): an 8-bit RGB image with no profile and no alpha becomes
/// an XYB VarDCT frame of DCT8 blocks with a constant quant field (E3a). Alpha is E3c's
/// (<see cref="JxlLossyAlphaEncoderTests"/>), and grey, 16-bit and float input E3d's
/// (<see cref="JxlLossyDepthEncoderTests"/>).
/// </summary>
/// <remarks>
/// The interim error bound: each channel's RMSE against the source, in 8-bit steps, is at most
/// <see cref="InterimRmseBound"/>. It only catches a broken encoder; E3f replaces it with the SSIMULACRA2
/// calibration check (T2). For reference, cjxl v0.12.0 <c>-e 3</c> on the flower crop gives RMSE (R, G, B)
/// of (3.78, 2.10, 9.29) at d1, (5.62, 3.40, 11.92) at d2 and (7.55, 4.99, 14.01) at d4, and djxl decodes
/// this encoder's files of the crop to the same 8-bit pixels as cjxl's.
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlLossyEncoderTests
{
    // The interim bound on each channel's RMSE, in 8-bit steps: 8 % of the range. The worst measured
    // channel is the flower's blue at d4 (14.0); a broken stage (a wrong DC, a lost channel or a
    // misplaced block) gives tens of steps.
    private const double InterimRmseBound = 20.0;

    public static TheoryData<string, float> PhotoReaderCases { get; } = CreatePhotoReaderCases();

    public static TheoryData<string, string> SizeReaderCases { get; } = CreateSizeReaderCases();

    [Theory]
    [MemberData(nameof(PhotoReaderCases))]
    public async Task Encode_Photo_DecodesThroughEachReaderWithinTheInterimBound(string reader, float distance)
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));

        using Image<Rgb24> decoded = await DecodeAsync(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, $"photo_d{distance}");
        AssertWithinInterimBound(image, decoded, $"d{distance}, {encoded.Length} bytes");
    }

    // The same image through cjxl v0.12.0 at effort 3, both files decoded by djxl. The quantised values
    // match but for the odd one that FMA in cjxl rounds the other way (ADR 0004); the decoder's DC
    // smoothing and EPF spread such a value over the blocks around it. Measured: the flower gives the same
    // pixels at every distance (RMSE 0, peak 0). In Bike the X DC of one block of 4,161 differs by one
    // step (block (5, 42)), which moves 342 pixels in 11 blocks by at most 5 steps (RMSE 0.07).
    [Theory]
    [InlineData("flower-384x244", 1.0f)]
    [InlineData("flower-384x244", 2.0f)]
    [InlineData("flower-384x244", 4.0f)]
    [InlineData("bike-582x453", 1.0f)]
    public async Task Encode_DecodesThroughDjxlToTheSamePixelsAsCjxlEffort3(string sizeCase, float distance)
    {
        JxlTools.GetToolPath("cjxl");
        using Image<Rgb24> image = LoadSizeCase(sizeCase);
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", "LossyEncoder");
        string name = $"{sizeCase}_d{distance.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        string pngPath = Path.Combine(directory, name + ".png");
        string cjxlPath = Path.Combine(directory, name + ".cjxl.jxl");
        image.SaveAsPng(pngPath);
        await JxlTools.RunAsync("cjxl", TestContext.Current.CancellationToken, pngPath, cjxlPath, "-e", "3", "-d", distance.ToString(System.Globalization.CultureInfo.InvariantCulture));

        using Image<Rgb24> ours = await DecodeAsync(JxlEncoderRoundTrip.Reader.Djxl, JxlEncoderRoundTrip.Encode(image, Lossy(distance)), name + ".ours");
        using Image<Rgb24> theirs = await DecodeAsync(JxlEncoderRoundTrip.Reader.Djxl, await File.ReadAllBytesAsync(cjxlPath, TestContext.Current.CancellationToken), name + ".cjxl");

        ChannelErrors errors = ChannelErrors.Measure(theirs, ours);
        TestContext.Current.TestOutputHelper?.WriteLine($"{name} against cjxl -e 3: {errors}");
        for (int c = 0; c < 3; c++)
        {
            Assert.True(errors.Rmse[c] <= 0.1 && errors.Peak[c] <= 8, $"{name}: channel {c} differs from cjxl's: {errors}");
        }
    }

    // libjxl's choices at effort 3, read back through the decoder's parse: XYB VarDCT, DCT8 everywhere, one
    // quant field value, Gaborish off, and EPF iterations of (d >= 0.7) + (d >= 1.5) + (d >= 4)
    // (LoopFilterFromParams); the X quantisation scale is 3 plus (d > 2.5) + (d > 5.5) + (d > 9.5)
    // (ComputeChromacityAdjustments), and B's is 2.
    [Theory]
    [InlineData(0.5f, 0u, 3u)]
    [InlineData(1.0f, 1u, 3u)]
    [InlineData(2.0f, 2u, 3u)]
    [InlineData(4.0f, 3u, 4u)]
    [InlineData(10.0f, 3u, 6u)]
    public void Encode_Photo_IsXybVarDctOfDct8BlocksWithTheEffort3LoopFilter(float distance, uint expectedEpfIters, uint expectedXQmScale)
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(distance)));

        JxlFrameHeader header = frame.FrameHeader;
        Assert.Equal(JxlFrameEncoding.VarDct, header.Encoding);
        Assert.True(header.NonserializedMetadata.M.XybEncoded);
        Assert.Equal(JxlColorTransform.Xyb, header.ColorTransform);
        Assert.False(header.LoopFilter.Gab);
        Assert.Equal(expectedEpfIters, header.LoopFilter.EpfIters);
        Assert.Equal(expectedXQmScale, header.XQmScale);
        Assert.Equal(2u, header.BQmScale);
        Assert.Equal(1u, header.Upsampling);
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
    }

    [Theory]
    [MemberData(nameof(SizeReaderCases))]
    public async Task Encode_Size_RoundTripsWithinTheInterimBound(string reader, string sizeCase)
    {
        using Image<Rgb24> image = LoadSizeCase(sizeCase);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(1.0f));

        using Image<Rgb24> decoded = await DecodeAsync(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), encoded, sizeCase);
        AssertWithinInterimBound(image, decoded, $"{sizeCase}, {encoded.Length} bytes");
    }

    // The group layout of each size case, as the decoder's parse reads it (VarDCT groups are 256 pixels and
    // LF groups 2048).
    [Theory]
    [InlineData("flower-256x244", 1, 1)]
    [InlineData("flower-17x23", 1, 1)]
    [InlineData("dot-1x1", 1, 1)]
    [InlineData("bike-582x453", 6, 1)]
    [InlineData("gradient-2049x9", 9, 2)]
    public void Encode_Size_HasTheGroupsOfItsSize(string sizeCase, long expectedGroups, long expectedDcGroups)
    {
        using Image<Rgb24> image = LoadSizeCase(sizeCase);

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(1.0f)));

        JxlFrameDimensions frameDim = frame.FrameHeader.ToFrameDimensions();
        Assert.Equal(expectedGroups, frameDim.NumGroups);
        Assert.Equal(expectedDcGroups, frameDim.NumDcGroups);
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

    // libjxl clamps distances below 0.05 to 0.05 (kMinButteraugliDistance), and the X quantisation scale
    // of both is 3, so the files are the same.
    [Fact]
    public void Encode_DistanceBelowTheMinimum_IsClampedTo005()
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        byte[] clamped = JxlEncoderRoundTrip.Encode(image, Lossy(0.01f));

        Assert.Equal(JxlEncoderRoundTrip.Encode(image, Lossy(0.05f)), clamped);
    }

    // From distance 10 libjxl encodes at half resolution; this port does not resample (a departure), so
    // the frame keeps the image's size and still decodes.
    [Theory]
    [InlineData(10.0f)]
    [InlineData(25.0f)]
    public void Encode_DistanceOfTenAndAbove_EncodesWithoutResampling(float distance)
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(encoded);
        Assert.Equal(1u, frame.FrameHeader.Upsampling);
        using Image<Rgb24> decoded = Image.Load<Rgb24>(JxlEncoderRoundTrip.CreateConfiguration(), encoded);
        Assert.Equal(image.Size(), decoded.Size());
    }

    // SHA-256 of this encoder's output (feat/jxl-e3a). It does not use FMA or the C runtime's maths on a
    // path that reaches the bytes, so every leg gives the same file. Any change to lossy Fastest output must
    // update these on purpose; E3b changes them all (it codes a custom coefficient order).
    [Theory]
    [InlineData("flower-384x244", 1.0f, "9F768D1DE417464B30246530946A91452FFFCEFF1258D877C6F0BDE0930D6DEB")]
    [InlineData("flower-384x244", 2.0f, "C8BF065A739A01FC3FC9094872663FF5B4F458E8607E0EEE65DCAB63D8255D9C")]
    [InlineData("flower-384x244", 4.0f, "6DFA9317AD4E55E0A792EB33FB901DC93414AD24D78A8BAABF942E6B6C982728")]
    [InlineData("flower-17x23", 1.0f, "35E5DC90898C3648C9DEEF26A688EA7CCA5FCE87816FCFC0056BB1DA9F7582CF")]
    [InlineData("dot-1x1", 1.0f, "2C853B7A3F1B6DF50A6F6E1F32608E39C456C0C15272C4006AF253A8C6E3AEBD")]
    [InlineData("bike-582x453", 1.0f, "6AA5C9DF65EF91A904EFE13D3C6E8CD0C97F2A57DD8DB77994EC438242B48708")]
    [InlineData("gradient-2049x9", 2.0f, "C0B0B1C7C7A935D4D4EE70959E836E1EDCCC90DF099FE01E6DB782DC9308C322")]
    public void Encode_Lossy_IsByteIdenticalToItsPin(string sizeCase, float distance, string expectedSha256)
    {
        using Image<Rgb24> image = LoadSizeCase(sizeCase);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));

        Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(encoded)));
    }

    [Theory]
    [InlineData(nameof(JxlEffort.Default), "E4")]
    [InlineData(nameof(JxlEffort.Thorough), "E5")]
    public void Encode_LossyAboveFastest_ThrowsNotSupportedNamingItsSlice(string effort, string slice)
    {
        using Image<Rgb24> image = new(1, 1);
        using MemoryStream stream = new();

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => image.Save(stream, new JxlEncoder { Distance = 1, Effort = Enum.Parse<JxlEffort>(effort) }));

        Assert.Contains(slice, ex.Message);
        Assert.Equal(0, stream.Length);
    }

    // The colour rules for a profile come with E3e: until then any profile throws.
    [Fact]
    public void Encode_LossyWithAProfile_ThrowsNotSupportedNamingE3e()
    {
        using Image<Rgb24> image = new(1, 1);
        image.Metadata.IccProfile = new IccProfile(File.ReadAllBytes(TestFile.GetInputFileFullPath(TestImages.Icc.LittleCmsSrgbV4)));

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => JxlEncoderRoundTrip.Encode(image, Lossy(1.0f)));

        Assert.Contains("E3e", ex.Message);
    }

    // libjxl's JxlEncoderSetFrameDistance accepts [0, 25].
    [Theory]
    [InlineData(-1.0f)]
    [InlineData(25.5f)]
    [InlineData(float.NaN)]
    public void Encode_DistanceOutOfRange_Throws(float distance)
    {
        using Image<Rgb24> image = new(1, 1);

        Assert.Throws<ImageFormatException>(() => JxlEncoderRoundTrip.Encode(image, new JxlEncoder { Distance = distance, Effort = JxlEffort.Fastest }));
    }

    internal static JxlEncoder Lossy(float distance) => new() { Distance = distance, Effort = JxlEffort.Fastest };

    // A size case: "flower-WxH" (a crop of the flower photo), "bike-582x453" (the Bike photo, odd in both
    // sides), "dot-1x1" or "gradient-WxH" (the round-trip gradient).
    internal static Image<Rgb24> LoadSizeCase(string sizeCase)
    {
        string[] parts = sizeCase.Split('-', 'x');
        int width = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        int height = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
        switch (parts[0])
        {
            case "flower":
            {
                Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();
                image.Mutate(x => x.Crop(new Rectangle(0, 0, width, height)));
                return image;
            }

            case "bike":
            {
                Image<Rgb24> image = Image.Load<Rgb24>(TestFile.GetInputFileFullPath(TestImages.Png.Bike));
                image.Metadata.IccProfile = null;
                Assert.Equal(new Size(width, height), image.Size());
                return image;
            }

            case "dot":
            {
                Image<Rgb24> image = new(width, height);
                image[0, 0] = new Rgb24(200, 120, 40);
                return image;
            }

            default:
                return JxlEncoderRoundTrip.CreateImage<Rgb24>(parts[0], width, height);
        }
    }

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

    private static TheoryData<string, string> CreateSizeReaderCases()
    {
        TheoryData<string, string> cases = new();
        foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
        {
            foreach (string sizeCase in new[] { "flower-256x244", "flower-17x23", "dot-1x1", "bike-582x453", "gradient-2049x9" })
            {
                cases.Add(reader.ToString(), sizeCase);
            }
        }

        return cases;
    }

    private static void AssertWithinInterimBound(Image<Rgb24> expected, Image<Rgb24> actual, string label)
    {
        Assert.Equal(expected.Size(), actual.Size());
        ChannelErrors errors = ChannelErrors.Measure(expected, actual);
        TestContext.Current.TestOutputHelper?.WriteLine($"{label}: {errors}");
        for (int c = 0; c < 3; c++)
        {
            Assert.True(errors.Rmse[c] <= InterimRmseBound, $"{label}: channel {c} RMSE {errors.Rmse[c]:F2} > {InterimRmseBound}");
        }
    }

    private static async Task<Image<Rgb24>> DecodeAsync(JxlEncoderRoundTrip.Reader reader, byte[] encoded, string name)
    {
        switch (reader)
        {
            case JxlEncoderRoundTrip.Reader.Ours:
                return Image.Load<Rgb24>(JxlEncoderRoundTrip.CreateConfiguration(), encoded);
            case JxlEncoderRoundTrip.Reader.Magick:
            {
                MagickReferenceDecoder.SkipUnlessJxlIsSupported();
                using MemoryStream stream = new(encoded);
                return MagickReferenceDecoder.Instance.Decode<Rgb24>(Configuration.Default, stream, CancellationToken.None);
            }

            default:
            {
                // Resolve the tool first, so the test skips before it writes anything.
                JxlTools.GetToolPath("djxl");
                string directory = TestEnvironment.CreateOutputDirectory("Jxl", "LossyEncoder");
                string jxlPath = Path.Combine(directory, name + ".jxl");
                string pngPath = Path.Combine(directory, name + ".djxl.png");
                File.Delete(pngPath);
                await File.WriteAllBytesAsync(jxlPath, encoded, TestContext.Current.CancellationToken);
                await JxlTools.RunAsync("djxl", TestContext.Current.CancellationToken, jxlPath, pngPath);
                return Image.Load<Rgb24>(pngPath);
            }
        }
    }

    /// <summary>The error of each channel of a decoded 8-bit image, in 8-bit steps.</summary>
    internal sealed record ChannelErrors(double[] Rmse, int[] Peak)
    {
        public static ChannelErrors Measure(Image<Rgb24> expected, Image<Rgb24> actual)
        {
            double[] sums = new double[3];
            int[] peak = new int[3];
            for (int y = 0; y < expected.Height; y++)
            {
                for (int x = 0; x < expected.Width; x++)
                {
                    Rgb24 e = expected[x, y];
                    Rgb24 a = actual[x, y];
                    int[] d = { a.R - e.R, a.G - e.G, a.B - e.B };
                    for (int c = 0; c < 3; c++)
                    {
                        sums[c] += d[c] * d[c];
                        peak[c] = Math.Max(peak[c], Math.Abs(d[c]));
                    }
                }
            }

            long n = (long)expected.Width * expected.Height;
            return new ChannelErrors(Array.ConvertAll(sums, s => Math.Sqrt(s / n)), peak);
        }

        public override string ToString()
            => $"RMSE R {this.Rmse[0]:F2} G {this.Rmse[1]:F2} B {this.Rmse[2]:F2}, peak R {this.Peak[0]} G {this.Peak[1]} B {this.Peak[2]}";
    }
}
