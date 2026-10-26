// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Formats.Tiff.Constants;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing.Processors.Quantization;

namespace PinataLabs.ImagePastel.Formats.Tiff.Writers;

internal static class TiffColorWriterFactory
{
    public static TiffBaseColorWriter<TPixel> Create<TPixel>(
        TiffPhotometricInterpretation? photometricInterpretation,
        ImageFrame<TPixel> image,
        IQuantizer quantizer,
        MemoryAllocator memoryAllocator,
        Configuration configuration,
        TiffEncoderEntriesCollector entriesCollector,
        int bitsPerPixel)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        switch (photometricInterpretation)
        {
            case TiffPhotometricInterpretation.PaletteColor:
                return new TiffPaletteWriter<TPixel>(image, quantizer, memoryAllocator, configuration, entriesCollector, bitsPerPixel);
            case TiffPhotometricInterpretation.BlackIsZero:
            case TiffPhotometricInterpretation.WhiteIsZero:
                if (bitsPerPixel == 1)
                {
                    return new TiffBiColorWriter<TPixel>(image, memoryAllocator, configuration, entriesCollector);
                }

                return new TiffGrayWriter<TPixel>(image, memoryAllocator, configuration, entriesCollector, photometricInterpretation == TiffPhotometricInterpretation.WhiteIsZero);
            default:
                return new TiffRgbWriter<TPixel>(image, memoryAllocator, configuration, entriesCollector);
        }
    }
}
