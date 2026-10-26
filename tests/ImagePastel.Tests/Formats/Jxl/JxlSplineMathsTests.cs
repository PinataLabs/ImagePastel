// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The spline maths that no test vector checks on its own: the continuous IDCT along a spline and the
/// intensity of one Gaussian segment. The expected values are what libjxl's own <c>ContinuousIDCT</c>,
/// <c>ComputeSegments</c> and <c>DrawSegment</c> (<c>lib/jxl/splines.cc</c>, commit ef67fde2) give when
/// compiled for Highway's scalar target without FMA (<c>-ffp-contract=off</c>), so the port must match
/// them exactly (ADR 0004).
/// </summary>
[Trait("Format", "Jxl")]
public class JxlSplineMathsTests
{
    // Measured: exact.
    [Theory]
    [InlineData(0F, 2.29965949F)]
    [InlineData(0.5F, -3.23341107F)]
    [InlineData(7.25F, -2.59455061F)]
    [InlineData(15.5F, 1.86676395F)]
    [InlineData(30.999F, -0.00484661199F)]
    [InlineData(31F, -0.00719433557F)]
    public void ContinuousIdct_GivesLibjxlsValue(float t, float expected)
    {
        float[] dct = new float[32];
        for (int i = 0; i < 32; i++)
        {
            dct[i] = (((i % 5) - 2) * 0.37F) + (0.01F * i);
        }

        Assert.Equal(expected, JxlSplines.ContinuousIdct(dct, t));
    }

    // Measured: exact. A DC coefficient alone gives a constant, the coefficient over kSqrt0_5.
    [Fact]
    public void ContinuousIdct_OfADcCoefficient_IsAboutConstant()
    {
        float[] dct = new float[32];
        dct[0] = 2.5F * 0.70710678118F;
        Assert.Equal(2.49999809F, JxlSplines.ContinuousIdct(dct, 3F));
    }

    // Measured: exact.
    [Fact]
    public void ComputeSegments_GivesLibjxlsSegment()
    {
        List<JxlSplineSegment> segments = new();
        List<(long Start, long End)> spans = new();
        JxlSplines.ComputeSegments(32, 10.3F, 5.7F, 0.8F, new[] { 1F, 0.5F, -0.25F }, 2.5F, segments, spans);

        JxlSplineSegment segment = Assert.Single(segments);
        Assert.Equal(11.8794899F, segment.MaximumDistance);
        Assert.Equal(0.400000006F, segment.InvSigma);
        Assert.Equal(0.5F, segment.SigmaOver4TimesIntensity);
        Assert.Equal((0L, 19L), Assert.Single(spans));
    }

    // Measured: exact. The colour is (1, 0.5, -0.25), so the first row holds the intensity itself.
    [Theory]
    [InlineData(5, 0, 1.11945619e-05F, 5.59728096e-06F, -2.79864048e-06F)]
    [InlineData(5, 10, 0.0481565073F, 0.0240782537F, -0.0120391268F)]
    [InlineData(5, 15, 0.00852777157F, 0.00426388578F, -0.00213194289F)]
    [InlineData(5, 22, 1.22719371e-06F, 6.13596853e-07F, -3.06798427e-07F)]
    [InlineData(5, 23, 0F, 0F, 0F)]
    [InlineData(9, 10, 0.0210274793F, 0.0105137397F, -0.00525686983F)]
    [InlineData(9, 19, 5.15029351e-05F, 2.57514675e-05F, -1.28757338e-05F)]
    [InlineData(0, 3, 5.48170319e-05F, 2.7408516e-05F, -1.3704258e-05F)]
    [InlineData(0, 22, 1.48948672e-07F, 7.4474336e-08F, -3.7237168e-08F)]
    public void DrawSegment_GivesLibjxlsIntensity(int y, int x, float expectedX, float expectedY, float expectedB)
    {
        List<JxlSplineSegment> segments = new();
        List<(long Start, long End)> spans = new();
        JxlSplines.ComputeSegments(32, 10.3F, 5.7F, 0.8F, new[] { 1F, 0.5F, -0.25F }, 2.5F, segments, spans);
        float[] rowX = new float[32];
        float[] rowY = new float[32];
        float[] rowB = new float[32];

        JxlSplines.DrawSegment(segments[0], y, 0, 32, rowX, rowY, rowB);

        Assert.Equal(expectedX, rowX[x]);
        Assert.Equal(expectedY, rowY[x]);
        Assert.Equal(expectedB, rowB[x]);
    }

    // libjxl rounds the span of a segment with std::llround, which on x86-64 gives LONG_MIN for a NaN, an
    // infinity or a value out of range. A NaN maximum distance (an infinite colour and a sigma whose square
    // is 0) or an infinite one (a sigma whose square overflows) then gives an empty span, and libjxl adds no
    // segment. .NET 9 and later convert a NaN to 0 and an infinity to long.MaxValue, so a plain cast would
    // add one. The spline bounds keep such colours and sigmas out of a stream: the port must not depend
    // on the runtime all the same. Measured with the harness: no segment for either.
    [Theory]
    [InlineData(float.PositiveInfinity, 1e-23F)] // NaN maximum distance
    [InlineData(1F, 1e20F)] // Infinite maximum distance
    public void ComputeSegments_WithANonFiniteMaximumDistance_AddsNoSegment(float color, float sigma)
    {
        List<JxlSplineSegment> segments = new();
        List<(long Start, long End)> spans = new();

        JxlSplines.ComputeSegments(32, 10.3F, 5.7F, 1F, new[] { color, 0F, 0F }, sigma, segments, spans);

        Assert.Empty(segments);
        Assert.Empty(spans);
    }

    // The same rounding in DrawSegment: libjxl draws nothing for a segment whose maximum distance is NaN,
    // infinite, or so large that the span is out of range. Measured with the harness: no pixel changed.
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(1e19F)]
    public void DrawSegment_WithAMaximumDistanceOutOfRange_DrawsNothing(float maximumDistance)
    {
        JxlSplineSegment segment = new(10.3F, 5.7F, maximumDistance, 0.4F, 0.5F, 1F, 0.5F, -0.25F);
        float[] rowX = new float[32];
        float[] rowY = new float[32];
        float[] rowB = new float[32];

        JxlSplines.DrawSegment(segment, 5, 0, 32, rowX, rowY, rowB);

        Assert.All(rowX, v => Assert.Equal(0F, v));
        Assert.All(rowY, v => Assert.Equal(0F, v));
        Assert.All(rowB, v => Assert.Equal(0F, v));
    }

    // A segment adds to what the row holds.
    [Fact]
    public void DrawSegment_AddsToTheRow()
    {
        List<JxlSplineSegment> segments = new();
        List<(long Start, long End)> spans = new();
        JxlSplines.ComputeSegments(32, 10.3F, 5.7F, 0.8F, new[] { 1F, 0.5F, -0.25F }, 2.5F, segments, spans);
        float[] rowX = new float[32];
        float[] rowY = new float[32];
        float[] rowB = new float[32];
        Array.Fill(rowX, 0.25F);

        JxlSplines.DrawSegment(segments[0], 5, 0, 32, rowX, rowY, rowB);

        Assert.Equal((1F * 0.0481565073F) + 0.25F, rowX[10]);
        Assert.Equal(0.25F, rowX[23]);
    }
}
