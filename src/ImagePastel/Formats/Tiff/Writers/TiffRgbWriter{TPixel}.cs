// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats.Tiff.Writers;

internal sealed class TiffRgbWriter<TPixel> : TiffCompositeColorWriter<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    public TiffRgbWriter(ImageFrame<TPixel> image, MemoryAllocator memoryAllocator, Configuration configuration, TiffEncoderEntriesCollector entriesCollector)
        : base(image, memoryAllocator, configuration, entriesCollector)
    {
    }

    /// <inheritdoc />
    public override int BitsPerPixel => 24;

    /// <inheritdoc />
    protected override void EncodePixels(Span<TPixel> pixels, Span<byte> buffer) => PixelOperations<TPixel>.Instance.ToRgb24Bytes(this.Configuration, pixels, buffer, pixels.Length);
}
