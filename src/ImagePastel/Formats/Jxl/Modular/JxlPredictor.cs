// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/modular_image.h, lib/jxl/modular/modular_image.cc and lib/jxl/modular/options.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.Modular;

/// <summary>The Modular predictors.</summary>
internal enum JxlPredictor : uint
{
    Zero = 0,
    Left = 1,
    Top = 2,
    Average0 = 3,
    Select = 4,
    Gradient = 5,
    Weighted = 6,
    TopRight = 7,
    TopLeft = 8,
    LeftLeft = 9,
    Average1 = 10,
    Average2 = 11,
    Average3 = 12,
    Average4 = 13,

    /// <summary>Best of Gradient and Weighted. Encoder only.</summary>
    Best = 14,

    /// <summary>Find the best decision tree for predictors/predictor per row. Encoder only.</summary>
    Variable = 15,
}
