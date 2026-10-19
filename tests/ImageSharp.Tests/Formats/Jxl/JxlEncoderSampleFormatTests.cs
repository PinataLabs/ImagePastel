// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

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
        Assert.Equal(12u, metadata.BitsPerSample);
        Assert.Equal(0u, metadata.ExponentBitsPerSample);
        Assert.Equal(48, info.PixelType.BitsPerPixel);
        using Image<Rgba64> reDecoded = Image.Load<Rgba64>(configuration, encoded);
        ImageComparer.Exact.VerifySimilarity(decoded, reDecoded);
        AssertSamePlanes(File.ReadAllBytes(TwelveBitPath), encoded);
    }

    [Fact]
    public void Encode_MetadataBitDepth_IsTheStoredDepth()
    {
        using Image<Rgb48> image = JxlEncoderRoundTrip.CreateImage<Rgb48>("noise", 17, 23);
        image.Metadata.GetFormatMetadata(JxlFormat.Instance).BitsPerSample = 10;

        byte[] encoded = JxlEncoderRoundTrip.Encode(image);

        IImageInfo info = Image.Identify(JxlEncoderRoundTrip.CreateConfiguration(), new MemoryStream(encoded));
        Assert.Equal(10u, info.Metadata.GetFormatMetadata(JxlFormat.Instance).BitsPerSample);
        Assert.Equal(30, info.PixelType.BitsPerPixel);
    }

    [Theory]
    [InlineData(25u, 0u)]
    [InlineData(32u, 9u)]
    [InlineData(10u, 8u)]
    public void Encode_InvalidMetadataBitDepth_ThrowsImageFormatExceptionAndWritesNothing(uint bits, uint exponentBits)
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
        uint expectedBits = json.RootElement.GetProperty("bits_per_sample")[0].GetUInt32();
        uint expectedExponentBits = json.RootElement.GetProperty("exp_bits_per_sample")[0].GetUInt32();

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

    [Fact]
    public void DeepClone_CopiesTheBitDepth()
    {
        JxlMetadata metadata = new() { BitsPerSample = 32, ExponentBitsPerSample = 8 };

        JxlMetadata clone = (JxlMetadata)metadata.DeepClone();

        Assert.Equal(32u, clone.BitsPerSample);
        Assert.Equal(8u, clone.ExponentBitsPerSample);
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
