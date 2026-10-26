// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// GamutMapScalar in lib/jxl/cms/tone_mapping.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using static SixLabors.ImageSharp.Formats.Jxl.Base.JxlScalarMath;

namespace SixLabors.ImageSharp.Formats.Jxl.Base;

/// <summary>
/// libjxl's scalar gamut map, which its ICC builder applies after tone mapping a PQ or HLG pixel: it brings
/// a linear colour into the 0 to 1 cube by mixing it with grey of its own luminance, then normalizing it.
/// </summary>
/// <remarks>
/// libjxl's render pipeline has a vector version (<c>GamutMap</c>), which only the tone-mapping stage uses;
/// that stage is not ported.
/// </remarks>
internal static class JxlGamutMap
{
    /// <summary>Maps one linear pixel into gamut, in place.</summary>
    /// <param name="rgb">The red, green and blue samples.</param>
    /// <param name="primariesLuminances">The luminances of the red, green and blue primaries: the Y row of their XYZ matrix.</param>
    /// <param name="preserveSaturation">
    /// How far, from 0 to 1, to keep the saturation rather than the luminance when a channel is above 1.
    /// </param>
    public static void GamutMapScalar(Span<float> rgb, ReadOnlySpan<float> primariesLuminances, float preserveSaturation = 0.1f)
    {
        float luminance = (primariesLuminances[0] * rgb[0]) + (primariesLuminances[1] * rgb[1]) + (primariesLuminances[2] * rgb[2]);

        // Desaturate out-of-gamut pixels. This is done by mixing each pixel
        // with just enough gray of the target luminance to make all
        // components non-negative.
        // - For saturation preservation, if a component is still larger than
        // 1 then the pixel is normalized to have a maximum component of 1.
        // That will reduce its luminance.
        // - For luminance preservation, getting all components below 1 is
        // done by mixing in yet more gray. That will desaturate it further.
        float grayMixSaturation = 0.0f;
        float grayMixLuminance = 0.0f;
        for (int i = 0; i < 3; i++)
        {
            float val = rgb[i];
            float valMinusGray = val - luminance;
            float invValMinusGray = 1.0f / ((valMinusGray == 0.0f) ? 1.0f : valMinusGray);
            float valOverValMinusGray = val * invValMinusGray;
            grayMixSaturation = (valMinusGray >= 0.0f)
                ? grayMixSaturation
                : StdMax(grayMixSaturation, valOverValMinusGray);
            grayMixLuminance = StdMax(
                grayMixLuminance,
                (valMinusGray <= 0.0f) ? grayMixSaturation : (valOverValMinusGray - invValMinusGray));
        }

        float grayMix = Clamp1((preserveSaturation * (grayMixSaturation - grayMixLuminance)) + grayMixLuminance, 0.0f, 1.0f);
        for (int i = 0; i < 3; i++)
        {
            float val = rgb[i];
            rgb[i] = (grayMix * (luminance - val)) + val;
        }

        // std::max({1.0f, r, g, b}).
        float maxClr = StdMax(StdMax(StdMax(1.0f, rgb[0]), rgb[1]), rgb[2]);
        float normalizer = 1.0f / maxClr;
        for (int i = 0; i < 3; i++)
        {
            rgb[i] *= normalizer;
        }
    }
}
