// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: EPF2Stage in
// lib/jxl/render_pipeline/stage_epf.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Numerics;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

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
    protected override void FilterPixel(Rows rows, int x, float invSigma, out float outX, out float outY, out float outB)
    {
        float rx = rows[0, 0, x];
        float ry = rows[1, 0, x];
        float rb = rows[2, 0, x];
        WeightedSum sum = new(rx, ry, rb);

        // Top row, the centre row left and right, the bottom row.
        this.AddPixel(ref sum, rows, -1, x, rx, ry, rb, invSigma);
        this.AddPixel(ref sum, rows, 0, x - 1, rx, ry, rb, invSigma);
        this.AddPixel(ref sum, rows, 0, x + 1, rx, ry, rb, invSigma);
        this.AddPixel(ref sum, rows, 1, x, rx, ry, rb, invSigma);

        sum.Normalize(out outX, out outY, out outB);
    }

    /// <inheritdoc/>
    protected override void FilterVector(Rows rows, int x, Vector<float> invSigma, out Vector<float> outX, out Vector<float> outY, out Vector<float> outB)
    {
        Vector<float> rx = rows.Load(0, 0, x);
        Vector<float> ry = rows.Load(1, 0, x);
        Vector<float> rb = rows.Load(2, 0, x);
        VectorWeightedSum sum = new(rx, ry, rb);

        this.AddPixel(ref sum, rows, -1, x, rx, ry, rb, invSigma);
        this.AddPixel(ref sum, rows, 0, x - 1, rx, ry, rb, invSigma);
        this.AddPixel(ref sum, rows, 0, x + 1, rx, ry, rb, invSigma);
        this.AddPixel(ref sum, rows, 1, x, rx, ry, rb, invSigma);

        sum.Normalize(out outX, out outY, out outB);
    }

    // AddPixel: the SAD of one pixel against the centre, over the three channels.
    private void AddPixel(ref WeightedSum sum, Rows rows, int dy, int x, float rx, float ry, float rb, float invSigma)
    {
        float cx = rows[0, dy, x];
        float cy = rows[1, dy, x];
        float cb = rows[2, dy, x];

        float[] scale = this.LoopFilter.EpfChannelScale;
        float sad = MathF.Abs(cx - rx) * scale[0];
        sad = (MathF.Abs(cy - ry) * scale[1]) + sad;
        sad = (MathF.Abs(cb - rb) * scale[2]) + sad;

        sum.Add(Weight(sad, invSigma), cx, cy, cb);
    }

    // The same for a vector of pixels.
    private void AddPixel(ref VectorWeightedSum sum, Rows rows, int dy, int x, Vector<float> rx, Vector<float> ry, Vector<float> rb, Vector<float> invSigma)
    {
        Vector<float> cx = rows.Load(0, dy, x);
        Vector<float> cy = rows.Load(1, dy, x);
        Vector<float> cb = rows.Load(2, dy, x);

        float[] scale = this.LoopFilter.EpfChannelScale;
        Vector<float> sad = Vector.Abs(cx - rx) * scale[0];
        sad = (Vector.Abs(cy - ry) * scale[1]) + sad;
        sad = (Vector.Abs(cb - rb) * scale[2]) + sad;

        sum.Add(Weight(sad, invSigma), cx, cy, cb);
    }
}
