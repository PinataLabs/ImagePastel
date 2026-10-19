// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: EPF1Stage in
// lib/jxl/render_pipeline/stage_epf.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// EPF pass 1, which every frame with EPF has: a 3x3 plus-shaped kernel with 5 SADs per pixel (also
/// 3x3 plus-shaped), so a 5x5 filter.
/// </summary>
internal sealed class JxlEpf1Stage : JxlEpfStage
{
    /// <summary>Initializes a new instance of the <see cref="JxlEpf1Stage"/> class.</summary>
    /// <param name="lf">The loop-filter fields.</param>
    /// <param name="sigma">The sigma image.</param>
    public JxlEpf1Stage(JxlLoopFilter lf, JxlEpfSigma sigma)
        : base(lf, sigma, 2, 1.65f)
    {
    }

    /// <inheritdoc/>
    protected override void FilterPixel(float[] rows, int center, int stride, int channelStride, float invSigma, out float outX, out float outY, out float outB)
    {
        float sad0 = 0;
        float sad1 = 0;
        float sad2 = 0;
        float sad3 = 0;

        // Compute the SADs. pXY is the pixel at column x + X - 2 and row y + Y - 2: the centre pixel is
        // p22 and the one above it p21. The order of the additions is libjxl's.
        for (int c = 0; c < 3; c++)
        {
            int p = center + (c * channelStride);
            float t;

            float p20 = rows[p - (2 * stride)];
            float p21 = rows[p - stride];
            float sad0c = MathF.Abs(p20 - p21); // SAD 2, 1

            float p11 = rows[p - stride - 1];
            float sad1c = MathF.Abs(p11 - p21); // SAD 1, 2

            float p31 = rows[p - stride + 1];
            float sad2c = MathF.Abs(p31 - p21); // SAD 3, 2

            float p02 = rows[p - 2];
            float p12 = rows[p - 1];
            sad1c += MathF.Abs(p02 - p12); // SAD 1, 2
            sad0c += MathF.Abs(p11 - p12); // SAD 2, 1

            float p22 = rows[p];
            t = MathF.Abs(p12 - p22);
            sad1c += t; // SAD 1, 2
            sad2c += t; // SAD 3, 2
            t = MathF.Abs(p22 - p21);
            float sad3c = t; // SAD 2, 3
            sad0c += t; // SAD 2, 1

            float p32 = rows[p + 1];
            sad0c += MathF.Abs(p31 - p32); // SAD 2, 1
            t = MathF.Abs(p22 - p32);
            sad1c += t; // SAD 1, 2
            sad2c += t; // SAD 3, 2

            float p42 = rows[p + 2];
            sad2c += MathF.Abs(p42 - p32); // SAD 3, 2

            float p13 = rows[p + stride - 1];
            sad3c += MathF.Abs(p13 - p12); // SAD 2, 3

            float p23 = rows[p + stride];
            t = MathF.Abs(p22 - p23);
            sad0c += t; // SAD 2, 1
            sad3c += t; // SAD 2, 3
            sad1c += MathF.Abs(p13 - p23); // SAD 1, 2

            float p33 = rows[p + stride + 1];
            sad2c += MathF.Abs(p33 - p23); // SAD 3, 2
            sad3c += MathF.Abs(p33 - p32); // SAD 2, 3

            float p24 = rows[p + (2 * stride)];
            sad3c += MathF.Abs(p24 - p23); // SAD 2, 3

            float scale = this.LoopFilter.EpfChannelScale[c];
            sad0 = (sad0c * scale) + sad0;
            sad1 = (sad1c * scale) + sad1;
            sad2 = (sad2c * scale) + sad2;
            sad3 = (sad3c * scale) + sad3;
        }

        int c1 = center + channelStride;
        int c2 = center + (2 * channelStride);
        float w = 1;
        float x = rows[center];
        float y = rows[c1];
        float b = rows[c2];

        // Top row, the centre row left and right, the bottom row.
        AddPixel(rows, center, c1, c2, -stride, sad0, invSigma, ref x, ref y, ref b, ref w);
        AddPixel(rows, center, c1, c2, -1, sad1, invSigma, ref x, ref y, ref b, ref w);
        AddPixel(rows, center, c1, c2, 1, sad2, invSigma, ref x, ref y, ref b, ref w);
        AddPixel(rows, center, c1, c2, stride, sad3, invSigma, ref x, ref y, ref b, ref w);

        float invW = 1.0f / w;
        outX = x * invW;
        outY = y * invW;
        outB = b * invW;
    }

    private static void AddPixel(float[] rows, int c0, int c1, int c2, int off, float sad, float invSigma, ref float x, ref float y, ref float b, ref float w)
    {
        float weight = Weight(sad, invSigma);
        w += weight;
        x = (weight * rows[c0 + off]) + x;
        y = (weight * rows[c1 + off]) + y;
        b = (weight * rows[c2 + off]) + b;
    }
}
