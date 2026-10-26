// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Linq;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;

[Trait("Format", "Jxl")]
public class JxlConformanceComparerTests
{
    // delta_palette: one frame, RMSE and peak limits of 0.000976562.
    private static readonly JxlConformanceVector DeltaPalette = JxlConformanceVector.Load("delta_palette");

    [Fact]
    public void IdenticalArrays_Pass()
    {
        NpyArray expected = Gradient(1, 4, 4, 3);
        NpyArray actual = Gradient(1, 4, 4, 3);

        Assert.Empty(JxlConformanceComparer.FindFailures(DeltaPalette, expected, actual));
    }

    [Fact]
    public void SmallErrorEverywhere_PassesWithinLimits()
    {
        NpyArray expected = Gradient(1, 4, 4, 3);
        NpyArray actual = Offset(expected, 0.0009F);

        Assert.Empty(JxlConformanceComparer.FindFailures(DeltaPalette, expected, actual));
    }

    [Fact]
    public void UniformErrorAboveLimit_FailsRmseAndPeak()
    {
        NpyArray expected = Gradient(1, 4, 4, 3);
        NpyArray actual = Offset(expected, 0.002F);

        string[] failures = JxlConformanceComparer.FindFailures(DeltaPalette, expected, actual).ToArray();

        Assert.Equal(2, failures.Length);
        Assert.Contains("RMSE", failures[0]);
        Assert.Contains("peak error", failures[1]);
    }

    [Fact]
    public void SingleOutlier_FailsPeakOnly()
    {
        // One of a channel's 16 samples is off by 0.003: its RMSE is 0.003 / 4 = 0.00075, under the
        // limit, but the peak error is over it.
        NpyArray expected = Gradient(1, 4, 4, 3);
        NpyArray actual = Offset(expected, 0F);
        actual.Data[5] += 0.003F;

        string failure = Assert.Single(JxlConformanceComparer.FindFailures(DeltaPalette, expected, actual));

        Assert.Contains("peak error", failure);
    }

    [Fact]
    public void RmseIsWorstChannel_NotAverageOverChannels()
    {
        // Every pixel's green channel is off by 0.0012: green RMSE 0.0012 fails, though the average over
        // channels would be under the limit.
        NpyArray expected = Gradient(1, 4, 4, 3);
        NpyArray actual = Offset(expected, 0F);
        for (int i = 1; i < actual.Data.Length; i += 3)
        {
            actual.Data[i] += 0.0012F;
        }

        JxlConformanceFrameResult result = Assert.Single(JxlConformanceComparer.Measure(expected, actual));

        Assert.Equal(0.0012, result.WorstChannelRmse, 6);
    }

    [Fact]
    public void NaN_Fails()
    {
        NpyArray expected = Gradient(1, 4, 4, 3);
        NpyArray actual = Offset(expected, 0F);
        actual.Data[0] = float.NaN;

        Assert.NotEmpty(JxlConformanceComparer.FindFailures(DeltaPalette, expected, actual));
    }

    [Fact]
    public void OutOfRangeReference_FailsStrict_PassesClamped()
    {
        // Conformance references are unclamped; an integer decoder outputs the clamped value.
        NpyArray expected = Gradient(1, 4, 4, 3);
        expected.Data[0] = -0.08F;
        expected.Data[1] = 1.09F;
        NpyArray actual = Offset(expected, 0F);
        actual.Data[0] = 0F;
        actual.Data[1] = 1F;

        Assert.NotEmpty(JxlConformanceComparer.FindFailures(DeltaPalette, expected, actual));
        Assert.Empty(JxlConformanceComparer.FindFailures(DeltaPalette, expected, actual, JxlConformanceComparison.ClampToUnitRange));
    }

    [Fact]
    public void ShapeMismatch_Throws()
    {
        NpyArray expected = Gradient(1, 4, 4, 3);
        NpyArray actual = Gradient(1, 4, 4, 4);

        Assert.Throws<ArgumentException>(() => JxlConformanceComparer.Measure(expected, actual));
    }

    [Fact]
    public void FrameCountMismatchWithTestJson_Fails()
    {
        NpyArray expected = Gradient(2, 4, 4, 3);

        string failure = Assert.Single(JxlConformanceComparer.FindFailures(DeltaPalette, expected, expected));

        Assert.Contains("frames", failure);
    }

    private static NpyArray Gradient(int frames, int height, int width, int channels)
    {
        float[] data = new float[frames * height * width * channels];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (float)i / data.Length;
        }

        return new NpyArray(new[] { frames, height, width, channels }, data);
    }

    private static NpyArray Offset(NpyArray source, float offset)
        => new((int[])source.Shape.Clone(), source.Data.Select(v => v + offset).ToArray());
}
