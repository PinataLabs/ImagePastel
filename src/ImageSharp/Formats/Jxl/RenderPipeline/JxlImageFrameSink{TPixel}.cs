// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// Writes the rendered planes into an <see cref="ImageFrame{TPixel}"/>, clamped to [0, 1].
/// </summary>
/// <typeparam name="TPixel">The pixel type.</typeparam>
internal sealed class JxlImageFrameSink<TPixel> : IJxlRenderSink
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Configuration configuration;
    private readonly ImageFrame<TPixel> frame;

    public JxlImageFrameSink(Configuration configuration, ImageFrame<TPixel> frame)
    {
        this.configuration = configuration;
        this.frame = frame;
    }

    public bool ConsumeFrame(Buffer2D<float>[] planes, int width, int height)
    {
        // Alpha, spot colours, CMYK and the sRGB transfer for XYB arrive with later slices.
        Buffer2D<TPixel> pixels = this.frame.PixelBuffer;
        using IMemoryOwner<Vector4> rowOwner = this.configuration.MemoryAllocator.Allocate<Vector4>(width);
        Span<Vector4> row = rowOwner.GetSpan();
        for (int y = 0; y < height; y++)
        {
            Span<float> r = planes[0].DangerousGetRowSpan(y);
            Span<float> g = planes[1].DangerousGetRowSpan(y);
            Span<float> b = planes[2].DangerousGetRowSpan(y);
            for (int x = 0; x < width; x++)
            {
                row[x] = Vector4.Clamp(new Vector4(r[x], g[x], b[x], 1F), Vector4.Zero, Vector4.One);
            }

            PixelOperations<TPixel>.Instance.FromVector4Destructive(this.configuration, row, pixels.DangerousGetRowSpan(y), PixelConversionModifiers.Scale);
        }

        return false;
    }
}
