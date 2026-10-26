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

/// <summary>The result of predicting one sample.</summary>
internal struct JxlPredictionResult
{
    public int Context;

    public long Guess;

    public JxlPredictor Predictor;

    public int Multiplier;
}
