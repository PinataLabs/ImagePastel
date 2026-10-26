// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/image_metadata.h (Orientation, ExtraChannel) and lib/jxl/cms/color_encoding_cms.h
// (ColorSpace, WhitePoint, Primaries, TransferFunction, RenderingIntent), with their EnumBits from
// lib/jxl/image_metadata.h and lib/jxl/color_encoding_internal.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>
/// Specifies the white point of a JPEG XL colour encoding. The values are libjxl's.
/// </summary>
public enum JxlWhitePoint : uint
{
    /// <summary>
    /// The CIE standard illuminant D65: x 0.3127, y 0.3290.
    /// </summary>
    D65 = 1,

    /// <summary>
    /// A custom white point, given by <see cref="JxlColorEncoding.CustomWhitePoint"/>.
    /// </summary>
    Custom = 2,

    /// <summary>
    /// The CIE standard illuminant E, equal energy: x 1/3, y 1/3.
    /// </summary>
    E = 10,

    /// <summary>
    /// The white point of the DCI-P3 reference projector (SMPTE RP 431-2): x 0.314, y 0.351.
    /// </summary>
    Dci = 11,
}
