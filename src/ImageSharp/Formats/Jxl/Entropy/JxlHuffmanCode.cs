// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_huffman.h, lib/jxl/dec_huffman.cc, lib/jxl/huffman_table.h and lib/jxl/huffman_table.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Formats.Jxl.Entropy;

/// <summary>One entry of a prefix-code lookup table.</summary>
internal struct JxlHuffmanCode
{
    /// <summary>The number of bits used for this symbol.</summary>
    public byte Bits;

    /// <summary>The symbol value, or the offset of a second-level table.</summary>
    public ushort Value;

    public JxlHuffmanCode(byte bits, ushort value)
    {
        this.Bits = bits;
        this.Value = value;
    }
}
