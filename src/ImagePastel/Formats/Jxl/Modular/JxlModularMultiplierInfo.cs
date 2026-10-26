// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/options.h (ModularMultiplierInfo).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace SixLabors.ImageSharp.Formats.Jxl.Modular;

/// <summary>
/// The residual multiplier of the channels and streams in <see cref="Range"/>, which quantised (lossy)
/// channels have. Tree learning gives each such box a leaf with the multiplier.
/// </summary>
internal struct JxlModularMultiplierInfo
{
    /// <summary>The channels and streams.</summary>
    public JxlStaticPropRange Range;

    /// <summary>The multiplier.</summary>
    public uint Multiplier;
}
