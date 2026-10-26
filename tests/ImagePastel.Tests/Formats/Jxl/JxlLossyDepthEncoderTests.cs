// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Lossy encoding at Fastest (libjxl effort 3) of grey, 16-bit and float input (slice E3d). libjxl loads
/// integer samples scaled to [0, 1] at the depth of the pixel format and float samples as they are, copies
/// a grey plane into all three colour planes, and computes XYB from them (<c>ToXYB</c>, where the grey
/// sRGB encoding counts as sRGB). The bit depth in the header is only metadata when the image is
/// XYB-encoded; the encoder writes the one E1's rules choose, so the decoder gives back the same pixel type.
/// The grey round trips and the grey-as-RGB frame check also run at Default (slice E4, #297 story 6) and at
/// Thorough (slice E5, #318 story 5).
/// </summary>
[Trait("Format", "Jxl")]
public class JxlLossyDepthEncoderTests
{
    // E3a's interim bound on each colour channel's RMSE, in 8-bit steps (JxlLossyEncoderTests). It is
    // scaled to each depth: errors are measured in 1/255 of the full range, so 20 steps are 5140 16-bit
    // steps and 20/255 for floats.
    private const double InterimRmseBound = 20.0;

    public static TheoryData<string, string, float> ReaderCases { get; } = CreateReaderCases();

    public static TheoryData<string, string, float, string> SlowerGreyReaderCases { get; } = CreateSlowerGreyReaderCases();

    public static TheoryData<string> AllPixelTypes { get; } = CreateAllPixelTypes();

