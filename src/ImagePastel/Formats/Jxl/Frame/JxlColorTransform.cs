// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/frame_header.h and lib/jxl/frame_header.cc (YCbCrChromaSubsampling, BlendingInfo,
// AnimationFrame, Passes, FrameHeader), and lib/jxl/frame_dimensions.h (FrameDimensions).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <summary>The colour transform of a frame's colour channels.</summary>
internal enum JxlColorTransform : uint
{
    /// <summary>Values are encoded with XYB. May only be used if the image is XYB-encoded.</summary>
    Xyb = 0,

    /// <summary>Values are encoded according to the attached colour profile.</summary>
    None = 1,

    /// <summary>Values are encoded according to the attached colour profile, but transformed to YCbCr.</summary>
    YCbCr = 2,
}
