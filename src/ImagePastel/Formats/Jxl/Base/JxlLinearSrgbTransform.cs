// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// the colour transform of ToXYB in lib/jxl/enc_xyb.cc (ApplyColorTransform to LinearSRGB(is_gray)), and of
// the CMS it calls in lib/jxl/cms/jxl_cms.cc: BeforeTransform (PQ scaled by the intensity target, HLG) and
// ApplyHlgOotf (forward), with the primaries matrix through XYZ (D50, Bradford) of the profiles that
// MaybeCreateProfile builds; and the TF_709 constants of lib/jxl/cms/transfer_functions-inl.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.ColorManagement.Icc;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;

namespace PinataLabs.ImagePastel.Formats.Jxl.Base;

/// <summary>
/// Takes samples of a colour encoding to linear sRGB (linear grey for a grey encoding), the input of the
/// encoder's forward XYB: the transfer function is undone, then the primaries are converted through XYZ
/// adapted to D50.
/// </summary>
/// <remarks>
/// Departure (ADR 0001): libjxl converts with its CMS (skcms or lcms) through ICC profiles. This library has
/// no CMS (#105, #115), so the conversion is analytic, from the encoding's fields:
/// <list type="bullet">
/// <item><description>
/// PQ and HLG are undone as the CMS's <c>BeforeTransform</c> undoes them, ported as libjxl has them: PQ
/// with <c>TF_PQ</c>'s rational polynomial (maximum error 3e-6), scaled by the intensity target, and HLG
/// with <c>TF_HLG_Base</c> in double precision and then the OOTF to the intensity target
/// (<c>ApplyHlgOotf</c>). The other curves (gamma, BT.709 and DCI's gamma 2.6) are the exact formulas in
/// double precision, where the CMS evaluates the profile's curve. Negative samples and samples above 1
/// extend the curve, mirrored.
/// </description></item>
/// <item><description>
/// The primaries matrix is the one the decoder's XYB stage inverts (<c>AdaptToPrimaries</c>), from the
/// enum's primaries and white point, not from the profile's colorants, which recognition snapped to the
/// enum within 1e-3. A grey encoding has no matrix: grey to grey keeps the white (relative colorimetric).
/// </description></item>
/// </list>
/// The sRGB and linear sRGB encodings do not come here: the encoder takes them straight to XYB, as libjxl does.
/// </remarks>
internal sealed class JxlLinearSrgbTransform
{
    // TF_709::DisplayFromEncoded.
    private const double Bt709InvThresh = 0.081;
    private const double Bt709InvMulLow = 1 / 4.5;
    private const double Bt709InvMulHi = 1 / 1.099;
    private const double Bt709InvPowHi = 1 / 0.45;
    private const double Bt709InvAdd = 0.099 * Bt709InvMulHi;

    private readonly JxlCustomTransferFunction tf;
    private readonly float pqScale;
    private readonly double gammaExponent;

    // The matrix from linear samples with the encoding's primaries to linear sRGB, row-major, or null for
    // sRGB primaries with a D65 white and for grey.
    private readonly float[,] toLinearSrgb;

    // HLG: whether to apply the OOTF, its gamma, and the luminances of the primaries (null for grey).
    private readonly bool applyHlgOotf;
    private readonly float hlgGamma;
    private readonly float[] hlgLuminances;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlLinearSrgbTransform"/> class.
    /// </summary>
    /// <param name="c">An RGB or grey colour encoding with a known transfer function or a gamma.</param>
    /// <param name="intensityTarget">The intensity target of the image, in nits.</param>
    public JxlLinearSrgbTransform(JxlColorEncodingHeader c, float intensityTarget)
    {
        this.tf = c.Tf;
        this.pqScale = JxlTransferFunctions.PqScaleFrom10000Nits(intensityTarget);
        if (c.Tf.HaveGamma)
        {
            this.gammaExponent = 1.0 / (c.Tf.Gamma * (1.0 / JxlCustomTransferFunction.GammaMul));
        }

        if (c.HasPrimaries && (c.Primaries != JxlPrimaries.Srgb || c.WhitePoint != JxlWhitePoint.D65))
        {
            this.toLinearSrgb = PrimariesToLinearSrgb(c);
        }

        if (c.Tf.IsHlg)
        {
            // ApplyHlgOotf: near 300 nits the gamma is about 1, so the OOTF is skipped.
            this.applyHlgOotf = !(intensityTarget >= 295 && intensityTarget <= 305);
            this.hlgGamma = 1.2f * MathF.Pow(1.111f, JxlHlgOotfBase.Log2(intensityTarget * 1e-3f));
            if (c.HasPrimaries)
            {
                this.hlgLuminances = c.PrimariesToXyz().Luminances;
            }
        }
    }

