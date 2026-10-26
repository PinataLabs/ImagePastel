// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/encoding/context_predict.h (FlatDecisionNode, MATreeLookup, ClampedGradient, Select,
// PrecomputeReferences, InitPropsRow, PredictOne, Predict and its decoder wrappers).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PinataLabs.ImagePastel.Formats.Jxl.Modular;

/// <summary>
/// A node of a flattened MA tree: a split with both of its children's splits, so one lookup step
/// descends two levels. libjxl overlays the leaf fields on the split fields with unions; here they
/// are separate fields.
/// </summary>
internal struct JxlFlatDecisionNode
{
    /// <summary>The property of the first split; -1 for a leaf.</summary>
    public int Property0;

    public int SplitVal0;

    public int SplitVals0;

    public int SplitVals1;

    /// <summary>The first of the four grandchildren, or the context id of a leaf.</summary>
    public uint ChildId;

    public short Properties0;

    public short Properties1;

    // Leaf fields.
    public JxlPredictor Predictor;

    public int Multiplier;

    public int PredictorOffset;
}
