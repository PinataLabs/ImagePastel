// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Lossy encoding at Fastest (libjxl effort 3): an 8-bit RGB image with no profile and no alpha becomes
/// an XYB VarDCT frame of DCT8 blocks with a constant quant field (E3a) and a custom DCT8 coefficient
/// order (E3b). Alpha is E3c's (<see cref="JxlLossyAlphaEncoderTests"/>), and grey, 16-bit and float
/// input E3d's (<see cref="JxlLossyDepthEncoderTests"/>).
/// </summary>
/// <remarks>
/// The interim error bound: each channel's RMSE against the source, in 8-bit steps, is at most
/// <see cref="InterimRmseBound"/>. It only catches a broken encoder. Where an image is in the calibration
/// table (Bike, CalliphoraPartial), the SSIMULACRA2 check of <c>JxlLossyCalibrationTests</c> replaces it
/// (E3f); the flower crop, the odd and tiny sizes and the gradient over two LF groups are not in the table,
/// so they keep it. For reference, cjxl v0.12.0 <c>-e 3</c> on the flower crop gives RMSE (R, G, B)
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
        Assert.All(frame.ExtraDcPrecision, p => Assert.Equal(0, p));
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

    // libjxl customises the DCT8 order at effort 3 (ComputeUsedOrders returns {1, 1} at Falcon, despite its
    // comment): each channel's coefficients are sorted by how often they are zero. The decoder fills an
    // order that is not coded with the natural one, so an order that differs from it was coded.
    [Fact]
    public void Encode_Photo_CodesACustomDct8OrderThatDiffersFromTheNaturalOrder()
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(JxlEncoderRoundTrip.Encode(image, Lossy(1.0f)));
        frame.DecodeAcGlobal();

        uint[] natural = new uint[64];
        JxlAcStrategy.FromRawStrategy((int)JxlAcStrategyType.Dct).ComputeNaturalCoeffOrder(natural);
        for (int c = 0; c < 3; c++)
        {
            uint[] order = frame.State.CoeffOrders.AsSpan(JxlCoeffOrder.Offset(0, c), 64).ToArray();
            Assert.Equal(0u, order[0]);
            Assert.Equal(natural.OrderBy(k => k), order.OrderBy(k => k));
            Assert.NotEqual(natural, order);
        }
    }

    // Customising the order changes only the order and the AC tokens: the quantised coefficients, and so
    // the decoded pixels, are those of the natural-order encode of the same image and distance.
    [Theory]
    [InlineData(nameof(JxlEncoderRoundTrip.Reader.Ours), "flower-384x244", 1.0f)]
    [InlineData(nameof(JxlEncoderRoundTrip.Reader.Ours), "bike-582x453", 4.0f)]
    [InlineData(nameof(JxlEncoderRoundTrip.Reader.Djxl), "flower-384x244", 1.0f)]
    [InlineData(nameof(JxlEncoderRoundTrip.Reader.Djxl), "bike-582x453", 4.0f)]
    public async Task Encode_CustomOrder_DecodesToThePixelsOfTheNaturalOrderEncode(string reader, string sizeCase, float distance)
    {
        using Image<Rgb24> image = LoadSizeCase(sizeCase);
        string name = $"{sizeCase}_d{distance.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        byte[] custom = JxlEncoderRoundTrip.Encode(image, Lossy(distance));
        byte[] natural = JxlEncoderRoundTrip.Encode(image, LossyNaturalOrder(distance));

        Assert.NotEqual(natural, custom);
        JxlEncoderRoundTrip.Reader readerKind = Enum.Parse<JxlEncoderRoundTrip.Reader>(reader);
        using Image<Rgb24> decodedNatural = await DecodeAsync(readerKind, natural, name + ".natural");
        using Image<Rgb24> decodedCustom = await DecodeAsync(readerKind, custom, name + ".custom");
        ChannelErrors errors = ChannelErrors.Measure(decodedNatural, decodedCustom);
        Assert.Equal(new[] { 0, 0, 0 }, errors.Peak);
    }

    // The custom order puts the coefficients that are usually zero last, so the run of tokens up to a block's
    // last non-zero is shorter. Over a set of photos that pays for coding the order, though not on every
    // photo: the flower at d1 grows by 213 bytes, as cjxl's does (25,933 bytes against our natural order's
    // 25,703). Measured: natural 172,683 bytes in all, custom 172,370.
    [Fact]
    public void Encode_CustomOrder_GivesASmallerTotalSizeOnPhotosThanTheNaturalOrder()
    {
        (string SizeCase, float Distance)[] photos =
        {
            ("flower-384x244", 1.0f), ("flower-384x244", 4.0f), ("bike-582x453", 1.0f), ("bike-582x453", 4.0f), ("calliphora-378x349", 1.0f),
        };
        long customTotal = 0;
        long naturalTotal = 0;
        foreach ((string sizeCase, float distance) in photos)
        {
            using Image<Rgb24> image = LoadSizeCase(sizeCase);
            int custom = JxlEncoderRoundTrip.Encode(image, Lossy(distance)).Length;
            int natural = JxlEncoderRoundTrip.Encode(image, LossyNaturalOrder(distance)).Length;
            TestContext.Current.TestOutputHelper?.WriteLine($"{sizeCase} d{distance}: natural {natural} B, custom {custom} B");
            customTotal += custom;
            naturalTotal += natural;
        }

        Assert.True(customTotal < naturalTotal, $"custom {customTotal} B, natural {naturalTotal} B");
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

    // SHA-256 of this encoder's output (feat/jxl-e3b). It does not use FMA or the C runtime's maths on a
    // path that reaches the bytes, so every leg gives the same file. Any change to lossy Fastest output must
    // update these on purpose. E3b changed every pin but two when it added the custom DCT8 order: in the
    // 17x23 crop and the 1x1 dot the sorted order comes out natural, so it is not coded (as in libjxl).
    [Theory]
    [InlineData("flower-384x244", 1.0f, "8944285EA9858A68C3F55B604CD88A8529A2268BD409E81E701215768C709724")]
    [InlineData("flower-384x244", 2.0f, "7A945B5AE662C142F89076491EE60437536C176595F7D50B4E6DC8DF2BE0FA3E")]
    [InlineData("flower-384x244", 4.0f, "39197B26906CB8EFEBDC9B44A0266DC52667FFC42673A1EE0C8F25F590A37EA1")]
    [InlineData("flower-17x23", 1.0f, "35E5DC90898C3648C9DEEF26A688EA7CCA5FCE87816FCFC0056BB1DA9F7582CF")]
    [InlineData("dot-1x1", 1.0f, "2C853B7A3F1B6DF50A6F6E1F32608E39C456C0C15272C4006AF253A8C6E3AEBD")]
    [InlineData("bike-582x453", 1.0f, "43D583A10131F3278B64A1373C459A992BA4131988D51085567D29AFF1AF10B7")]
    [InlineData("gradient-2049x9", 2.0f, "9238BA54F722B9F387F1D3CCB8DCDFF1EA48804FA24EA16B7E314E11EE9A8CEE")]
    public void Encode_Lossy_IsByteIdenticalToItsPin(string sizeCase, float distance, string expectedSha256)
    {
        using Image<Rgb24> image = LoadSizeCase(sizeCase);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, Lossy(distance));

        Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(JxlBoxList.Codestream(encoded))));
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

    // The same encode with every coefficient order natural, as E3a wrote it.
    private static JxlEncoder LossyNaturalOrder(float distance) => new() { Distance = distance, Effort = JxlEffort.Fastest, TestHooks = new() { NaturalCoeffOrders = true } };

    // A size case: "flower-WxH" (a crop of the flower photo), "bike-582x453" (the Bike photo, odd in both
    // sides), "calliphora-378x349" (the partial Calliphora photo), "dot-1x1" or "gradient-WxH" (the
    // round-trip gradient).
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

            case "calliphora":
            {
                Image<Rgb24> image = Image.Load<Rgb24>(TestFile.GetInputFileFullPath(TestImages.Png.CalliphoraPartial));
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
            // Bike (six groups, odd in both sides) is a calibration image: JxlLossyCalibrationTests checks it
            // through each reader at d1, d2 and d4 against cjxl's SSIMULACRA2, in place of the interim bound.
            foreach (string sizeCase in new[] { "flower-256x244", "flower-17x23", "dot-1x1", "gradient-2049x9" })
            {
                cases.Add(reader.ToString(), sizeCase);
            }
        }

        return cases;
    }

    internal static void AssertWithinInterimBound(Image<Rgb24> expected, Image<Rgb24> actual, string label)
    {
        Assert.Equal(expected.Size(), actual.Size());
        ChannelErrors errors = ChannelErrors.Measure(expected, actual);
        TestContext.Current.TestOutputHelper?.WriteLine($"{label}: {errors}");
        for (int c = 0; c < 3; c++)
        {
            Assert.True(errors.Rmse[c] <= InterimRmseBound, $"{label}: channel {c} RMSE {errors.Rmse[c]:F2} > {InterimRmseBound}");
        }
    }

    internal static Task<Image<Rgb24>> DecodeAsync(JxlEncoderRoundTrip.Reader reader, byte[] encoded, string name)
        => JxlEncoderRoundTrip.DecodeAsync<Rgb24>(reader, encoded, "LossyEncoder", name);

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
