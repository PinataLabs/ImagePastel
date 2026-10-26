// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Binarization;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Binarization;

[Trait("Category", "Processors")]
public class AdaptiveThresholdTests : BaseImageOperationsExtensionTest
{
    [Fact]
    public void AdaptiveThreshold_UsesDefaults_Works()
    {
        // arrange
        float expectedThresholdLimit = .85f;
        Color expectedUpper = Color.White;
        Color expectedLower = Color.Black;

        // act
        this.operations.AdaptiveThreshold();

        // assert
        AdaptiveThresholdProcessor p = this.Verify<AdaptiveThresholdProcessor>();
        Assert.Equal(expectedThresholdLimit, p.ThresholdLimit);
        Assert.Equal(expectedUpper, p.Upper);
        Assert.Equal(expectedLower, p.Lower);
    }

    [Fact]
    public void AdaptiveThreshold_SettingThresholdLimit_Works()
    {
        // arrange
        float expectedThresholdLimit = .65f;

        // act
        this.operations.AdaptiveThreshold(expectedThresholdLimit);

        // assert
        AdaptiveThresholdProcessor p = this.Verify<AdaptiveThresholdProcessor>();
        Assert.Equal(expectedThresholdLimit, p.ThresholdLimit);
        Assert.Equal(Color.White, p.Upper);
        Assert.Equal(Color.Black, p.Lower);
    }

    [Fact]
    public void AdaptiveThreshold_SettingUpperLowerThresholds_Works()
    {
        // arrange
        Color expectedUpper = Color.HotPink;
        Color expectedLower = Color.Yellow;

        // act
        this.operations.AdaptiveThreshold(expectedUpper, expectedLower);

        // assert
        AdaptiveThresholdProcessor p = this.Verify<AdaptiveThresholdProcessor>();
        Assert.Equal(expectedUpper, p.Upper);
        Assert.Equal(expectedLower, p.Lower);
    }

    [Fact]
    public void AdaptiveThreshold_SettingUpperLowerWithThresholdLimit_Works()
    {
        // arrange
        float expectedThresholdLimit = .77f;
        Color expectedUpper = Color.HotPink;
        Color expectedLower = Color.Yellow;

        // act
        this.operations.AdaptiveThreshold(expectedUpper, expectedLower, expectedThresholdLimit);

        // assert
        AdaptiveThresholdProcessor p = this.Verify<AdaptiveThresholdProcessor>();
        Assert.Equal(expectedThresholdLimit, p.ThresholdLimit);
        Assert.Equal(expectedUpper, p.Upper);
        Assert.Equal(expectedLower, p.Lower);
    }

    [Fact]
    public void AdaptiveThreshold_SettingUpperLowerWithThresholdLimit_WithRectangle_Works()
    {
        // arrange
        float expectedThresholdLimit = .77f;
        Color expectedUpper = Color.HotPink;
        Color expectedLower = Color.Yellow;

        // act
        this.operations.AdaptiveThreshold(expectedUpper, expectedLower, expectedThresholdLimit, this.rect);

        // assert
        AdaptiveThresholdProcessor p = this.Verify<AdaptiveThresholdProcessor>(this.rect);
        Assert.Equal(expectedThresholdLimit, p.ThresholdLimit);
        Assert.Equal(expectedUpper, p.Upper);
        Assert.Equal(expectedLower, p.Lower);
    }

    [Theory]
    [WithFile(TestImages.Png.Bradley01, PixelTypes.Rgba32)]
    [WithFile(TestImages.Png.Bradley02, PixelTypes.Rgba32)]
    [WithFile(TestImages.Png.Ducky, PixelTypes.Rgba32)]
    public void AdaptiveThreshold_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        {
            image.Mutate(img => img.AdaptiveThreshold());
            image.DebugSave(provider);
            image.CompareToReferenceOutput(ImageComparer.Exact, provider);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(16, 16)]
    [InlineData(17, 3)]
    [InlineData(31, 40)]
    public void AdaptiveThreshold_NarrowImage_DoesNotThrow(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(200, 200, 200));

        Exception ex = Record.Exception(() => image.Mutate(img => img.AdaptiveThreshold()));

        Assert.Null(ex);
    }

    [Fact]
    public void AdaptiveThreshold_NarrowImage_SeparatesDarkPixel()
    {
        using var image = new Image<Rgba32>(31, 40, new Rgba32(200, 200, 200));
        image[15, 20] = new Rgba32(10, 10, 10);

        image.Mutate(img => img.AdaptiveThreshold());

        for (int y = 19; y <= 21; y++)
        {
            for (int x = 14; x <= 16; x++)
            {
                Rgba32 expected = x == 15 && y == 20 ? Color.Black : Color.White;
                Assert.Equal(expected, image[x, y]);
            }
        }
    }

    [Fact]
    public void AdaptiveThreshold_NarrowRectangle_DoesNotThrow()
    {
        using var image = new Image<Rgba32>(100, 100, new Rgba32(200, 200, 200));

        Exception ex = Record.Exception(
            () => image.Mutate(img => img.AdaptiveThreshold(Color.White, Color.Black, new Rectangle(10, 10, 20, 50))));

        Assert.Null(ex);
    }

    [Theory]
    [WithFile(TestImages.Png.Bradley02, PixelTypes.Rgba32)]
    public void AdaptiveThreshold_WithRectangle_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        {
            image.Mutate(img => img.AdaptiveThreshold(Color.White, Color.Black, new Rectangle(60, 90, 200, 30)));
            image.DebugSave(provider);
            image.CompareToReferenceOutput(ImageComparer.Exact, provider);
        }
    }
}
