// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// TF_HLG_Base::DisplayFromEncoded, InvOETF and OOTF in lib/jxl/cms/transfer_functions.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl.Base;

/// <summary>
/// libjxl's scalar double-precision Hybrid Log-Gamma curve (ITU-R BT.2100), which its ICC code uses: the
/// table curve of an HLG profile, and the tone mapping of the 3D-LUT profile. The render pipeline uses
/// the float version instead (<c>TF_HLG</c>); the two are kept apart, as in libjxl.
/// </summary>
/// <remarks>
/// libjxl returns the double through <c>copysignf</c>, so every result is rounded to float; this keeps
/// that. Negative inputs are mirrored (f(-x) = -f(x)) and inputs above 1 extend the curve.
/// </remarks>
internal static class JxlTfHlgBase
{
    /// <summary>The HLG constant a.</summary>
    internal const double A = 0.17883277;

    /// <summary>The reciprocal of <see cref="A"/>.</summary>
    internal const double RA = 1.0 / A;

    /// <summary>The HLG constant b, 1 - 4a.</summary>
    internal const double B = 1 - (4 * A);

    /// <summary>The HLG constant c.</summary>
    internal const double C = 0.5599107295;

    /// <summary>1 / 12, where the OETF changes from the square root to the logarithm.</summary>
    internal const double Inv12 = 1.0 / 12.0;

    /// <summary>
    /// The HLG EOTF (as libjxl defines it, with no system gamma): the display value of an encoded value.
    /// </summary>
    /// <param name="e">The encoded value.</param>
    /// <returns>The display value, rounded to float.</returns>
    public static double DisplayFromEncoded(double e) => Ootf(InvOetf(e));

    // e = encoded, returns scene.
    private static double InvOetf(double e)
    {
        if (e == 0.0)
        {
            return 0.0;
        }

        double originalSign = e;
        e = Math.Abs(e);

        if (e <= 0.5)
        {
            return CopySignF(e * e * (1.0 / 3), originalSign);
        }

        double s = (Math.Exp((e - C) * RA) + B) * Inv12;
        return CopySignF(s, originalSign);
    }

    // s = scene, returns display.
    private static double Ootf(double s)
    {
        // The actual (red channel) OOTF is RD = alpha * YS^(gamma-1) * RS, where
        // YS = 0.2627 * RS + 0.6780 * GS + 0.0593 * BS. Let alpha = 1 so we return
        // "display" (normalized [0, 1]) instead of nits. Our transfer function
        // interface does not allow a dependency on YS. Fortunately, the system
        // gamma at 334 nits is 1.0, so this reduces to RD = RS.
        return s;
    }

    // C's copysignf on doubles: both are converted to float, so the result is a float.
    private static double CopySignF(double magnitude, double sign)
    {
        float m = Math.Abs((float)magnitude);
        return float.IsNegative((float)sign) ? -m : m;
    }
}
