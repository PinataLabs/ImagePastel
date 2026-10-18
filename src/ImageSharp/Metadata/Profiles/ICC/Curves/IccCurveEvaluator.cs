// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Metadata.Profiles.Icc;

/// <summary>
/// Evaluates ICC one-dimensional curves (<c>curveType</c> and <c>parametricCurveType</c>), as
/// ICC.1:2022 sections 10.6 and 10.18 define them.
/// </summary>
internal static class IccCurveEvaluator
{
    /// <summary>
    /// Evaluates a curve tag at <paramref name="x"/>.
    /// </summary>
    /// <param name="curve">A <see cref="IccCurveTagDataEntry"/> or <see cref="IccParametricCurveTagDataEntry"/>.</param>
    /// <param name="x">The input, in the range [0, 1].</param>
    /// <param name="y">The output. It is not clamped.</param>
    /// <returns><see langword="true"/> if <paramref name="curve"/> is a curve type this class evaluates.</returns>
    public static bool TryEvaluate(IccTagDataEntry curve, float x, out float y)
    {
        switch (curve)
        {
            case IccCurveTagDataEntry table:
                y = Evaluate(table, x);
                return true;
            case IccParametricCurveTagDataEntry parametric:
                y = Evaluate(parametric.Curve, x);
                return true;
            default:
                y = 0;
                return false;
        }
    }

    /// <summary>
    /// Evaluates a <c>curveType</c> curve: identity, a single gamma, or a table that is
    /// interpolated linearly.
    /// </summary>
    /// <param name="curve">The curve.</param>
    /// <param name="x">The input, in the range [0, 1].</param>
    /// <returns>The output.</returns>
    public static float Evaluate(IccCurveTagDataEntry curve, float x)
    {
        if (curve.IsIdentityResponse)
        {
            return x;
        }

        if (curve.IsGamma)
        {
            return Pow(x, curve.Gamma);
        }

        float[] table = curve.CurveData;
        float position = Numerics.Clamp(x, 0, 1) * (table.Length - 1);
        int index = Math.Min((int)position, table.Length - 2);
        float fraction = position - index;
        return table[index] + ((table[index + 1] - table[index]) * fraction);
    }

    /// <summary>
    /// Evaluates a <c>parametricCurveType</c> curve of function type 0 to 4.
    /// </summary>
    /// <param name="curve">The curve.</param>
    /// <param name="x">The input, in the range [0, 1].</param>
    /// <returns>The output.</returns>
    public static float Evaluate(IccParametricCurve curve, float x)
    {
        float g = curve.G;
        float a = curve.A;
        float b = curve.B;

        // Types 1 and 2 switch where aX + b = 0, that is at X = -b/a for a positive a.
        return curve.Type switch
        {
            IccParametricCurveType.Type1 => Pow(x, g),
            IccParametricCurveType.Cie122_1996 => (a * x) + b >= 0 ? Pow((a * x) + b, g) : 0,
            IccParametricCurveType.Iec61966_3 => ((a * x) + b >= 0 ? Pow((a * x) + b, g) : 0) + curve.C,
            IccParametricCurveType.SRgb => x >= curve.D ? Pow((a * x) + b, g) : curve.C * x,
            IccParametricCurveType.Type5 => x >= curve.D ? Pow((a * x) + b, g) + curve.E : (curve.C * x) + curve.F,
            _ => x,
        };
    }

    private static float Pow(float value, float exponent) => value > 0 ? MathF.Pow(value, exponent) : 0;
}
