// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

// ReSharper disable InconsistentNaming
using PinataLabs.ImagePastel.Formats;
using PinataLabs.ImagePastel.Formats.Tiff;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using PinataLabs.ImagePastel.Tests.TestUtilities.ReferenceCodecs;

namespace PinataLabs.ImagePastel.Tests.Formats.Tiff;

public abstract class TiffDecoderBaseTester
{
    protected static TiffDecoder TiffDecoder => new();

    protected static MagickReferenceDecoder ReferenceDecoder => new();

    protected static void TestTiffDecoder<TPixel>(TestImageProvider<TPixel> provider, IImageDecoder referenceDecoder = null, bool useExactComparer = true, float compareTolerance = 0.001f)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage(TiffDecoder);
        image.DebugSave(provider);
        image.CompareToOriginal(
            provider,
            useExactComparer ? ImageComparer.Exact : ImageComparer.Tolerant(compareTolerance),
            referenceDecoder ?? ReferenceDecoder);
    }
}
