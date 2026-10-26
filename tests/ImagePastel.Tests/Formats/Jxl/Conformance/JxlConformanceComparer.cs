// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;

/// <summary>
/// Compares decoded output with a conformance reference the way libjxl's <c>conformance.py</c> does
/// (<c>CompareNPY</c>): per frame, the worst channel's RMSE must not exceed <c>rms_error</c>, and the
/// largest absolute error over all samples must not exceed <c>peak_error</c>. There is no PSNR.
/// Errors are accumulated in double precision; <c>conformance.py</c> uses NumPy float32 maths.
/// <para>
/// The references are not clamped: some vectors hold samples below 0 or above 1. Only a float decoder
/// (djxl, or the decoder's internal float planes) can match them exactly. Output that is clamped by
/// nature, such as integer pixels from Magick.NET or an <see cref="Image{TPixel}"/>, is compared with
/// <see cref="JxlConformanceComparison.ClampToUnitRange"/>, which clamps both sides to [0, 1] first,
/// as the <c>--lax</c> mode of upstream's newer <c>scripts/conformance.py</c> does.
/// </para>
/// </summary>
internal static class JxlConformanceComparer
{
    /// <summary>Measures each frame. Both arrays must have the shape (frames, height, width, channels).</summary>
    public static JxlConformanceFrameResult[] Measure(NpyArray expected, NpyArray actual, JxlConformanceComparison comparison = JxlConformanceComparison.Strict)
    {
        if (expected.Shape.Length != 4)
        {
            throw new ArgumentException($"Expected a (frames, height, width, channels) array, got {expected.ShapeText}.", nameof(expected));
        }

        if (!expected.Shape.SequenceEqual(actual.Shape))
        {
            throw new ArgumentException($"Expected shape {expected.ShapeText} but found {actual.ShapeText}.", nameof(actual));
        }

        int frames = expected.Shape[0];
        int channels = expected.Shape[3];
        int frameLength = expected.Shape[1] * expected.Shape[2] * channels;
        int pixels = expected.Shape[1] * expected.Shape[2];

        JxlConformanceFrameResult[] results = new JxlConformanceFrameResult[frames];
        double[] squaredErrorSums = new double[channels];
        for (int f = 0; f < frames; f++)
        {
            Array.Clear(squaredErrorSums);
            double peak = 0;
            ReadOnlySpan<float> e = expected.Data.AsSpan(f * frameLength, frameLength);
            ReadOnlySpan<float> a = actual.Data.AsSpan(f * frameLength, frameLength);
            bool clamp = comparison == JxlConformanceComparison.ClampToUnitRange;
            for (int i = 0; i < frameLength; i++)
            {
                double expectedValue = clamp ? Math.Clamp(e[i], 0F, 1F) : e[i];
                double actualValue = clamp ? Math.Clamp(a[i], 0F, 1F) : a[i];
                double error = Math.Abs(expectedValue - actualValue);

                // NaN compares false against any threshold, so count it as an unbounded error.
                if (double.IsNaN(error))
                {
                    error = double.PositiveInfinity;
                }

                squaredErrorSums[i % channels] += error * error;
                peak = Math.Max(peak, error);
            }

            double worstRmse = squaredErrorSums.Max(sum => Math.Sqrt(sum / pixels));
            results[f] = new JxlConformanceFrameResult(f, worstRmse, peak);
        }

        return results;
    }

