// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_xyb.cc (OpsinAbsorbance, StoreXYB, LinearRGBToXYB, LinearRGBRowToXYB, SRGBToXYB and
// ComputePremulAbsorb), lib/jxl/base/fast_math-inl.h (CubeRootAndAdd) and lib/jxl/cms/opsin_params.h
// (kOpsinAbsorbanceMatrix, kOpsinAbsorbanceBias), for one lane.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl.Base;

/// <summary>
/// The encoder's forward XYB: linear RGB through the opsin absorbance matrix and its bias, a cube root,
/// then X = (r - g) / 2, Y = (r + g) / 2 and B = b.
/// </summary>
/// <remarks>
/// A deliberate departure from libjxl (ADR 0004): libjxl's <c>MulAdd</c> and <c>NegMulAdd</c> are fused on
/// AVX2, and here each multiply and add is rounded on its own, in libjxl's nesting order. So the XYB values
/// can differ from cjxl's in the last bits, and our bytes from cjxl's.
/// </remarks>
internal static class JxlXybEncoder
{
    /// <summary>The number of values <see cref="ComputePremulAbsorb"/> gives: the 3x3 matrix, then the 3 negated bias cube roots.</summary>
    public const int PremulAbsorbSize = 12;

    // Parameters for opsin absorbance; each difference is a float, as in libjxl's constexpr.
    private const float M02 = 0.078f;
    private const float M00 = 0.30f;
    private const float M12 = 0.078f;
    private const float M10 = 0.23f;
    private const float M20 = 0.24342268924547819f;
    private const float M21 = 0.20476744424496821f;
    private const float OpsinAbsorbanceBias = 0.0037930732552754493f;

    // cbrtf(OpsinAbsorbanceBias), correctly rounded. libjxl calls cbrtf on the constant; a literal keeps the
    // encoder's output the same on every platform, whatever its C runtime's cube root gives (ADR 0004).
    private const float OpsinAbsorbanceBiasCbrt = 0.15595419704914093f;

    private static readonly float M01 = 1.0f - M02 - M00;
    private static readonly float M11 = 1.0f - M12 - M10;
    private static readonly float M22 = 1.0f - M20 - M21;

    // Opsin absorbance matrix is now frozen.
    private static readonly float[] OpsinAbsorbanceMatrix = { M00, M01, M02, M10, M11, M12, M20, M21, M22 };

    /// <summary>
    /// Returns libjxl's <c>premul_absorb</c>: the opsin absorbance matrix scaled by
    /// <paramref name="intensityTarget"/> / 255, then the negated cube roots of the biases.
    /// </summary>
    /// <param name="intensityTarget">The intensity target of the image, in nits.</param>
    /// <returns>The <see cref="PremulAbsorbSize"/> values.</returns>
    public static float[] ComputePremulAbsorb(float intensityTarget)
    {
        float[] premulAbsorb = new float[PremulAbsorbSize];
        float mul = intensityTarget / 255.0f;
        for (int j = 0; j < 3; ++j)
        {
            for (int i = 0; i < 3; ++i)
            {
                premulAbsorb[(j * 3) + i] = OpsinAbsorbanceMatrix[(j * 3) + i] * mul;
            }
        }

        for (int i = 0; i < 3; ++i)
        {
            premulAbsorb[9 + i] = -OpsinAbsorbanceBiasCbrt;
        }

        return premulAbsorb;
    }

    /// <summary>
    /// Converts rows of sRGB-encoded samples, nominally in [0, 1], to X, Y and B in place, as libjxl's
    /// <c>SRGBToXYB</c>.
    /// </summary>
    /// <param name="row0">The red samples, then X.</param>
    /// <param name="row1">The green samples, then Y.</param>
    /// <param name="row2">The blue samples, then B.</param>
    /// <param name="premulAbsorb">The values of <see cref="ComputePremulAbsorb"/>.</param>
    public static void SrgbRowToXyb(Span<float> row0, Span<float> row1, Span<float> row2, ReadOnlySpan<float> premulAbsorb)
    {
        for (int x = 0; x < row0.Length; x++)
        {
            float inR = JxlTransferFunctions.SrgbDisplayFromEncoded(row0[x]);
            float inG = JxlTransferFunctions.SrgbDisplayFromEncoded(row1[x]);
            float inB = JxlTransferFunctions.SrgbDisplayFromEncoded(row2[x]);
            LinearRgbToXyb(inR, inG, inB, premulAbsorb, out row0[x], out row1[x], out row2[x]);
        }
    }

