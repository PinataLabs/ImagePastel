// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/transform/squeeze_params.h and lib/jxl/modular/transform/squeeze_params.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using static PinataLabs.ImagePastel.Formats.Jxl.Fields.JxlU32Distr;

namespace PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms;

/// <summary>One step of a squeeze transform: which channels it halves, and in which direction.</summary>
internal sealed class JxlSqueezeParams : JxlFields
{
    public JxlSqueezeParams() => JxlBundle.Init(this);

    /// <summary>Gets or sets a value indicating whether the step halves the width rather than the height.</summary>
    public bool Horizontal { get; set; }

    /// <summary>Gets or sets a value indicating whether the residual channels follow the squeezed channels, rather than go last.</summary>
    public bool InPlace { get; set; }

    /// <summary>Gets or sets the first channel the step squeezes.</summary>
    public uint BeginC { get; set; }

    /// <summary>Gets or sets the number of channels the step squeezes.</summary>
    public uint NumC { get; set; }

    public override void VisitFields(JxlVisitor visitor)
    {
        this.Horizontal = visitor.Bool(false, this.Horizontal);
        this.InPlace = visitor.Bool(false, this.InPlace);
        this.BeginC = visitor.U32(Bits(3), BitsOffset(6, 8), BitsOffset(10, 72), BitsOffset(13, 1096), 0, this.BeginC);
        this.NumC = visitor.U32(Val(1), Val(2), Val(3), BitsOffset(4, 4), 2, this.NumC);
    }
}
