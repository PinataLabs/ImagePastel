// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Bmp;

/// <summary>
/// The rendering intent of a bitmap, stored in the <c>bV5Intent</c> field of the BITMAPV5HEADER structure.
/// </summary>
/// <seealso href="https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-bitmapv5header"/>
internal enum BmpRenderingIntent
{
    /// <summary>
    /// <c>LCS_GM_BUSINESS</c>: the ICC saturation intent.
    /// </summary>
    Business = 1,

    /// <summary>
    /// <c>LCS_GM_GRAPHICS</c>: the ICC relative colorimetric intent.
    /// </summary>
    Graphics = 2,

    /// <summary>
    /// <c>LCS_GM_IMAGES</c>: the ICC perceptual intent.
    /// </summary>
    Images = 4,

    /// <summary>
    /// <c>LCS_GM_ABS_COLORIMETRIC</c>: the ICC absolute colorimetric intent.
    /// </summary>
    AbsoluteColorimetric = 8,
}
