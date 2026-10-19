// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// UpsamplingStage in lib/jxl/render_pipeline/stage_upsampling.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// Upsamples one channel 2, 4 or 8 times in each direction. Each input pixel becomes an N x N block,
/// and each pixel of the block is a weighted sum of the 5 x 5 input pixels around the input pixel, with
/// one kernel of 25 weights per position in the block. The sum is clamped to the smallest and largest of
/// those 25 pixels, so the stage never rings past its input.
/// </summary>
/// <remarks>
/// The weights come from the image header (<see cref="JxlCustomTransformData"/>), which holds the
/// defaults unless it has custom ones. libjxl adds the 25 products with <c>MulAdd</c>, a fused
/// multiply-add on AVX2, into three sums in a fixed order. This port keeps the order and rounds each
/// product and each sum (ADR 0004), so its output can differ from libjxl's in the last bits.
/// </remarks>
internal sealed class JxlUpsamplingStage : IJxlNeighbourStage
{
    private readonly int c;
    private readonly int shift;
    private readonly float[] kernel;

    /// <summary>Initializes a new instance of the <see cref="JxlUpsamplingStage"/> class.</summary>
    /// <param name="transformData">The image's transform data, with the upsampling weights.</param>
    /// <param name="c">The channel the stage upsamples: the colour channels, then the extra channels.</param>
    /// <param name="shift">log2 of the upsampling: 1, 2 or 3.</param>
    public JxlUpsamplingStage(JxlCustomTransformData transformData, int c, int shift)
    {
        // libjxl's GetUpsamplingStage aborts on any other shift; the frame header allows no other.
        DebugGuard.MustBeBetweenOrEqualTo(shift, 1, 3, nameof(shift));
        this.c = c;
        this.shift = shift;
        this.kernel = CreateKernel(transformData, shift);
    }

    /// <inheritdoc/>
    public int Border => 2;

    /// <inheritdoc/>
    public int ShiftX => this.shift;

    /// <inheritdoc/>
    public int ShiftY => this.shift;

    /// <summary>
    /// Builds the kernels from the header's weights, as libjxl's <c>UpsamplingStage</c> constructor: for
    /// each output position <c>k = N * oy + ox</c> in the N x N block, 25 weights, the one for input pixel
    /// <c>(ix, iy)</c> of the window (each from -2 to 2) at <c>k * 25 + 5 * (iy + 2) + (ix + 2)</c>. The
    /// header stores the kernels of the top-left quadrant's N/2 x N/2 positions side by side, as a square
    /// of 5N/2 x 5N/2 weights, and only the triangle on and above its diagonal (15, 55 or 210 weights);
    /// the rest is mirrored from them.
    /// </summary>
    /// <param name="transformData">The image's transform data.</param>
    /// <param name="shift">log2 of the upsampling: 1, 2 or 3.</param>
    /// <returns>The <c>N * N * 25</c> weights.</returns>
    internal static float[] CreateKernel(JxlCustomTransformData transformData, int shift)
    {
        float[] weights = shift == 1 ? transformData.Upsampling2Weights
            : shift == 2 ? transformData.Upsampling4Weights
            : transformData.Upsampling8Weights;
        int n = 1 << shift;
        int h = n / 2;
        float[] kernel = new float[n * n * 25];
        for (int ky = 0; ky < h; ++ky)
        {
            for (int kx = 0; kx < h; ++kx)
            {
                int offset0 = ((ky * n) + kx) * 25;
                int offset1 = ((ky * n) + (n - 1 - kx)) * 25;
                int offset2 = (((n - 1 - ky) * n) + kx) * 25;
                int offset3 = (((n - 1 - ky) * n) + (n - 1 - kx)) * 25;
                for (int py = 0; py < 5; ++py)
                {
                    for (int px = 0; px < 5; ++px)
                    {
                        int j = (5 * ky) + py;
                        int i = (5 * kx) + px;
                        int my = Math.Min(i, j);
                        int mx = Math.Max(i, j);
                        float w = weights[(5 * h * my) - (my * (my - 1) / 2) + mx - my];
                        kernel[offset0 + (py * 5) + px] = w;
                        kernel[offset1 + (py * 5) + (4 - px)] = w;
                        kernel[offset2 + ((4 - py) * 5) + px] = w;
                        kernel[offset3 + ((4 - py) * 5) + (4 - px)] = w;
                    }
                }
            }
        }

        return kernel;
    }

    /// <inheritdoc/>
    public bool ChangesChannel(int c) => c == this.c;

    /// <inheritdoc/>
    public void ProcessRow(JxlNeighbourRows input, Buffer2D<float>[] output, int xsize, int y)
    {
        int n = 1 << this.shift;
        ReadOnlySpan<float> window = input.Window;
        ReadOnlySpan<float> kernel = this.kernel;

        // Element x of each widened row is pixel x - 2, so the 5 x 5 window of pixel x starts at element x
        // of each row: rowStart[r] + x is pixel (x - 2, y + r - 2).
        Span<int> rowStart = stackalloc int[5];
        for (int r = 0; r < 5; r++)
        {
            rowStart[r] = input.GetRowStart(this.c, r - 2);
        }

        Span<float> pixels = stackalloc float[25];
        Buffer2D<float> plane = output[this.c];
        for (int x = 0; x < xsize; x++)
        {
            // PreProcessRowImpl: the smallest and largest pixel of the window. Min and max are exact, so
            // the order libjxl takes them in does not matter.
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int r = 0; r < 5; r++)
            {
                ReadOnlySpan<float> row = window.Slice(rowStart[r] + x, 5);
                for (int i = 0; i < 5; i++)
                {
                    float v = row[i];
                    pixels[(5 * r) + i] = v;
                    min = v < min ? v : min;
                    max = v > max ? v : max;
                }
            }

            for (int oy = 0; oy < n; oy++)
            {
                Span<float> dst = plane.DangerousGetRowSpan((y << this.shift) + oy).Slice(x << this.shift, n);
                for (int ox = 0; ox < n; ox++)
                {
                    ReadOnlySpan<float> k = kernel.Slice(((n * oy) + ox) * 25, 25);

                    // ProcessRowImpl: three sums, each MulAdd split into a product and a sum.
                    float acc0 = pixels[0] * k[0];
                    float acc1 = pixels[1] * k[1];
                    float acc2 = pixels[2] * k[2];
                    for (int i = 3; i < 24; i += 3)
                    {
                        acc0 = (pixels[i] * k[i]) + acc0;
                        acc1 = (pixels[i + 1] * k[i + 1]) + acc1;
                        acc2 = (pixels[i + 2] * k[i + 2]) + acc2;
                    }

                    acc0 = (pixels[24] * k[24]) + acc0;
                    float result = (acc1 + acc2) + acc0;

                    // Highway's Clamp is Min(Max(v, lo), hi).
                    result = result > min ? result : min;
                    dst[ox] = result < max ? result : max;
                }
            }
        }
    }
}
