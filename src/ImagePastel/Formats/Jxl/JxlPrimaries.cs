// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/image_metadata.h (Orientation, ExtraChannel) and lib/jxl/cms/color_encoding_cms.h
// (ColorSpace, WhitePoint, Primaries, TransferFunction, RenderingIntent), with their EnumBits from
// lib/jxl/image_metadata.h and lib/jxl/color_encoding_internal.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>
/// Specifies the red, green and blue primaries of a JPEG XL colour encoding. The values are libjxl's.
/// </summary>
public enum JxlPrimaries : uint
{
    /// <summary>
    /// The primaries of sRGB and ITU-R BT.709.
    /// </summary>
    Srgb = 1,

    /// <summary>
    /// Custom primaries, given by <see cref="JxlColorEncoding.CustomRedPrimary"/>,
    /// <see cref="JxlColorEncoding.CustomGreenPrimary"/> and <see cref="JxlColorEncoding.CustomBluePrimary"/>.
    /// </summary>
    Custom = 2,

    /// <summary>
    /// The primaries of ITU-R BT.2100 and BT.2020.
    /// </summary>
    Bt2100 = 9,

    /// <summary>
    /// The primaries of DCI-P3 and Display P3.
    /// </summary>
    P3 = 11,
}
