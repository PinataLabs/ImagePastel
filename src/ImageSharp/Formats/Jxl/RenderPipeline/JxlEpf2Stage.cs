// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: EPF2Stage in
// lib/jxl/render_pipeline/stage_epf.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// EPF pass 2, which a frame with two or three EPF iterations has: a 3x3 plus-shaped kernel with one
/// SAD per pixel, so a 3x3 filter.
/// </summary>
internal sealed class JxlEpf2Stage : JxlEpfStage
{
    /// <summary>Initializes a new instance of the <see cref="JxlEpf2Stage"/> class.</summary>
    /// <param name="lf">The loop-filter fields.</param>
    /// <param name="sigma">The sigma image.</param>
    public JxlEpf2Stage(JxlLoopFilter lf, JxlEpfSigma sigma)

        // libjxl multiplies the float scale by the double 1.65, then stores the product as a float.
        : base(lf, sigma, 1, (float)(lf.EpfPass2SigmaScale * 1.65))
    {
    }

    /// <inheritdoc/>
    protected override void FilterPixel(float[] rows, int center, int stride, int channelStride, float invSigma, out float outX, out float outY, out float outB)
    {
        int c1 = center + channelStride;
        int c2 = center + (2 * channelStride);
        float rx = rows[center];
        float ry = rows[c1];
        float rb = rows[c2];
        float w = 1;
        float x = rx;
        float y = ry;
        float b = rb;

        // Top row, the centre row left and right, the bottom row.
        this.AddPixel(rows, center, c1, c2, -stride, rx, ry, rb, invSigma, ref x, ref y, ref b, ref w);
        this.AddPixel(rows, center, c1, c2, -1, rx, ry, rb, invSigma, ref x, ref y, ref b, ref w);
        this.AddPixel(rows, center, c1, c2, 1, rx, ry, rb, invSigma, ref x, ref y, ref b, ref w);
        this.AddPixel(rows, center, c1, c2, stride, rx, ry, rb, invSigma, ref x, ref y, ref b, ref w);

        float invW = 1.0f / w;
        outX = x * invW;
        outY = y * invW;
        outB = b * invW;
    }

    private void AddPixel(float[] rows, int c0, int c1, int c2, int off, float rx, float ry, float rb, float invSigma, ref float x, ref float y, ref float b, ref float w)
    {
        float cx = rows[c0 + off];
        float cy = rows[c1 + off];
        float cb = rows[c2 + off];

        float[] scale = this.LoopFilter.EpfChannelScale;
        float sad = MathF.Abs(cx - rx) * scale[0];
        sad = (MathF.Abs(cy - ry) * scale[1]) + sad;
        sad = (MathF.Abs(cb - rb) * scale[2]) + sad;

        float weight = Weight(sad, invSigma);
        w += weight;
        x = (weight * cx) + x;
        y = (weight * cy) + y;
        b = (weight * cb) + b;
    }
}
