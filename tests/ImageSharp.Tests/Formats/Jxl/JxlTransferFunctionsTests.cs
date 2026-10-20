// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Checks the render pipeline's transfer functions: the sRGB curve against IEC 61966-2-1 in double
/// precision, and the others against the values that libjxl's own code gives when it is built without
/// FMA (Highway's scalar target, <c>-ffp-contract=off</c>; ADR 0004).
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

    // libjxl's TF_709 at its linear segment, its threshold (0.018, which is still linear: libjxl's test is
    // x <= threshold), just above it, and at 1. Measured: exact.
    [Theory]
    [InlineData(1e-5F, 4.50000007e-05F)]
    [InlineData(0.018F, 0.0810000002F)]
    [InlineData(0.0181F, 0.0816979036F)]
    [InlineData(0.1F, 0.290940076F)]
    [InlineData(0.5F, 0.705514491F)]
    [InlineData(1F, 0.999999285F)]
    [InlineData(1.5F, 1.21998107F)]
    public void Bt709EncodedFromDisplay_GivesLibjxlsValue(float linear, float expected)
        => Assert.Equal(expected, JxlTransferFunctions.Bt709EncodedFromDisplay(linear));

    [Fact]
    public void Bt709EncodedFromDisplay_OfANegativeSample_IsTheLinearSegment()
        => Assert.Equal(-1.125F, JxlTransferFunctions.Bt709EncodedFromDisplay(-0.25F));

    // Measured: 9.6e-7 against BT.709's formula in double precision. libjxl documents a maximum error
    // of 1e-6.
    [Fact]
    public void Bt709EncodedFromDisplay_IsCloseToTheFormula()
    {
        Random random = new(7);
        double worst = 0;
        for (int i = 0; i < 200_000; i++)
        {
            float f = (float)random.NextDouble();
            double formula = f <= 0.018 ? 4.5 * f : (1.099 * Math.Pow(f, 0.45)) - 0.099;
            worst = Math.Max(worst, Math.Abs(JxlTransferFunctions.Bt709EncodedFromDisplay(f) - formula));
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Worst error {worst:G4}");
        Assert.True(worst < 1e-6, $"Worst absolute error {worst:G4}");
    }

    // libjxl's OpGamma at 1e-5 (the threshold, which gives 0), at the samples above it, and at 1, for the
    // exponent of a 2.2 gamma ("g0.45455") and of DCI (1 / 2.6). Measured: exact.
    [Theory]
    [InlineData(1e-5F, 0.45455F, 0F)]
    [InlineData(5e-5F, 0.45455F, 0.0110909082F)]
    [InlineData(0.018F, 0.45455F, 0.161040112F)]
    [InlineData(0.5F, 0.45455F, 0.729737282F)]
    [InlineData(1F, 0.45455F, 0.999999344F)]
    [InlineData(1.5F, 0.45455F, 1.20238101F)]
    [InlineData(1e-5F, 1F / 2.6F, 0F)]
    [InlineData(5e-5F, 1F / 2.6F, 0.0221696068F)]
    [InlineData(0.5F, 1F / 2.6F, 0.765982807F)]
    [InlineData(1F, 1F / 2.6F, 0.999999523F)]
    public void GammaEncodedFromDisplay_GivesLibjxlsValue(float linear, float inverseGamma, float expected)
        => Assert.Equal(expected, JxlTransferFunctions.GammaEncodedFromDisplay(linear, inverseGamma));

    [Theory]
    [InlineData(0F)]
    [InlineData(-0.25F)]
    [InlineData(9e-6F)]
    public void GammaEncodedFromDisplay_AtOrBelowTheThreshold_IsZero(float linear)
        => Assert.Equal(0F, JxlTransferFunctions.GammaEncodedFromDisplay(linear, 0.45455F));

    // libjxl's TF_HLG at 0, on the square-root segment, at its threshold 1/12 (which is still on it:
    // libjxl's test is x <= 1/12, and it gives 0.5), just above it, on the log segment, and above 1.
    // Measured: exact.
    [Theory]
    [InlineData(0F, 0F)]
    [InlineData(1e-6F, 0.00173205079F)]
    [InlineData(1e-3F, 0.0547722578F)]
    [InlineData(0.05F, 0.387298346F)]
    [InlineData(1F / 12, 0.5F)]
    [InlineData(0.0834F, 0.500199556F)]
    [InlineData(0.2F, 0.693893969F)]
    [InlineData(0.5F, 0.871643186F)]
    [InlineData(0.75F, 0.947098374F)]
    [InlineData(1F, 0.999999762F)]
    [InlineData(2F, 1.12611687F)]
    [InlineData(12F, 1.44832218F)]
    public void HlgEncodedFromDisplay_GivesLibjxlsValue(float linear, float expected)
        => Assert.Equal(expected, JxlTransferFunctions.HlgEncodedFromDisplay(linear));

    [Theory]
    [InlineData(-1e-3F, -0.0547722578F)]
    [InlineData(-0.25F, -0.738549232F)]
    [InlineData(-1F, -0.999999762F)]
    public void HlgEncodedFromDisplay_OfANegativeSample_IsLibjxlsMirroredValue(float linear, float expected)
        => Assert.Equal(expected, JxlTransferFunctions.HlgEncodedFromDisplay(linear));

    // Measured: 3.7e-7 against BT.2100's HLG OETF in double precision. libjxl documents a maximum
    // error of 5e-7.
    [Fact]
    public void HlgEncodedFromDisplay_IsCloseToTheFormula()
    {
        const double a = 0.17883277;
        const double b = 1 - (4 * a);
        const double c = 0.5599107295;
        Random random = new(7);
        double worst = 0;
        for (int i = 0; i < 200_000; i++)
        {
            float f = (float)random.NextDouble();
            double formula = f <= 1.0 / 12 ? Math.Sqrt(3 * f) : (a * Math.Log((12 * f) - b)) + c;
            worst = Math.Max(worst, Math.Abs(JxlTransferFunctions.HlgEncodedFromDisplay(f) - formula));
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Worst error {worst:G4}");
        Assert.True(worst < 1e-6, $"Worst absolute error {worst:G4}");
    }

    private static double LinearToSrgb(double v) => v <= 0.0031308 ? 12.92 * v : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;
}
