// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Processors.Effects;

[Trait("Category", "Processors")]
[GroupOutput("Effects")]
public class OilPaintTest
{
    public static readonly TheoryData<int, int> OilPaintValues = new TheoryData<int, int>
                                                                     {
                                                                         { 15, 10 },
                                                                         { 6, 5 }
                                                                     };

    public static readonly string[] InputImages =
        {
            TestImages.Png.CalliphoraPartial,
            TestImages.Bmp.Car
        };

    [Theory]
    [WithFileCollection(nameof(InputImages), nameof(OilPaintValues), PixelTypes.Rgba32)]
    public void FullImage<TPixel>(TestImageProvider<TPixel> provider, int levels, int brushSize)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        provider.RunValidatingProcessorTest(
            x =>
            {
                x.OilPaint(levels, brushSize);
                return $"{levels}-{brushSize}";
            },
            ImageComparer.TolerantPercentage(0.01F),
            appendPixelTypeToFileName: false);
    }

    [Theory]
    [WithFileCollection(nameof(InputImages), nameof(OilPaintValues), PixelTypes.Rgba32)]
    [WithTestPatternImages(nameof(OilPaintValues), 100, 100, PixelTypes.Rgba32)]
    public void InBox<TPixel>(TestImageProvider<TPixel> provider, int levels, int brushSize)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        provider.RunRectangleConstrainedValidatingProcessorTest(
            (x, rect) => x.OilPaint(levels, brushSize, rect),
            $"{levels}-{brushSize}",
            ImageComparer.TolerantPercentage(0.01F));
    }

    [Theory]
    [InlineData(1000F, 3000F, 2000F)]
    [InlineData(-1000F, -3000F, -2000F)]
    public void OutOfRangeVectorValues_DoNotProduceNaN(float r, float g, float b)
    {
        Configuration configuration = Configuration.CreateDefaultInstance();

        // Unpooled buffers turn any out-of-bounds bin write into an access violation instead of silent pool corruption.
        configuration.MemoryAllocator = new SimpleGcMemoryAllocator();

        using var image = new Image<RgbaVector>(configuration, 64, 64, new RgbaVector(r, g, b));

        image.Mutate(x => x.OilPaint(10, 5));

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Vector4 pixel = image[x, y].ToVector4();
                Assert.False(float.IsNaN(pixel.X) || float.IsNaN(pixel.Y) || float.IsNaN(pixel.Z) || float.IsNaN(pixel.W));
            }
        }
    }
}
