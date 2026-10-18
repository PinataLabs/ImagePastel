// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/modular_image.h, lib/jxl/modular/modular_image.cc and lib/jxl/modular/options.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.Modular;

/// <summary>Limits on the channels a Modular stream decodes.</summary>
internal sealed class JxlModularOptions
{
    /// <summary>Gets or sets the largest channel decoded in this stream; larger channels are decoded per group.</summary>
    public long MaxChanSize { get; set; } = 0xFFFFFF;

    /// <summary>Gets or sets the group size.</summary>
    public long GroupDim { get; set; } = 0x1FFFFFFF;
}
