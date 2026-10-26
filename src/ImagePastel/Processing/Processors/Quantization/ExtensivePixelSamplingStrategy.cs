// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Processing.Processors.Quantization;

/// <summary>
/// A pixel sampling strategy that enumerates all pixels.
/// </summary>
public class ExtensivePixelSamplingStrategy : IPixelSamplingStrategy
{
    /// <inheritdoc />
    public IEnumerable<Buffer2DRegion<TPixel>> EnumeratePixelRegions<TPixel>(Image<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        foreach (ImageFrame<TPixel> frame in image.Frames)
        {
            yield return frame.PixelBuffer.GetRegion();
        }
    }
}
