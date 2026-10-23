// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_gaborish.cc (GaborishInverse).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The encoder side of the Gaborish loop filter: an approximate inverse of the decoder's 3x3 blur,
/// applied to the opsin image before the DCT so that the decoder's blur gives it back.
/// </summary>
internal static class JxlEncGaborish
{
    // Only an approximation. One or even two 3x3, and rank-1 (separable) 5x5 are insufficient. The numbers
    // here have been obtained by butteraugli based optimizing the whole system and the errors produced are
    // likely more favorable for good rate-distortion compromises rather than just using mathematical
    // optimization to find the inverse.
    private static readonly float[] Gaborish =
    {
        -0.09495815671340026f, -0.041031725066768575f, 0.013710004822696948f,
        0.006510206083837737f, -0.0014789063378272242f,
    };

    /// <summary>
    /// Filters each of the three planes in place with the 5x5 inverse Gaborish kernel scaled by its
    /// <paramref name="mul"/>, as libjxl's <c>GaborishInverse</c> does over the whole image.
    /// </summary>
    /// <remarks>
    /// libjxl filters <c>rect.Extend(3, image)</c>; the encoder always passes the whole (padded) image, so
    /// that is the whole image here too. libjxl juggles one spare plane to save memory; the result is each
    /// plane filtered on its own.
    /// </remarks>
    /// <param name="inOut">The three planes; each is replaced by its filtered copy.</param>
    /// <param name="mul">The weight of the filter of each plane: 1 inverts libjxl's default Gaborish.</param>
    /// <param name="allocator">The allocator.</param>
    public static void GaborishInverse(Buffer2D<float>[] inOut, ReadOnlySpan<float> mul, MemoryAllocator allocator)
    {
        for (int i = 0; i < 3; ++i)
        {
            // libjxl sums the kernel in float and adds it to 1 in double.
            double sum = 1.0 + (mul[i] * 4 * (Gaborish[0] + Gaborish[1] + Gaborish[2] + Gaborish[4] + (2 * Gaborish[3])));
            if (sum < 1e-5)
            {
                sum = 1e-5;
            }

            float normalize = (float)(1.0 / sum);
            float normalizeMul = mul[i] * normalize;
            JxlWeightsSymmetric5 weights = new(
                normalize,
                normalizeMul * Gaborish[0],
                normalizeMul * Gaborish[2],
                normalizeMul * Gaborish[1],
                normalizeMul * Gaborish[4],
                normalizeMul * Gaborish[3]);

            Buffer2D<float> plane = inOut[i];
            JxlRect rect = new(0, 0, plane.Width, plane.Height);
            Buffer2D<float> filtered = allocator.Allocate2D<float>(plane.Width, plane.Height);
            JxlConvolve.Symmetric5(plane, rect, weights, filtered, rect);
            inOut[i] = filtered;
            plane.Dispose();
        }
    }
}
