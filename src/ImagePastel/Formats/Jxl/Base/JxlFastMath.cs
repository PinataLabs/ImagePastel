// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/base/fast_math-inl.h (FastLog2f, FastPow2f, FastPowf, FastCosf, FastErff),
// lib/jxl/base/rational_polynomial-inl.h (EvalRationalPolynomial) and FastLinearToSRGB from
// lib/jxl/cms/transfer_functions-inl.h, for one lane.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Formats.Jxl.Base;

/// <summary>
/// libjxl's fast approximations of the maths functions, in single precision.
/// </summary>
/// <remarks>
/// A deliberate departure from libjxl (ADR 0004): libjxl's Highway code uses <c>MulAdd</c>, a fused
/// multiply-add on AVX2, and this port has no FMA in scalar code. Each <c>MulAdd(a, b, c)</c> is
/// <c>a * b + c</c> with two roundings, and each <c>NegMulAdd(a, b, c)</c> is <c>c - a * b</c>, so the
/// results can differ from djxl's in the last bits. The documented errors still hold; the unit tests
/// measure them.
/// </remarks>
internal static class JxlFastMath
{
    // kPi of lib/jxl/base/common.h, a double.
    private const double Pi = 3.14159265358979323846264338327950288;

    // 2,2 rational polynomial approximation of std::log1p(x) / std::log(2).
    private static readonly float[] Log2P = { -1.8503833400518310E-06f, 1.4287160470083755E+00f, 7.4245873327820566E-01f };
    private static readonly float[] Log2Q = { 9.9032814277590719E-01f, 1.0096718572241148E+00f, 1.7409343003366853E-01f };

    // The powers of 2^(1/2.4) = 2^(5/12) times two, in bits 25 to 18 and 17 to 10 of the float; bits 26
    // to 31 are always the same (FastLinearToSRGB).
    private const uint K2to512PowersBaseBits = 0x40000000;

    private static ReadOnlySpan<byte> K2to512Powers25to18Bits => new byte[]
    {
        0x0, 0xa, 0x19, 0x26, 0x32, 0x41, 0x4d, 0x5c,
        0x68, 0x75, 0x83, 0x8f, 0xa0, 0xaa, 0xb9, 0xc6,
    };

    private static ReadOnlySpan<byte> K2to512Powers17to10Bits => new byte[]
    {
        0x0, 0xb7, 0x4, 0xd, 0xcb, 0xe7, 0x41, 0x68,
        0x51, 0xd1, 0xeb, 0xf2, 0x0, 0xb7, 0x4, 0xd,
    };

    /// <summary>
    /// Evaluates the rational polynomial p(x) / q(x) by Horner's scheme. The coefficients are in
    /// increasing order of degree, and each polynomial has degree at most 7.
    /// </summary>
    /// <param name="x">The argument.</param>
    /// <param name="p">The coefficients of the numerator.</param>
    /// <param name="q">The coefficients of the denominator.</param>
    /// <returns>p(x) / q(x).</returns>
    public static float EvalRationalPolynomial(float x, ReadOnlySpan<float> p, ReadOnlySpan<float> q)
    {
        float yp = p[p.Length - 1];
        for (int i = p.Length - 2; i >= 0; i--)
        {
            yp = (yp * x) + p[i];
        }

        float yq = q[q.Length - 1];
        for (int i = q.Length - 2; i >= 0; i--)
        {
            yq = (yq * x) + q[i];
        }

        // libjxl's FastDivision divides exactly ("Faster on SKX").
        return yp / yq;
    }

    /// <summary>
    /// Computes the base-2 logarithm, like <c>std::log2</c>, with an absolute error of about 3.9e-6.
    /// Undefined if <paramref name="x"/> is negative or NaN.
    /// </summary>
    /// <param name="x">The argument.</param>
    /// <returns>log2(x).</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static float FastLog2f(float x)
    {
        int xBits = BitConverter.SingleToInt32Bits(x);

        // Range reduction to [-1/3, 1/3] - 3 integer, 2 float ops
        int expBits = unchecked(xBits - 0x3f2aaaab); // = 2/3

        // Shifted exponent = log2; also used to clear mantissa.
        int expShifted = expBits >> 23;
        float mantissa = BitConverter.Int32BitsToSingle(unchecked(xBits - (expShifted << 23)));
        float expVal = expShifted;
        return EvalRationalPolynomial(mantissa - 1.0f, Log2P, Log2Q) + expVal;
    }

    /// <summary>Computes 2 to the power <paramref name="x"/>, with a relative error of about 3e-7.</summary>
    /// <param name="x">The exponent.</param>
    /// <returns>2^x.</returns>
    public static float FastPow2f(float x)
    {
        float floorx = MathF.Floor(x);
        float exp = BitConverter.Int32BitsToSingle(unchecked(((int)floorx + 127) << 23));
        float frac = x - floorx;
        float num = frac + 1.01749063e+01f;
        num = (num * frac) + 4.88687798e+01f;
        num = (num * frac) + 9.85506591e+01f;
        num *= exp;
        float den = (frac * 2.10242958e-01f) + -2.22328856e-02f;
        den = (den * frac) + -1.94414990e+01f;
        den = (den * frac) + 9.85506633e+01f;
        return num / den;
    }

