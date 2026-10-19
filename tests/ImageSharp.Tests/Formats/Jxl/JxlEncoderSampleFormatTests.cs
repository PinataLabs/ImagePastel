// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The sample formats of slice E1d: 9 to 16 integer bits, float32 and straight alpha, and the bit depth
/// <see cref="JxlMetadata"/> reports and the encoder keeps.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderSampleFormatTests
{
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
}
