// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Metadata;

namespace PinataLabs.ImagePastel.Formats.Gif;

/// <summary>
/// Decoder for generating an image out of a gif encoded stream.
/// </summary>
internal interface IGifDecoderOptions : IColorProfileDecoderOptions
{
    /// <summary>
    /// Gets the decoding mode for multi-frame images.
    /// </summary>
    FrameDecodingMode DecodingMode { get; }

    /// <summary>
    /// Gets or sets the maximum number of gif frames.
    /// </summary>
    uint MaxFrames { get; set; }
}
