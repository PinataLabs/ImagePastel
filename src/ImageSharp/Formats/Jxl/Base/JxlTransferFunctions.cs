// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// TF_SRGB::EncodedFromDisplay (the vector overload) in lib/jxl/cms/transfer_functions-inl.h, for one lane.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl.Base;

/// <summary>
/// libjxl's transfer functions, as the render pipeline applies them. Only the sRGB encoding is ported
/// so far; the others come with the transfer functions slice (D11).
/// </summary>
internal static class JxlTransferFunctions
{
    private const float ThreshLinearToSrgb = 0.0031308f;
    private const float LowDiv = 12.92f;

    // Computed via af_cheb_rational (k=100).
    private static readonly float[] SrgbP = { -5.135152395e-04f, 5.287254571e-03f, 3.903842876e-01f, 1.474205315e+00f, 7.352629620e-01f };
    private static readonly float[] SrgbQ = { 1.004519624e-02f, 3.036675394e-01f, 1.340816930e+00f, 9.258482155e-01f, 2.424867759e-02f };

    /// <summary>
    /// Encodes a linear sample with the sRGB curve, with an error of about 5e-7. A negative sample is
    /// encoded as the negated encoding of its magnitude.
    /// </summary>
    /// <remarks>
    /// libjxl's from-linear stage uses this, not <see cref="JxlFastMath.FastLinearToSrgb"/>, because
    /// libjxl builds with <c>JXL_HIGH_PRECISION</c> set by default (lib/jxl/common.h). The rational
    /// polynomial is evaluated without FMA (ADR 0004).
    /// </remarks>
    /// <param name="v">The linear sample.</param>
    /// <returns>The encoded sample.</returns>
    public static float SrgbEncodedFromDisplay(float v)
    {
        float x = MathF.Abs(v);
        float magnitude = x > ThreshLinearToSrgb
            ? JxlFastMath.EvalRationalPolynomial(MathF.Sqrt(x), SrgbP, SrgbQ)
            : x * LowDiv;

        // Or(AndNot(kSign, magnitude), original_sign): the magnitude with the sign of v.
        int bits = (BitConverter.SingleToInt32Bits(magnitude) & 0x7fffffff) | (BitConverter.SingleToInt32Bits(v) & unchecked((int)0x80000000));
        return BitConverter.Int32BitsToSingle(bits);
    }
}
