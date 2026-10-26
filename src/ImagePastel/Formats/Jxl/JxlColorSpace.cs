// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/image_metadata.h (Orientation, ExtraChannel) and lib/jxl/cms/color_encoding_cms.h
// (ColorSpace, WhitePoint, Primaries, TransferFunction, RenderingIntent), with their EnumBits from
// lib/jxl/image_metadata.h and lib/jxl/color_encoding_internal.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Specifies the colour space of the colour channels of a JPEG XL image. The values are libjxl's.
/// </summary>
public enum JxlColorSpace : uint
{
    /// <summary>
    /// Red, green and blue channels.
    /// </summary>
    Rgb = 0,

    /// <summary>
    /// A single grey channel.
    /// </summary>
    Gray = 1,

    /// <summary>
    /// The XYB colour space of JPEG XL, which has no primaries.
    /// </summary>
    Xyb = 2,

    /// <summary>
    /// An unknown colour space. A stream may signal it only with an ICC profile.
    /// </summary>
    Unknown = 3,
}
