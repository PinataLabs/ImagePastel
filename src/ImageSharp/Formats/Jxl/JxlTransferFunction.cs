// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/image_metadata.h (Orientation, ExtraChannel) and lib/jxl/cms/color_encoding_cms.h
// (ColorSpace, WhitePoint, Primaries, TransferFunction, RenderingIntent), with their EnumBits from
// lib/jxl/image_metadata.h and lib/jxl/color_encoding_internal.h. Gamma is JXL_TRANSFER_FUNCTION_GAMMA of
// lib/include/jxl/color_encoding.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Specifies the transfer function of a JPEG XL colour encoding. The values are libjxl's.
/// </summary>
public enum JxlTransferFunction : uint
{
    /// <summary>
    /// The transfer function of ITU-R BT.709.
    /// </summary>
    Bt709 = 1,

    /// <summary>
    /// An unknown transfer function. A stream may signal it only with an ICC profile.
    /// </summary>
    Unknown = 2,

    /// <summary>
    /// A linear transfer function: the samples are proportional to light.
    /// </summary>
    Linear = 8,

    /// <summary>
    /// The transfer function of sRGB.
    /// </summary>
    Srgb = 13,

    /// <summary>
    /// The perceptual quantizer of ITU-R BT.2100 (SMPTE ST 2084).
    /// </summary>
    Pq = 16,

    /// <summary>
    /// The 2.6 gamma of the DCI-P3 reference projector (SMPTE RP 431-2).
    /// </summary>
    Dci = 17,

    /// <summary>
    /// The hybrid log-gamma of ITU-R BT.2100.
    /// </summary>
    Hlg = 18,

    // libjxl's public JXL_TRANSFER_FUNCTION_GAMMA. A stream codes a gamma as a flag and a number, never as
    // this value, and JxlVisitor.Enum rejects any value of 64 or more, so only the public description uses it.

    /// <summary>
    /// A gamma curve, whose exponent <see cref="JxlColorEncoding.Gamma"/> gives.
    /// </summary>
    Gamma = 65535,
}
