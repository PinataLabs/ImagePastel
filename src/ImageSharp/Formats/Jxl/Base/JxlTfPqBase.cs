// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// TF_PQ_Base::DisplayFromEncoded and EncodedFromDisplay in lib/jxl/cms/transfer_functions.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl.Base;

/// <summary>
/// libjxl's scalar double-precision Perceptual Quantization curve (SMPTE ST 2084), which its ICC code
/// uses: the table curve of a PQ profile, and the tone mapping of the 3D-LUT profile. The render pipeline
/// uses the float rational-polynomial version instead (<c>TF_PQ</c>); the two are kept apart, as in libjxl.
/// </summary>
/// <remarks>
/// libjxl returns the double through <c>copysignf</c>, so every result is rounded to float; this keeps
/// that. Negative inputs are mirrored (f(-x) = -f(x)) and inputs above 1 extend the curve, as libjxl's
/// unbounded transfer functions do.
/// </remarks>
internal static class JxlTfPqBase
{
    private const double M1 = 2610.0 / 16384;
    private const double M2 = 2523.0 / 4096 * 128;
    private const double C1 = 3424.0 / 4096;
    private const double C2 = 2413.0 / 4096 * 32;
    private const double C3 = 2392.0 / 4096 * 32;

    /// <summary>
    /// The PQ EOTF: the display luminance of an encoded value, where 1 is
    /// <paramref name="displayIntensityTarget"/> nits.
    /// </summary>
    /// <param name="displayIntensityTarget">The luminance, in nits, that the result 1 stands for.</param>
    /// <param name="e">The encoded value.</param>
    /// <returns>The display value, rounded to float.</returns>
    public static double DisplayFromEncoded(float displayIntensityTarget, double e)
    {
        if (e == 0.0)
        {
            return 0.0;
        }

        double originalSign = e;
        e = Math.Abs(e);

        double xp = Math.Pow(e, 1.0 / M2);
        double num = Math.Max(xp - C1, 0.0);
        double den = C2 - (C3 * xp);
        double d = Math.Pow(num / den, 1.0 / M1);

        // 10000.0f / display_intensity_target is a float division; the product is double.
        return JxlScalarMath.CopySign((float)(d * (10000.0f / displayIntensityTarget)), (float)originalSign);
    }

    /// <summary>
    /// The inverse PQ EOTF: the encoded value of a display luminance, where 1 is
    /// <paramref name="displayIntensityTarget"/> nits.
    /// </summary>
    /// <param name="displayIntensityTarget">The luminance, in nits, that the display value 1 stands for.</param>
    /// <param name="d">The display value.</param>
    /// <returns>The encoded value, rounded to float.</returns>
    public static double EncodedFromDisplay(float displayIntensityTarget, double d)
    {
        if (d == 0.0)
        {
            return 0.0;
        }

        double originalSign = d;
        d = Math.Abs(d);

        // display_intensity_target * (1.0f / 10000.0f) is a float product; the rest is double.
        double xp = Math.Pow(d * (displayIntensityTarget * (1.0f / 10000.0f)), M1);
        double num = C1 + (xp * C2);
        double den = 1.0 + (xp * C3);
        double e = Math.Pow(num / den, M2);
        return JxlScalarMath.CopySign((float)e, (float)originalSign);
    }
}
