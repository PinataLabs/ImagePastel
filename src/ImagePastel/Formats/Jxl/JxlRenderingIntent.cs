// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/image_metadata.h (Orientation, ExtraChannel) and lib/jxl/cms/color_encoding_cms.h
// (ColorSpace, WhitePoint, Primaries, TransferFunction, RenderingIntent), with their EnumBits from
// lib/jxl/image_metadata.h and lib/jxl/color_encoding_internal.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>
/// Specifies the rendering intent of a JPEG XL colour encoding. The values are libjxl's, which match the
/// ICC ones.
/// </summary>
public enum JxlRenderingIntent : uint
{
    /// <summary>
    /// The perceptual rendering intent.
    /// </summary>
    Perceptual = 0,

    /// <summary>
    /// The media-relative colorimetric rendering intent.
    /// </summary>
    Relative = 1,

    /// <summary>
    /// The saturation rendering intent.
    /// </summary>
    Saturation = 2,

    /// <summary>
    /// The ICC-absolute colorimetric rendering intent.
    /// </summary>
    Absolute = 3,
}
