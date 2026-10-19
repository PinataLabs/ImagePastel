// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/encoding/encoding.h and lib/jxl/modular/encoding/encoding.cc (GroupHeader, FilterTree,
// DecodeModularChannelMAANS, ValidateChannelDimensions, ModularDecode, ModularGenericDecompress).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Modular;

/// <summary>The header of a Modular sub-stream: tree choice, weighted predictor parameters and transforms.</summary>
internal sealed class JxlGroupHeader : JxlFields
{
    public JxlGroupHeader() => JxlBundle.Init(this);

    /// <summary>Gets or sets a value indicating whether the stream uses the frame's global tree and histograms.</summary>
    public bool UseGlobalTree { get; set; }

    /// <summary>Gets the weighted predictor parameters.</summary>
    public JxlWeightedHeader WpHeader { get; } = new();

    /// <summary>Gets the transforms, in the order they were applied.</summary>
    public List<JxlTransform> Transforms { get; } = new();

    public override void VisitFields(JxlVisitor visitor)
    {
        this.UseGlobalTree = visitor.Bool(false, this.UseGlobalTree);
        visitor.VisitNested(this.WpHeader);
        uint numTransforms = visitor.U32(Val(0), Val(1), BitsOffset(4, 2), BitsOffset(8, 18), 0, (uint)this.Transforms.Count);
        if (visitor.IsReading)
        {
            this.Transforms.Clear();
            for (uint i = 0; i < numTransforms; i++)
            {
                this.Transforms.Add(new JxlTransform());
            }
        }

        for (int i = 0; i < numTransforms; i++)
        {
            visitor.VisitNested(this.Transforms[i]);
        }
    }
}
