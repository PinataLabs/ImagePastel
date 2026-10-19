// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Writes an image as a bare JPEG XL codestream.
/// </summary>
internal sealed class JxlEncoderCore : IImageEncoderInternals
{
    private readonly MemoryAllocator memoryAllocator;
    private readonly float distance;
    private readonly JxlEffort effort;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlEncoderCore"/> class.
    /// </summary>
    /// <param name="options">The encoder options.</param>
    /// <param name="memoryAllocator">The memory allocator.</param>
    public JxlEncoderCore(IJxlEncoderOptions options, MemoryAllocator memoryAllocator)
    {
        this.memoryAllocator = memoryAllocator;
        this.distance = options.Distance;
        this.effort = options.Effort;
    }

    /// <inheritdoc/>
    public void Encode<TPixel>(Image<TPixel> image, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(image, nameof(image));
        Guard.NotNull(stream, nameof(stream));
        this.CheckOptions();
        throw new NotImplementedException();
    }

    // Only lossless at Fastest is ported. Each other combination names the slice of the spec that adds it.
    private void CheckOptions()
    {
        if (this.distance != 0)
        {
            throw new NotSupportedException($"JPEG XL lossy encoding (distance {this.distance}) is not supported yet; slice E3 adds it.");
        }

        if (this.effort != JxlEffort.Fastest)
        {
            throw new NotSupportedException($"JPEG XL lossless encoding at effort {this.effort} is not supported yet; slice E2 adds it.");
        }
    }
}
