// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/field_encodings.h (U32Distr, U32Enc, Val, Bits, BitsOffset).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace SixLabors.ImageSharp.Formats.Jxl.Fields;

/// <summary>
/// A 32-bit field encoding: a 2-bit selector picks one of four distributions.
/// </summary>
internal readonly struct JxlU32Enc
{
    private readonly JxlU32Distr d0;
    private readonly JxlU32Distr d1;
    private readonly JxlU32Distr d2;
    private readonly JxlU32Distr d3;

    public JxlU32Enc(JxlU32Distr d0, JxlU32Distr d1, JxlU32Distr d2, JxlU32Distr d3)
    {
        this.d0 = d0;
        this.d1 = d1;
        this.d2 = d2;
        this.d3 = d3;
    }

    /// <summary>Returns the distribution for <paramref name="selector"/> 0 to 3.</summary>
    /// <param name="selector">The selector.</param>
    /// <returns>The distribution.</returns>
    public JxlU32Distr GetDistr(uint selector) => selector switch
    {
        0 => this.d0,
        1 => this.d1,
        2 => this.d2,
        _ => this.d3,
    };
}