    /// <summary>
    /// Computes <paramref name="b"/> to the power <paramref name="e"/>, with a relative error of about 3e-5.
    /// </summary>
    /// <param name="b">The base, which must be positive.</param>
    /// <param name="e">The exponent.</param>
    /// <returns>b^e.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static float FastPowf(float b, float e) => FastPow2f(FastLog2f(b) * e);

    /// <summary>Computes the cosine, like <c>std::cos</c>, with an absolute error of 7e-5.</summary>
    /// <param name="x">The angle, in radians.</param>
    /// <returns>cos(x).</returns>
    public static float FastCosf(float x)
    {
        // Step 1: range reduction to [0, 2pi)
        const float pi2 = (float)(Pi * 2.0f);
        const float pi2Inv = (float)(0.5f / Pi);
        float npi2 = MathF.Floor(x * pi2Inv) * pi2;
        float xmodpi2 = x - npi2;

        // Step 2: range reduction to [0, pi]
        float xPi = MathF.Min(xmodpi2, pi2 - xmodpi2);

        // Step 3: range reduction to [0, pi/2]
        bool abovePihalf = xPi >= (float)(Pi / 2.0f);
        float xPihalf = abovePihalf ? (float)Pi - xPi : xPi;

        // Step 4: Taylor-like approximation, scaled by 2**0.75 to make angle
        // duplication steps faster, on x/4.
        float xs = xPihalf * 0.25f;
        float x2 = xs * xs;
        float x4 = x2 * x2;
        float cosxPrescaling = (x4 * 0.06960438f) + ((x2 * -0.84087373f) + 1.68179268f);

        // Step 5: angle duplication.
        float cosxScale1 = (cosxPrescaling * cosxPrescaling) + -1.414213562f;
        float cosxScale2 = (cosxScale1 * cosxScale1) + -1;

        // Step 6: change sign if needed.
        return abovePihalf ? -cosxScale2 : cosxScale2;
    }

    /// <summary>Computes the error function, like <c>std::erf</c>, with an absolute error of 7e-4.</summary>
    /// <param name="x">The argument.</param>
    /// <returns>erf(x).</returns>
    public static float FastErff(float x)
    {
        // Formula from
        // https://en.wikipedia.org/wiki/Error_function#Numerical_approximations
        // but constants have been recomputed.
        bool xle0 = x <= 0;
        float absx = MathF.Abs(x);

        // Compute 1 - 1 / ((((x * a + b) * x + c) * x + d) * x + 1)**4
        float denom1 = (absx * 7.77394369e-02f) + 2.05260015e-04f;
        float denom2 = (denom1 * absx) + 2.32120216e-01f;
        float denom3 = (denom2 * absx) + 2.77820801e-01f;
        float denom4 = (denom3 * absx) + 1.0f;
        float denom5 = denom4 * denom4;
        float invDenom5 = 1.0f / denom5;
        float result = 1.0f - (invDenom5 * invDenom5);

        // Change sign if needed.
        return xle0 ? -result : result;
    }

    /// <summary>
    /// Converts a linear sample to the sRGB encoding, with an absolute error of at most 1.2e-4 on [0, 1].
    /// </summary>
    /// <param name="v">The linear sample.</param>
    /// <returns>The sRGB-encoded sample.</returns>
    public static float FastLinearToSrgb(float v)
    {
        // Convert to 0.25 - 0.5 range.
        uint vBits = unchecked((uint)BitConverter.SingleToInt32Bits(v));
        float v025To05 = BitConverter.Int32BitsToSingle(unchecked((int)((vBits | 0x3e800000) & 0x3effffff)));

        // third degree polynomial approximation between 0.25 and 0.5
        // of 1.055/2^(7/2.4) * x^(1/2.4) * 0.5. A degree 4 polynomial only improves
        // accuracy by about 3x.
        float d1 = (v025To05 * 0.059914046f) + -0.108894556f;
        float d2 = (d1 * v025To05) + 0.107963754f;
        float pow = (d2 * v025To05) + 0.018092343f;

        // Compute extra multiplier depending on exponent. Valid exponent range for
        // [0.0031308f, 1.0) is 0...8 after subtracting 118.
        // libjxl's SIMD path looks the multiplier up with TableLookupBytes, which on x86 gives 0 for an
        // index byte with its top bit set and otherwise takes the low four bits; djxl runs that path, so
        // this port does too. Its scalar fallback takes the low four bits only. Both differ only where the
        // linear segment is taken, or for samples of 2^119 and more.
        int exp = (BitConverter.SingleToInt32Bits(v) >> 23) - 118;
        uint index = unchecked((uint)exp) & 0xFF;
        uint bits25to18 = (index & 0x80) != 0 ? 0u : K2to512Powers25to18Bits[(int)(index & 0xF)];
        uint bits17to10 = (index & 0x80) != 0 ? 0u : K2to512Powers17to10Bits[(int)(index & 0xF)];
        float mul = BitConverter.Int32BitsToSingle(unchecked((int)((bits25to18 << 18) | (bits17to10 << 10) | K2to512PowersBaseBits)));
        return v < 0.0031308f ? v * 12.92f : (pow * mul) + -0.055f;
    }
}
