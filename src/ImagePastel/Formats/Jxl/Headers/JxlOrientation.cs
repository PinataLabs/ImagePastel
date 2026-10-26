// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// Orientation in lib/jxl/image_metadata.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace SixLabors.ImageSharp.Formats.Jxl.Headers;

/// <summary>
/// The EXIF orientation of the image: the transformation the decoder applies after decoding to show
/// the image the right way up. The values match the EXIF definitions.
/// </summary>
internal enum JxlOrientation : uint
{
    Identity = 1,
    FlipHorizontal = 2,
    Rotate180 = 3,
    FlipVertical = 4,
    Transpose = 5,

    /// <summary>Rotate 90 degrees clockwise.</summary>
    Rotate90 = 6,
    AntiTranspose = 7,

    /// <summary>Rotate 90 degrees counter-clockwise.</summary>
    Rotate270 = 8,
}
