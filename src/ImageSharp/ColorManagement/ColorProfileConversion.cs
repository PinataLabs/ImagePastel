// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.ColorManagement;

/// <summary>
/// Selects whether a decoder converts the decoded pixels from the file's embedded ICC profile to
/// another color space before it returns the image.
/// </summary>
/// <remarks>
/// <para>
/// The conversion runs only when the file has an embedded RGB or gray profile that the
/// <see cref="IccColorTransform"/> engine supports. An image without a profile, or with a profile
/// that is corrupt or not supported yet, is returned as decoded, with its profile kept.
/// When the decoder ignores metadata, it reads no profile and converts nothing.
/// </para>
/// <para>
/// The conversion uses the default <see cref="IccColorTransformOptions"/>.
/// </para>
/// </remarks>
public enum ColorProfileConversion
{
    /// <summary>
    /// Do not convert. The pixels stay as the file stores them, and the embedded profile becomes the
    /// pixel profile (<c>Metadata.IccProfile</c>). This is the default.
    /// </summary>
    None = 0,

    /// <summary>
    /// Convert the pixels to sRGB. The image then has no pixel profile.
    /// </summary>
    ToSrgb = 1,

    /// <summary>
    /// Convert the pixels to the profile that the decoder's <c>ColorProfileConversionTarget</c> option gives.
    /// That profile (a copy) becomes the pixel profile.
    /// </summary>
    ToTargetProfile = 2,
}