    /// <summary>
    /// Converts rows of linear RGB samples to X, Y and B in place, as libjxl's <c>LinearRGBRowToXYB</c>.
    /// </summary>
    /// <param name="row0">The red samples, then X.</param>
    /// <param name="row1">The green samples, then Y.</param>
    /// <param name="row2">The blue samples, then B.</param>
    /// <param name="premulAbsorb">The values of <see cref="ComputePremulAbsorb"/>.</param>
    public static void LinearRgbRowToXyb(Span<float> row0, Span<float> row1, Span<float> row2, ReadOnlySpan<float> premulAbsorb)
    {
        for (int x = 0; x < row0.Length; x++)
        {
            LinearRgbToXyb(row0[x], row1[x], row2[x], premulAbsorb, out row0[x], out row1[x], out row2[x]);
        }
    }

    /// <summary>Converts one linear RGB sample to XYB, as libjxl's <c>LinearRGBToXYB</c>.</summary>
    /// <param name="r">The linear red sample.</param>
    /// <param name="g">The linear green sample.</param>
    /// <param name="b">The linear blue sample.</param>
    /// <param name="premulAbsorb">The values of <see cref="ComputePremulAbsorb"/>.</param>
    /// <param name="valx">The X value.</param>
    /// <param name="valy">The Y value.</param>
    /// <param name="valz">The B value.</param>
    public static void LinearRgbToXyb(float r, float g, float b, ReadOnlySpan<float> premulAbsorb, out float valx, out float valy, out float valz)
    {
        // OpsinAbsorbance: MulAdd(m0, r, MulAdd(m1, g, MulAdd(m2, b, bias))), not fused.
        float mixed0 = (premulAbsorb[0] * r) + ((premulAbsorb[1] * g) + ((premulAbsorb[2] * b) + OpsinAbsorbanceBias));
        float mixed1 = (premulAbsorb[3] * r) + ((premulAbsorb[4] * g) + ((premulAbsorb[5] * b) + OpsinAbsorbanceBias));
        float mixed2 = (premulAbsorb[6] * r) + ((premulAbsorb[7] * g) + ((premulAbsorb[8] * b) + OpsinAbsorbanceBias));

        // mixed* should be non-negative even for wide-gamut, so clamp to zero.
        mixed0 = ZeroIfNegative(mixed0);
        mixed1 = ZeroIfNegative(mixed1);
        mixed2 = ZeroIfNegative(mixed2);

        mixed0 = CubeRootAndAdd(mixed0, premulAbsorb[9]);
        mixed1 = CubeRootAndAdd(mixed1, premulAbsorb[10]);
        mixed2 = CubeRootAndAdd(mixed2, premulAbsorb[11]);

        // StoreXYB.
        valx = 0.5f * (mixed0 - mixed1);
        valy = 0.5f * (mixed0 + mixed1);
        valz = mixed2;
    }

    /// <summary>
    /// Returns cbrt(x) + add with 6 ulp max error, for x that is not negative, as libjxl's
    /// <c>CubeRootAndAdd</c>: an exponent estimate and Newton-Raphson iterations, not a library cube root.
    /// </summary>
    /// <param name="x">The value, not negative.</param>
    /// <param name="add">The value to add.</param>
    /// <returns>The cube root plus <paramref name="add"/>.</returns>
    public static float CubeRootAndAdd(float x, float add)
    {
        const int expBias = 0x54800000; // cast(1.) + cast(1.) / 3
        const int expMul = 0x002AAAAA; // shifted 1/3
        const float k13 = 1.0f / 3;
        const float k43 = 4.0f / 3;

        float xa = x; // assume inputs never negative
        float xa3 = k13 * xa;

        // Multiply exponent by -1/3. 0 is represented with an exponent of 0, so "expBias - 1/3 * exp"
        // would give the wrong result; it is set to 0, which prevents NaNs below.
        int m1 = BitConverter.SingleToInt32Bits(xa);
        int m2 = m1 == 0 ? 0 : expBias - ((m1 >> 23) * expMul);
        float r = BitConverter.Int32BitsToSingle(m2);

        // Newton-Raphson iterations. libjxl: NegMulAdd(xa_3, r2 * r2, k4_3 * r), not fused.
        for (int i = 0; i < 3; i++)
        {
            float r2 = r * r;
            r = (k43 * r) - (xa3 * (r2 * r2));
        }

        // Final iteration. libjxl: MulAdd(k1_3, NegMulAdd(xa, r2 * r2, r), r), then MulAdd(r2, x, add).
        float rr = r * r;
        r = (k13 * (r - (xa * (rr * rr)))) + r;
        rr = r * r;
        return (rr * x) + add;
    }

    private static float ZeroIfNegative(float v) => v < 0 ? 0 : v;
}
