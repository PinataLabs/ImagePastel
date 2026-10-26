// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// GaborishStage in lib/jxl/render_pipeline/stage_gaborish.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// The Gaborish loop filter: a 3x3 smoothing of each colour channel, with a weight for the centre, one
/// for the four pixels beside it and one for the four corners. The weights come from the frame header
/// (<see cref="JxlLoopFilter"/>), and are scaled so that each channel's kernel sums to 1.
/// </summary>
/// <remarks>
/// libjxl adds the weighted sums with <c>MulAdd</c>, which is a fused multiply-add on AVX2. This port
/// rounds each product and each sum (ADR 0004), so its output can differ from libjxl's in the last bits.
/// </remarks>
internal sealed class JxlGaborishStage : IJxlNeighbourStage
{
    private const int NumColorChannels = 3;

    // Per channel: the centre weight, then the weight of the four pixels beside it and of the four corners.
    private readonly float[] weights = new float[3 * NumColorChannels];

    /// <summary>Initializes a new instance of the <see cref="JxlGaborishStage"/> class.</summary>
    /// <param name="loopFilter">The loop-filter fields of the frame, with the Gaborish weights.</param>
    public JxlGaborishStage(JxlLoopFilter loopFilter)
    {
        this.weights[0] = 1;
        this.weights[1] = loopFilter.GabXWeight1;
        this.weights[2] = loopFilter.GabXWeight2;
        this.weights[3] = 1;
        this.weights[4] = loopFilter.GabYWeight1;
        this.weights[5] = loopFilter.GabYWeight2;
        this.weights[6] = 1;
        this.weights[7] = loopFilter.GabBWeight1;
        this.weights[8] = loopFilter.GabBWeight2;

        // Normalize. JxlLoopFilter rejects weights whose sum is near 0.
        for (int c = 0; c < NumColorChannels; c++)
        {
            float div = this.weights[3 * c] + (4 * (this.weights[(3 * c) + 1] + this.weights[(3 * c) + 2]));
            float mul = 1.0f / div;
            this.weights[3 * c] *= mul;
            this.weights[(3 * c) + 1] *= mul;
            this.weights[(3 * c) + 2] *= mul;
        }
    }

    /// <inheritdoc/>
    public int Border => 1;

    /// <inheritdoc/>
    public bool ChangesChannel(int c) => c < NumColorChannels;

    /// <inheritdoc/>
    public void ProcessRow(JxlNeighbourRows input, Buffer2D<float>[] output, int xsize, int y)
    {
        for (int c = 0; c < NumColorChannels; c++)
        {
            // Element x of each span is pixel x - 1, so pixel x and its neighbours are elements x to x + 2.
            ReadOnlySpan<float> rowT = input.GetRow(c, -1);
            ReadOnlySpan<float> rowM = input.GetRow(c, 0);
            ReadOnlySpan<float> rowB = input.GetRow(c, 1);
            Span<float> rowOut = output[c].DangerousGetRowSpan(y).Slice(0, xsize);
            float w0 = this.weights[3 * c];
            float w1 = this.weights[(3 * c) + 1];
            float w2 = this.weights[(3 * c) + 2];
            for (int x = 0; x < rowOut.Length; x++)
            {
                float t = rowT[x + 1];
                float tl = rowT[x];
                float tr = rowT[x + 2];
                float m = rowM[x + 1];
                float l = rowM[x];
                float r = rowM[x + 2];
                float b = rowB[x + 1];
                float bl = rowB[x];
                float br = rowB[x + 2];
                float sum0 = m;
                float sum1 = (l + r) + (t + b);
                float sum2 = (tl + tr) + (bl + br);

                // libjxl: MulAdd(sum2, w2, MulAdd(sum1, w1, Mul(sum0, w0))).
                rowOut[x] = (sum2 * w2) + ((sum1 * w1) + (sum0 * w0));
            }
        }
    }
}
