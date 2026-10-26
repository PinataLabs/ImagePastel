// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;

[Trait("Format", "Jxl")]
public class JxlConformanceImagesTests
{
    [Fact]
    public void ToNpy_ScalesEveryPixelTypeToUnitRange()
    {
        // Byte4.ToVector4 returns 0 to 255; the conformance layout needs nominal [0, 1].
        using Image<Byte4> image = new(2, 1);
        image[0, 0] = new Byte4(255, 0, 51, 255);
        image[1, 0] = new Byte4(0, 255, 0, 0);

        NpyArray array = JxlConformanceImages.ToNpy(image, 4);

        Assert.Equal(new[] { 1, 1, 2, 4 }, array.Shape);
        AssertClose(new[] { 1F, 0F, 0.2F, 1F, 0F, 1F, 0F, 0F }, array.Data);
    }

    [Theory]
    [InlineData(1, new[] { 0.2F })]
    [InlineData(2, new[] { 0.2F, 0.6F })]
    [InlineData(3, new[] { 0.2F, 0.4F, 1F })]
    public void ToNpy_SelectsChannels(int channels, float[] expected)
    {
        using Image<Rgba32> image = new(1, 1);
        image[0, 0] = new Rgba32(51, 102, 255, 153);

        NpyArray array = JxlConformanceImages.ToNpy(image, channels);

        Assert.Equal(new[] { 1, 1, 1, channels }, array.Shape);
        AssertClose(expected, array.Data);
    }

    private static void AssertClose(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], 1e-6F);
        }
    }
}
