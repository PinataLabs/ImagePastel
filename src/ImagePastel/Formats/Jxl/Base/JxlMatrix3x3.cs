// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// Mul3x3Vector, Mul3x3Matrix and Inv3x3Matrix in lib/jxl/base/matrix_ops.h, in double precision, and the
// sRGB to XYZ (D50) setup of OutputEncodingInfo::SetColorEncoding (AdaptToPrimaries) in lib/jxl/dec_xyb.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.ColorManagement.Icc;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;

namespace PinataLabs.ImagePastel.Formats.Jxl.Base;

/// <summary>
/// The 3x3 colour matrices that the JPEG XL codec shares: the primaries matrices through XYZ adapted to D50,
/// in float as the decoder computes them, and double-precision products and inverse for profile recognition.
/// </summary>
/// <remarks>
/// The float matrices use <see cref="IccProfileBuilder"/>, as the decoder's ICC profiles do. Recognition
/// keeps its own double versions: <see cref="IccProfileBuilder.Inv3x3Matrix"/> works in float and throws for
/// a singular matrix, where recognition needs a result it can refuse.
/// </remarks>
internal static class JxlMatrix3x3
{
    /// <summary>
    /// Gets the matrix from linear sRGB (D65 white) to XYZ adapted to libjxl's D50, as the decoder's
    /// <c>AdaptToPrimaries</c> computes it.
    /// </summary>
    /// <returns>The row-major matrix.</returns>
    public static float[,] SrgbToXyzD50()
    {
        JxlColorEncodingHeader srgb = new();
        ((double X, double Y) R, (double X, double Y) G, (double X, double Y) B) p = srgb.PrimariesXy;
        (double X, double Y) w = srgb.WhitePointXy;
        return IccProfileBuilder.PrimariesToXyzD50(
            (float)p.R.X, (float)p.R.Y, (float)p.G.X, (float)p.G.Y, (float)p.B.X, (float)p.B.Y, (float)w.X, (float)w.Y, IccProfileBuilder.LibjxlD50);
    }

    /// <summary>
    /// Adapts a primaries matrix from its white point to libjxl's D50 (Bradford): <c>adapt(white) × toXyz</c>.
    /// </summary>
    /// <param name="white">The white point of the primaries.</param>
    /// <param name="toXyz">The matrix from the primaries to XYZ, at <paramref name="white"/>.</param>
    /// <returns>The matrix from the primaries to XYZ adapted to D50.</returns>
    public static float[,] ToXyzD50((double X, double Y) white, float[,] toXyz)
    {
        float[,] adaptToD50 = IccProfileBuilder.AdaptToXyzD50((float)white.X, (float)white.Y, IccProfileBuilder.LibjxlD50);
        return IccProfileBuilder.Mul3x3Matrix(adaptToD50, toXyz);
    }

    /// <summary>Multiplies a matrix by a vector, in double precision.</summary>
    /// <param name="m">The matrix.</param>
    /// <param name="v">The vector.</param>
    /// <returns><paramref name="m"/> × <paramref name="v"/>.</returns>
    public static double[] Multiply(double[,] m, double[] v)
    {
        double[] result = new double[3];
        for (int i = 0; i < 3; i++)
        {
            result[i] = (m[i, 0] * v[0]) + (m[i, 1] * v[1]) + (m[i, 2] * v[2]);
        }

        return result;
    }

    /// <summary>Multiplies two matrices, in double precision.</summary>
    /// <param name="a">The left matrix.</param>
    /// <param name="b">The right matrix.</param>
    /// <returns><paramref name="a"/> × <paramref name="b"/>.</returns>
    public static double[,] Multiply(double[,] a, double[,] b)
    {
        double[,] result = new double[3, 3];
        for (int j = 0; j < 3; j++)
        {
            for (int i = 0; i < 3; i++)
            {
                result[j, i] = (a[j, 0] * b[0, i]) + (a[j, 1] * b[1, i]) + (a[j, 2] * b[2, i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Inverts a matrix in double precision, as libjxl's <c>Inv3x3Matrix</c>: it fails when the determinant
    /// is within 1e-10 of 0 (or not a number).
    /// </summary>
    /// <param name="m">The matrix.</param>
    /// <param name="inverse">The inverse.</param>
    /// <returns>Whether the matrix could be inverted.</returns>
    public static bool TryInvert(double[,] m, out double[,] inverse)
    {
        inverse = new double[3, 3];
        inverse[0, 0] = (m[1, 1] * m[2, 2]) - (m[1, 2] * m[2, 1]);
        inverse[0, 1] = (m[0, 2] * m[2, 1]) - (m[0, 1] * m[2, 2]);
        inverse[0, 2] = (m[0, 1] * m[1, 2]) - (m[0, 2] * m[1, 1]);
        inverse[1, 0] = (m[1, 2] * m[2, 0]) - (m[1, 0] * m[2, 2]);
        inverse[1, 1] = (m[0, 0] * m[2, 2]) - (m[0, 2] * m[2, 0]);
        inverse[1, 2] = (m[0, 2] * m[1, 0]) - (m[0, 0] * m[1, 2]);
        inverse[2, 0] = (m[1, 0] * m[2, 1]) - (m[1, 1] * m[2, 0]);
        inverse[2, 1] = (m[0, 1] * m[2, 0]) - (m[0, 0] * m[2, 1]);
        inverse[2, 2] = (m[0, 0] * m[1, 1]) - (m[0, 1] * m[1, 0]);
        double det = (m[0, 0] * inverse[0, 0]) + (m[0, 1] * inverse[1, 0]) + (m[0, 2] * inverse[2, 0]);
        if (!(Math.Abs(det) > 1e-10))
        {
            return false;
        }

        for (int j = 0; j < 3; j++)
        {
            for (int i = 0; i < 3; i++)
            {
                inverse[j, i] /= det;
            }
        }

        return true;
    }
}
