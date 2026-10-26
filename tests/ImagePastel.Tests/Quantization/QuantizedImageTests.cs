// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Dithering;
using PinataLabs.ImagePastel.Processing.Processors.Quantization;
using PinataLabs.ImagePastel.Tests.Memory;

using Xunit;

namespace PinataLabs.ImagePastel.Tests;

public class QuantizedImageTests
{
    private Configuration Configuration => Configuration.Default;

    [Fact]
    public void QuantizersDitherByDefault()
    {
        var werner = new WernerPaletteQuantizer();
        var webSafe = new WebSafePaletteQuantizer();
        var octree = new OctreeQuantizer();
        var wu = new WuQuantizer();

        Assert.NotNull(werner.Options.Dither);
        Assert.NotNull(webSafe.Options.Dither);
        Assert.NotNull(octree.Options.Dither);
        Assert.NotNull(wu.Options.Dither);

        using (IQuantizer<Rgba32> quantizer = werner.CreatePixelSpecificQuantizer<Rgba32>(this.Configuration))
        {
            Assert.NotNull(quantizer.Options.Dither);
        }

        using (IQuantizer<Rgba32> quantizer = webSafe.CreatePixelSpecificQuantizer<Rgba32>(this.Configuration))
        {
            Assert.NotNull(quantizer.Options.Dither);
        }

        using (IQuantizer<Rgba32> quantizer = octree.CreatePixelSpecificQuantizer<Rgba32>(this.Configuration))
        {
            Assert.NotNull(quantizer.Options.Dither);
        }

        using (IQuantizer<Rgba32> quantizer = wu.CreatePixelSpecificQuantizer<Rgba32>(this.Configuration))
        {
            Assert.NotNull(quantizer.Options.Dither);
        }
    }

