// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Text.Json;
using SixLabors.ImageSharp.Formats.Jxl;
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
    public void DeepClone_CopiesTheBitDepth()
    {
        JxlMetadata metadata = new() { BitsPerSample = 32, ExponentBitsPerSample = 8 };

        JxlMetadata clone = (JxlMetadata)metadata.DeepClone();

        Assert.Equal(32u, clone.BitsPerSample);
        Assert.Equal(8u, clone.ExponentBitsPerSample);
    }
}
