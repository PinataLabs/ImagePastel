// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// TF_SRGB::EncodedFromDisplay, TF_709::EncodedFromDisplay and TF_HLG::EncodedFromDisplay (the vector
// overloads) in lib/jxl/cms/transfer_functions-inl.h, TF_HLG_Base's constants in
// lib/jxl/cms/transfer_functions.h, kInvLog2e in lib/jxl/base/common.h, and the curve of OpGamma in
// lib/jxl/render_pipeline/stage_from_linear.cc, for one lane.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl.Base;

/// <summary>
/// libjxl's transfer functions, as the render pipeline applies them.
/// </summary>
/// <remarks>
/// A deliberate departure from libjxl (ADR 0004): each <c>MulAdd</c> is split into a multiply and an add,
/// and <see cref="JxlFastMath"/> stands for libjxl's fast maths. libjxl computes both branches of a curve
/// and selects one; this port computes only the chosen branch, which gives the same value.
/// </remarks>
internal static class JxlTransferFunctions
{
    private const float ThreshLinearToSrgb = 0.0031308f;
    private const float LowDiv = 12.92f;

    // TF_709's constants are doubles that Set converts to float; each literal here is the same float.
    private const float Bt709Thresh = 0.018f;
    private const float Bt709MulLow = 4.5f;
    private const float Bt709MulHi = 1.099f;
    private const float Bt709PowHi = 0.45f;
    private const float Bt709Sub = -0.099f;

    // OpGamma's threshold, at or below which a sample becomes 0.
    private const float GammaThresh = 1e-5f;

    // TF_HLG_Base's constants, which TF_HLG inherits, are doubles; kInvLog2e is a float.
    private const double HlgA = 0.17883277;
    private const double HlgB = 1 - (4 * HlgA);
    private const double HlgC = 0.5599107295;
    private const double HlgInv12 = 1.0 / 12.0;
    private const double Hlg3 = 3.0;
    private const float InvLog2e = 0.6931471805599453f;

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

        return WithSignOf(magnitude, v);
    }

    /// <summary>
    /// Encodes a linear sample with the BT.709 curve, as libjxl's <c>TF_709</c>, with a maximum error of
    /// about 1e-6 from the fast power. A sample at or below 0.018, negative ones too, takes the linear segment.
    /// </summary>
    /// <param name="x">The linear sample.</param>
    /// <returns>The encoded sample.</returns>
    public static float Bt709EncodedFromDisplay(float x)
    {
        if (x <= Bt709Thresh)
        {
            return Bt709MulLow * x;
        }

        // libjxl: MulAdd(Set(d, kMulHi), FastPowf(d, x, Set(d, kPowHi)), Set(d, kSub)).
        return (Bt709MulHi * JxlFastMath.FastPowf(x, Bt709PowHi)) + Bt709Sub;
    }

    /// <summary>
    /// Encodes a linear sample with a gamma curve, as libjxl's <c>OpGamma</c>: a sample at or below 1e-5,
    /// negative ones too, becomes 0, and the others are raised to <paramref name="inverseGamma"/> with the
    /// fast power.
    /// </summary>
    /// <param name="v">The linear sample.</param>
    /// <param name="inverseGamma">The encoding exponent, libjxl's <c>inverse_gamma</c> (for example 1 / 2.6 for DCI).</param>
    /// <returns>The encoded sample.</returns>
    public static float GammaEncodedFromDisplay(float v, float inverseGamma)
        => v <= GammaThresh ? 0f : JxlFastMath.FastPowf(v, inverseGamma);

    /// <summary>
    /// Encodes a linear sample with the HLG curve (BT.2100's OETF, with scene light normalised to [0, 1]),
    /// as libjxl's <c>TF_HLG</c>, with a maximum error of about 5e-7. A sample at or below 1/12 takes the
    /// square-root segment, and a negative sample is encoded as the negated encoding of its magnitude.
    /// </summary>
    /// <param name="x">The linear (scene-light) sample.</param>
    /// <returns>The encoded sample.</returns>
    public static float HlgEncodedFromDisplay(float x)
    {
        float a = MathF.Abs(x);
        float magnitude;
        if (a <= (float)HlgInv12)
        {
            magnitude = MathF.Sqrt((float)Hlg3 * a);
        }
        else
        {
            // libjxl: MulAdd(Set(d, kA * kInvLog2e), FastLog2f(d, MulAdd(Set(d, 12), x, Set(d, -kB))),
            // Set(d, kC)). kA * kInvLog2e is a double product that Set rounds to float.
            float log = JxlFastMath.FastLog2f((12f * a) + (float)-HlgB);
            magnitude = ((float)(HlgA * InvLog2e) * log) + (float)HlgC;
        }

        return WithSignOf(magnitude, x);
    }

    // Or(AndNot(kSign, magnitude), original_sign): the magnitude with the sign of v.
    private static float WithSignOf(float magnitude, float v)
    {
        int bits = (BitConverter.SingleToInt32Bits(magnitude) & 0x7fffffff) | (BitConverter.SingleToInt32Bits(v) & unchecked((int)0x80000000));
        return BitConverter.Int32BitsToSingle(bits);
    }
}
