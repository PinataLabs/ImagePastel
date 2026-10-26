// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Checks each fast approximation against the exact result in double precision, over the input range
/// that libjxl's <c>fast_math_test.cc</c> samples, within the error that libjxl documents for it. The
/// measured worst error of this port is noted on each test.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlFastMathTests
{
    private const int Samples = 200_000;

    // Measured: 3.00e-6.
    [Fact]
    public void FastLog2f_IsWithinItsAbsoluteError()
        => AssertWorstError(1, 1e-7F, 1e3F, JxlFastMath.FastLog2f, f => Math.Log2(f), relative: false, 3.9e-6);

    // Measured: 2.07e-7.
    [Fact]
    public void FastPow2f_IsWithinItsRelativeError()
        => AssertWorstError(2, -100F, 100F, JxlFastMath.FastPow2f, f => Math.Pow(2, f), relative: true, 3e-7);

    // Measured: 2.18e-5.
    [Fact]
    public void FastPowf_IsWithinItsRelativeError()
    {
        Random random = new(3);
        double worst = 0;
        for (int i = 0; i < Samples; i++)
        {
            float b = Uniform(random, 1e-3F, 1e3F);
            float e = Uniform(random, -10F, 10F);
            double expected = Math.Pow(b, e);
            worst = Math.Max(worst, Math.Abs(JxlFastMath.FastPowf(b, e) - expected) / expected);
        }

        Report(worst);
        Assert.True(worst < 3e-5, $"Worst relative error {worst:G4}");
    }

    // Measured: 6.07e-5.
    [Fact]
    public void FastCosf_IsWithinItsAbsoluteError()
        => AssertWorstError(4, -1e3F, 1e3F, JxlFastMath.FastCosf, f => Math.Cos(f), relative: false, 7e-5);

    // Measured: 5.88e-4.
    [Fact]
    public void FastErff_IsWithinItsAbsoluteError()
        => AssertWorstError(5, -5F, 5F, JxlFastMath.FastErff, f => Erf(f), relative: false, 7e-4);

    // Measured: 1.17e-4.
    [Fact]
    public void FastLinearToSrgb_IsWithinItsAbsoluteError()
        => AssertWorstError(6, 0F, 1F, JxlFastMath.FastLinearToSrgb, f => LinearToSrgb(f), relative: false, 1.2e-4);

    [Theory]
    [InlineData(0F)]
    [InlineData(0.002F)]
    [InlineData(-0.5F)]
    public void FastLinearToSrgb_BelowTheThreshold_IsTheLinearSegment(float linear)
        => Assert.Equal(linear * 12.92F, JxlFastMath.FastLinearToSrgb(linear));

    [Fact]
    public void FastErff_IsOdd()
    {
        Assert.Equal(-JxlFastMath.FastErff(1.5F), JxlFastMath.FastErff(-1.5F));
        Assert.Equal(0F, JxlFastMath.FastErff(0F));
    }

    [Fact]
    public void EvalRationalPolynomial_EvaluatesBothPolynomials()
    {
        // (1 + 2x + 3x^2) / (4 + 5x) at x = 2 is 17 / 14.
        float[] p = { 1F, 2F, 3F };
        float[] q = { 4F, 5F };
        Assert.Equal(17F / 14F, JxlFastMath.EvalRationalPolynomial(2F, p, q));
    }

    private static void AssertWorstError(int seed, float min, float max, Func<float, float> actual, Func<float, double> expected, bool relative, double bound)
    {
        Random random = new(seed);
        double worst = 0;
        for (int i = 0; i < Samples; i++)
        {
            float f = Uniform(random, min, max);
            double e = expected(f);
            double error = Math.Abs(actual(f) - e);
            worst = Math.Max(worst, relative ? error / Math.Abs(e) : error);
        }

        Report(worst);
        Assert.True(worst < bound, $"Worst {(relative ? "relative" : "absolute")} error {worst:G4}, bound {bound:G4}");
    }

    private static float Uniform(Random random, float min, float max) => (float)(min + (random.NextDouble() * (max - min)));

    // The sRGB encoding of IEC 61966-2-1, in double precision.
    private static double LinearToSrgb(double v) => v <= 0.0031308 ? 12.92 * v : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;

    // erf from the series 2/sqrt(pi) exp(-x^2) sum 2^n x^(2n+1) / (1 3 5 ... (2n+1)). Its terms all have
    // the sign of x, so nothing cancels and it keeps double precision up to |x| = 5.
    private static double Erf(double x)
    {
        double term = x;
        double sum = x;
        for (int n = 1; n < 200; n++)
        {
            term *= 2 * x * x / ((2 * n) + 1);
            sum += term;
        }

        return 2 / Math.Sqrt(Math.PI) * Math.Exp(-x * x) * sum;
    }

    private static void Report(double worst) => TestContext.Current.TestOutputHelper?.WriteLine($"Worst error {worst:G4}");
}
