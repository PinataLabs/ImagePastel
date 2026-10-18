// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_ans.h and lib/jxl/dec_ans.cc (HybridUintConfig, LZ77Params, ANSCode).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

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
}