    [Theory]
    [WithFile(TestImages.Gif.Giphy, PixelTypes.Rgba32, true)]
    [WithFile(TestImages.Gif.Giphy, PixelTypes.Rgba32, false)]
    public void OctreeQuantizerYieldsCorrectTransparentPixel<TPixel>(
        TestImageProvider<TPixel> provider,
        bool dither)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        {
            Assert.True(image[0, 0].Equals(default));

            var options = new QuantizerOptions();
            if (!dither)
            {
                options.Dither = null;
            }

            var quantizer = new OctreeQuantizer(options);

            foreach (ImageFrame<TPixel> frame in image.Frames)
            {
                using (IQuantizer<TPixel> frameQuantizer = quantizer.CreatePixelSpecificQuantizer<TPixel>(this.Configuration))
                using (IndexedImageFrame<TPixel> quantized = frameQuantizer.BuildPaletteAndQuantizeFrame(frame, frame.Bounds()))
                {
                    int index = this.GetTransparentIndex(quantized);
                    Assert.Equal(index, quantized.DangerousGetRowSpan(0)[0]);
                }
            }
        }
    }

    [Theory]
    [WithFile(TestImages.Gif.Giphy, PixelTypes.Rgba32, true)]
    [WithFile(TestImages.Gif.Giphy, PixelTypes.Rgba32, false)]
    public void WuQuantizerYieldsCorrectTransparentPixel<TPixel>(TestImageProvider<TPixel> provider, bool dither)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        {
            Assert.True(image[0, 0].Equals(default));

            var options = new QuantizerOptions();
            if (!dither)
            {
                options.Dither = null;
            }

            var quantizer = new WuQuantizer(options);

            foreach (ImageFrame<TPixel> frame in image.Frames)
            {
                using (IQuantizer<TPixel> frameQuantizer = quantizer.CreatePixelSpecificQuantizer<TPixel>(this.Configuration))
                using (IndexedImageFrame<TPixel> quantized = frameQuantizer.BuildPaletteAndQuantizeFrame(frame, frame.Bounds()))
                {
                    int index = this.GetTransparentIndex(quantized);
                    Assert.Equal(index, quantized.DangerousGetRowSpan(0)[0]);
                }
            }
        }
    }

    // Test case for issue: https://github.com/SixLabors/ImageSharp/issues/1505
    [Theory]
    [WithFile(TestImages.Gif.Issues.Issue1505, PixelTypes.Rgba32)]
    public void Issue1505<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        {
            var octreeQuantizer = new OctreeQuantizer();
            IQuantizer<TPixel> quantizer = octreeQuantizer.CreatePixelSpecificQuantizer<TPixel>(Configuration.Default, new QuantizerOptions() { MaxColors = 128 });
            ImageFrame<TPixel> frame = image.Frames[0];
            quantizer.BuildPaletteAndQuantizeFrame(frame, frame.Bounds());
        }
    }

    public static readonly TheoryData<int, int, int, int, string> BoundsAndDithers = CreateBoundsAndDithers();

    [Theory]
    [MemberData(nameof(BoundsAndDithers))]
    public void QuantizeFrameMapsEveryPixelInsideBounds(int x, int y, int width, int height, string ditherName)
    {
        // Dirty, unpooled buffers, so an index the quantizer does not write cannot pass as a correct one.
        Configuration configuration = Configuration.Default.Clone();
        configuration.MemoryAllocator = new TestMemoryAllocator();

        var bounds = new Rectangle(x, y, width, height);
        using Image<Rgba32> image = CreateTwoTonePattern(20, 16);

        // A zero dither scale leaves the pixels unchanged, so every dither gives the plain mapping.
        var options = new QuantizerOptions { Dither = GetDither(ditherName), DitherScale = 0F };
        var quantizer = new PaletteQuantizer(new[] { Color.White, Color.Black }, options);

        using IQuantizer<Rgba32> frameQuantizer = quantizer.CreatePixelSpecificQuantizer<Rgba32>(configuration);
        using IndexedImageFrame<Rgba32> quantized = frameQuantizer.QuantizeFrame(image.Frames.RootFrame, bounds);

        Assert.Equal(width, quantized.Width);
        Assert.Equal(height, quantized.Height);

        // Palette order: white is 0, black is 1.
        for (int j = 0; j < height; j++)
        {
            ReadOnlySpan<byte> row = quantized.DangerousGetRowSpan(j);
            for (int i = 0; i < width; i++)
            {
                byte expected = IsDark(x + i, y + j) ? (byte)1 : (byte)0;
                Assert.Equal(expected, row[i]);
            }
        }
    }

    private static TheoryData<int, int, int, int, string> CreateBoundsAndDithers()
    {
        var data = new TheoryData<int, int, int, int, string>();
        foreach (string dither in new[] { "None", nameof(KnownDitherings.FloydSteinberg), nameof(KnownDitherings.Bayer8x8) })
        {
            // A band at the top, a box inside the frame, and a box that touches the right and bottom edges.
            data.Add(0, 0, 20, 8, dither);
            data.Add(3, 5, 9, 6, dither);
            data.Add(11, 10, 9, 6, dither);
        }

        return data;
    }

    private static IDither GetDither(string name) => name switch
    {
        nameof(KnownDitherings.FloydSteinberg) => KnownDitherings.FloydSteinberg,
        nameof(KnownDitherings.Bayer8x8) => KnownDitherings.Bayer8x8,
        _ => null
    };

    private static bool IsDark(int x, int y) => ((x * 7) + (y * 3)) % 5 < 2;

    private static Image<Rgba32> CreateTwoTonePattern(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image[x, y] = IsDark(x, y) ? new Rgba32(30, 30, 30) : new Rgba32(220, 220, 220);
            }
        }

        return image;
    }

    private int GetTransparentIndex<TPixel>(IndexedImageFrame<TPixel> quantized)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Transparent pixels are much more likely to be found at the end of a palette
        int index = -1;
        ReadOnlySpan<TPixel> paletteSpan = quantized.Palette.Span;
        Span<Rgba32> colorSpan = stackalloc Rgba32[QuantizerConstants.MaxColors].Slice(0, paletteSpan.Length);

        PixelOperations<TPixel>.Instance.ToRgba32(quantized.Configuration, paletteSpan, colorSpan);
        for (int i = colorSpan.Length - 1; i >= 0; i--)
        {
            if (colorSpan[i].Equals(default))
            {
                index = i;
            }
        }

        return index;
    }
}
