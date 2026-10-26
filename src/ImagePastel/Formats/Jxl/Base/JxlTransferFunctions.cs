// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// TF_SRGB::DisplayFromEncoded, TF_SRGB::EncodedFromDisplay, TF_709::EncodedFromDisplay, TF_PQ (the scaling factors of its
// constructor, DisplayFromEncoded and EncodedFromDisplay) and TF_HLG::EncodedFromDisplay (the vector overloads) in
// lib/jxl/cms/transfer_functions-inl.h, kInvLog2e in lib/jxl/base/common.h, and the curve of OpGamma in
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
    private const float ThreshSrgbToLinear = 0.04045f;
    private const float LowDiv = 12.92f;
    private const float LowDivInv = 1.0f / LowDiv;

    // TF_709's constants are doubles that Set converts to float; each literal here is the same float.
    private const float Bt709Thresh = 0.018f;
    private const float Bt709MulLow = 4.5f;
    private const float Bt709MulHi = 1.099f;
    private const float Bt709PowHi = 0.45f;
    private const float Bt709Sub = -0.099f;

    // OpGamma's threshold, at or below which a sample becomes 0.
    private const float GammaThresh = 1e-5f;

    // TF_PQ::EncodedFromDisplay switches to the low polynomials below this sample, before scaling.
    private const float PqLowThresh = 1e-4f;

    // TF_HLG's own constant k3; the others it inherits from TF_HLG_Base (JxlTfHlgBase), as doubles.
    // kInvLog2e is a float.
    private const double Hlg3 = 3.0;
    private const float InvLog2e = 0.6931471805599453f;

    // Computed via af_cheb_rational (k=100).
    private static readonly float[] SrgbP = { -5.135152395e-04f, 5.287254571e-03f, 3.903842876e-01f, 1.474205315e+00f, 7.352629620e-01f };
    private static readonly float[] SrgbQ = { 1.004519624e-02f, 3.036675394e-01f, 1.340816930e+00f, 9.258482155e-01f, 2.424867759e-02f };

    // TF_SRGB::DisplayFromEncoded, computed via af_cheb_rational (k=100).
    private static readonly float[] SrgbToLinearP = { 2.200248328e-04f, 1.043637593e-02f, 1.624820318e-01f, 7.961564959e-01f, 8.210152774e-01f };
    private static readonly float[] SrgbToLinearQ = { 2.631846970e-01f, 1.076976492e+00f, 4.987528350e-01f, -5.512498495e-02f, 6.521209011e-03f };

    // TF_PQ::DisplayFromEncoded: 4-over-4-degree rational polynomial approximation on x+x*x. This improves
    // the maximum error by about 5x over a rational polynomial for x.
    private static readonly float[] PqToLinearP = { 2.62975656e-04f, -6.23553089e-03f, 7.38602301e-01f, 2.64553172e+00f, 5.50034862e-01f };
    private static readonly float[] PqToLinearQ = { 4.21350107e+02f, -4.28736818e+02f, 1.74364667e+02f, -3.39078883e+01f, 2.67718770e+00f };

    // TF_PQ::EncodedFromDisplay: 4-over-4-degree rational polynomial approximation on x**0.25, with two
    // different polynomials above and below 1e-4.
    private static readonly float[] PqP = { 1.351392e-02f, -1.095778e+00f, 5.522776e+01f, 1.492516e+02f, 4.838434e+01f };
    private static readonly float[] PqQ = { 1.012416e+00f, 2.016708e+01f, 9.263710e+01f, 1.120607e+02f, 2.590418e+01f };
    private static readonly float[] PqPLow = { 9.863406e-06f, 3.881234e-01f, 1.352821e+02f, 6.889862e+04f, -2.864824e+05f };
    private static readonly float[] PqQLow = { 3.371868e+01f, 1.477719e+03f, 1.608477e+04f, -4.389884e+04f, -2.072546e+05f };

    /// <summary>
    /// Decodes an sRGB-encoded sample to linear, as libjxl's <c>TF_SRGB::DisplayFromEncoded</c>, which
    /// the encoder's forward XYB uses. A negative sample is decoded as the negated decoding of its magnitude.
    /// </summary>
    /// <remarks>The rational polynomial is evaluated without FMA (ADR 0004).</remarks>
    /// <param name="v">The encoded sample.</param>
    /// <returns>The linear sample.</returns>
    public static float SrgbDisplayFromEncoded(float v)
    {
        float x = MathF.Abs(v);
        float magnitude = x > ThreshSrgbToLinear
            ? JxlFastMath.EvalRationalPolynomial(x, SrgbToLinearP, SrgbToLinearQ)
            : x * LowDivInv;

        return JxlScalarMath.CopySign(magnitude, v);
    }

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

        return JxlScalarMath.CopySign(magnitude, v);
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
    /// Returns the factor that maps a linear sample, where 1.0 is <paramref name="displayIntensityTarget"/>
    /// nits, to PQ's scale, where 1.0 is 10,000 nits: libjxl's <c>display_scaling_factor_to_10000_nits_</c>
    /// of <c>TF_PQ</c>.
    /// </summary>
    /// <param name="displayIntensityTarget">The intensity target, in nits.</param>
    /// <returns>The factor, a float product as in libjxl.</returns>
    public static float PqScaleTo10000Nits(float displayIntensityTarget) => displayIntensityTarget * (1.0f / 10000.0f);

    /// <summary>
    /// Returns the factor that maps a PQ display value, where 1.0 is 10,000 nits, to a linear sample where
    /// 1.0 is <paramref name="displayIntensityTarget"/> nits: libjxl's
    /// <c>display_scaling_factor_from_10000_nits_</c> of <c>TF_PQ</c>.
    /// </summary>
    /// <param name="displayIntensityTarget">The intensity target, in nits.</param>
    /// <returns>The factor, a float quotient as in libjxl.</returns>
    public static float PqScaleFrom10000Nits(float displayIntensityTarget) => 10000.0f / displayIntensityTarget;

    /// <summary>
    /// Decodes a PQ-encoded sample to linear, as libjxl's <c>TF_PQ::DisplayFromEncoded</c> (maximum error
    /// 3e-6), which its CMS applies to a PQ image before the colour transform. 0 gives 6.24e-7 of 10,000
    /// nits, the polynomial's value. A negative sample is decoded as the negated decoding of its magnitude.
    /// </summary>
    /// <remarks>The rational polynomial is evaluated without FMA (ADR 0004).</remarks>
    /// <param name="x">The encoded sample.</param>
    /// <param name="scaleFrom10000Nits">The factor from <see cref="PqScaleFrom10000Nits"/>.</param>
    /// <returns>The linear sample, where 1.0 is the intensity target.</returns>
    public static float PqDisplayFromEncoded(float x, float scaleFrom10000Nits)
    {
        float a = MathF.Abs(x);

        // libjxl: MulAdd(x, x, x).
        float xpxx = (a * a) + a;
        float magnitude = JxlFastMath.EvalRationalPolynomial(xpxx, PqToLinearP, PqToLinearQ);
        return JxlScalarMath.CopySign(magnitude * scaleFrom10000Nits, x);
    }

    /// <summary>
    /// Encodes a linear sample with the PQ curve, as libjxl's <c>TF_PQ</c> (not <c>TF_PQ_Base</c>, which
    /// the ICC profile uses), with a maximum error of about 7e-7. 0 gives 2.9e-7, the low polynomial's
    /// value. A negative sample is encoded as the negated encoding of its magnitude.
    /// </summary>
    /// <param name="x">The linear sample.</param>
    /// <param name="scaleTo10000Nits">The factor from <see cref="PqScaleTo10000Nits"/>.</param>
    /// <returns>The encoded sample.</returns>
    public static float PqEncodedFromDisplay(float x, float scaleTo10000Nits)
    {
        float a = MathF.Abs(x);
        float xto025 = MathF.Sqrt(MathF.Sqrt(a * scaleTo10000Nits));

        // libjxl compares the sample before it is scaled.
        float magnitude = a < PqLowThresh
            ? JxlFastMath.EvalRationalPolynomial(xto025, PqPLow, PqQLow)
            : JxlFastMath.EvalRationalPolynomial(xto025, PqP, PqQ);

        return JxlScalarMath.CopySign(magnitude, x);
    }

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
        if (a <= (float)JxlTfHlgBase.Inv12)
        {
            magnitude = MathF.Sqrt((float)Hlg3 * a);
        }
        else
        {
            // libjxl: MulAdd(Set(d, kA * kInvLog2e), FastLog2f(d, MulAdd(Set(d, 12), x, Set(d, -kB))),
            // Set(d, kC)). kA * kInvLog2e is a double product that Set rounds to float.
            float log = JxlFastMath.FastLog2f((12f * a) + (float)-JxlTfHlgBase.B);
            magnitude = ((float)(JxlTfHlgBase.A * InvLog2e) * log) + (float)JxlTfHlgBase.C;
        }

        return JxlScalarMath.CopySign(magnitude, x);
    }
}
