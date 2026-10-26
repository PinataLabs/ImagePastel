// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/convolve_symmetric5.cc (Symmetric5), for one lane.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.Base;

/// <summary>
/// libjxl's convolutions of the encoder.
/// </summary>
internal static class JxlConvolve
{
    /// <summary>
    /// Convolves <paramref name="inRect"/> of <paramref name="input"/> with a symmetric 5x5 kernel into
    /// <paramref name="outRect"/> of <paramref name="output"/>, as libjxl's <c>Symmetric5</c>. Pixels
    /// outside the input image are mirrored (the edge pixel repeated once).
    /// </summary>
    /// <remarks>
    /// libjxl splits each row into a vector interior and scalar borders, which share one order of
    /// operations: each row of the kernel is <c>sum_2 + (sum_1 + sum_0)</c>, and the rows add up as
    /// <c>((r0 + r-2) + r-1) + (r+2 + r+1)</c>. There are only multiplies and adds (no fused multiply-add),
    /// so this one-lane port gives libjxl's values bit for bit at any vector width.
    /// </remarks>
    /// <param name="input">The input image.</param>
    /// <param name="inRect">The rectangle of the input to convolve.</param>
    /// <param name="weights">The kernel.</param>
    /// <param name="output">The output image; not <paramref name="input"/>.</param>
    /// <param name="outRect">The rectangle of the output, of the size of <paramref name="inRect"/>.</param>
    public static void Symmetric5(Buffer2D<float> input, JxlRect inRect, in JxlWeightsSymmetric5 weights, Buffer2D<float> output, JxlRect outRect)
    {
        DebugGuard.IsTrue(inRect.XSize == outRect.XSize && inRect.YSize == outRect.YSize, "The rectangles have one size");
        int xsize = input.Width;
        int ysize = input.Height;
        float w0 = weights.C;
        float w1 = weights.R;
        float w2 = weights.BigR;
        float w4 = weights.D;
        float w5 = weights.L;
        float w8 = weights.BigD;

        // The rows are independent: this loop is where libjxl runs them in parallel.
        for (int riy = 0; riy < inRect.YSize; riy++)
        {
            int iy = (int)inRect.Y0 + riy;
            Span<float> rowOut = output.DangerousGetRowSpan((int)outRect.Y0 + riy).Slice((int)outRect.X0, (int)outRect.XSize);
            ReadOnlySpan<float> row0 = input.DangerousGetRowSpan(JxlNeighbourRows.Mirror(iy, ysize));
            ReadOnlySpan<float> rowM2 = input.DangerousGetRowSpan(JxlNeighbourRows.Mirror(iy - 2, ysize));
            ReadOnlySpan<float> rowP2 = input.DangerousGetRowSpan(JxlNeighbourRows.Mirror(iy + 2, ysize));
            ReadOnlySpan<float> rowM1 = input.DangerousGetRowSpan(JxlNeighbourRows.Mirror(iy - 1, ysize));
            ReadOnlySpan<float> rowP1 = input.DangerousGetRowSpan(JxlNeighbourRows.Mirror(iy + 1, ysize));
            for (int rix = 0; rix < inRect.XSize; rix++)
            {
                int ix = (int)inRect.X0 + rix;

                // Unrolled loop over all 5 rows of the kernel.
                float sum0 = WeightedSum(row0, ix, xsize, w0, w1, w2);
                sum0 += WeightedSum(rowM2, ix, xsize, w2, w5, w8);
                float sum1 = WeightedSum(rowP2, ix, xsize, w2, w5, w8);
                sum0 += WeightedSum(rowM1, ix, xsize, w1, w4, w5);
                sum1 += WeightedSum(rowP1, ix, xsize, w1, w4, w5);
                rowOut[rix] = sum0 + sum1;
            }
        }
    }

    // Weighted sum of 1x5 pixels around ix with [wx2 wx1 wx0 wx1 wx2] (libjxl's WeightedSumBorder).
    private static float WeightedSum(ReadOnlySpan<float> row, int ix, int xsize, float wx0, float wx1, float wx2)
    {
        float inM2 = row[JxlNeighbourRows.Mirror(ix - 2, xsize)];
        float inP2 = row[JxlNeighbourRows.Mirror(ix + 2, xsize)];
        float inM1 = row[JxlNeighbourRows.Mirror(ix - 1, xsize)];
        float inP1 = row[JxlNeighbourRows.Mirror(ix + 1, xsize)];
        float in00 = row[ix];
        float sum2 = wx2 * (inM2 + inP2);
        float sum1 = wx1 * (inM1 + inP1);
        float sum0 = wx0 * in00;
        return sum2 + (sum1 + sum0);
    }
}
