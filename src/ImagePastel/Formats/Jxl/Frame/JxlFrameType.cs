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

/// <summary>The role of a frame.</summary>
internal enum JxlFrameType : uint
{
    /// <summary>A regular frame: shown, and may be saved as a reference.</summary>
    Regular = 0,

    /// <summary>A DC frame: the 1:8 DC of a later frame.</summary>
    Dc = 1,

    /// <summary>A frame only saved as a reference for patches or blending.</summary>
    ReferenceOnly = 2,

    /// <summary>A regular frame that is not shown on its own in progressive decoding.</summary>
    SkipProgressive = 3,
}
