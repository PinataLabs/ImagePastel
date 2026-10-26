// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/encoding/dec_ma.h, lib/jxl/modular/encoding/dec_ma.cc and
// lib/jxl/modular/encoding/ma_common.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Entropy;

namespace PinataLabs.ImagePastel.Formats.Jxl.Modular;

/// <summary>A node of a meta-adaptive (MA) tree: a split on a property, or a leaf.</summary>
internal struct JxlPropertyDecisionNode
{
    public int SplitVal;

    /// <summary>The property to split on; -1 for a leaf, whose <see cref="LChild"/> is the leaf id.</summary>
    public short Property;

    public uint LChild;

    public uint RChild;

    public JxlPredictor Predictor;

    public long PredictorOffset;

    public uint Multiplier;

    public JxlPropertyDecisionNode(int property, int splitVal, uint lchild, uint rchild, JxlPredictor predictor, long predictorOffset, uint multiplier)
    {
        this.SplitVal = splitVal;
        this.Property = (short)property;
        this.LChild = lchild;
        this.RChild = rchild;
        this.Predictor = predictor;
        this.PredictorOffset = predictorOffset;
        this.Multiplier = multiplier;
    }
}
