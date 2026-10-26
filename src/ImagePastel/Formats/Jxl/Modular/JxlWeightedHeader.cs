// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/encoding/context_predict.h (namespace weighted: Header, State).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Runtime.CompilerServices;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;

namespace PinataLabs.ImagePastel.Formats.Jxl.Modular;

/// <summary>The parameters of the self-correcting weighted predictor.</summary>
internal sealed class JxlWeightedHeader : JxlFields
{
    public JxlWeightedHeader() => JxlBundle.Init(this);

    public int P1C { get; set; }

    public int P2C { get; set; }

    public int P3Ca { get; set; }

    public int P3Cb { get; set; }

    public int P3Cc { get; set; }

    public int P3Cd { get; set; }

    public int P3Ce { get; set; }

    /// <summary>Gets the maximum weight of each of the four sub-predictors.</summary>
    public uint[] W { get; } = new uint[JxlWeightedState.NumPredictors];

    public override void VisitFields(JxlVisitor visitor)
    {
        if (visitor.AllDefault(this))
        {
            visitor.SetDefault(this);
            return;
        }

        this.P1C = VisitP(visitor, 16, this.P1C);
        this.P2C = VisitP(visitor, 10, this.P2C);
        this.P3Ca = VisitP(visitor, 7, this.P3Ca);
        this.P3Cb = VisitP(visitor, 7, this.P3Cb);
        this.P3Cc = VisitP(visitor, 7, this.P3Cc);
        this.P3Cd = VisitP(visitor, 0, this.P3Cd);
        this.P3Ce = VisitP(visitor, 0, this.P3Ce);
        this.W[0] = visitor.Bits(4, 0xd, this.W[0]);
        this.W[1] = visitor.Bits(4, 0xc, this.W[1]);
        this.W[2] = visitor.Bits(4, 0xc, this.W[2]);
        this.W[3] = visitor.Bits(4, 0xc, this.W[3]);
    }

    private static int VisitP(JxlVisitor visitor, int val, int p) => (int)visitor.Bits(5, (uint)val, (uint)p);
}
