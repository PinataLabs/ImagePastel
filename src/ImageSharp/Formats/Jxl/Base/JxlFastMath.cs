// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/base/fast_math-inl.h (FastLog2f) and lib/jxl/base/rational_polynomial-inl.h (EvalRationalPolynomial).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Formats.Jxl.Base;

/// <summary>
/// libjxl's fast approximations of maths functions, as scalars. libjxl evaluates them with SIMD
/// <c>MulAdd</c>, which can be a fused multiply-add; this port multiplies and adds separately
/// (ADR 0004), so results can differ from libjxl's in the last bit.
/// </summary>
internal static class JxlFastMath
{
    /// <summary>
    /// Computes the base-2 logarithm like <c>std::log2</c>, with an L1 error of about 3.9E-6.
    /// Undefined if negative or NaN.
    /// </summary>
    /// <param name="x">The value.</param>
    /// <returns>The approximate base-2 logarithm.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static float FastLog2f(float x)
    {
        // 2,2 rational polynomial approximation of std::log1p(x) / std::log(2).
        const float p0 = -1.8503833400518310E-06f;
        const float p1 = 1.4287160470083755E+00f;
        const float p2 = 7.4245873327820566E-01f;
        const float q0 = 9.9032814277590719E-01f;
        const float q1 = 1.0096718572241148E+00f;
        const float q2 = 1.7409343003366853E-01f;

        int xBits = BitConverter.SingleToInt32Bits(x);

        // Range reduction to [-1/3, 1/3] - 3 integer, 2 float ops
        int expBits = xBits - 0x3f2aaaab; // = 2/3

        // Shifted exponent = log2; also used to clear mantissa.
        int expShifted = expBits >> 23;
        float mantissa = BitConverter.Int32BitsToSingle(xBits - (expShifted << 23));
        float expVal = expShifted;

        // EvalRationalPolynomial: Horner's scheme on both polynomials, then a division.
        float m = mantissa - 1.0f;
        float yp = (((p2 * m) + p1) * m) + p0;
        float yq = (((q2 * m) + q1) * m) + q0;
        return (yp / yq) + expVal;
    }
}
