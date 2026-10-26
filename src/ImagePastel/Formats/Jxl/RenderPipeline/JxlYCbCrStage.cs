// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// kYCbCrStage in lib/jxl/render_pipeline/stage_ycbcr.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// Converts the colour planes from YCbCr to RGB in place: full-range BT.601, as JFIF defines it
/// (ITU-T T.871 clause 7). The planes come in as Cb, Y and Cr, the order a JPEG XL frame codes them in,
/// and go out as R, G and B. The output is in the image's own colour encoding, so no from-linear stage
/// follows.
/// </summary>
internal sealed class JxlYCbCrStage : IJxlColorStage
{
    // libjxl's constants are float expressions, rounded to float at each step as written.
    private const float C128 = 128.0f / 255;
    private const float CrCr = 1.402f;
    private const float CgCb = -0.114f * 1.772f / 0.587f;
    private const float CgCr = -0.299f * 1.402f / 0.587f;
    private const float CbCb = 1.772f;

    public void ProcessRow(Buffer2D<float>[] planes, int xsize, int y)
    {
        Span<float> row0 = planes[0].DangerousGetRowSpan(y).Slice(0, xsize);
        Span<float> row1 = planes[1].DangerousGetRowSpan(y).Slice(0, xsize);
        Span<float> row2 = planes[2].DangerousGetRowSpan(y).Slice(0, xsize);
        for (int x = 0; x < xsize; x++)
        {
            float yValue = row1[x] + C128;
            float cb = row0[x];
            float cr = row2[x];

            // libjxl uses MulAdd, which fuses on targets with FMA; each is split here (ADR 0004). G adds
            // the Cb term to Y first, then the Cr term.
            row0[x] = (CrCr * cr) + yValue;
            row1[x] = (CgCr * cr) + ((CgCb * cb) + yValue);
            row2[x] = (CbCb * cb) + yValue;
        }
    }
}
