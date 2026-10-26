// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Processing.Processors.Quantization;

[Trait("Category", "Processors")]
public class OctreeQuantizerTests
{
    [Fact]
    public void OctreeQuantizerConstructor()
    {
        var expected = new QuantizerOptions { MaxColors = 128 };
        var quantizer = new OctreeQuantizer(expected);

        Assert.Equal(expected.MaxColors, quantizer.Options.MaxColors);
        Assert.Equal(QuantizerConstants.DefaultDither, quantizer.Options.Dither);

        expected = new QuantizerOptions { Dither = null };
        quantizer = new OctreeQuantizer(expected);
        Assert.Equal(QuantizerConstants.MaxColors, quantizer.Options.MaxColors);
        Assert.Null(quantizer.Options.Dither);

        expected = new QuantizerOptions { Dither = KnownDitherings.Atkinson };
        quantizer = new OctreeQuantizer(expected);
        Assert.Equal(QuantizerConstants.MaxColors, quantizer.Options.MaxColors);
        Assert.Equal(KnownDitherings.Atkinson, quantizer.Options.Dither);

        expected = new QuantizerOptions { Dither = KnownDitherings.Atkinson, MaxColors = 0 };
        quantizer = new OctreeQuantizer(expected);
        Assert.Equal(QuantizerConstants.MinColors, quantizer.Options.MaxColors);
        Assert.Equal(KnownDitherings.Atkinson, quantizer.Options.Dither);
    }

    [Fact]
    public void OctreeQuantizerCanCreateFrameQuantizer()
    {
        var quantizer = new OctreeQuantizer();
        IQuantizer<Rgba32> frameQuantizer = quantizer.CreatePixelSpecificQuantizer<Rgba32>(Configuration.Default);

        Assert.NotNull(frameQuantizer);
        Assert.NotNull(frameQuantizer.Options);
        Assert.Equal(QuantizerConstants.DefaultDither, frameQuantizer.Options.Dither);
        frameQuantizer.Dispose();

        quantizer = new OctreeQuantizer(new QuantizerOptions { Dither = null });
        frameQuantizer = quantizer.CreatePixelSpecificQuantizer<Rgba32>(Configuration.Default);

        Assert.NotNull(frameQuantizer);
        Assert.Null(frameQuantizer.Options.Dither);
        frameQuantizer.Dispose();

        quantizer = new OctreeQuantizer(new QuantizerOptions { Dither = KnownDitherings.Atkinson });
        frameQuantizer = quantizer.CreatePixelSpecificQuantizer<Rgba32>(Configuration.Default);
        Assert.NotNull(frameQuantizer);
        Assert.Equal(KnownDitherings.Atkinson, frameQuantizer.Options.Dither);
        frameQuantizer.Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AlphaGradientKeepsAlpha(bool dither)
    {
        var options = new QuantizerOptions();
        if (!dither)
        {
            options.Dither = null;
        }

        using Image<Rgba32> source = CreateAlphaGradient();
        using Image<Rgba32> octree = source.Clone(x => x.Quantize(new OctreeQuantizer(options)));
        using Image<Rgba32> wu = source.Clone(x => x.Quantize(new WuQuantizer(options)));

        double octreeError = PaletteQuantizerTests.BlockMeanAlphaError(source, octree);
        double wuError = PaletteQuantizerTests.BlockMeanAlphaError(source, wu);

        Assert.True(octreeError <= wuError + 1, $"Octree block alpha error {octreeError:F2} vs Wu {wuError:F2}.");
    }

    [Fact]
    public void OpaquePaletteIsUnchanged()
    {
        // Captured from the RGB-only octree. Opaque images must keep this palette.
        uint[] expected =
        {
            0xFF1E1E1E, 0xFF341454, 0xFF345414, 0xFF492969, 0xFF496929, 0xFF5E5E5E, 0xFF5E1E9E, 0xFF7414D4,
            0xFF745494, 0xFF69A929, 0xFF8929E9, 0xFF8969A9, 0xFF9E5EDE, 0xFF94D454, 0xFFBEBEBE, 0x00000000
        };

        using var image = new Image<Rgba32>(64, 64);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                image[x, y] = new Rgba32((byte)(x * 4), (byte)(y * 4), (byte)((x + y) * 2), 255);
            }
        }

        var quantizer = new OctreeQuantizer(new QuantizerOptions { Dither = null, MaxColors = 16 });
        using IQuantizer<Rgba32> frameQuantizer = quantizer.CreatePixelSpecificQuantizer<Rgba32>(Configuration.Default);
        using IndexedImageFrame<Rgba32> quantized = frameQuantizer.BuildPaletteAndQuantizeFrame(image.Frames.RootFrame, image.Bounds());

        ReadOnlySpan<Rgba32> palette = quantized.Palette.Span;
        uint[] actual = new uint[palette.Length];
        for (int i = 0; i < palette.Length; i++)
        {
            actual[i] = palette[i].PackedValue;
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void LargeTransparentAreaStaysTransparent()
    {
        // 9 passes over 1024x1024 pixels put about 9.4M fully transparent pixels in one leaf.
        // At 255 each, their total transparency is more than int.MaxValue.
        using var image = new Image<Rgba32>(1024, 1024, new Rgba32(10, 20, 30, 0));
        var quantizer = new OctreeQuantizer(new QuantizerOptions { Dither = null, MaxColors = 16 });
        using IQuantizer<Rgba32> frameQuantizer = quantizer.CreatePixelSpecificQuantizer<Rgba32>(Configuration.Default);

        Buffer2DRegion<Rgba32> region = image.Frames.RootFrame.PixelBuffer.GetRegion();
        for (int i = 0; i < 9; i++)
        {
            frameQuantizer.AddPaletteColors(region);
        }

        Assert.Equal(new Rgba32(10, 20, 30, 0), frameQuantizer.Palette.Span[0]);
    }

    private static Image<Rgba32> CreateAlphaGradient()
    {
        var image = new Image<Rgba32>(256, 64);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                image[x, y] = new Rgba32(200, 40, 40, (byte)x);
            }
        }

        return image;
    }
}
