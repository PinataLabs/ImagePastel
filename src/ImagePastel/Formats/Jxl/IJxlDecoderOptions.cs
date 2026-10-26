// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>
/// Image decoder options for decoding a JPEG XL stream.
/// </summary>
internal interface IJxlDecoderOptions : IColorProfileDecoderOptions
{
    /// <summary>
    /// Gets the maximum number of displayed frames to decode, with the semantics of
    /// <see cref="Gif.IGifDecoderOptions.MaxFrames"/>: decoding stops once that many are out.
    /// </summary>
    uint MaxFrames { get; }
}
