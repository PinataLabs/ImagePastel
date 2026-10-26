// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;

/// <summary>
/// Converts an <see cref="Image{TPixel}"/> to the conformance reference layout (frames, height, width, channels).
/// An <see cref="Image{TPixel}"/> keeps at most RGBA, clamped to [0, 1], so this only suits vectors whose
/// reference has no other extra channels and no out-of-range values. It also compares such an image with
/// the clamped reference.
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

    /// <summary>
    /// Converts the decoder's float planes to the reference layout, one channel per plane, values as they are.
    /// </summary>
    public static NpyArray ToNpy(JxlDecodedImage image)
    {
        int width = image.Width;
        int height = image.Height;
        int frames = image.Frames.Count;
        int channels = frames == 0 ? 0 : image.Frames[0].Planes.Count;
        float[] data = new float[frames * height * width * channels];
        int index = 0;
        for (int f = 0; f < frames; f++)
        {
            JxlDecodedFrame frame = image.Frames[f];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        Buffer2D<float> plane = frame.Planes[c];
                        data[index++] = plane.DangerousGetRowSpan(y)[x];
                    }
                }
            }
        }

        return new NpyArray(new[] { frames, height, width, channels }, data);
    }

    /// <summary>
    /// Checks that an image of one frame (<see cref="ToNpy{TPixel}"/>) is within one 8-bit step of the
    /// reference, channel by channel, with both clamped to [0, 1], and records the peak error.
    /// </summary>
    /// <param name="expected">The reference.</param>
    /// <param name="actual">The image, with the reference's channels.</param>
    public static void AssertWithinOneStepOfTheClampedReference(NpyArray expected, NpyArray actual)
    {
        JxlConformanceFrameResult result = Assert.Single(JxlConformanceComparer.Measure(expected, actual, JxlConformanceComparison.ClampToUnitRange));
        TestContext.Current.TestOutputHelper?.WriteLine($"Peak error {result.PeakError * 255:G3} of one 8-bit step");
        Assert.True(result.PeakError <= (1.0 / 255) + 1e-6, $"Peak error {result.PeakError * 255:G4} steps");
    }

    /// <summary>
    /// Checks that an RGB image (<see cref="ToNpy{TPixel}"/> with 3 channels) is within one 8-bit step of
    /// the reference clamped to [0, 1]. A grey reference has one channel, which each of R, G and B is
    /// compared with, as a grey image is R = G = B.
    /// </summary>
    /// <param name="expected">The reference, with one or three channels.</param>
    /// <param name="actual">The image, with three channels.</param>
    public static void AssertRgbWithinOneStepOfTheClampedReference(NpyArray expected, NpyArray actual)
    {
        Assert.Equal(expected.Shape[1], actual.Shape[1]);
        Assert.Equal(expected.Shape[2], actual.Shape[2]);
        int channels = expected.Shape[3];
        int pixels = actual.Shape[1] * actual.Shape[2];
        double worst = 0;
        for (int i = 0; i < pixels; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                double e = Math.Clamp(expected.Data[(i * channels) + (channels == 1 ? 0 : c)], 0F, 1F);
                worst = Math.Max(worst, Math.Abs(actual.Data[(i * 3) + c] - e) * 255);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Largest difference {worst:G3} of one 8-bit step");
        Assert.True(worst <= 1 + 1e-4, $"Largest difference {worst:G4} 8-bit steps");
    }
}
