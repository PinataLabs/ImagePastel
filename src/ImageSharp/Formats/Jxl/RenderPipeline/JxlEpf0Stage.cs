// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: EPF0Stage in
// lib/jxl/render_pipeline/stage_epf.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// EPF pass 0, which only a frame with three EPF iterations has: a 5x5 plus-shaped kernel with 5 SADs
/// per pixel (3x3 plus-shaped), so a 7x7 filter.
/// </summary>
internal sealed class JxlEpf0Stage : JxlEpfStage
{
    // The 12 pixels of the kernel around the centre, as (row, column) offsets.
    private static readonly (int Dy, int Dx)[] SadsOff =
    {
        (-2, 0), (-1, -1), (-1, 0), (-1, 1), (0, -2), (0, -1),
        (0, 1), (0, 2), (1, -1), (1, 0), (1, 1), (2, 0),
    };

    // The 5 pixels of the patch around each pixel.
    private static readonly (int Dy, int Dx)[] PlusOff = { (0, 0), (-1, 0), (0, -1), (1, 0), (0, 1) };

    /// <summary>Initializes a new instance of the <see cref="JxlEpf0Stage"/> class.</summary>
    /// <param name="lf">The loop-filter fields.</param>
    /// <param name="sigma">The sigma image.</param>
    public JxlEpf0Stage(JxlLoopFilter lf, JxlEpfSigma sigma)

        // libjxl multiplies the float scale by the double 1.65, then stores the product as a float.
        : base(lf, sigma, 3, (float)(lf.EpfPass0SigmaScale * 1.65))
    {
    }

    /// <inheritdoc/>
    protected override void FilterPixel(float[] rows, int center, int stride, int channelStride, float invSigma, out float outX, out float outY, out float outB)
    {
        // Compute the SADs.
        Span<float> sads = stackalloc float[12];
        sads.Clear();
        for (int c = 0; c < 3; c++)
        {
            int cc = center + (c * channelStride);
            float scale = this.LoopFilter.EpfChannelScale[c];
            for (int i = 0; i < 12; i++)
            {
                int other = cc + (SadsOff[i].Dy * stride) + SadsOff[i].Dx;
                float sad = 0;
                foreach ((int dy, int dx) in PlusOff)
                {
                    int off = (dy * stride) + dx;
                    sad += MathF.Abs(rows[cc + off] - rows[other + off]);
                }

                sads[i] = (sad * scale) + sads[i];
            }
        }

        int c1 = center + channelStride;
        int c2 = center + (2 * channelStride);
        float w = 1;
        float x = rows[center];
        float y = rows[c1];
        float b = rows[c2];
        for (int i = 0; i < 12; i++)
        {
            int off = (SadsOff[i].Dy * stride) + SadsOff[i].Dx;
            float weight = Weight(sads[i], invSigma);
            w += weight;
            x = (weight * rows[center + off]) + x;
            y = (weight * rows[c1 + off]) + y;
            b = (weight * rows[c2 + off]) + b;
        }

        float invW = 1.0f / w;
        outX = x * invW;
        outY = y * invW;
        outB = b * invW;
    }
}
