// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_ans.h (Token).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Formats.Jxl.Entropy;

/// <summary>
/// An integer to be encoded by an entropy coder, either ANS or prefix codes, in a context. An LZ77
/// copy is two tokens: its length, marked with <see cref="IsLz77Length"/>, then its distance.
/// </summary>
internal readonly struct JxlToken
{
    public JxlToken(uint context, uint value, bool isLz77Length = false)
    {
        this.Context = context;
        this.Value = value;
        this.IsLz77Length = isLz77Length;
    }

    /// <summary>Gets the context.</summary>
    public uint Context { get; }

    /// <summary>Gets the value.</summary>
    public uint Value { get; }

    /// <summary>Gets a value indicating whether the value is the length of an LZ77 copy.</summary>
    public bool IsLz77Length { get; }
}
