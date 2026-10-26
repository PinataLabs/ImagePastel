// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// Rec2408ToneMapperBase in lib/jxl/cms/tone_mapping.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using static PinataLabs.ImagePastel.Formats.Jxl.Base.JxlScalarMath;

namespace PinataLabs.ImagePastel.Formats.Jxl.Base;

/// <summary>
/// The scalar Rec. ITU-R BT.2408 tone mapper (its Annex 5 EETF) that libjxl's ICC builder uses for the
/// 3D-LUT profile of a PQ image: it maps the luminance of a pixel from a source range of nits to a target
/// range on the PQ scale, with a Hermite spline above a knee, and scales red, green and blue by the same
/// ratio.
/// </summary>
/// <remarks>
/// It is float code, as libjxl writes it; only the calls into <see cref="JxlTfPqBase"/> are double, and their
/// results are floats. libjxl's render pipeline has a vector version (<c>Rec2408ToneMapper</c>), which the
/// tone-mapping stage uses; that stage is not ported.
/// </remarks>
internal sealed class JxlRec2408ToneMapperBase
{
    private readonly (float Min, float Max) sourceRange;
    private readonly (float Min, float Max) targetRange;
    private readonly float redY;
    private readonly float greenY;
    private readonly float blueY;

    private readonly float pqMasteringMin;
    private readonly float pqMasteringMax;
    private readonly float pqMasteringRange;
    private readonly float invPqMasteringRange;
    private readonly float minLum;
    private readonly float maxLum;
    private readonly float ks;
    private readonly float invOneMinusKs;
    private readonly float normalizer;
    private readonly float invTargetPeak;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlRec2408ToneMapperBase"/> class.
    /// </summary>
    /// <param name="sourceRange">The luminance range of the source, in nits.</param>
    /// <param name="targetRange">The luminance range of the target, in nits.</param>
    /// <param name="primariesLuminances">The luminances of the red, green and blue primaries: the Y row of their XYZ matrix.</param>
    public JxlRec2408ToneMapperBase((float Min, float Max) sourceRange, (float Min, float Max) targetRange, ReadOnlySpan<float> primariesLuminances)
    {
        this.sourceRange = sourceRange;
        this.targetRange = targetRange;
        this.redY = primariesLuminances[0];
        this.greenY = primariesLuminances[1];
        this.blueY = primariesLuminances[2];

        // libjxl's member initialisers, in their order.
        this.pqMasteringMin = InvEotf(sourceRange.Min);
        this.pqMasteringMax = InvEotf(sourceRange.Max);
        this.pqMasteringRange = this.pqMasteringMax - this.pqMasteringMin;
        this.invPqMasteringRange = 1.0f / this.pqMasteringRange;

        // TODO(eustas): divide instead of inverse-multiply?
        this.minLum = (InvEotf(targetRange.Min) - this.pqMasteringMin) * this.invPqMasteringRange;

        // TODO(eustas): divide instead of inverse-multiply?
        this.maxLum = (InvEotf(targetRange.Max) - this.pqMasteringMin) * this.invPqMasteringRange;
        this.ks = (1.5f * this.maxLum) - 0.5f;

        this.invOneMinusKs = 1.0f / StdMax(1e-6f, 1.0f - this.ks);

        this.normalizer = sourceRange.Max / targetRange.Max;
        this.invTargetPeak = 1.0f / targetRange.Max;
    }

    /// <summary>
    /// Tone-maps one pixel in place. The samples are linear, where 1 is the top of the source range; the
    /// result has 1 at the top of the target range.
    /// </summary>
    /// <param name="rgb">The red, green and blue samples.</param>
    public void ToneMap(Span<float> rgb)
    {
        float luminance = this.sourceRange.Max * ((this.redY * rgb[0]) + (this.greenY * rgb[1]) + (this.blueY * rgb[2]));
        float normalizedPq = StdMin(1.0f, (InvEotf(luminance) - this.pqMasteringMin) * this.invPqMasteringRange);
        float e2 = normalizedPq < this.ks ? normalizedPq : this.P(normalizedPq);
        float oneMinusE2 = 1 - e2;
        float oneMinusE2Squared = oneMinusE2 * oneMinusE2;
        float oneMinusE2Fourth = oneMinusE2Squared * oneMinusE2Squared;
        float e3 = (this.minLum * oneMinusE2Fourth) + e2;
        float e4 = (e3 * this.pqMasteringRange) + this.pqMasteringMin;
        float d4 = (float)JxlTfPqBase.DisplayFromEncoded(1.0f, e4);
        float newLuminance = Clamp1(d4, 0.0f, this.targetRange.Max);
        const float minLuminance = 1e-6f;
        bool useCap = luminance <= minLuminance;
        float ratio = newLuminance / StdMax(luminance, minLuminance);
        float cap = newLuminance * this.invTargetPeak;
        float multiplier = ratio * this.normalizer;
        for (int i = 0; i < 3; i++)
        {
            rgb[i] = useCap ? cap : rgb[i] * multiplier;
        }
    }

    private static float InvEotf(float luminance) => (float)JxlTfPqBase.EncodedFromDisplay(1.0f, luminance);

    private float T(float a) => (a - this.ks) * this.invOneMinusKs;

    private float P(float b)
    {
        float tB = this.T(b);
        float tB2 = tB * tB;
        float tB3 = tB2 * tB;
        return (((2 * tB3) - (3 * tB2) + 1) * this.ks)
            + ((tB3 - (2 * tB2) + tB) * (1 - this.ks))
            + (((-2 * tB3) + (3 * tB2)) * this.maxLum);
    }
}
