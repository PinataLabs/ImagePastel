// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;

/// <summary>
/// Converts an <see cref="Image{TPixel}"/> to the conformance reference layout (frames, height, width, channels).
/// An <see cref="Image{TPixel}"/> keeps at most RGBA, clamped to [0, 1], so this only suits vectors whose
/// reference has no other extra channels and no out-of-range values.
/// </summary>
internal static class JxlConformanceImages
{
    /// <summary>
    /// Converts the image. <paramref name="channels"/> picks the layout: 1 = grey (R), 2 = grey and alpha,
    /// 3 = RGB, 4 = RGBA.
    /// </summary>
    public static NpyArray ToNpy<TPixel>(Image<TPixel> image, int channels)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (channels is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Expected 1 to 4 channels.");
        }

        int width = image.Width;
        int height = image.Height;
        int frames = image.Frames.Count;
        float[] data = new float[frames * height * width * channels];
        Vector4[] row = new Vector4[width];
        int index = 0;
        for (int f = 0; f < frames; f++)
        {
            ImageFrame<TPixel> frame = image.Frames[f];
            for (int y = 0; y < height; y++)
            {
                // Scale: some pixel types (Byte4, Short4, ...) report unscaled values from ToVector4.
                PixelOperations<TPixel>.Instance.ToVector4(Configuration.Default, frame.PixelBuffer.DangerousGetRowSpan(y), row, PixelConversionModifiers.Scale);
                foreach (Vector4 v in row)
                {
                    switch (channels)
                    {
                        case 1:
                            data[index++] = v.X;
                            break;
                        case 2:
                            data[index++] = v.X;
                            data[index++] = v.W;
                            break;
                        case 3:
                            data[index++] = v.X;
                            data[index++] = v.Y;
                            data[index++] = v.Z;
                            break;
                        default:
                            data[index++] = v.X;
                            data[index++] = v.Y;
                            data[index++] = v.Z;
                            data[index++] = v.W;
                            break;
                    }
                }
            }
        }

        return new NpyArray(new[] { frames, height, width, channels }, data);
    }
}
