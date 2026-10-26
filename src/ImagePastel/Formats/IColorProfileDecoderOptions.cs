// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.Formats;

/// <summary>
/// The decoder options that <see cref="DecoderColorProfileConversion"/> reads. A format's decoder
/// options interface extends this one.
/// </summary>
internal interface IColorProfileDecoderOptions
{
    /// <summary>
    /// Gets a value indicating whether the metadata should be ignored when the image is being decoded.
    /// </summary>
    bool IgnoreMetadata { get; }

    /// <summary>
    /// Gets the conversion to run from the embedded ICC profile after the pixels are decoded.
    /// </summary>
    ColorProfileConversion ColorProfileConversion { get; }

    /// <summary>
    /// Gets the profile to convert to when <see cref="ColorProfileConversion"/> is
    /// <see cref="ColorProfileConversion.ToTargetProfile"/>.
    /// </summary>
    IccProfile ColorProfileConversionTarget { get; }
}