    // Each pixel type decodes through each reader to its own type, within the interim bound where it is
    // visible, with the alpha exact.
    [Theory]
    [MemberData(nameof(ReaderCases))]
    public Task Encode_PixelType_DecodesThroughEachReaderWithinTheScaledBoundWithAlphaExact(string reader, string pixelType, float distance)
        => JxlPixelTypes.Verify(pixelType, new ReaderCase(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), distance, JxlEffort.Fastest));

    // #297 story 6 and #318 story 5: grey input, 8-bit and 16-bit, works at Default and at Thorough as at
    // Fastest. Each decodes through each reader to its own type within the scaled bound. (The 16-bit and
    // float colour input of both efforts is checked against cjxl's scores by JxlLossyCalibrationTests, and
    // their alpha by JxlLossyDefaultEncoderTests and JxlLossyThoroughEncoderTests.)
    [Theory]
    [MemberData(nameof(SlowerGreyReaderCases))]
    public Task Encode_GreyAtASlowerEffort_DecodesThroughEachReaderWithinTheScaledBound(string reader, string pixelType, float distance, string effort)
        => JxlPixelTypes.Verify(pixelType, new ReaderCase(Enum.Parse<JxlEncoderRoundTrip.Reader>(reader), distance, Enum.Parse<JxlEffort>(effort)));

    // Every pixel type that the lossless encoder takes encodes lossily: the 8-bit orders, the packed
    // types (16-bit integers), halves and the signed types (floats of their scaled values), with and
    // without alpha. Each decodes to its own type with the alpha exact.
    [Theory]
    [MemberData(nameof(AllPixelTypes))]
    public void Encode_AnyPixelTypeOfTheLosslessEncoder_DecodesWithinTheScaledBoundWithAlphaExact(string pixelType)
        => JxlPixelTypes.Verify(pixelType, new SmallCase());

    // SHA-256 of this encoder's output for grey, 16-bit and float input with no profile, beside the 8-bit
    // pins of JxlLossyEncoderTests and the alpha pins of JxlLossyAlphaEncoderTests: any change to lossy
    // Fastest output must update these on purpose. The Thorough rows pin the grey encodes that
    // Encode_GreyAtASlowerEffort_DecodesThroughEachReaderWithinTheScaledBound checks (#318 story 5), beside
    // the 8-bit Thorough pins of JxlLossyThoroughEncoderTests. The test image is built with exact
    // arithmetic, and the sRGB path uses no C runtime maths, so every leg gives the same file. A recognised
    // profile's output is not pinned: its curves use Math.Pow.
    [Theory]
    [InlineData(nameof(L8), nameof(JxlEffort.Fastest), 1.0f, "E11F06866DE27C6524B6F7F84A992058C4C44814734A360779C1F915E62B7F4B")]
    [InlineData(nameof(Rgb48), nameof(JxlEffort.Fastest), 1.0f, "3B17B8175F5452883955891CACACA92D03A1825804D159BD565538AA24E351EE")]
    [InlineData(nameof(RgbaVector), nameof(JxlEffort.Fastest), 1.0f, "AC360A23EF0260C3C845530962AE29724791E3180BEE817DA798A5D0C165F08F")]
    [InlineData(nameof(L8), nameof(JxlEffort.Thorough), 1.0f, "0E44FD7E86BBC896427F6F310838AE12344AADDD57B91B6C7D58961E8CBDB7A0")]
    [InlineData(nameof(L8), nameof(JxlEffort.Thorough), 4.0f, "792C246F2B0AAD15B942E057BAB2CB00F8B6A53A70ECD832DDB6F6F9CC8A0D01")]
    [InlineData(nameof(L16), nameof(JxlEffort.Thorough), 1.0f, "8B090D75EF5E79522C4E769669687FD9815E72E6B038990E75BFC0B005EE7DA8")]
    [InlineData(nameof(L16), nameof(JxlEffort.Thorough), 4.0f, "A435C1B22DCC4CA5B840D91D01A1C6DCF715191FA066B15F44D703501ED9711A")]
    public void Encode_LossyDepth_IsByteIdenticalToItsPin(string pixelType, string effort, float distance, string expectedSha256)
    {
        byte[] encoded = JxlPixelTypes.Call(pixelType, new EncodeTestImage(distance, Enum.Parse<JxlEffort>(effort)));

        // Logged in full, so that a change to the output on purpose can regenerate the rows.
        string actual = Convert.ToHexString(SHA256.HashData(JxlBoxList.Codestream(encoded)));
        TestContext.Current.TestOutputHelper?.WriteLine($"[InlineData(nameof({pixelType}), nameof(JxlEffort.{effort}), {distance:0.0#}f, \"{actual}\")]");
        Assert.Equal(expectedSha256, actual);
    }

    // A grey image is written with the grey sRGB encoding, and its depth is the one E1's rules choose: the
    // depth of the pixel type, or the requested depth (JxlMetadata, as cjxl keeps the depth of the file it
    // reads) when that holds every sample. So the decoder gives back the same pixel type, and 16-bit input
    // keeps 16 bits.
    [Theory]
    [InlineData(nameof(L8), 0, true, 8, 0, 8)]
    [InlineData(nameof(L16), 0, true, 16, 0, 16)]
    [InlineData(nameof(La16), 0, true, 8, 0, 16)]
    [InlineData(nameof(La32), 0, true, 16, 0, 32)]
    [InlineData(nameof(Rgb48), 0, false, 16, 0, 48)]
    [InlineData(nameof(Rgba64), 0, false, 16, 0, 64)]
    [InlineData(nameof(RgbaVector), 0, false, 32, 8, 128)]
    [InlineData(nameof(L16), 8, true, 8, 0, 8)]
    [InlineData(nameof(Rgb48), 8, false, 8, 0, 24)]
    public void Identify_LossyPixelType_GivesItsColourSpaceAndTheDepthOfE1sRules(string pixelType, int requestedBits, bool expectedGrey, int expectedBits, int expectedExponentBits, int expectedBitsPerPixel)
    {
        // Grey ramps for a grey type and eight colours with eight alphas otherwise: 8-bit samples.
        string pattern = expectedGrey ? "ramp-stripes" : "translucent-icon";
        byte[] encoded = JxlPixelTypes.Call(pixelType, new EncodeEightBitSamples(pattern, requestedBits));

        IImageInfo info = Image.Identify(JxlEncoderRoundTrip.CreateConfiguration(), new MemoryStream(encoded));

        JxlMetadata metadata = info.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.True(metadata.IsXybEncoded);
        Assert.NotNull(metadata.ColorEncoding);
        Assert.Equal(expectedGrey ? JxlColorSpace.Gray : JxlColorSpace.Rgb, metadata.ColorEncoding.ColorSpace);
        Assert.Equal(JxlTransferFunction.Srgb, metadata.ColorEncoding.TransferFunction);
        Assert.Equal(expectedBits, metadata.BitsPerSample);
        Assert.Equal(expectedExponentBits, metadata.ExponentBitsPerSample);
        Assert.Equal(expectedBitsPerPixel, info.PixelType.BitsPerPixel);
    }

    // A requested depth that does not hold the samples is not used: 16-bit samples keep 16 bits.
    [Fact]
    public void Identify_SixteenBitSamplesWithAnEightBitRequest_KeepSixteenBits()
    {
        using Image<L16> image = CreateImage<L16>();
        image.Metadata.GetFormatMetadata(JxlFormat.Instance).BitsPerSample = 8;

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, JxlLossyEncoderTests.Lossy(1.0f));

        IImageInfo info = Image.Identify(JxlEncoderRoundTrip.CreateConfiguration(), new MemoryStream(encoded));
        Assert.Equal(16, info.Metadata.GetFormatMetadata(JxlFormat.Instance).BitsPerSample);
        Assert.Equal(16, info.PixelType.BitsPerPixel);
    }

    // libjxl copies a grey plane into all three colour planes and computes XYB from them, so the frame of
    // a grey image is the frame of the RGB image with that grey in every channel; only the colour
    // encoding in the image header differs.
    [Theory]
    [InlineData(1.0f, nameof(JxlEffort.Fastest))]
    [InlineData(4.0f, nameof(JxlEffort.Fastest))]
    [InlineData(1.0f, nameof(JxlEffort.Default))]
    [InlineData(4.0f, nameof(JxlEffort.Default))]
    [InlineData(1.0f, nameof(JxlEffort.Thorough))]
    [InlineData(4.0f, nameof(JxlEffort.Thorough))]
    public void Encode_Grey_GivesTheFrameOfTheSameGreyAsRgb(float distance, string effort)
    {
        using Image<L8> grey = CreateImage<L8>();
        using Image<Rgb24> rgb = grey.CloneAs<Rgb24>();
        JxlEncoder encoder = new() { Distance = distance, Effort = Enum.Parse<JxlEffort>(effort) };

        byte[] greyFile = JxlEncoderRoundTrip.Encode(grey, encoder);
        byte[] rgbFile = JxlEncoderRoundTrip.Encode(rgb, encoder);

        Assert.Equal(JxlColorSpace.Gray, JxlCodestreamFrames.ReadImageHeader(greyFile, out int greyFrame).M.ColorEncoding.ColorSpace);
        Assert.Equal(JxlColorSpace.Rgb, JxlCodestreamFrames.ReadImageHeader(rgbFile, out int rgbFrame).M.ColorEncoding.ColorSpace);
        Assert.Equal(rgbFile.AsSpan(rgbFrame).ToArray(), greyFile.AsSpan(greyFrame).ToArray());
    }

    // The flower crop as the pixel type, with detail below 8 bits in the colour (so 16-bit and float
    // samples are more than 8-bit ones scaled up) and, for a type with alpha, a disk of opaque pixels
    // ramping to fully transparent corners. The alpha levels are 16-bit for a 16-bit type and 8-bit
    // levels otherwise, so float alpha survives the 16-bit PNG that djxl writes and Magick's 16 bits.
    internal static Image<TPixel> CreateImage<TPixel>()
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<Rgb24> rgb = JxlLossyEncoderTests.LoadSizeCase("flower-256x244");
        Image<TPixel> image = new(rgb.Width, rgb.Height);
        bool alpha16 = typeof(TPixel) == typeof(Rgba64) || typeof(TPixel) == typeof(La32);
        double cx = (rgb.Width - 1) / 2.0;
        double cy = (rgb.Height - 1) / 2.0;
        double radius = Math.Min(rgb.Width, rgb.Height) / 2.0;
        for (int y = 0; y < rgb.Height; y++)
        {
            for (int x = 0; x < rgb.Width; x++)
            {
                double r = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy))) / radius;
                double ramp = Math.Clamp((1.1 - r) / 0.6, 0, 1);
                float a = alpha16 ? (float)(Math.Round(65535 * ramp) / 65535) : (float)Math.Round(255 * ramp) / 255;

                // A fraction of an 8-bit step that varies along both axes.
                float fine = (((x * 7) + (y * 13)) % 17) / (17F * 255);
                Rgb24 p = rgb[x, y];
                Vector4 v = new(
                    Math.Min(1, (p.R / 255F) + fine),
                    Math.Min(1, (p.G / 255F) + fine),
                    Math.Min(1, (p.B / 255F) + fine),
                    a);
                image[x, y] = FromVector<TPixel>(v);
            }
        }

        return image;
    }

    // The samples that the encoder takes from a pixel, in [0, 1] for the test image: a half's own value
    // (its scaled vector maps [-1, 1] to [0, 1], and XYB clamps negative light), and the scaled vector of
    // any other type (which is what the encoder reads of a signed type).
    private static Vector4 ToVector<TPixel>(TPixel pixel)
        where TPixel : unmanaged, IPixel<TPixel>
        => IsHalf<TPixel>() ? pixel.ToVector4() : pixel.ToScaledVector4();

    private static TPixel FromVector<TPixel>(Vector4 v)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        TPixel pixel = default;
        if (IsHalf<TPixel>())
        {
            pixel.FromVector4(v);
        }
        else
        {
            pixel.FromScaledVector4(v);
        }

        return pixel;
    }

    private static bool IsHalf<TPixel>()
        where TPixel : unmanaged, IPixel<TPixel>
        => typeof(TPixel) == typeof(HalfSingle) || typeof(TPixel) == typeof(HalfVector2) || typeof(TPixel) == typeof(HalfVector4);

    // Alpha is compared exactly; the colour of each visible pixel within the bound, in 8-bit steps of the
    // full range. A grey image's colour is its one channel.
    private static void AssertAlphaExactAndColourWithinBound<TPixel>(Image<TPixel> expected, Image<TPixel> actual, string label)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Assert.Equal(expected.Size(), actual.Size());
        bool hasAlpha = PixelOperations<TPixel>.Instance.GetPixelTypeInfo().AlphaRepresentation != PixelAlphaRepresentation.None;
        double[] sums = new double[3];
        long n = 0;
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                Vector4 e = ToVector(expected[x, y]);
                Vector4 a = ToVector(actual[x, y]);
                if (hasAlpha && BitConverter.SingleToInt32Bits(e.W) != BitConverter.SingleToInt32Bits(a.W))
                {
                    Assert.Fail($"{label}: alpha at ({x}, {y}) is {a.W:R}, expected {e.W:R}");
                }

                if (e.W == 0)
                {
                    continue;
                }

                Vector4 d = (a - e) * 255;
                sums[0] += d.X * d.X;
                sums[1] += d.Y * d.Y;
                sums[2] += d.Z * d.Z;
                n++;
            }
        }

        double[] rmse = Array.ConvertAll(sums, s => Math.Sqrt(s / Math.Max(n, 1)));
        TestContext.Current.TestOutputHelper?.WriteLine($"{label}: visible RMSE R {rmse[0]:F2} G {rmse[1]:F2} B {rmse[2]:F2} (8-bit steps) over {n} pixels");
        for (int c = 0; c < 3; c++)
        {
            Assert.True(rmse[c] <= InterimRmseBound, $"{label}: channel {c} visible RMSE {rmse[c]:F2} > {InterimRmseBound}");
        }
    }

    private static TheoryData<string> CreateAllPixelTypes()
    {
        TheoryData<string> data = new();
        foreach (string pixelType in new[]
        {
            nameof(A8), nameof(Abgr32), nameof(Argb32), nameof(Bgr24), nameof(Bgr565), nameof(Bgra32), nameof(Bgra4444),
            nameof(Bgra5551), nameof(Byte4), nameof(HalfSingle), nameof(HalfVector2), nameof(HalfVector4), nameof(L16),
            nameof(L8), nameof(La16), nameof(La32), nameof(NormalizedByte2), nameof(NormalizedByte4),
            nameof(NormalizedShort2), nameof(NormalizedShort4), nameof(Rg32), nameof(Rgb24), nameof(Rgb48),
            nameof(Rgba1010102), nameof(Rgba32), nameof(Rgba64), nameof(RgbaVector), nameof(Short2), nameof(Short4),
        })
        {
            data.Add(pixelType);
        }

        return data;
    }

    private static TheoryData<string, string, float> CreateReaderCases()
    {
        string[] pixelTypes = { nameof(L8), nameof(L16), nameof(Rgb48), nameof(La16), nameof(Rgba64), nameof(RgbaVector) };
        TheoryData<string, string, float> data = new();
        foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
        {
            foreach (string pixelType in pixelTypes)
            {
                foreach (float distance in new[] { 1.0f, 4.0f })
                {
                    data.Add(reader.ToString(), pixelType, distance);
                }
            }
        }

        return data;
    }

    private static TheoryData<string, string, float, string> CreateSlowerGreyReaderCases()
    {
        TheoryData<string, string, float, string> data = new();
        foreach (string effort in new[] { nameof(JxlEffort.Default), nameof(JxlEffort.Thorough) })
        {
            foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
            {
                foreach (string pixelType in new[] { nameof(L8), nameof(L16) })
                {
                    foreach (float distance in new[] { 1.0f, 4.0f })
                    {
                        data.Add(reader.ToString(), pixelType, distance, effort);
                    }
                }
            }
        }

        return data;
    }

    private sealed record ReaderCase(JxlEncoderRoundTrip.Reader Reader, float Distance, JxlEffort Effort) : IJxlPixelTypeCall<Task>
    {
        public async Task Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> image = CreateImage<TPixel>();

            byte[] encoded = JxlEncoderRoundTrip.Encode(image, new JxlEncoder { Distance = this.Distance, Effort = this.Effort });

            string name = $"{typeof(TPixel).Name}-{this.Effort}-d{this.Distance.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            using Image<TPixel> decoded = await JxlEncoderRoundTrip.DecodeAsync<TPixel>(this.Reader, encoded, "LossyDepthEncoder", name, highBitDepth: true);
            AssertAlphaExactAndColourWithinBound(image, decoded, $"{name} {this.Reader}, {encoded.Length} bytes");
        }
    }

    // A 17x23 crop of the test image as the pixel type, through our decoder.
    private sealed record SmallCase : IJxlPixelTypeCall<bool>
    {
        public bool Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> image = CreateImage<TPixel>();
            image.Mutate(x => x.Crop(new Rectangle(120, 110, 17, 23)));

            byte[] encoded = JxlEncoderRoundTrip.Encode(image, JxlLossyEncoderTests.Lossy(1.0f));

            using Image<TPixel> decoded = IsHalf<TPixel>() ? LoadHalves<TPixel>(encoded) : Image.Load<TPixel>(JxlEncoderRoundTrip.CreateConfiguration(), encoded);
            AssertAlphaExactAndColourWithinBound(image, decoded, $"{typeof(TPixel).Name} 17x23, {encoded.Length} bytes");
            return true;
        }

        // The decoder gives a float image, and ImageSharp's conversion of it to a half type maps [0, 1] to
        // [-1, 1] (the halves' scaled vector). The halves are made from the float values instead, as the
        // encoder read them.
        private static Image<TPixel> LoadHalves<TPixel>(byte[] encoded)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<RgbaVector> floats = Image.Load<RgbaVector>(JxlEncoderRoundTrip.CreateConfiguration(), encoded);
            Image<TPixel> halves = new(floats.Width, floats.Height);
            for (int y = 0; y < floats.Height; y++)
            {
                for (int x = 0; x < floats.Width; x++)
                {
                    halves[x, y] = FromVector<TPixel>(floats[x, y].ToVector4());
                }
            }

            return halves;
        }
    }

    // Encodes the test image as the pixel type at the distance and effort.
    private sealed record EncodeTestImage(float Distance, JxlEffort Effort) : IJxlPixelTypeCall<byte[]>
    {
        public byte[] Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> image = CreateImage<TPixel>();
            return JxlEncoderRoundTrip.Encode(image, new JxlEncoder { Distance = this.Distance, Effort = this.Effort });
        }
    }

    // Encodes a pattern of JxlEncoderRoundTrip as the pixel type, with a requested depth (0 for none).
    private sealed record EncodeEightBitSamples(string Pattern, int RequestedBits) : IJxlPixelTypeCall<byte[]>
    {
        public byte[] Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> image = JxlEncoderRoundTrip.CreateImage<TPixel>(this.Pattern, 17, 23);
            image.Metadata.GetFormatMetadata(JxlFormat.Instance).BitsPerSample = this.RequestedBits;
            return JxlEncoderRoundTrip.Encode(image, JxlLossyEncoderTests.Lossy(1.0f));
        }
    }
}
