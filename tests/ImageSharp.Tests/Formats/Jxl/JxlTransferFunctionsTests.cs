// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Checks the render pipeline's transfer functions against IEC 61966-2-1 in double precision.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlTransferFunctionsTests
{
    // Measured: 5.1e-7. libjxl documents an error of about 5e-7.
    [Fact]
    public void SrgbEncodedFromDisplay_IsWithinItsError()
    {
        Random random = new(7);
        double worst = 0;
        for (int i = 0; i < 200_000; i++)
        {
            float f = (float)random.NextDouble();
            worst = Math.Max(worst, Math.Abs(JxlTransferFunctions.SrgbEncodedFromDisplay(f) - LinearToSrgb(f)));
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Worst error {worst:G4}");
        Assert.True(worst < 1e-6, $"Worst absolute error {worst:G4}");
    }

    [Theory]
    [InlineData(0.5F)]
    [InlineData(0.002F)]
    [InlineData(1.5F)]
    public void SrgbEncodedFromDisplay_OfANegativeSample_IsTheNegatedEncodingOfItsMagnitude(float magnitude)
        => Assert.Equal(-JxlTransferFunctions.SrgbEncodedFromDisplay(magnitude), JxlTransferFunctions.SrgbEncodedFromDisplay(-magnitude));

    [Fact]
    public void SrgbEncodedFromDisplay_BelowTheThreshold_IsTheLinearSegment()
        => Assert.Equal(0.002F * 12.92F, JxlTransferFunctions.SrgbEncodedFromDisplay(0.002F));

    private static double LinearToSrgb(double v) => v <= 0.0031308 ? 12.92 * v : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;
}
