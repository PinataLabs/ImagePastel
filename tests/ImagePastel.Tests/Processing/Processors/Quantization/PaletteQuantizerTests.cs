// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PinataLabs.ImagePastel.Formats;
using PinataLabs.ImagePastel.Formats.Bmp;
using PinataLabs.ImagePastel.Formats.Gif;
using PinataLabs.ImagePastel.Formats.Png;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Dithering;
using PinataLabs.ImagePastel.Processing.Processors.Quantization;
using PinataLabs.ImagePastel.Tests.Memory;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Processors.Quantization;

[Trait("Category", "Processors")]
public class PaletteQuantizerTests
{
    private static readonly Color[] Palette = { Color.Red, Color.Green, Color.Blue };

    [Fact]
    public void PaletteQuantizerConstructor()
    {
        var expected = new QuantizerOptions { MaxColors = 128 };
        var quantizer = new PaletteQuantizer(Palette, expected);

        Assert.Equal(expected.MaxColors, quantizer.Options.MaxColors);
        Assert.Equal(QuantizerConstants.DefaultDither, quantizer.Options.Dither);

        expected = new QuantizerOptions { Dither = null };
        quantizer = new PaletteQuantizer(Palette, expected);
        Assert.Equal(QuantizerConstants.MaxColors, quantizer.Options.MaxColors);
        Assert.Null(quantizer.Options.Dither);

        expected = new QuantizerOptions { Dither = KnownDitherings.Atkinson };
        quantizer = new PaletteQuantizer(Palette, expected);
        Assert.Equal(QuantizerConstants.MaxColors, quantizer.Options.MaxColors);
        Assert.Equal(KnownDitherings.Atkinson, quantizer.Options.Dither);

        expected = new QuantizerOptions { Dither = KnownDitherings.Atkinson, MaxColors = 0 };
        quantizer = new PaletteQuantizer(Palette, expected);
        Assert.Equal(QuantizerConstants.MinColors, quantizer.Options.MaxColors);
        Assert.Equal(KnownDitherings.Atkinson, quantizer.Options.Dither);
    }

    [Fact]
    public void PaletteQuantizerCanCreateFrameQuantizer()
    {
        var quantizer = new PaletteQuantizer(Palette);
        IQuantizer<Rgba32> frameQuantizer = quantizer.CreatePixelSpecificQuantizer<Rgba32>(Configuration.Default);

        Assert.NotNull(frameQuantizer);
        Assert.NotNull(frameQuantizer.Options);
        Assert.Equal(QuantizerConstants.DefaultDither, frameQuantizer.Options.Dither);
        frameQuantizer.Dispose();

        quantizer = new PaletteQuantizer(Palette, new QuantizerOptions { Dither = null });
        frameQuantizer = quantizer.CreatePixelSpecificQuantizer<Rgba32>(Configuration.Default);

        Assert.NotNull(frameQuantizer);
        Assert.Null(frameQuantizer.Options.Dither);
        frameQuantizer.Dispose();

        quantizer = new PaletteQuantizer(Palette, new QuantizerOptions { Dither = KnownDitherings.Atkinson });
        frameQuantizer = quantizer.CreatePixelSpecificQuantizer<Rgba32>(Configuration.Default);
        Assert.NotNull(frameQuantizer);
        Assert.Equal(KnownDitherings.Atkinson, frameQuantizer.Options.Dither);
        frameQuantizer.Dispose();
    }

    [Fact]
    public void KnownQuantizersWebSafeTests()
    {
        IQuantizer quantizer = KnownQuantizers.WebSafe;
        Assert.Equal(QuantizerConstants.DefaultDither, quantizer.Options.Dither);
    }

    [Fact]
    public void KnownQuantizersWernerTests()
    {
        IQuantizer quantizer = KnownQuantizers.Werner;
        Assert.Equal(QuantizerConstants.DefaultDither, quantizer.Options.Dither);
    }

    public enum ColorWalk
    {
        Gray254,
        Gray256,
        Rgb256,
        Alpha256
    }

