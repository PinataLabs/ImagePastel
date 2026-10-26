// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_ans.h and lib/jxl/dec_ans.cc (HybridUintConfig, LZ77Params, ANSCode).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Entropy;

/// <summary>
/// How a token splits into a value: tokens below <see cref="SplitToken"/> are the value; larger ones
/// carry the exponent and some most- and least-significant bits, and the rest follows as raw bits.
/// </summary>
internal readonly struct JxlHybridUintConfig
{
    public JxlHybridUintConfig(uint splitExponent = 4, uint msbInToken = 2, uint lsbInToken = 0)
    {
        this.SplitExponent = splitExponent;
        this.SplitToken = 1u << (int)splitExponent;
        this.MsbInToken = msbInToken;
        this.LsbInToken = lsbInToken;
    }

    public uint SplitExponent { get; }

    public uint SplitToken { get; }

    public uint MsbInToken { get; }

    public uint LsbInToken { get; }

    /// <summary>Gets the mask of the least-significant bits a token carries.</summary>
    public uint LsbMask => (1u << (int)this.LsbInToken) - 1;

    /// <summary>
    /// Splits <paramref name="value"/> into a token and the raw bits that follow it: the encoder's
    /// half of <see cref="JxlAnsSymbolReader"/>'s hybrid uint read.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="token">The token.</param>
    /// <param name="nbits">The number of raw bits.</param>
    /// <param name="bits">The raw bits.</param>
    public void Encode(uint value, out uint token, out uint nbits, out uint bits)
    {
        if (value < this.SplitToken)
        {
            token = value;
            nbits = 0;
            bits = 0;
        }
        else
        {
            uint n = (uint)JxlBits.FloorLog2Nonzero(value);
            uint m = value - (1u << (int)n);
            token = this.SplitToken +
                ((n - this.SplitExponent) << (int)(this.MsbInToken + this.LsbInToken)) +
                ((m >> (int)(n - this.MsbInToken)) << (int)this.LsbInToken) +
                (m & ((1u << (int)this.LsbInToken) - 1));
            nbits = n - this.MsbInToken - this.LsbInToken;
            bits = (uint)((value >> (int)this.LsbInToken) & ((1UL << (int)nbits) - 1));
        }
    }
}
