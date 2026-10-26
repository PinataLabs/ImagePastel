// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_ans.h and lib/jxl/dec_ans.cc (HybridUintConfig, LZ77Params, ANSCode).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using static PinataLabs.ImagePastel.Formats.Jxl.Fields.JxlU32Distr;

namespace PinataLabs.ImagePastel.Formats.Jxl.Entropy;

/// <summary>Whether and how LZ77 is used in an entropy-coded stream.</summary>
internal sealed class JxlLz77Params : JxlFields
{
    public JxlLz77Params() => JxlBundle.Init(this);

    /// <summary>Gets or sets a value indicating whether LZ77 is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the first token that starts a copy. Tokens from it on are lengths.</summary>
    public uint MinSymbol { get; set; }

    /// <summary>Gets or sets the shortest copy length.</summary>
    public uint MinLength { get; set; }

    /// <summary>Gets or sets the hybrid uint configuration of copy lengths.</summary>
    public JxlHybridUintConfig LengthUintConfig { get; set; } = new(0, 0, 0);

    /// <summary>Gets or sets the context of copy distances, which is not serialized here.</summary>
    public int NonserializedDistanceContext { get; set; }

    public override void VisitFields(JxlVisitor visitor)
    {
        this.Enabled = visitor.Bool(false, this.Enabled);
        if (!visitor.Conditional(this.Enabled))
        {
            return;
        }

        this.MinSymbol = visitor.U32(Val(224), Val(512), Val(4096), BitsOffset(15, 8), 224, this.MinSymbol);
        this.MinLength = visitor.U32(Val(3), Val(4), BitsOffset(2, 5), BitsOffset(8, 9), 3, this.MinLength);
    }
}
