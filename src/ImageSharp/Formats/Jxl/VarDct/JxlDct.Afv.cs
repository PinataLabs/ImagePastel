// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_transforms-inl.h (AFVIDCT4x4 with its k4x4AFVBasis, and AFVTransformToPixels).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <content>
/// The AFV strategies: an 8x8 block whose 4x4 corner has a basis of its own, beside a 4x4 DCT and above
/// or below a 4x8 DCT.
/// </content>
internal static partial class JxlDct
{
    // k4x4AFVBasis: 16 patterns of 16 pixels each, pattern j at row j.
    private static ReadOnlySpan<float> Afv4X4Basis => new[]
    {
        0.25f, 0.25f, 0.25f, 0.25f,
        0.25f, 0.25f, 0.25f, 0.25f,
        0.25f, 0.25f, 0.25f, 0.25f,
        0.25f, 0.25f, 0.25f, 0.25f,

        0.876902929799142f, 0.2206518106944235f, -0.10140050393753763f, -0.1014005039375375f,
        0.2206518106944236f, -0.10140050393753777f, -0.10140050393753772f, -0.10140050393753763f,
        -0.10140050393753758f, -0.10140050393753769f, -0.1014005039375375f, -0.10140050393753768f,
        -0.10140050393753768f, -0.10140050393753759f, -0.10140050393753763f, -0.10140050393753741f,

        0.0f, 0.0f, 0.40670075830260755f, 0.44444816619734445f,
        0.0f, 0.0f, 0.19574399372042936f, 0.2929100136981264f,
        -0.40670075830260716f, -0.19574399372042872f, 0.0f, 0.11379074460448091f,
        -0.44444816619734384f, -0.29291001369812636f, -0.1137907446044814f, 0.0f,

        0.0f, 0.0f, -0.21255748058288748f, 0.3085497062849767f,
        0.0f, 0.4706702258572536f, -0.1621205195722993f, 0.0f,
        -0.21255748058287047f, -0.16212051957228327f, -0.47067022585725277f, -0.1464291867126764f,
        0.3085497062849487f, 0.0f, -0.14642918671266536f, 0.4251149611657548f,

        0.0f, -0.7071067811865474f, 0.0f, 0.0f,
        0.7071067811865476f, 0.0f, 0.0f, 0.0f,
        0.0f, 0.0f, 0.0f, 0.0f,
        0.0f, 0.0f, 0.0f, 0.0f,

        -0.4105377591765233f, 0.6235485373547691f, -0.06435071657946274f, -0.06435071657946266f,
        0.6235485373547694f, -0.06435071657946284f, -0.0643507165794628f, -0.06435071657946274f,
        -0.06435071657946272f, -0.06435071657946279f, -0.06435071657946266f, -0.06435071657946277f,
        -0.06435071657946277f, -0.06435071657946273f, -0.06435071657946274f, -0.0643507165794626f,

        0.0f, 0.0f, -0.4517556589999482f, 0.15854503551840063f,
        0.0f, -0.04038515160822202f, 0.0074182263792423875f, 0.39351034269210167f,
        -0.45175565899994635f, 0.007418226379244351f, 0.1107416575309343f, 0.08298163094882051f,
        0.15854503551839705f, 0.3935103426921022f, 0.0829816309488214f, -0.45175565899994796f,

        0.0f, 0.0f, -0.304684750724869f, 0.5112616136591823f,
        0.0f, 0.0f, -0.290480129728998f, -0.06578701549142804f,
        0.304684750724884f, 0.2904801297290076f, 0.0f, -0.23889773523344604f,
        -0.5112616136592012f, 0.06578701549142545f, 0.23889773523345467f, 0.0f,

        0.0f, 0.0f, 0.3017929516615495f, 0.25792362796341184f,
        0.0f, 0.16272340142866204f, 0.09520022653475037f, 0.0f,
        0.3017929516615503f, 0.09520022653475055f, -0.16272340142866173f, -0.35312385449816297f,
        0.25792362796341295f, 0.0f, -0.3531238544981624f, -0.6035859033230976f,

        0.0f, 0.0f, 0.40824829046386274f, 0.0f,
        0.0f, 0.0f, 0.0f, -0.4082482904638628f,
        -0.4082482904638635f, 0.0f, 0.0f, -0.40824829046386296f,
        0.0f, 0.4082482904638634f, 0.408248290463863f, 0.0f,

        0.0f, 0.0f, 0.1747866975480809f, 0.0812611176717539f,
        0.0f, 0.0f, -0.3675398009862027f, -0.307882213957909f,
        -0.17478669754808135f, 0.3675398009862011f, 0.0f, 0.4826689115059883f,
        -0.08126111767175039f, 0.30788221395790305f, -0.48266891150598584f, 0.0f,

        0.0f, 0.0f, -0.21105601049335784f, 0.18567180916109802f,
        0.0f, 0.0f, 0.49215859013738733f, -0.38525013709251915f,
        0.21105601049335806f, -0.49215859013738905f, 0.0f, 0.17419412659916217f,
        -0.18567180916109904f, 0.3852501370925211f, -0.1741941265991621f, 0.0f,

        0.0f, 0.0f, -0.14266084808807264f, -0.3416446842253372f,
        0.0f, 0.7367497537172237f, 0.24627107722075148f, -0.08574019035519306f,
        -0.14266084808807344f, 0.24627107722075137f, 0.14883399227113567f, -0.04768680350229251f,
        -0.3416446842253373f, -0.08574019035519267f, -0.047686803502292804f, -0.14266084808807242f,

        0.0f, 0.0f, -0.13813540350758585f, 0.3302282550303788f,
        0.0f, 0.08755115000587084f, -0.07946706605909573f, -0.4613374887461511f,
        -0.13813540350758294f, -0.07946706605910261f, 0.49724647109535086f, 0.12538059448563663f,
        0.3302282550303805f, -0.4613374887461554f, 0.12538059448564315f, -0.13813540350758452f,

        0.0f, 0.0f, -0.17437602599651067f, 0.0702790691196284f,
        0.0f, -0.2921026642334881f, 0.3623817333531167f, 0.0f,
        -0.1743760259965108f, 0.36238173335311646f, 0.29210266423348785f, -0.4326608024727445f,
        0.07027906911962818f, 0.0f, -0.4326608024727457f, 0.34875205199302267f,

        0.0f, 0.0f, 0.11354987314994337f, -0.07417504595810355f,
        0.0f, 0.19402893032594343f, -0.435190496523228f, 0.21918684838857466f,
        0.11354987314994257f, -0.4351904965232251f, 0.5550443808910661f, -0.25468277124066463f,
        -0.07417504595810233f, 0.2191868483885728f, -0.25468277124066413f, 0.1135498731499429f,
    };

