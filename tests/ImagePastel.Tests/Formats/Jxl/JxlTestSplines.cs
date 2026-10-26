// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Writes the spline dictionary that libjxl (https://github.com/libjxl/libjxl, commit ef67fde2) reads in
// Splines::Decode (lib/jxl/splines.cc), in the same order of values and contexts.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// A spline dictionary for a <see cref="JxlTestFrame"/>: splines, each a starting point, control points
/// as delta-deltas, and its colour and sigma DCTs. The values are entropy coded by
/// <see cref="JxlTestEntropyValues"/>, one cluster per context.
/// </summary>
internal sealed class JxlTestSplines
{
    private const int QuantizationAdjustmentContext = 0;
    private const int StartingPositionContext = 1;
    private const int NumSplinesContext = 2;
    private const int NumControlPointsContext = 3;
    private const int ControlPointsContext = 4;
    private const int DctContext = 5;

    private static readonly int[] ContextMap = { 0, 1, 2, 3, 4, 5 };

    public JxlTestSplines(params JxlTestSpline[] splines) => this.Splines = splines;

    public IReadOnlyList<JxlTestSpline> Splines { get; }

    /// <summary>Gets the number of splines to write, minus one as coded, when it is not the number given minus one.</summary>
    public long? NumSplinesMinusOne { get; init; }

    /// <summary>Gets the quantization adjustment.</summary>
    public int QuantizationAdjustment { get; init; }

    /// <summary>Gets the initial ANS state; see <see cref="JxlTestEntropyValues.Write"/>.</summary>
    public uint? AnsState { get; init; }

    /// <summary>Writes the dictionary.</summary>
    /// <param name="w">The writer.</param>
    public void Write(JxlTestBitWriter w)
        => JxlTestEntropyValues.Write(w, ContextMap, this.Values(), this.AnsState);

    // The values in the order Splines::Decode reads them.
    private List<(int Context, long Value)> Values()
    {
        List<(int Context, long Value)> values = new() { (NumSplinesContext, this.NumSplinesMinusOne ?? (this.Splines.Count - 1)) };
        for (int i = 0; i < this.Splines.Count; i++)
        {
            JxlTestSpline spline = this.Splines[i];
            if (i == 0)
            {
                values.Add((StartingPositionContext, spline.X));
                values.Add((StartingPositionContext, spline.Y));
            }
            else
            {
                JxlTestSpline prev = this.Splines[i - 1];
                values.Add((StartingPositionContext, JxlBits.PackSigned((int)(spline.X - prev.X))));
                values.Add((StartingPositionContext, JxlBits.PackSigned((int)(spline.Y - prev.Y))));
            }
        }

        values.Add((QuantizationAdjustmentContext, JxlBits.PackSigned(this.QuantizationAdjustment)));
        foreach (JxlTestSpline spline in this.Splines)
        {
            values.Add((NumControlPointsContext, spline.NumControlPoints ?? spline.DeltaDeltas.Count));
            foreach ((int dx, int dy) in spline.DeltaDeltas)
            {
                values.Add((ControlPointsContext, JxlBits.PackSigned(dx)));
                values.Add((ControlPointsContext, JxlBits.PackSigned(dy)));
            }

            foreach (int[] dct in new[] { spline.ColorX, spline.ColorY, spline.ColorB, spline.Sigma })
            {
                for (int i = 0; i < 32; i++)
                {
                    values.Add((DctContext, dct != null && i < dct.Length ? JxlBits.PackSigned(dct[i]) : 0));
                }
            }

            if (spline.RawDctValue is uint raw)
            {
                // Replace the last sigma coefficient with a raw coded value.
                values[^1] = (DctContext, raw);
            }
        }

        return values;
    }
}

/// <summary>
/// One spline: its starting point, its control points as delta-deltas from it, and its quantized
/// colour and sigma DCTs (32 coefficients each; missing ones are 0).
/// </summary>
internal sealed class JxlTestSpline
{
    public JxlTestSpline(long x, long y, params (int Dx, int Dy)[] deltaDeltas)
    {
        this.X = x;
        this.Y = y;
        this.DeltaDeltas = deltaDeltas;
    }

    public long X { get; }

    public long Y { get; }

    public IReadOnlyList<(int Dx, int Dy)> DeltaDeltas { get; }

    /// <summary>Gets the number of control points to write, when it is not the number given.</summary>
    public long? NumControlPoints { get; init; }

    public int[] ColorX { get; init; }

    public int[] ColorY { get; init; }

    public int[] ColorB { get; init; }

    public int[] Sigma { get; init; }

    /// <summary>Gets a raw coded value for the last sigma coefficient, before it is unpacked.</summary>
    public uint? RawDctValue { get; init; }
}
