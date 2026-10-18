// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Tiff.PhotometricInterpretation;

/// <summary>
/// Implements the 'Separated' photometric interpretation with the CMYK ink set, chunky, with 8 or 16 bits per sample.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal class CmykTiffColor<TPixel> : TiffBaseColorDecoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Configuration configuration;

    private readonly MemoryAllocator memoryAllocator;

    private readonly int bitsPerSample;

    private readonly bool isBigEndian;

    /// <summary>
    /// Initializes a new instance of the <see cref="CmykTiffColor{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="memoryAllocator">The memory allocator.</param>
    /// <param name="bitsPerSample">The bits per sample: 8 or 16.</param>
    /// <param name="isBigEndian">Whether 16-bit samples are big endian.</param>
    public CmykTiffColor(Configuration configuration, MemoryAllocator memoryAllocator, int bitsPerSample, bool isBigEndian)
    {
        this.configuration = configuration;
        this.memoryAllocator = memoryAllocator;
        this.bitsPerSample = bitsPerSample;
        this.isBigEndian = isBigEndian;
    }

    /// <inheritdoc/>
    public override void Decode(ReadOnlySpan<byte> data, Buffer2D<TPixel> pixels, int left, int top, int width, int height)
    {
        using IMemoryOwner<Vector4> rowBuffer = this.memoryAllocator.Allocate<Vector4>(width);
        Span<Vector4> row = rowBuffer.GetSpan();
        int bytesPerRow = width * this.bitsPerSample / 2;
        int offset = 0;
        for (int y = top; y < top + height; y++)
        {
            TiffCmykSamples.ReadChunky(data.Slice(offset, bytesPerRow), this.bitsPerSample, this.isBigEndian, row);
            TiffCmykFormula.ToRgb(row);

            Span<TPixel> pixelRow = pixels.DangerousGetRowSpan(y).Slice(left, width);
            PixelOperations<TPixel>.Instance.FromVector4Destructive(this.configuration, row, pixelRow, PixelConversionModifiers.Scale);
            offset += bytesPerRow;
        }
    }
}
