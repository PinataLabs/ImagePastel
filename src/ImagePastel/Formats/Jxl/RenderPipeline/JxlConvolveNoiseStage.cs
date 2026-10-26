// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// ConvolveNoiseStage in lib/jxl/render_pipeline/stage_noise.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// The convolve-noise stage: a 5x5 high-pass filter of each of the three noise planes, 4 times
/// (1 - the box kernel), which turns the uniform random floats into noise around 0.
/// </summary>
/// <remarks>
/// libjxl adds the weighted centre with <c>MulAdd</c>, which is a fused multiply-add on AVX2. This port
/// rounds the product and the sum (ADR 0004), so its output can differ from libjxl's in the last bits.
/// </remarks>
internal sealed class JxlConvolveNoiseStage : IJxlNeighbourStage
{
    private readonly int firstC;

    /// <summary>Initializes a new instance of the <see cref="JxlConvolveNoiseStage"/> class.</summary>
    /// <param name="firstC">The first noise channel: three colour channels, then the extra channels, then the noise.</param>
    public JxlConvolveNoiseStage(int firstC) => this.firstC = firstC;

    /// <inheritdoc/>
    public int Border => 2;

    /// <inheritdoc/>
    public bool ChangesChannel(int c) => c >= this.firstC && c < this.firstC + 3;

    /// <inheritdoc/>
    public void ProcessRow(JxlNeighbourRows input, Buffer2D<float>[] output, int xsize, int y)
    {
        for (int c = this.firstC; c < this.firstC + 3; c++)
        {
            // Element x + 2 of each span is pixel x.
            ReadOnlySpan<float> row0 = input.GetRow(c, -2);
            ReadOnlySpan<float> row1 = input.GetRow(c, -1);
            ReadOnlySpan<float> row2 = input.GetRow(c, 0);
            ReadOnlySpan<float> row3 = input.GetRow(c, 1);
            ReadOnlySpan<float> row4 = input.GetRow(c, 2);
            Span<float> rowOut = output[c].DangerousGetRowSpan(y).Slice(0, xsize);
            for (int x = 0; x < rowOut.Length; x++)
            {
                int m = x + 2;
                float p00 = row2[m];
                float others = 0;

                // The order of libjxl's sums.
                for (int i = -2; i <= 2; i++)
                {
                    others += row0[m + i];
                    others += row1[m + i];
                    others += row3[m + i];
                    others += row4[m + i];
                }

                others += row2[m - 2];
                others += row2[m - 1];
                others += row2[m + 1];
                others += row2[m + 2];

                // 4 * (1 - box kernel)
                // libjxl: MulAdd(others, Set(d, 0.16), Mul(p00, Set(d, -3.84))).
                rowOut[x] = (others * 0.16f) + (p00 * -3.84f);
            }
        }
    }
}
