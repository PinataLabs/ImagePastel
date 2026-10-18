// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Image decoder options for decoding a JPEG XL stream.
/// </summary>
internal interface IJxlDecoderOptions
{
    /// <summary>
    /// Gets a value indicating whether the metadata should be ignored when the image is being decoded.
    /// </summary>
    bool IgnoreMetadata { get; }
}
