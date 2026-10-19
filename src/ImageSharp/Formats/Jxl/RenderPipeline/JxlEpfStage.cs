// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: what the EPF0Stage,
// EPF1Stage and EPF2Stage of lib/jxl/render_pipeline/stage_epf.cc share (Weight, the SAD multipliers,
// the sigma test and GetChannelMode).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// One pass of the edge-preserving filter: a weighted average of the pixels around each pixel, whose
/// weights fall with the difference between the patches around them (their sum of absolute differences,
/// SAD) and rise with the block's sigma. It mixes the three colour channels.
/// </summary>
/// <remarks>
/// libjxl's Highway code uses <c>MulAdd</c>, an FMA on AVX2, which ADR 0004 forbids here: each one is a
/// multiplication and then an addition. The reciprocal of the weight sum is a division, as in libjxl's
/// default build (<c>JXL_HIGH_PRECISION</c> is 1), not <c>ApproximateReciprocal</c>. libjxl filters a
/// vector of pixels at a time, all of one block; this filters one pixel at a time, with the same values.
/// </remarks>
internal abstract class JxlEpfStage : IJxlLoopFilterStage
{
    /// <summary>The side of a block, libjxl's <c>kBlockDim</c>.</summary>
    protected const int BlockDim = 8;

    private readonly float sadMul;
    private readonly float borderSadMul;
    private float[] rows;

    /// <summary>Initializes a new instance of the <see cref="JxlEpfStage"/> class.</summary>
    /// <param name="lf">The loop-filter fields.</param>
    /// <param name="sigma">The sigma image.</param>
    /// <param name="border">The pass's border.</param>
    /// <param name="sadMul">The pass's SAD multiplier inside a block, libjxl's <c>sm</c>.</param>
    protected JxlEpfStage(JxlLoopFilter lf, JxlEpfSigma sigma, int border, float sadMul)
    {
        this.LoopFilter = lf;
        this.Sigma = sigma;
        this.Border = border;
        this.sadMul = sadMul;
        this.borderSadMul = sadMul * lf.EpfBorderSadMul;
    }

    /// <inheritdoc/>
    public int Border { get; }

    /// <summary>Gets the loop-filter fields.</summary>
    protected JxlLoopFilter LoopFilter { get; }

    /// <summary>Gets the sigma image.</summary>
    protected JxlEpfSigma Sigma { get; }

    /// <inheritdoc/>
    public bool ChangesChannel(int c) => c < 3;

    /// <inheritdoc/>
    public void ProcessRow(JxlNeighbourRows input, Buffer2D<float>[] output, int xsize, int y)
    {
        // The rows the pass reads, one after another: channel c, row dy is at (c * (2 * Border + 1) +
        // Border + dy) * stride, and pixel x of it at Border + x past that.
        int stride = xsize + (2 * this.Border);
        int rowsPerChannel = (2 * this.Border) + 1;
        int channelStride = rowsPerChannel * stride;
        if (this.rows == null || this.rows.Length != 3 * channelStride)
        {
            this.rows = new float[3 * channelStride];
        }

        for (int c = 0; c < 3; c++)
        {
            for (int dy = -this.Border; dy <= this.Border; dy++)
            {
                input.GetRow(c, dy).CopyTo(this.rows.AsSpan(((c * rowsPerChannel) + this.Border + dy) * stride, stride));
            }
        }

        ReadOnlySpan<float> rowSigma = this.Sigma.GetRow(y);

        // The pixels on a block's edge rows and columns get the border multiplier.
        bool borderRow = y % BlockDim == 0 || y % BlockDim == BlockDim - 1;
        Span<float> outX = output[0].DangerousGetRowSpan(y);
        Span<float> outY = output[1].DangerousGetRowSpan(y);
        Span<float> outB = output[2].DangerousGetRowSpan(y);
        int center0 = (this.Border * stride) + this.Border;
        for (int x = 0; x < xsize; x++)
        {
            int center = center0 + x;
            float blockSigma = rowSigma[x / BlockDim];
            if (blockSigma < JxlEpfSigma.MinSigma)
            {
                outX[x] = this.rows[center];
                outY[x] = this.rows[center + channelStride];
                outB[x] = this.rows[center + (2 * channelStride)];
                continue;
            }

            int ix = x % BlockDim;
            float vsm = borderRow || ix == 0 || ix == BlockDim - 1 ? this.borderSadMul : this.sadMul;
            float invSigma = blockSigma * vsm;
            this.FilterPixel(this.rows, center, stride, channelStride, invSigma, out outX[x], out outY[x], out outB[x]);
        }
    }

    /// <summary>Filters one pixel.</summary>
    /// <param name="rows">The rows around the pixel, of every channel.</param>
    /// <param name="center">The index of the pixel in channel 0.</param>
    /// <param name="stride">The distance between two rows.</param>
    /// <param name="channelStride">The distance between two channels.</param>
    /// <param name="invSigma">The inverse sigma of the pixel, with its SAD multiplier.</param>
    /// <param name="outX">The filtered X.</param>
    /// <param name="outY">The filtered Y.</param>
    /// <param name="outB">The filtered B.</param>
    protected abstract void FilterPixel(float[] rows, int center, int stride, int channelStride, float invSigma, out float outX, out float outY, out float outB);

    /// <summary>libjxl's <c>Weight</c>: <c>max(0, sad * inv_sigma + 1)</c>, without the FMA.</summary>
    /// <param name="sad">The SAD of the two patches.</param>
    /// <param name="invSigma">The inverse sigma, which is negative.</param>
    /// <returns>The weight.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static float Weight(float sad, float invSigma)
    {
        float v = (sad * invSigma) + 1.0f;

        // ZeroIfNegative.
        return v < 0 ? 0 : v;
    }
}
