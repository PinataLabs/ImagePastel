// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Formats.Webp.Lossy;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Webp;

[Trait("Format", "Webp")]
public class Vp8EncoderTests
{
    [Theory]
    [InlineData(256, 32)]
    [InlineData(512, 16)]
    public void Encode_NonSquareImage_AveragesAlphaLikeItsTranspose(int width, int height)
    {
        // A transposed image swaps the vertical and horizontal predictions, so its macroblocks get almost the same alphas.
        // Only the default top and left edge values (127 and 129) differ.
        Rgba32[] pixels = CreatePixels(width, height);
        using var wide = Image.LoadPixelData<Rgba32>(pixels, width, height);
        using var tall = Image.LoadPixelData<Rgba32>(Transpose(pixels, width, height), height, width);

        (int wideAlpha, int wideUvAlpha) = EncodeAndGetAverageAlphas(wide);
        (int tallAlpha, int tallUvAlpha) = EncodeAndGetAverageAlphas(tall);

        // Alpha is clamped to [0, 255] for each macroblock. UV alpha is not.
        Assert.InRange(wideAlpha, 10, 245);
        Assert.True(wideUvAlpha > 10, $"UV alpha {wideUvAlpha} is too small to show the average.");
        Assert.InRange(Math.Abs(wideAlpha - tallAlpha), 0, 2);
        Assert.InRange(Math.Abs(wideUvAlpha - tallUvAlpha), 0, 2);
    }

    private static (int Alpha, int UvAlpha) EncodeAndGetAverageAlphas(Image<Rgba32> image)
    {
        Configuration configuration = Configuration.Default;
        using var encoder = new Vp8Encoder(
            configuration.MemoryAllocator,
            configuration,
            image.Width,
            image.Height,
            75,
            WebpEncodingMethod.Default,
            1,
            60,
            50,
            true);
        using var stream = new MemoryStream();
        encoder.Encode(image, stream);
        return (encoder.AverageAlpha, encoder.AverageUvAlpha);
    }

    private static Rgba32[] CreatePixels(int width, int height)
    {
        var random = new Random(42);
        var pixels = new Rgba32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int gradient = ((x * 3) + (y * 5)) % 128;
                pixels[(y * width) + x] = new Rgba32(
                    (byte)(gradient + random.Next(64)),
                    (byte)(64 + random.Next(32)),
                    (byte)(192 - gradient + random.Next(48)));
            }
        }

        return pixels;
    }

    private static Rgba32[] Transpose(Rgba32[] pixels, int width, int height)
    {
        var transposed = new Rgba32[pixels.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                transposed[(x * height) + y] = pixels[(y * width) + x];
            }
        }

        return transposed;
    }
}
