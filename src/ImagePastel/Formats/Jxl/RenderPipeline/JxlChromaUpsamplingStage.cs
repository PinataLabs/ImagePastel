// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// HorizontalChromaUpsamplingStage and VerticalChromaUpsamplingStage in
// lib/jxl/render_pipeline/stage_chroma_upsampling.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// Doubles one subsampled channel of a YCbCr frame in one direction: each sample becomes two, each
/// three quarters of itself and one quarter of its neighbour on that side. A channel subsampled both
/// ways takes the horizontal stage, then the vertical one.
/// </summary>
/// <remarks>
/// <para>
/// The channel is mirrored at its subsampled size (<see cref="JxlNeighbourRows"/>), so an edge sample's
/// outer neighbour is itself. The pipeline then shrinks the doubled channel to its size at the next
/// shift, so an odd size drops the last column or row.
/// </para>
/// <para>
/// Any colour channel can be subsampled, Y too: <c>cjxl</c> writes some 4:2:2 JPEGs with Y subsampled
/// vertically and Cb and Cr not.
/// </para>
/// <para>
/// libjxl adds the quarter with <c>MulAdd</c>, a fused multiply-add on AVX2. This port rounds the product
/// and the sum (ADR 0004), so its output can differ from libjxl's in the last bit.
/// </para>
/// </remarks>
internal sealed class JxlChromaUpsamplingStage : IJxlNeighbourStage
{
    private const float ThreeFour = 0.75f;
    private const float OneFour = 0.25f;

    private readonly int channel;
    private readonly bool horizontal;

    /// <summary>Initializes a new instance of the <see cref="JxlChromaUpsamplingStage"/> class.</summary>
    /// <param name="channel">The colour channel the stage doubles: 0, 1 or 2.</param>
    /// <param name="horizontal">
    /// <see langword="true"/> to double the width (libjxl's <c>HorizontalChromaUpsamplingStage</c>), or
    /// <see langword="false"/> to double the height (<c>VerticalChromaUpsamplingStage</c>).
    /// </param>
    public JxlChromaUpsamplingStage(int channel, bool horizontal)
    {
        this.channel = channel;
        this.horizontal = horizontal;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// libjxl's horizontal stage reads one column on each side and no rows (<c>Settings::ShiftX</c>), and
    /// the vertical one a row above and below. The border here is the same both ways, so the horizontal
    /// stage is given rows it does not read.
    /// </remarks>
    public int Border => 1;

    /// <inheritdoc/>
    public int ShiftX => this.horizontal ? 1 : 0;

    /// <inheritdoc/>
    public int ShiftY => this.horizontal ? 0 : 1;

    /// <inheritdoc/>
    public bool ChangesChannel(int c) => c == this.channel;

    /// <inheritdoc/>
    public void ProcessRow(JxlNeighbourRows input, Buffer2D<float>[] output, int xsize, int y)
    {
        // Element x + 1 of each input row is sample x.
        Buffer2D<float> plane = output[this.channel];
        if (this.horizontal)
        {
            ReadOnlySpan<float> rowIn = input.GetRow(this.channel, 0);
            Span<float> rowOut = plane.DangerousGetRowSpan(y).Slice(0, 2 * xsize);
            for (int x = 0; x < xsize; x++)
            {
                float current = rowIn[x + 1] * ThreeFour;
                float prev = rowIn[x];
                float next = rowIn[x + 2];

                // libjxl: MulAdd(onefour, prev, current) and MulAdd(onefour, next, current).
                rowOut[2 * x] = (OneFour * prev) + current;
                rowOut[(2 * x) + 1] = (OneFour * next) + current;
            }
        }
        else
        {
            ReadOnlySpan<float> rowTop = input.GetRow(this.channel, -1);
            ReadOnlySpan<float> rowMid = input.GetRow(this.channel, 0);
            ReadOnlySpan<float> rowBot = input.GetRow(this.channel, 1);
            Span<float> rowOut0 = plane.DangerousGetRowSpan(2 * y).Slice(0, xsize);
            Span<float> rowOut1 = plane.DangerousGetRowSpan((2 * y) + 1).Slice(0, xsize);
            for (int x = 0; x < xsize; x++)
            {
                float midScaled = rowMid[x + 1] * ThreeFour;

                // libjxl: MulAdd(it, onefour, im_scaled) and MulAdd(ib, onefour, im_scaled).
                rowOut0[x] = (rowTop[x + 1] * OneFour) + midScaled;
                rowOut1[x] = (rowBot[x + 1] * OneFour) + midScaled;
            }
        }
    }
}