    [Theory]
    [InlineData(ColorWalk.Gray254, false)]
    [InlineData(ColorWalk.Gray254, true)]
    [InlineData(ColorWalk.Gray256, false)]
    [InlineData(ColorWalk.Gray256, true)]
    [InlineData(ColorWalk.Rgb256, false)]
    [InlineData(ColorWalk.Rgb256, true)]
    [InlineData(ColorWalk.Alpha256, false)]
    [InlineData(ColorWalk.Alpha256, true)]
    public void QuantizeWithPaletteOfEveryColorIsLossless(ColorWalk walk, bool dither)
    {
        Rgba32[] colors = GetColorWalk(walk);
        using Image<Rgba32> image = CreateColorWalkImage(colors);
        using Image<Rgba32> expected = image.Clone();

        image.Mutate(x => x.Quantize(CreatePaletteQuantizer(colors, dither)));

        AssertPixelsEqual(expected, image);
    }

    [Theory]
    [InlineData(ColorWalk.Gray254, false)]
    [InlineData(ColorWalk.Gray254, true)]
    [InlineData(ColorWalk.Gray256, false)]
    [InlineData(ColorWalk.Gray256, true)]
    [InlineData(ColorWalk.Rgb256, false)]
    [InlineData(ColorWalk.Rgb256, true)]
    [InlineData(ColorWalk.Alpha256, false)]
    [InlineData(ColorWalk.Alpha256, true)]
    public void EncodePalettePngWithPaletteOfEveryColorIsLossless(ColorWalk walk, bool dither)
    {
        Rgba32[] colors = GetColorWalk(walk);
        var encoder = new PngEncoder
        {
            ColorType = PngColorType.Palette,
            BitDepth = PngBitDepth.Bit8,
            TransparentColorMode = PngTransparentColorMode.Preserve,
            Quantizer = CreatePaletteQuantizer(colors, dither)
        };

        AssertEncodeIsLossless(colors, encoder);
    }

    [Theory]
    [InlineData(ColorWalk.Gray254, false)]
    [InlineData(ColorWalk.Gray254, true)]
    [InlineData(ColorWalk.Rgb256, false)]
    [InlineData(ColorWalk.Rgb256, true)]
    public void EncodeGifWithPaletteOfEveryColorIsLossless(ColorWalk walk, bool dither)
    {
        Rgba32[] colors = GetColorWalk(walk);
        AssertEncodeIsLossless(colors, new GifEncoder { Quantizer = CreatePaletteQuantizer(colors, dither) });
    }

    [Theory]
    [InlineData(ColorWalk.Gray254, false)]
    [InlineData(ColorWalk.Gray254, true)]
    [InlineData(ColorWalk.Rgb256, false)]
    [InlineData(ColorWalk.Rgb256, true)]
    public void EncodeIndexedBmpWithPaletteOfEveryColorIsLossless(ColorWalk walk, bool dither)
    {
        Rgba32[] colors = GetColorWalk(walk);
        var encoder = new BmpEncoder
        {
            BitsPerPixel = BmpBitsPerPixel.Pixel8,
            Quantizer = CreatePaletteQuantizer(colors, dither)
        };

        AssertEncodeIsLossless(colors, encoder);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GetQuantizedColorReturnsNearestPaletteEntry(bool clustered, bool duplicated)
    {
        // A clustered palette packs every entry into a few levels per channel, so many entries compete for each color.
        // A duplicated palette repeats a few colors many times, as quantizers do when they pad unused entries.
        var random = new Random(21);
        var colors = new Color[256];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = duplicated && i >= 4
                ? colors[i % 4]
                : clustered
                ? new Color(new Rgba32((byte)random.Next(96, 112), (byte)random.Next(96, 112), (byte)random.Next(96, 112), (byte)random.Next(200, 216)))
                : new Color(new Rgba32((uint)random.NextInt64(1L << 32)));
        }

        // Query colors close to the palette entries, where a coarse lookup is most likely to go wrong, and random colors.
        var queries = new List<Rgba32>();
        foreach (Color color in colors)
        {
            Rgba32 entry = color.ToPixel<Rgba32>();
            for (int i = 0; i < 64; i++)
            {
                queries.Add(new Rgba32(
                    Nudge(entry.R, random),
                    Nudge(entry.G, random),
                    Nudge(entry.B, random),
                    Nudge(entry.A, random)));
            }
        }

        for (int i = 0; i < 20000; i++)
        {
            queries.Add(new Rgba32((uint)random.NextInt64(1L << 32)));
        }

        using IQuantizer<Rgba32> quantizer = new PaletteQuantizer(colors, new QuantizerOptions { Dither = null })
            .CreatePixelSpecificQuantizer<Rgba32>(Configuration.Default);
        ReadOnlySpan<Rgba32> palette = quantizer.Palette.Span;

        // Ask twice so the second pass is answered by whatever the first pass remembered.
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (Rgba32 query in queries)
            {
                int expected = NearestIndex(palette, query);
                byte actual = quantizer.GetQuantizedColor(query, out Rgba32 match);

                Assert.True(expected == actual, $"{query} mapped to {palette[actual]} instead of {palette[expected]}.");
                Assert.Equal(palette[expected], match);
            }
        }

