// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Extensions.Transforms;
using PinataLabs.ImagePastel.Processing.Processors.Transforms;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Processors.Transforms;

[Trait("Category", "Processors")]
[GroupOutput("Transforms")]
public class SwizzleTests
{
    private struct InvertXAndYSwizzler : ISwizzler
    {
        public InvertXAndYSwizzler(Size sourceSize)
        {
            this.DestinationSize = new Size(sourceSize.Height, sourceSize.Width);
        }

        public Size DestinationSize { get; }

        public Point Transform(Point point)
            => new Point(point.Y, point.X);
    }

    [Theory]
    [WithTestPatternImages(20, 37, PixelTypes.Rgba32)]
    [WithTestPatternImages(53, 37, PixelTypes.Byte4)]
    [WithTestPatternImages(17, 32, PixelTypes.Rgba32)]
    public void InvertXAndYSwizzle<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> expectedImage = provider.GetImage();
        using Image<TPixel> image = provider.GetImage();

        image.Mutate(ctx => ctx.Swizzle(new InvertXAndYSwizzler(new Size(image.Width, image.Height))));

        image.DebugSave(
            provider,
            nameof(InvertXAndYSwizzler),
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: true);

        image.Mutate(ctx => ctx.Swizzle(new InvertXAndYSwizzler(new Size(image.Width, image.Height))));

        image.DebugSave(
            provider,
            "Unswizzle",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: true);

        ImageComparer.Exact.VerifySimilarity(expectedImage, image);
    }
}
