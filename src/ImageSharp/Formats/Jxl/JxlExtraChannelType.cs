// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/image_metadata.h (Orientation, ExtraChannel) and lib/jxl/cms/color_encoding_cms.h
// (ColorSpace, WhitePoint, Primaries, TransferFunction, RenderingIntent), with their EnumBits from
// lib/jxl/image_metadata.h and lib/jxl/color_encoding_internal.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace SixLabors.ImageSharp.Formats.Jxl;

// Each enum defines exactly the values in libjxl's EnumBits, because JxlVisitor.Enum
// rejects any value the enum does not define.

/// <summary>
/// Specifies the type of an extra channel of a JPEG XL image. The values are libjxl's; its reserved values
/// are invalid in a stream, so they are left out.
/// </summary>
public enum JxlExtraChannelType : uint
{
    /// <summary>
    /// An alpha channel.
    /// </summary>
    Alpha = 0,

    /// <summary>
    /// A depth map.
    /// </summary>
    Depth = 1,

    /// <summary>
    /// A spot colour channel: the amount of an extra ink.
    /// </summary>
    SpotColor = 2,

    /// <summary>
    /// A selection mask.
    /// </summary>
    SelectionMask = 3,

    /// <summary>
    /// The black (K) channel of a CMYK image.
    /// </summary>
    Black = 4,

    /// <summary>
    /// A colour filter array channel: the raw samples of a sensor with a Bayer or similar pattern.
    /// </summary>
    Cfa = 5,

    /// <summary>
    /// A thermal channel.
    /// </summary>
    Thermal = 6,

    /// <summary>
    /// An unknown channel type. The decoder never gives it: as in libjxl, a stream that signals it is invalid.
    /// </summary>
    Unknown = 15,

    /// <summary>
    /// An optional channel that a decoder may ignore.
    /// </summary>
    Optional = 16,
}
