// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Metadata;

namespace SixLabors.ImageSharp.Formats.Tiff;

/// <summary>
/// Encapsulates the options for the <see cref="TiffDecoder"/>.
/// </summary>
internal interface ITiffDecoderOptions : IColorProfileDecoderOptions
{
    /// <summary>
    /// Gets the decoding mode for multi-frame images.
    /// </summary>
    FrameDecodingMode DecodingMode { get; }
}