    /// <summary>Converts one row of each plane to linear sRGB in place.</summary>
    /// <param name="row0">The red (or grey) samples.</param>
    /// <param name="row1">The green samples (the grey again, for a grey encoding).</param>
    /// <param name="row2">The blue samples (the grey again, for a grey encoding).</param>
    public void ConvertRow(Span<float> row0, Span<float> row1, Span<float> row2)
    {
        for (int x = 0; x < row0.Length; x++)
        {
            float r = this.DisplayFromEncoded(row0[x]);
            float g = this.DisplayFromEncoded(row1[x]);
            float b = this.DisplayFromEncoded(row2[x]);
            if (this.applyHlgOotf)
            {
                this.ApplyHlgOotf(ref r, ref g, ref b);
            }

            float[,] m = this.toLinearSrgb;
            if (m is not null)
            {
                float lr = (m[0, 0] * r) + (m[0, 1] * g) + (m[0, 2] * b);
                float lg = (m[1, 0] * r) + (m[1, 1] * g) + (m[1, 2] * b);
                float lb = (m[2, 0] * r) + (m[2, 1] * g) + (m[2, 2] * b);
                r = lr;
                g = lg;
                b = lb;
            }

            row0[x] = r;
            row1[x] = g;
            row2[x] = b;
        }
    }

    // The matrix that the decoder's AdaptToPrimaries puts in front of the inverse opsin matrix, inverted:
    // the encoding's primaries to XYZ, adapted from its white to D50 (Bradford), then to linear sRGB.
    private static float[,] PrimariesToLinearSrgb(JxlColorEncodingHeader c)
    {
        float[,] originalToXyzD50 = JxlMatrix3x3.ToXyzD50(c.WhitePointXy, c.PrimariesToXyz().ToXyz);
        return IccProfileBuilder.Mul3x3Matrix(IccProfileBuilder.Inv3x3Matrix(JxlMatrix3x3.SrgbToXyzD50()), originalToXyzD50);
    }

    private float DisplayFromEncoded(float e)
    {
        JxlTransferFunction transferFunction = this.tf.HaveGamma ? JxlTransferFunction.Unknown : this.tf.TransferFunction;
        switch (transferFunction)
        {
            case JxlTransferFunction.Linear:
                return e;
            case JxlTransferFunction.Srgb:
                return JxlTransferFunctions.SrgbDisplayFromEncoded(e);
            case JxlTransferFunction.Pq:
                return JxlTransferFunctions.PqDisplayFromEncoded(e, this.pqScale);
            case JxlTransferFunction.Hlg:
                return (float)JxlTfHlgBase.DisplayFromEncoded(e);
        }

        // The power curves, of the magnitude, with the sign of the sample.
        double v = Math.Abs((double)e);
        double magnitude = transferFunction switch
        {
            JxlTransferFunction.Bt709 => v < Bt709InvThresh ? v * Bt709InvMulLow : Math.Pow((v * Bt709InvMulHi) + Bt709InvAdd, Bt709InvPowHi),
            JxlTransferFunction.Dci => Math.Pow(v, 2.6),
            _ => Math.Pow(v, this.gammaExponent),
        };

        return (float)(e < 0 ? -magnitude : magnitude);
    }

    // ApplyHlgOotf(forward: true): scene light to display light at the intensity target. RGB is scaled by a
    // power of its luminance; if that brightens a saturated highlight out of gamut (gamma below 1), it is
    // scaled back, keeping its hue and saturation. Grey is raised to the gamma.
    private void ApplyHlgOotf(ref float r, ref float g, ref float b)
    {
        float gamma = this.hlgGamma;
        if (this.hlgLuminances is null)
        {
            r = MathF.Pow(r, gamma);
            g = r;
            b = r;
            return;
        }

        float[] l = this.hlgLuminances;
        float luminance = (r * l[0]) + (g * l[1]) + (b * l[2]);
        float ratio = MathF.Pow(luminance, gamma - 1);
        if (float.IsFinite(ratio))
        {
            r *= ratio;
            g *= ratio;
            b *= ratio;
            if (gamma < 1)
            {
                float maximum = MathF.Max(r, MathF.Max(g, b));
                if (maximum > 1)
                {
                    float normalizer = 1.0f / maximum;
                    r *= normalizer;
                    g *= normalizer;
                    b *= normalizer;
                }
            }
        }
    }
}
