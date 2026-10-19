// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

[Trait("Format", "Jxl")]
public class JxlEncoderTests
{
    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void Encode_LossyDistance_ThrowsNotSupportedNamingE3(float distance)
    {
        using Image<Rgb24> image = new(1, 1);
        using MemoryStream stream = new();

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => image.Save(stream, new JxlEncoder { Distance = distance, Effort = JxlEffort.Fastest }));

        Assert.Contains("E3", ex.Message);
    }

    [Theory]
    [InlineData((int)JxlEffort.Default)]
    [InlineData((int)JxlEffort.Thorough)]
    public void Encode_LosslessAboveFastest_ThrowsNotSupportedNamingE2(int effort)
    {
        using Image<Rgb24> image = new(1, 1);
        using MemoryStream stream = new();

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => image.Save(stream, new JxlEncoder { Distance = 0, Effort = (JxlEffort)effort }));

        Assert.Contains("E2", ex.Message);
    }

    [Fact]
    public void Encoder_Defaults_AreDistanceOneAndEffortDefault()
    {
        JxlEncoder encoder = new();

        Assert.Equal(1.0f, encoder.Distance);
        Assert.Equal(JxlEffort.Default, encoder.Effort);
    }
}