    /// <summary>Returns a description of each frame that breaks its thresholds; empty when all pass.</summary>
    public static IReadOnlyList<string> FindFailures(JxlConformanceVector vector, NpyArray expected, NpyArray actual, JxlConformanceComparison comparison = JxlConformanceComparison.Strict)
    {
        JxlConformanceFrameResult[] results = Measure(expected, actual, comparison);
        if (results.Length != vector.Frames.Count)
        {
            return new[] { $"{vector.Id}: test.json lists {vector.Frames.Count} frames but the reference has {results.Length}." };
        }

        List<string> failures = new();
        foreach (JxlConformanceFrameResult result in results)
        {
            JxlConformanceFrame limits = vector.Frames[result.Frame];
            if (result.WorstChannelRmse > limits.RmsError)
            {
                failures.Add($"{vector.Id} frame {result.Frame}: RMSE {result.WorstChannelRmse:G6} > {limits.RmsError:G6}");
            }

            if (result.PeakError > limits.PeakError)
            {
                failures.Add($"{vector.Id} frame {result.Frame}: peak error {result.PeakError:G6} > {limits.PeakError:G6}");
            }
        }

        return failures;
    }

    public static void AssertWithinThresholds(JxlConformanceVector vector, NpyArray expected, NpyArray actual, JxlConformanceComparison comparison = JxlConformanceComparison.Strict)
    {
        IReadOnlyList<string> failures = FindFailures(vector, expected, actual, comparison);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Asserts that every sample is within 1e-6 of djxl's: the check for the generated files, whose
    /// djxl references are made from float output.
    /// </summary>
    public static void AssertMatchesDjxl(NpyArray expected, NpyArray actual)
    {
        Assert.Equal(expected.Shape, actual.Shape);
        for (int i = 0; i < expected.Data.Length; i++)
        {
            Assert.True(MathF.Abs(expected.Data[i] - actual.Data[i]) <= 1e-6F, $"Sample {i}: djxl {expected.Data[i]}, ImageSharp {actual.Data[i]}.");
        }
    }
}

/// <summary>
/// The lossy check of the generated files (docs/jxl/spec.md, "Tolerance for lossy output"): XYB and VarDCT
/// output cannot match djxl bit for bit, because libjxl uses FMA and this port does not, so each frame's worst
/// channel RMSE must be at most 1e-4 and its peak error at most 4e-3.
/// </summary>
internal static class JxlLossyComparer
{
    /// <summary>The largest RMSE of any channel.</summary>
    public const double RmsError = 1e-4;

    /// <summary>
    /// The largest RMSE of any channel for a frame transcoded from a JPEG: the JPEG conformance vectors
    /// allow ten times less than the general rule.
    /// </summary>
    public const double JpegRmsError = 1e-5;

    /// <summary>The largest error of any sample.</summary>
    public const double PeakError = 4e-3;

    /// <summary>Asserts that every frame is within the lossy thresholds, and returns the measured errors.</summary>
    /// <param name="expected">The djxl output.</param>
    /// <param name="actual">The decoded output.</param>
    /// <param name="rmsError">The largest RMSE of any channel.</param>
    /// <returns>The measured error of each frame.</returns>
    public static JxlConformanceFrameResult[] AssertWithinThresholds(NpyArray expected, NpyArray actual, double rmsError = RmsError)
    {
        JxlConformanceFrameResult[] results = JxlConformanceComparer.Measure(expected, actual);
        foreach (JxlConformanceFrameResult result in results)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"Frame {result.Frame}: worst channel RMSE {result.WorstChannelRmse:G3}, peak {result.PeakError:G3}");
            Assert.True(result.WorstChannelRmse <= rmsError, $"Frame {result.Frame}: RMSE {result.WorstChannelRmse:G6} > {rmsError}");
            Assert.True(result.PeakError <= PeakError, $"Frame {result.Frame}: peak error {result.PeakError:G6} > {PeakError}");
        }

        return results;
    }
}

/// <summary>The measured error of one frame.</summary>
internal sealed record JxlConformanceFrameResult(int Frame, double WorstChannelRmse, double PeakError);

/// <summary>How decoded output is compared with a conformance reference.</summary>
internal enum JxlConformanceComparison
{
    /// <summary>Compare the values as they are. This is the conformance check.</summary>
    Strict,

    /// <summary>Clamp both sides to [0, 1] first, for output that cannot hold out-of-range samples.</summary>
    ClampToUnitRange,
}
