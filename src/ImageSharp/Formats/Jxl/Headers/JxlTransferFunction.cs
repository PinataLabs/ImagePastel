// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/image_metadata.h (Orientation, ExtraChannel) and lib/jxl/cms/color_encoding_cms.h
// (ColorSpace, WhitePoint, Primaries, TransferFunction, RenderingIntent), with their EnumBits from
// lib/jxl/image_metadata.h and lib/jxl/color_encoding_internal.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace SixLabors.ImageSharp.Formats.Jxl.Headers;

/// <summary>The transfer function of the colour encoding, when it is not a gamma value.</summary>
internal enum JxlTransferFunction : uint
{
    Bt709 = 1,
    Unknown = 2,
    Linear = 8,
    Srgb = 13,
    Pq = 16,
    Dci = 17,
    Hlg = 18,
}
