// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing.Processors.Quantization;

namespace PinataLabs.ImagePastel.Formats.Gif;

/// <summary>
/// The configuration options used for encoding gifs.
/// </summary>
internal interface IGifEncoderOptions
{
    /// <summary>
    /// Gets the quantizer used to generate the color palette.
    /// </summary>
    IQuantizer Quantizer { get; }

    /// <summary>
    /// Gets the color table mode: Global or local.
    /// </summary>
    GifColorTableMode? ColorTableMode { get; }

    /// <summary>
    /// Gets the <see cref="IPixelSamplingStrategy"/> used for quantization when building a global color table.
    /// </summary>
    IPixelSamplingStrategy GlobalPixelSamplingStrategy { get; }
}
