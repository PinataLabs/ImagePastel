// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats.Jxl.Modular;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// The pixels the encoder takes: which channels and bit depth a pixel type gives, and the copy of
/// its samples into Modular channels. libjxl takes float planes and converts them back to integers
/// (<c>CopyColorChannels</c> and <c>float_to_int</c> in <c>enc_modular.cc</c>); this port copies the
/// integer samples of <typeparamref name="TPixel"/> straight into the channels instead, which gives the
/// same integers without a round trip through float.
/// </summary>
/// <typeparam name="TPixel">The pixel type.</typeparam>
internal static class JxlEncoderInput<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <summary>
    /// Gets a value indicating whether the pixel type is greyscale, which gives one colour channel
    /// and a grey colour encoding.
    /// </summary>
    public static bool IsGray => typeof(TPixel) == typeof(L8);

    /// <summary>Gets the bits per colour sample.</summary>
    public static uint BitsPerSample => 8;

    /// <summary>
    /// Throws <see cref="NotSupportedException"/> for a pixel type the encoder does not take yet. Only
    /// <see cref="L8"/> and <see cref="Rgb24"/> are ported; slice E1d adds the other bit depths, float
    /// samples and alpha.
    /// </summary>
    public static void CheckSupported()
    {
        if (typeof(TPixel) != typeof(L8) && typeof(TPixel) != typeof(Rgb24))
        {
            throw new NotSupportedException($"JPEG XL encoding of {typeof(TPixel).Name} pixels is not supported yet; slice E1d adds it.");
        }
    }

    /// <summary>
    /// Copies the colour samples of <paramref name="image"/> into the first channels of
    /// <paramref name="gi"/>: one for grey, three for RGB.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="gi">The Modular image, with full-size channels.</param>
    public static void CopyColorChannels(Image<TPixel> image, JxlModularImage gi)
    {
        Configuration configuration = image.GetConfiguration();
        Buffer2D<TPixel> pixels = image.Frames.RootFrame.PixelBuffer;
        int width = image.Width;
        if (IsGray)
        {
            using IMemoryOwner<L8> row = configuration.MemoryAllocator.Allocate<L8>(width);
            Span<L8> grey = row.Memory.Span;
            for (int y = 0; y < image.Height; y++)
            {
                PixelOperations<TPixel>.Instance.ToL8(configuration, pixels.DangerousGetRowSpan(y), grey);
                Span<int> output = gi.Channel[0].Row(y);
                for (int x = 0; x < width; x++)
                {
                    output[x] = grey[x].PackedValue;
                }
            }

            return;
        }

        using IMemoryOwner<Rgb24> rgbRow = configuration.MemoryAllocator.Allocate<Rgb24>(width);
        Span<Rgb24> rgb = rgbRow.Memory.Span;
        for (int y = 0; y < image.Height; y++)
        {
            PixelOperations<TPixel>.Instance.ToRgb24(configuration, pixels.DangerousGetRowSpan(y), rgb);
            Span<int> r = gi.Channel[0].Row(y);
            Span<int> g = gi.Channel[1].Row(y);
            Span<int> b = gi.Channel[2].Row(y);
            for (int x = 0; x < width; x++)
            {
                r[x] = rgb[x].R;
                g[x] = rgb[x].G;
                b[x] = rgb[x].B;
            }
        }
    }
}
