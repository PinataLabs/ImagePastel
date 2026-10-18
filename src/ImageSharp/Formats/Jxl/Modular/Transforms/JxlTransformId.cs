// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/transform/transform.h and lib/jxl/modular/transform/transform.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;

/// <summary>The Modular transforms.</summary>
internal enum JxlTransformId : uint
{
    /// <summary>Reversible colour transform: a permutation and a YCoCg-R or simpler decorrelation of three channels.</summary>
    Rct = 0,

    /// <summary>Palette: channels become an index channel plus a palette meta channel.</summary>
    Palette = 1,

    /// <summary>Squeeze: a Haar-like wavelet transform.</summary>
    Squeeze = 2,

    /// <summary>Invalid. Also used to mark an uninitialized transform.</summary>
    Invalid = 3,
}