        static byte Nudge(byte value, Random random) => (byte)Math.Clamp(value + random.Next(-8, 9), 0, 255);
    }

    [Fact]
    public void GetQuantizedColorMemoryDoesNotGrowWithDistinctColors()
    {
        var allocator = new TestMemoryAllocator();
        allocator.EnableNonThreadSafeLogging();
        var configuration = new Configuration { MemoryAllocator = allocator };

        Color[] colors = GetColorWalk(ColorWalk.Rgb256).Select(x => new Color(x)).ToArray();
        using IQuantizer<Rgba32> quantizer = new PaletteQuantizer(colors, new QuantizerOptions { Dither = null })
            .CreatePixelSpecificQuantizer<Rgba32>(configuration);

        // Warm up so one-time setup is not counted.
        for (uint i = 0; i < 1 << 16; i++)
        {
            quantizer.GetQuantizedColor(new Rgba32(i * 2246822519U), out _);
        }

        int allocationsBefore = allocator.AllocationLog.Count;
        long managedBytesBefore = GC.GetAllocatedBytesForCurrentThread();

        for (uint i = 0; i < 1 << 20; i++)
        {
            quantizer.GetQuantizedColor(new Rgba32(i * 2654435761U), out _);
        }

        // Managed allocations are not seen by the test allocator, so count them separately.
        long managedBytes = GC.GetAllocatedBytesForCurrentThread() - managedBytesBefore;

        Assert.Equal(allocationsBefore, allocator.AllocationLog.Count);
        Assert.True(allocator.AllocationLog.Sum(x => (long)x.LengthInBytes) <= 4 * 1024 * 1024);
        Assert.True(managedBytes < 64 * 1024, $"Lookups allocated {managedBytes} managed bytes.");
    }

    [Fact]
    public void PixelMapRejectsEmptyPalette()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new EuclideanPixelMap<Rgba32>(Configuration.Default, ReadOnlyMemory<Rgba32>.Empty));

        Assert.Equal("palette", ex.ParamName);
    }

    [Fact]
    public void PixelMapClearRejectsEmptyPalette()
    {
        Rgba32[] palette = { Color.Red.ToPixel<Rgba32>(), Color.Blue.ToPixel<Rgba32>() };
        using var map = new EuclideanPixelMap<Rgba32>(Configuration.Default, palette);

        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => map.Clear(ReadOnlyMemory<Rgba32>.Empty));

        Assert.Equal("palette", ex.ParamName);

        // The map keeps its previous palette.
        Assert.Equal(1, map.GetClosestColor(Color.Navy.ToPixel<Rgba32>(), out Rgba32 match));
        Assert.Equal(palette[1], match);
    }

    [Fact]
    public void PixelSpecificPaletteQuantizerRejectsEmptyPalette()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new PaletteQuantizer<Rgba32>(Configuration.Default, new QuantizerOptions(), ReadOnlyMemory<Rgba32>.Empty));

        Assert.Equal("palette", ex.ParamName);
    }

    [Fact]
    public void QuantizeRejectsEmptyPalette()
    {
        using var image = new Image<Rgba32>(4, 4, Color.Red.ToPixel<Rgba32>());

        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => image.Mutate(x => x.Quantize(new PaletteQuantizer(ReadOnlyMemory<Color>.Empty))));

        Assert.Equal("palette", ex.ParamName);
    }

    [Fact]
    public void DitherRejectsEmptyPalette()
    {
        using var image = new Image<Rgba32>(4, 4, Color.Red.ToPixel<Rgba32>());

        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => image.Mutate(x => x.Dither(KnownDitherings.FloydSteinberg, ReadOnlyMemory<Color>.Empty)));

        Assert.Equal("palette", ex.ParamName);
    }

    [Theory]
    [InlineData(nameof(KnownDitherings.FloydSteinberg))]
    [InlineData(nameof(KnownDitherings.Atkinson))]
    [InlineData(nameof(KnownDitherings.Stucki))]
    public void ErrorDiffusionOfAlphaGradientIsNoWorseThanNoDither(string ditherName)
    {
        IDither dither = (IDither)typeof(KnownDitherings).GetProperty(ditherName).GetValue(null);

        // Alpha levels every five steps, so most of the gradient falls between two palette entries.
        Color[] colors = Enumerable.Range(0, 52).Select(x => new Color(new Rgba32(200, 40, 40, (byte)(x * 5)))).ToArray();

        using var source = new Image<Rgba32>(256, 64);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                source[x, y] = new Rgba32(200, 40, 40, (byte)x);
            }
        }

        using Image<Rgba32> undithered = source.Clone(x => x.Quantize(new PaletteQuantizer(colors, new QuantizerOptions { Dither = null })));
        using Image<Rgba32> dithered = source.Clone(x => x.Quantize(new PaletteQuantizer(colors, new QuantizerOptions { Dither = dither })));

        double unditheredError = BlockMeanAlphaError(source, undithered);
        double ditheredError = BlockMeanAlphaError(source, dithered);

        Assert.True(ditheredError <= unditheredError, $"Dithered block error {ditheredError:F2} vs undithered {unditheredError:F2}.");
    }

    /// <summary>
    /// Gets the mean, over 8x8 blocks, of the absolute difference in average alpha. Error diffusion
    /// spreads the error across neighbours, so this measures alpha the way a viewer sees it.
    /// </summary>
    internal static double BlockMeanAlphaError(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        const int size = 8;
        double total = 0;
        int blocks = 0;
        for (int by = 0; by < expected.Height; by += size)
        {
            for (int bx = 0; bx < expected.Width; bx += size)
            {
                int difference = 0;
                for (int y = by; y < by + size; y++)
                {
                    for (int x = bx; x < bx + size; x++)
                    {
                        difference += actual[x, y].A - expected[x, y].A;
                    }
                }

                total += Math.Abs(difference / (double)(size * size));
                blocks++;
            }
        }

        return total / blocks;
    }

    private static Rgba32[] GetColorWalk(ColorWalk walk)
        => walk switch
        {
            ColorWalk.Gray254 => Enumerable.Range(0, 254).Select(x => new Rgba32((byte)x, (byte)x, (byte)x, 255)).ToArray(),
            ColorWalk.Gray256 => Enumerable.Range(0, 256).Select(x => new Rgba32((byte)x, (byte)x, (byte)x, 255)).ToArray(),
            ColorWalk.Rgb256 => Enumerable.Range(0, 256).Select(x => new Rgba32((byte)x, (byte)(x / 2), (byte)(255 - x), 255)).ToArray(),
            ColorWalk.Alpha256 => Enumerable.Range(0, 256).Select(x => new Rgba32(40, 80, 120, (byte)x)).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(walk))
        };

    private static int NearestIndex(ReadOnlySpan<Rgba32> palette, Rgba32 color)
    {
        int nearest = 0;
        int nearestDistance = int.MaxValue;
        for (int i = 0; i < palette.Length; i++)
        {
            Rgba32 entry = palette[i];
            int r = entry.R - color.R;
            int g = entry.G - color.G;
            int b = entry.B - color.B;
            int a = entry.A - color.A;
            int distance = (r * r) + (g * g) + (b * b) + (a * a);
            if (distance < nearestDistance)
            {
                nearest = i;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private static Image<Rgba32> CreateColorWalkImage(Rgba32[] colors)
    {
        var image = new Image<Rgba32>(colors.Length, 4);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                image[x, y] = colors[x];
            }
        }

        return image;
    }

    private static PaletteQuantizer CreatePaletteQuantizer(Rgba32[] colors, bool dither)
        => new(
            colors.Select(x => new Color(x)).ToArray(),
            new QuantizerOptions { Dither = dither ? KnownDitherings.FloydSteinberg : null });

    private static void AssertEncodeIsLossless(Rgba32[] colors, IImageEncoder encoder)
    {
        using Image<Rgba32> image = CreateColorWalkImage(colors);
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        stream.Position = 0;
        using Image<Rgba32> decoded = Image.Load<Rgba32>(stream);

        AssertPixelsEqual(image, decoded);
    }

    private static void AssertPixelsEqual(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        int wrong = 0;
        string first = null;
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                if (expected[x, y] != actual[x, y])
                {
                    wrong++;
                    first ??= $"({x},{y}) expected {expected[x, y]} but was {actual[x, y]}";
                }
            }
        }

        Assert.True(wrong == 0, $"{wrong} of {expected.Width * expected.Height} pixels differ; first at {first}.");
    }
}
