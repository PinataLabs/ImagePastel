// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Bmp;

/// <summary>
/// Image decoder options for decoding Windows bitmap streams.
/// </summary>
internal interface IBmpDecoderOptions
{
    /// <summary>
    /// Gets the value indicating how to deal with skipped pixels, which can occur during decoding run length encoded bitmaps.
    /// </summary>
    RleSkippedPixelHandling RleSkippedPixelHandling { get; }

    /// <summary>
    /// Gets a value indicating whether the metadata should be ignored when the image is being decoded.
    /// When set, the embedded ICC profile is not read.
    /// </summary>
    bool IgnoreMetadata { get; }
}
