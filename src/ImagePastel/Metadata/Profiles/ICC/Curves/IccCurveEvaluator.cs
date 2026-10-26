// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// The inverse parametric curves follow DefaultEvalParametricFn (cases -1 to -5) in src/cmsgamma.c,
// and the choice of solution on flat parts of an inverted table follows cmsReverseToneCurveEx and
// GetInterval in the same file, of Little CMS (https://github.com/mm2/Little-CMS) at commit 5176347
// (vendored in libjxl third_party/lcms).
// Copyright (c) Marti Maria Saguer. MIT License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace PinataLabs.ImagePastel.Metadata.Profiles.Icc;

/// <summary>
/// Evaluates ICC one-dimensional curves (<c>curveType</c> and <c>parametricCurveType</c>), as
/// ICC.1:2022 sections 10.6 and 10.18 define them, and their inverses.
/// </summary>
internal static class IccCurveEvaluator
{
    // Below this, a curve parameter counts as zero and the inverse is degenerate (Little CMS's MATRIX_DET_TOLERANCE).
    private const float Tolerance = 1e-4F;

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
    /// Evaluates the inverse of a curve tag at <paramref name="y"/>: the input that gives <paramref name="y"/>.
    /// </summary>
    /// <param name="curve">A <see cref="IccCurveTagDataEntry"/> or <see cref="IccParametricCurveTagDataEntry"/>.</param>
    /// <param name="y">The curve output to invert.</param>
    /// <param name="x">The curve input. It is not clamped.</param>
    /// <returns><see langword="true"/> if <paramref name="curve"/> is a curve type this class evaluates.</returns>
    public static bool TryEvaluateInverse(IccTagDataEntry curve, float y, out float x)
    {
        switch (curve)
        {
            case IccCurveTagDataEntry table:
                x = EvaluateInverse(table, y);
                return true;
            case IccParametricCurveTagDataEntry parametric:
                x = EvaluateInverse(parametric.Curve, y);
                return true;
            default:
                x = 0;
                return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether <paramref name="curve"/> is a curve type this class evaluates.
    /// </summary>
    /// <param name="curve">The tag data entry.</param>
    /// <returns><see langword="true"/> for <c>curveType</c> and <c>parametricCurveType</c> entries.</returns>
    public static bool IsCurve(IccTagDataEntry curve)
        => curve is IccCurveTagDataEntry or IccParametricCurveTagDataEntry;

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
    /// Evaluates the inverse of a <c>curveType</c> curve. A table is inverted numerically: the
    /// bracketing entries are found by a binary search, which assumes the table is monotonic, and
    /// interpolated linearly. Where an increasing table is flat at <paramref name="y"/> the largest
    /// matching input is returned, and for a decreasing table the smallest, as Little CMS does.
    /// Outputs beyond the table's range give 0 or 1.
    /// </summary>
    /// <param name="curve">The curve.</param>
    /// <param name="y">The curve output to invert.</param>
    /// <returns>The curve input.</returns>
    public static float EvaluateInverse(IccCurveTagDataEntry curve, float y)
    {
        if (curve.IsIdentityResponse)
        {
            return y;
        }

        if (curve.IsGamma)
        {
            float gamma = curve.Gamma;
            return MathF.Abs(gamma) < Tolerance ? 0 : Pow(y, 1 / gamma);
        }

        float[] table = curve.CurveData;
        int last = table.Length - 1;
        if (table[0] <= table[last])
        {
            // Increasing: the last entry not above y.
            if (y < table[0])
            {
                return 0;
            }

            int low = 0;
            int high = last;
            while (low < high)
            {
                int middle = (low + high + 1) >> 1;
                if (table[middle] <= y)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            if (low == last)
            {
                return 1;
            }

            return (low + ((y - table[low]) / (table[low + 1] - table[low]))) / last;
        }
        else
        {
            // Decreasing: the first entry not above y.
            if (y < table[last])
            {
                return 1;
            }

            int low = 0;
            int high = last;
            while (low < high)
            {
                int middle = (low + high) >> 1;
                if (table[middle] <= y)
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }

            if (low == 0)
            {
                return 0;
            }

            return (low - ((y - table[low]) / (table[low - 1] - table[low]))) / last;
        }
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

    /// <summary>
    /// Evaluates the inverse of a <c>parametricCurveType</c> curve of function type 0 to 4 analytically.
    /// A degenerate curve (a zero gamma, or a zero slope where the inverse divides by it) gives 0.
    /// </summary>
    /// <param name="curve">The curve.</param>
    /// <param name="y">The curve output to invert.</param>
    /// <returns>The curve input.</returns>
    public static float EvaluateInverse(IccParametricCurve curve, float y)
    {
        float g = curve.G;
        float a = curve.A;
        float b = curve.B;
        float c = curve.C;
        float d = curve.D;
        bool degeneratePower = MathF.Abs(g) < Tolerance || MathF.Abs(a) < Tolerance;

        switch (curve.Type)
        {
            case IccParametricCurveType.Type1:
                // X = Y ^ (1 / g)
                return MathF.Abs(g) < Tolerance ? 0 : Pow(y, 1 / g);

            case IccParametricCurveType.Cie122_1996:
                // X = (Y ^ (1 / g) - b) / a, never below 0
                return degeneratePower || y < 0 ? 0 : MathF.Max(0, (Pow(y, 1 / g) - b) / a);

            case IccParametricCurveType.Iec61966_3:
                // X = ((Y - c) ^ (1 / g) - b) / a above c; the flat part's input -b/a below it
                if (degeneratePower)
                {
                    return 0;
                }

                if (y >= c)
                {
                    float e = y - c;
                    return e > 0 ? (Pow(e, 1 / g) - b) / a : 0;
                }

                return -b / a;

            case IccParametricCurveType.SRgb:
            {
                // X = (Y ^ (1 / g) - b) / a above (ad + b) ^ g, X = Y / c below it
                float e = (a * d) + b;
                float switchPoint = e < 0 ? 0 : Pow(e, g);
                if (y >= switchPoint)
                {
                    return degeneratePower ? 0 : (Pow(y, 1 / g) - b) / a;
                }

                return MathF.Abs(c) < Tolerance ? 0 : y / c;
            }

            case IccParametricCurveType.Type5:
            {
                // X = ((Y - e) ^ (1 / g) - b) / a above cd + f, X = (Y - f) / c below it. Little CMS
                // switches at the linear piece's value at d (unlike type 3), so this does too.
                if (y >= (c * d) + curve.F)
                {
                    float e = y - curve.E;
                    return e < 0 || degeneratePower ? 0 : (Pow(e, 1 / g) - b) / a;
                }

                return MathF.Abs(c) < Tolerance ? 0 : (y - curve.F) / c;
            }

            default:
                return y;
        }
    }

    private static float Pow(float value, float exponent) => value > 0 ? MathF.Pow(value, exponent) : 0;
}
