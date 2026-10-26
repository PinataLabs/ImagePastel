// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The sample formats of slice E1d: 9 to 16 integer bits, float32 and straight alpha, and the bit depth
/// <see cref="JxlMetadata"/> reports and the encoder keeps.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderSampleFormatTests
{
    private static string TwelveBitPath => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", "twelve_bit.jxl");

    [Fact]
    public void ReEncode_TwelveBitFile_KeepsTwelveBitsAndThePixels()
    {
        Configuration configuration = JxlEncoderRoundTrip.CreateConfiguration();
        using Image<Rgba64> decoded = Image.Load<Rgba64>(configuration, TwelveBitPath);
        using Image<Rgba64> reference = Image.Load<Rgba64>(Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", "twelve_bit.png"));
        ImageComparer.Exact.VerifySimilarity(reference, decoded);

        byte[] encoded = JxlEncoderRoundTrip.Encode(decoded);

        IImageInfo info = Image.Identify(configuration, new MemoryStream(encoded));
        JxlMetadata metadata = info.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.Equal(12, metadata.BitsPerSample);
        Assert.Equal(0, metadata.ExponentBitsPerSample);
        Assert.Equal(48, info.PixelType.BitsPerPixel);
        using Image<Rgba64> reDecoded = Image.Load<Rgba64>(configuration, encoded);
        ImageComparer.Exact.VerifySimilarity(decoded, reDecoded);
        AssertSamePlanes(File.ReadAllBytes(TwelveBitPath), encoded);
    }

    // libjxl's CheckValidBitdepth takes 1 to 24 integer bits; a decoded file can have any of them.
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(24)]
    public void Encode_MetadataBitDepthThatHoldsTheSamples_IsTheStoredDepth(int bits)
    {
        using Image<Rgba64> image = CreateImageAtDepth(bits, 17, 23);
        image.Metadata.GetFormatMetadata(JxlFormat.Instance).BitsPerSample = bits;

        byte[] encoded = JxlEncoderRoundTrip.Encode(image);

        AssertStoredDepth(encoded, bits, 0);
        using Image<Rgba64> decoded = Image.Load<Rgba64>(JxlEncoderRoundTrip.CreateConfiguration(), encoded);
        ImageComparer.Exact.VerifySimilarity(image, decoded);
    }

    [Fact]
    public void Encode_MetadataBitDepthTooNarrowForTheSamples_KeepsTheDepthOfThePixelType()
    {
        // Lossless keeps every sample, and 16-bit noise does not fit in 10 bits.
        using Image<Rgb48> image = JxlEncoderRoundTrip.CreateImage<Rgb48>("noise", 17, 23);
        image.Metadata.GetFormatMetadata(JxlFormat.Instance).BitsPerSample = 10;

        byte[] encoded = JxlEncoderRoundTrip.Encode(image);

        AssertStoredDepth(encoded, 16, 0);
        using Image<Rgb48> decoded = Image.Load<Rgb48>(JxlEncoderRoundTrip.CreateConfiguration(), encoded);
        ImageComparer.Exact.VerifySimilarity(image, decoded);
    }

    [Fact]
    public void Encode_IntegerMetadataBitDepthWithFloatPixels_WritesFloat32()
    {
        using Image<RgbaVector> image = CreateWideRangeFloatImage();
        image.Metadata.GetFormatMetadata(JxlFormat.Instance).BitsPerSample = 12;

        byte[] encoded = JxlEncoderRoundTrip.Encode(image);

        AssertStoredDepth(encoded, 32, 8);
        JxlEncoderRoundTrip.AssertFloatBitsEqual(image, encoded);
    }

    // Float layouts at least as wide as f16 in both exponent and mantissa hold every half, so a half
    // image keeps them; the decoder reads these layouts too.
    [Theory]
    [InlineData(32, 8)]
    [InlineData(24, 8)]
    [InlineData(18, 6)]
    public void Encode_FloatMetadataLayoutThatHoldsTheHalves_IsTheStoredLayout(int bits, int exponentBits)
    {
        using Image<HalfVector4> image = CreateWideRangeImage<HalfVector4>(halfSamples: true);
        JxlMetadata metadata = image.Metadata.GetFormatMetadata(JxlFormat.Instance);
        metadata.BitsPerSample = bits;
        metadata.ExponentBitsPerSample = exponentBits;

        byte[] encoded = JxlEncoderRoundTrip.Encode(image);

        AssertStoredDepth(encoded, bits, exponentBits);
        AssertPlanesRebuildThePixels(image, encoded, 4, halfSamples: true);
    }

    // The integer range, then each float rule: more than 8 exponent bits, fewer than 3 + exponent
    // bits, and more than 24 + exponent bits. The public depth is an int (#108), so a negative one is
    // invalid too.
    [Theory]
    [InlineData(25, 0)]
    [InlineData(32, 9)]
    [InlineData(10, 8)]
    [InlineData(3, 1)]
    [InlineData(33, 8)]
    [InlineData(-1, 0)]
    [InlineData(16, -1)]
    public void Encode_InvalidMetadataBitDepth_ThrowsImageFormatExceptionAndWritesNothing(int bits, int exponentBits)
    {
        using Image<Rgb24> image = new(1, 1);
        JxlMetadata metadata = image.Metadata.GetFormatMetadata(JxlFormat.Instance);
        metadata.BitsPerSample = bits;
        metadata.ExponentBitsPerSample = exponentBits;
        using MemoryStream stream = new();

        Assert.Throws<ImageFormatException>(() => image.Save(stream, JxlEncoderRoundTrip.LosslessFastest));

        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData("grayscale")]
    [InlineData("alpha_triangles")]
    [InlineData("sunset_logo")]
    [InlineData("alpha_nonpremultiplied")]
    [InlineData("spot")]
    [InlineData("lossless_pfm")]
    public void Identify_ConformanceVector_ReportsTheBitDepthOfItsTestJson(string id)
    {
        JxlConformanceVector vector = JxlConformanceVector.Load(id);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(vector.DirectoryPath, "test.json")));
        int expectedBits = json.RootElement.GetProperty("bits_per_sample")[0].GetInt32();
        int expectedExponentBits = json.RootElement.GetProperty("exp_bits_per_sample")[0].GetInt32();

        using FileStream stream = File.OpenRead(vector.CommittedInputPath);
        JxlMetadata metadata = Image.Identify(JxlEncoderRoundTrip.CreateConfiguration(), stream).Metadata.GetFormatMetadata(JxlFormat.Instance);

        Assert.Equal(expectedBits, metadata.BitsPerSample);
        Assert.Equal(expectedExponentBits, metadata.ExponentBitsPerSample);
    }

    [Fact]
    public void RoundTrip_FloatsOutsideZeroToOne_KeepTheirBitPatterns()
    {
        using Image<RgbaVector> image = CreateWideRangeFloatImage();

        byte[] encoded = JxlEncoderRoundTrip.Encode(image);

        JxlEncoderRoundTrip.AssertFloatBitsEqual(image, encoded);
    }

    [Fact]
    public async Task RoundTrip_FloatsOutsideZeroToOne_DjxlAccepts()
    {
        // Resolve the tool first, so the test skips before it writes anything.
        JxlTools.GetToolPath("djxl");
        using Image<RgbaVector> image = CreateWideRangeFloatImage();
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", "Encoder");
        string jxlPath = Path.Combine(directory, "float_wide_range.jxl");
        string pfmPath = Path.Combine(directory, "float_wide_range.djxl.pfm");
        File.Delete(pfmPath);
        await File.WriteAllBytesAsync(jxlPath, JxlEncoderRoundTrip.Encode(image), TestContext.Current.CancellationToken);

        await JxlTools.RunAsync("djxl", TestContext.Current.CancellationToken, jxlPath, pfmPath);

        Assert.True(File.Exists(pfmPath));
    }

    // A half is stored as JPEG XL's f16, which holds it exactly, including negative values, values
    // above 1 and subnormals.
    [Theory]
    [InlineData(nameof(HalfSingle), 3)]
    [InlineData(nameof(HalfVector2), 3)]
    [InlineData(nameof(HalfVector4), 4)]
    public void RoundTrip_HalfFloats_KeepEveryHalfAsF16(string pixelType, int planes)
        => JxlPixelTypes.Verify(pixelType, new WideRangeRoundTrip(planes, HalfSamples: true));

    // A signed sample is stored as float32 of its scaled value, through which ImageSharp's other
    // encoders and our decoder map it, so the extremes of its range are kept too.
    [Theory]
    [InlineData(nameof(NormalizedByte2), 3)]
    [InlineData(nameof(NormalizedByte4), 4)]
    [InlineData(nameof(NormalizedShort2), 3)]
    [InlineData(nameof(NormalizedShort4), 4)]
    [InlineData(nameof(Short2), 3)]
    [InlineData(nameof(Short4), 4)]
    public void RoundTrip_SignedSamples_KeepEverySampleAsFloat32(string pixelType, int planes)
        => JxlPixelTypes.Verify(pixelType, new WideRangeRoundTrip(planes, HalfSamples: false));

    [Theory]
    [InlineData(nameof(HalfVector4))]
    [InlineData(nameof(Short4))]
    public Task RoundTrip_HalfAndSignedSamples_DjxlAccepts(string pixelType)
        => JxlPixelTypes.Verify(pixelType, new DjxlAcceptsWideRange());

    // A half-float file loaded as Rgba64 holds 16-bit integers, which f16 cannot hold, so it is
    // saved at 16 integer bits.
    [Fact]
    public void ReEncode_HalfFloatFile_AsRgba64_WritesSixteenBitIntegersAndKeepsThePixels()
    {
        Configuration configuration = JxlEncoderRoundTrip.CreateConfiguration();
        using Image<Rgba64> decoded = Image.Load<Rgba64>(configuration, EncodeHalfFloatFile());
        JxlMetadata metadata = decoded.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.Equal(16, metadata.BitsPerSample);
        Assert.Equal(5, metadata.ExponentBitsPerSample);

        byte[] encoded = JxlEncoderRoundTrip.Encode(decoded);

        AssertStoredDepth(encoded, 16, 0);
        using Image<Rgba64> reDecoded = Image.Load<Rgba64>(configuration, encoded);
        ImageComparer.Exact.VerifySimilarity(decoded, reDecoded);
    }

    // The f16 layout of the file holds the samples of HalfVector4, but not those of RgbaVector.
    [Theory]
    [InlineData(nameof(HalfVector4), 16, 5)]
    [InlineData(nameof(RgbaVector), 32, 8)]
    public void ReEncode_HalfFloatFile_AsFloatPixels_KeepsF16OnlyWhereItHoldsTheSamples(string pixelType, int bits, int exponentBits)
        => JxlPixelTypes.Verify(pixelType, new ReEncodeHalfFloatFile(bits, exponentBits));

    [Theory]
    [InlineData(nameof(La16), 2u)]
    [InlineData(nameof(Rgba32), 4u)]
    public void Encode_EightColourIconWithAlpha_WritesOnePaletteOverColourAndAlpha(string pixelType, uint channels)
    {
        // libjxl's first palette try takes every channel, alpha included.
        byte[] encoded = JxlPixelTypes.Call(pixelType, new EncodeIcon());

        List<JxlTransform> transforms = JxlEncodedFrame.ReadGlobalTransforms(encoded);

        JxlTransform palette = Assert.Single(transforms);
        Assert.Equal(JxlTransformId.Palette, palette.Id);
        Assert.Equal(0u, palette.BeginC);
        Assert.Equal(channels, palette.NumC);
        Assert.Equal(8u, palette.NbColors);
    }

    [Fact]
    public void DeepClone_CopiesTheBitDepth()
    {
        JxlMetadata metadata = new() { BitsPerSample = 32, ExponentBitsPerSample = 8 };

        JxlMetadata clone = (JxlMetadata)metadata.DeepClone();

        Assert.Equal(32, clone.BitsPerSample);
        Assert.Equal(8, clone.ExponentBitsPerSample);
    }

    // Negative values, values above 1, a negative zero, a subnormal and the extremes of float32, in
    // every channel including alpha.
    private static Image<RgbaVector> CreateWideRangeFloatImage()
    {
        float[] values = { -1F, -0F, 0F, 0.5F, 1F, 1.5F, 1000F, -65504.25F, 1E-40F, float.MaxValue, float.MinValue, float.Epsilon, 3.14159274F };
        Image<RgbaVector> image = new(values.Length, 5);
        Random random = new(211);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                float Pick() => y == 0 ? values[x] : values[random.Next(values.Length)] * (float)(random.NextDouble() + 0.5);
                image[x, y] = new RgbaVector(Pick(), Pick(), Pick(), Pick());
            }
        }

        return image;
    }

    // Samples that fit in `bits` bits: each is a `bits`-bit value scaled to 16 bits and rounded, in
    // every channel including alpha.
    private static Image<Rgba64> CreateImageAtDepth(int bits, int width, int height)
    {
        Image<Rgba64> image = new(width, height);
        Random random = new(bits);
        long max = (1L << bits) - 1;
        ushort Pick()
        {
            long q = random.NextInt64(max + 1);
            return (ushort)(((q * 65535 * 2) + max) / (2 * max));
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image[x, y] = new Rgba64(Pick(), Pick(), Pick(), Pick());
            }
        }

        return image;
    }

    // Halves: the first row holds negative values, values above 1, the largest half, subnormals and
    // both zeros, and the other rows any finite half. Signed samples: the first row holds values past
    // both ends of the type's range (which give its extremes), negative values and zero, and the other
    // rows random values.
    private static Image<TPixel> CreateWideRangeImage<TPixel>(bool halfSamples)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        float[] halves = { -1F, -0F, 0F, 0.5F, 1F, 1.5F, 1000F, 65504F, -65504F, 5.96046448E-08F, -6.09755516E-05F, 3.140625F };
        float[] signed = { -1E9F, -32767F, -300F, -1F, -0.5F, 0F, 0.25F, 1F, 300F, 1E9F };
        float[] firstRow = halfSamples ? halves : signed;
        Image<TPixel> image = new(firstRow.Length, 7);
        Random random = new(halfSamples ? 16 : 32);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                float Pick()
                {
                    if (y == 0)
                    {
                        return firstRow[x];
                    }

                    if (!halfSamples)
                    {
                        return (float)((random.NextDouble() * 2) - 1) * (random.Next(2) == 0 ? 1F : 40000F);
                    }

                    // Any half but infinity and NaN, whose exponent bits are all ones.
                    ushort half;
                    do
                    {
                        half = (ushort)random.Next(65536);
                    }
                    while ((half & 0x7C00) == 0x7C00);

                    return HalfTypeHelper.Unpack(half);
                }

                TPixel pixel = default;
                pixel.FromVector4(new Vector4(Pick(), Pick(), Pick(), Pick()));
                image[x, y] = pixel;
            }
        }

        return image;
    }

    // Each pixel is rebuilt from the unclamped float planes of the codestream: through FromVector4 for
    // halves, whose planes are their values, and through FromScaledVector4 for signed samples.
    private static void AssertPlanesRebuildThePixels<TPixel>(Image<TPixel> expected, byte[] encoded, int planeCount, bool halfSamples)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(new MemoryStream(encoded), CancellationToken.None);
        IReadOnlyList<Buffer2D<float>> planes = decoded.Frames[0].Planes;
        Assert.Equal(planeCount, planes.Count);
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                float Sample(int c) => planes[c].DangerousGetRowSpan(y)[x];
                Vector4 vector = new(Sample(0), Sample(1), Sample(2), planeCount == 4 ? Sample(3) : 1F);
                TPixel actual = default;
                if (halfSamples)
                {
                    actual.FromVector4(vector);
                }
                else
                {
                    actual.FromScaledVector4(vector);
                }

                if (!actual.Equals(expected[x, y]))
                {
                    Assert.Fail($"Pixel ({x}, {y}): expected {expected[x, y]}, got {actual} from planes {vector}.");
                }
            }
        }
    }

    private static void AssertStoredDepth(byte[] encoded, int bits, int exponentBits)
    {
        JxlMetadata metadata = Image.Identify(JxlEncoderRoundTrip.CreateConfiguration(), new MemoryStream(encoded)).Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.Equal(bits, metadata.BitsPerSample);
        Assert.Equal(exponentBits, metadata.ExponentBitsPerSample);
    }

    // A half-float file, made with our encoder from halves between 0 and 1.
    private static byte[] EncodeHalfFloatFile()
    {
        using Image<HalfVector4> image = new(19, 11);
        Random random = new(5);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                float Pick() => HalfTypeHelper.Unpack((ushort)random.Next(0x3C01));
                image[x, y] = new HalfVector4(Pick(), Pick(), Pick(), Pick());
            }
        }

        byte[] encoded = JxlEncoderRoundTrip.Encode(image);
        AssertStoredDepth(encoded, 16, 5);
        return encoded;
    }

    private sealed record WideRangeRoundTrip(int Planes, bool HalfSamples) : IJxlPixelTypeCall<bool>
    {
        public bool Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> image = CreateWideRangeImage<TPixel>(this.HalfSamples);

            byte[] encoded = JxlEncoderRoundTrip.Encode(image);

            AssertStoredDepth(encoded, this.HalfSamples ? 16 : 32, this.HalfSamples ? 5 : 8);
            AssertPlanesRebuildThePixels(image, encoded, this.Planes, this.HalfSamples);
            return true;
        }
    }

    private sealed record DjxlAcceptsWideRange : IJxlPixelTypeCall<Task>
    {
        public async Task Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            // Resolve the tool first, so the test skips before it writes anything.
            JxlTools.GetToolPath("djxl");
            using Image<TPixel> image = CreateWideRangeImage<TPixel>(typeof(TPixel) == typeof(HalfVector4));
            string directory = TestEnvironment.CreateOutputDirectory("Jxl", "Encoder");
            string jxlPath = Path.Combine(directory, $"wide_range_{typeof(TPixel).Name}.jxl");
            string pfmPath = Path.Combine(directory, $"wide_range_{typeof(TPixel).Name}.djxl.pfm");
            File.Delete(pfmPath);
            await File.WriteAllBytesAsync(jxlPath, JxlEncoderRoundTrip.Encode(image), TestContext.Current.CancellationToken);

            await JxlTools.RunAsync("djxl", TestContext.Current.CancellationToken, jxlPath, pfmPath);

            Assert.True(File.Exists(pfmPath));
        }
    }

    private sealed record ReEncodeHalfFloatFile(int Bits, int ExponentBits) : IJxlPixelTypeCall<bool>
    {
        public bool Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> decoded = Image.Load<TPixel>(JxlEncoderRoundTrip.CreateConfiguration(), EncodeHalfFloatFile());

            byte[] encoded = JxlEncoderRoundTrip.Encode(decoded);

            AssertStoredDepth(encoded, this.Bits, this.ExponentBits);
            AssertPlanesRebuildThePixels(decoded, encoded, 4, halfSamples: true);
            return true;
        }
    }

    private sealed record EncodeIcon : IJxlPixelTypeCall<byte[]>
    {
        public byte[] Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> image = JxlEncoderRoundTrip.CreateImage<TPixel>("translucent-icon", 64, 64);
            return JxlEncoderRoundTrip.Encode(image);
        }
    }

    // The stored samples of both files are equal: their unclamped float planes have the same bits.
    private static void AssertSamePlanes(byte[] expected, byte[] actual)
    {
        using JxlDecodedImage e = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(new MemoryStream(expected), CancellationToken.None);
        using JxlDecodedImage a = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(new MemoryStream(actual), CancellationToken.None);
        Assert.Equal(e.Frames[0].Planes.Count, a.Frames[0].Planes.Count);
        for (int c = 0; c < e.Frames[0].Planes.Count; c++)
        {
            for (int y = 0; y < e.Height; y++)
            {
                Assert.True(e.Frames[0].Planes[c].DangerousGetRowSpan(y).SequenceEqual(a.Frames[0].Planes[c].DangerousGetRowSpan(y)), $"Channel {c}, row {y}");
            }
        }
    }
}
