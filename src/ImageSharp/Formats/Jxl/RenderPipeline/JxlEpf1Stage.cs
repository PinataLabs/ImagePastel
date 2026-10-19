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
    protected override void FilterPixel(Rows rows, int x, float invSigma, out float outX, out float outY, out float outB)
    {
        float sad0 = 0;
        float sad1 = 0;
        float sad2 = 0;
        float sad3 = 0;

        // Compute the SADs. pXY is the pixel at column x + X - 2 and row y + Y - 2: the centre pixel is
        // p22 and the one above it p21. The order of the additions is libjxl's.
        for (int c = 0; c < 3; c++)
        {
            float t;

            float p20 = rows[c, -2, x];
            float p21 = rows[c, -1, x];
            float sad0c = MathF.Abs(p20 - p21); // SAD 2, 1

            float p11 = rows[c, -1, x - 1];
            float sad1c = MathF.Abs(p11 - p21); // SAD 1, 2

            float p31 = rows[c, -1, x + 1];
            float sad2c = MathF.Abs(p31 - p21); // SAD 3, 2

            float p02 = rows[c, 0, x - 2];
            float p12 = rows[c, 0, x - 1];
            sad1c += MathF.Abs(p02 - p12); // SAD 1, 2
            sad0c += MathF.Abs(p11 - p12); // SAD 2, 1

            float p22 = rows[c, 0, x];
            t = MathF.Abs(p12 - p22);
            sad1c += t; // SAD 1, 2
            sad2c += t; // SAD 3, 2
            t = MathF.Abs(p22 - p21);
            float sad3c = t; // SAD 2, 3
            sad0c += t; // SAD 2, 1

            float p32 = rows[c, 0, x + 1];
            sad0c += MathF.Abs(p31 - p32); // SAD 2, 1
            t = MathF.Abs(p22 - p32);
            sad1c += t; // SAD 1, 2
            sad2c += t; // SAD 3, 2

            float p42 = rows[c, 0, x + 2];
            sad2c += MathF.Abs(p42 - p32); // SAD 3, 2

            float p13 = rows[c, 1, x - 1];
            sad3c += MathF.Abs(p13 - p12); // SAD 2, 3

            float p23 = rows[c, 1, x];
            t = MathF.Abs(p22 - p23);
            sad0c += t; // SAD 2, 1
            sad3c += t; // SAD 2, 3
            sad1c += MathF.Abs(p13 - p23); // SAD 1, 2

            float p33 = rows[c, 1, x + 1];
            sad2c += MathF.Abs(p33 - p23); // SAD 3, 2
            sad3c += MathF.Abs(p33 - p32); // SAD 2, 3

            float p24 = rows[c, 2, x];
            sad3c += MathF.Abs(p24 - p23); // SAD 2, 3

            float scale = this.LoopFilter.EpfChannelScale[c];
            sad0 = (sad0c * scale) + sad0;
            sad1 = (sad1c * scale) + sad1;
            sad2 = (sad2c * scale) + sad2;
            sad3 = (sad3c * scale) + sad3;
        }

        WeightedSum sum = new(rows[0, 0, x], rows[1, 0, x], rows[2, 0, x]);

        // Top row, the centre row left and right, the bottom row.
        AddPixel(ref sum, rows, -1, x, sad0, invSigma);
        AddPixel(ref sum, rows, 0, x - 1, sad1, invSigma);
        AddPixel(ref sum, rows, 0, x + 1, sad2, invSigma);
        AddPixel(ref sum, rows, 1, x, sad3, invSigma);

        sum.Normalize(out outX, out outY, out outB);
    }

    private static void AddPixel(ref WeightedSum sum, Rows rows, int dy, int x, float sad, float invSigma)
        => sum.Add(Weight(sad, invSigma), rows[0, dy, x], rows[1, dy, x], rows[2, dy, x]);
}
