// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: EPF0Stage in
// lib/jxl/render_pipeline/stage_epf.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Numerics;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

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
    protected override void FilterPixel(Rows rows, int x, float invSigma, out float outX, out float outY, out float outB)
    {
        // Compute the SADs.
        Span<float> sads = stackalloc float[12];
        sads.Clear();
        for (int c = 0; c < 3; c++)
        {
            float scale = this.LoopFilter.EpfChannelScale[c];
            for (int i = 0; i < 12; i++)
            {
                (int sy, int sx) = SadsOff[i];
                float sad = 0;
                foreach ((int dy, int dx) in PlusOff)
                {
                    sad += MathF.Abs(rows[c, dy, x + dx] - rows[c, sy + dy, x + sx + dx]);
                }

                sads[i] = (sad * scale) + sads[i];
            }
        }

        WeightedSum sum = new(rows[0, 0, x], rows[1, 0, x], rows[2, 0, x]);
        for (int i = 0; i < 12; i++)
        {
            (int dy, int dx) = SadsOff[i];
            sum.Add(Weight(sads[i], invSigma), rows[0, dy, x + dx], rows[1, dy, x + dx], rows[2, dy, x + dx]);
        }

        sum.Normalize(out outX, out outY, out outB);
    }

    /// <inheritdoc/>
    protected override void FilterVector(Rows rows, int x, Vector<float> invSigma, out Vector<float> outX, out Vector<float> outY, out Vector<float> outB)
    {
        // FilterPixel's SADs, in its order. The patch around the centre is loaded once per channel.
        Span<Vector<float>> sads = stackalloc Vector<float>[12];
        sads.Clear();
        Span<Vector<float>> center = stackalloc Vector<float>[PlusOff.Length];
        for (int c = 0; c < 3; c++)
        {
            for (int j = 0; j < PlusOff.Length; j++)
            {
                (int dy, int dx) = PlusOff[j];
                center[j] = rows.Load(c, dy, x + dx);
            }

            float scale = this.LoopFilter.EpfChannelScale[c];
            for (int i = 0; i < 12; i++)
            {
                (int sy, int sx) = SadsOff[i];
                Vector<float> sad = Vector<float>.Zero;
                for (int j = 0; j < PlusOff.Length; j++)
                {
                    (int dy, int dx) = PlusOff[j];
                    sad += Vector.Abs(center[j] - rows.Load(c, sy + dy, x + sx + dx));
                }

                sads[i] = (sad * scale) + sads[i];
            }
        }

        VectorWeightedSum sum = new(rows.Load(0, 0, x), rows.Load(1, 0, x), rows.Load(2, 0, x));
        for (int i = 0; i < 12; i++)
        {
            (int dy, int dx) = SadsOff[i];
            sum.Add(Weight(sads[i], invSigma), rows.Load(0, dy, x + dx), rows.Load(1, dy, x + dx), rows.Load(2, dy, x + dx));
        }

        sum.Normalize(out outX, out outY, out outB);
    }
}
