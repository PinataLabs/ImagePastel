// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// Writes the rendered planes into an <see cref="ImageFrame{TPixel}"/>, clamped to [0, 1]. The
/// first alpha channel becomes the pixel alpha; other extra channels are dropped (issue #111).
/// </summary>
/// <typeparam name="TPixel">The pixel type.</typeparam>
internal sealed class JxlImageFrameSink<TPixel> : IJxlRenderSink
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Configuration configuration;
    private readonly ImageFrame<TPixel> frame;
    private readonly int alphaPlane;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlImageFrameSink{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="frame">The frame to write.</param>
    /// <param name="alphaChannel">The extra channel that holds alpha, or -1 for an opaque image.</param>
    public JxlImageFrameSink(Configuration configuration, ImageFrame<TPixel> frame, int alphaChannel)
    {
        this.configuration = configuration;
        this.frame = frame;
        this.alphaPlane = alphaChannel < 0 ? -1 : 3 + alphaChannel;
    }

    public bool ConsumeFrame(Buffer2D<float>[] planes, int width, int height)
    {
        // Spot colours, CMYK and the sRGB transfer for XYB arrive with later slices.
        Buffer2D<TPixel> pixels = this.frame.PixelBuffer;
        using IMemoryOwner<Vector4> rowOwner = this.configuration.MemoryAllocator.Allocate<Vector4>(width);
        Span<Vector4> row = rowOwner.GetSpan();
        for (int y = 0; y < height; y++)
        {
            Span<float> r = planes[0].DangerousGetRowSpan(y);
            Span<float> g = planes[1].DangerousGetRowSpan(y);
            Span<float> b = planes[2].DangerousGetRowSpan(y);
            if (this.alphaPlane < 0)
            {
                for (int x = 0; x < width; x++)
                {
                    row[x] = Vector4.Clamp(new Vector4(r[x], g[x], b[x], 1F), Vector4.Zero, Vector4.One);
                }
            }
            else
            {
                Span<float> a = planes[this.alphaPlane].DangerousGetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    row[x] = Vector4.Clamp(new Vector4(r[x], g[x], b[x], a[x]), Vector4.Zero, Vector4.One);
                }
            }

            PixelOperations<TPixel>.Instance.FromVector4Destructive(this.configuration, row, pixels.DangerousGetRowSpan(y), PixelConversionModifiers.Scale);
        }

        return false;
    }
}