    // AFVTransformToPixels<afv_kind>: the corner is at (4 afvX, 4 afvY), mirrored to put the basis's
    // first pixel at the block's corner.
    private static void AfvTransformToPixels(int afvKind, ReadOnlySpan<float> coefficients, Span<float> pixels, int pixelsStride, Span<float> scratch)
    {
        int afvX = afvKind & 1;
        int afvY = afvKind / 2;
        float block00 = coefficients[0];
        float block01 = coefficients[1];
        float block10 = coefficients[8];
        float dc0 = (block00 + block10 + block01) * 4.0f;
        float dc1 = block00 + block10 - block01;
        float dc2 = block00 - block10;

        // IAFV: (even, even) positions.
        Span<float> coeff = stackalloc float[4 * 4];
        coeff[0] = dc0;
        for (int iy = 0; iy < 4; iy++)
        {
            for (int ix = 0; ix < 4; ix++)
            {
                if (ix == 0 && iy == 0)
                {
                    continue;
                }

                coeff[(iy * 4) + ix] = coefficients[(iy * 2 * 8) + (ix * 2)];
            }
        }

        Span<float> block = stackalloc float[4 * 8];
        AfvIdct4X4(coeff, block);
        for (int iy = 0; iy < 4; iy++)
        {
            for (int ix = 0; ix < 4; ix++)
            {
                pixels[((iy + (afvY * 4)) * pixelsStride) + (afvX * 4) + ix] =
                    block[((afvY == 1 ? 3 - iy : iy) * 4) + (afvX == 1 ? 3 - ix : ix)];
            }
        }

        // IDCT4x4 in (odd, even) positions.
        block[0] = dc1;
        for (int iy = 0; iy < 4; iy++)
        {
            for (int ix = 0; ix < 4; ix++)
            {
                if (ix == 0 && iy == 0)
                {
                    continue;
                }

                block[(iy * 4) + ix] = coefficients[(iy * 2 * 8) + (ix * 2) + 1];
            }
        }

        ComputeScaledIdct(4, 4, block, pixels.Slice((afvY * 4 * pixelsStride) + (afvX == 1 ? 0 : 4)), pixelsStride, scratch);

        // IDCT4x8.
        block[0] = dc2;
        for (int iy = 0; iy < 4; iy++)
        {
            for (int ix = 0; ix < 8; ix++)
            {
                if (ix == 0 && iy == 0)
                {
                    continue;
                }

                block[(iy * 8) + ix] = coefficients[(((iy * 2) + 1) * 8) + ix];
            }
        }

        ComputeScaledIdct(4, 8, block, pixels.Slice((afvY == 1 ? 0 : 4) * pixelsStride), pixelsStride, scratch);
    }

    // AFVIDCT4x4: pixel i is the sum over j of coefficient j times pattern j's pixel i, added in order of
    // j. libjxl fuses each multiply and add on AVX2; here they are separate (ADR 0004).
    private static void AfvIdct4X4(ReadOnlySpan<float> coeffs, Span<float> pixels)
    {
        ReadOnlySpan<float> basis = Afv4X4Basis;
        for (int i = 0; i < 16; i++)
        {
            float pixel = 0;
            for (int j = 0; j < 16; j++)
            {
                pixel += coeffs[j] * basis[(j * 16) + i];
            }

            pixels[i] = pixel;
        }
    }
}
