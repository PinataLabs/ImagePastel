// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_ans.h (Token).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace SixLabors.ImageSharp.Formats.Jxl.Entropy;

/// <summary>
/// An integer to be encoded by an entropy coder, either ANS or prefix codes, in a context. libjxl's
/// token also carries an <c>is_lz77_length</c> bit; the encoder writes no LZ77 yet, so it is left out.
/// </summary>
internal readonly struct JxlToken
{
    public JxlToken(uint context, uint value)
    {
        this.Context = context;
        this.Value = value;
    }

    /// <summary>Gets the context.</summary>
    public uint Context { get; }

    /// <summary>Gets the value.</summary>
    public uint Value { get; }
}
