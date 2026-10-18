// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Bmp;

/// <summary>
/// The color space of a bitmap, stored in the <c>bV4CSType</c> and <c>bV5CSType</c> fields of the
/// BITMAPV4HEADER and BITMAPV5HEADER structures.
/// </summary>
/// <seealso href="https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-bitmapv5header"/>
internal enum BmpColorSpaceType
{
    /// <summary>
    /// <c>LCS_CALIBRATED_RGB</c>: the header's endpoints and gamma values describe the color space.
    /// </summary>
    CalibratedRgb = 0,

    /// <summary>
    /// <c>LCS_sRGB</c> (<c>'sRGB'</c>): the bitmap is in the sRGB color space.
    /// </summary>
    Srgb = 0x73524742,

    /// <summary>
    /// <c>LCS_WINDOWS_COLOR_SPACE</c> (<c>'Win '</c>): the bitmap is in the system default color space, sRGB.
    /// </summary>
    WindowsColorSpace = 0x57696E20,

    /// <summary>
    /// <c>PROFILE_LINKED</c> (<c>'LINK'</c>): the profile data is the file name of a profile.
    /// </summary>
    ProfileLinked = 0x4C494E4B,

    /// <summary>
    /// <c>PROFILE_EMBEDDED</c> (<c>'MBED'</c>): the profile data is an ICC profile.
    /// </summary>
    ProfileEmbedded = 0x4D424544,
}
