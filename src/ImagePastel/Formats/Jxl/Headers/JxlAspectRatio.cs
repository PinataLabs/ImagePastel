// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/headers.h and lib/jxl/headers.cc (SizeHeader, PreviewHeader, AnimationHeader).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Headers;

/// <summary>Helpers shared by the size headers.</summary>
internal static class JxlAspectRatio
{
    // Other candidates: 5/4, 7/5, 14/9, 16/10, 5/3, 21/9, 12/5.
    private static readonly (uint Num, uint Den)[] Ratios =
    {
        (1, 1), // square
        (12, 10),
        (4, 3), // camera
        (3, 2), // mobile camera
        (16, 9), // camera/display
        (5, 4),
        (2, 1),
    };

    /// <summary>Returns floor(<paramref name="ysize"/> * ratio) for a ratio code 1 to 7.</summary>
    /// <param name="ratio">The ratio code.</param>
    /// <param name="ysize">The height.</param>
    /// <returns>The width.</returns>
    public static ulong MulTruncate(uint ratio, ulong ysize)
    {
        (uint num, uint den) = Ratios[ratio - 1];
        return (ulong)(uint)ysize * num / den;
    }

    /// <summary>Returns the ratio code 1 to 7 that gives <paramref name="xsize"/> from <paramref name="ysize"/>, or 0.</summary>
    /// <param name="xsize">The width.</param>
    /// <param name="ysize">The height.</param>
    /// <returns>The ratio code, or 0 if the width must be sent.</returns>
    public static uint Find(uint xsize, uint ysize)
    {
        for (uint r = 1; r < 8; ++r)
        {
            if (xsize == MulTruncate(r, ysize))
            {
                return r;
            }
        }

        return 0; // Must send xsize instead
    }
}
