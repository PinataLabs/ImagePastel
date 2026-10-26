// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats.Tiff.Writers;

internal sealed class TiffGrayWriter<TPixel> : TiffCompositeColorWriter<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly bool whiteIsZero;

    public TiffGrayWriter(ImageFrame<TPixel> image, MemoryAllocator memoryAllocator, Configuration configuration, TiffEncoderEntriesCollector entriesCollector, bool whiteIsZero)
        : base(image, memoryAllocator, configuration, entriesCollector)
        => this.whiteIsZero = whiteIsZero;

    /// <inheritdoc />
    public override int BitsPerPixel => 8;

    /// <inheritdoc />
    protected override void EncodePixels(Span<TPixel> pixels, Span<byte> buffer)
    {
        PixelOperations<TPixel>.Instance.ToL8Bytes(this.Configuration, pixels, buffer, pixels.Length);

        // L8 samples are BlackIsZero, so WhiteIsZero samples are their inverse.
        if (this.whiteIsZero)
        {
            Span<byte> samples = buffer.Slice(0, pixels.Length);
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (byte)~samples[i];
            }
        }
    }
}
