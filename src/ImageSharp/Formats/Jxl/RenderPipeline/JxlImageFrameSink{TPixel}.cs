// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Un-premultiplying follows libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// UnpremultiplyAlpha in lib/jxl/alpha.cc and kSmallAlpha in lib/jxl/alpha.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// Writes each rendered frame into a frame of an <see cref="Image{TPixel}"/>, clamped to [0, 1]: the
/// first into the root frame, each later one into a new frame. The first alpha channel becomes the pixel alpha, un-premultiplied if it is premultiplied; other extra
/// channels are dropped (issue #111). A grey plane goes into every colour component.
/// </summary>
/// <typeparam name="TPixel">The pixel type.</typeparam>
internal sealed class JxlImageFrameSink<TPixel> : IJxlRenderSink
    where TPixel : unmanaged, IPixel<TPixel>
{
    // libjxl's kSmallAlpha: un-premultiplying divides by at least this, so alpha 0 gives a defined colour.
    private const float SmallAlpha = 1F / (1u << 26);

    private readonly Configuration configuration;
    private readonly Image<TPixel> image;
    private readonly int greenPlane;
    private readonly int bluePlane;
    private readonly int alphaPlane;
    private readonly bool unpremultiply;
    private int framesWritten;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlImageFrameSink{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="image">The image whose frames are written.</param>
    /// <param name="colorPlanes">The number of colour planes: 1 for grey, 3 for RGB.</param>
    /// <param name="alphaChannel">The extra channel that holds alpha, or -1 for an opaque image.</param>
    /// <param name="alphaPremultiplied">Whether the colours are premultiplied by that alpha.</param>
    public JxlImageFrameSink(Configuration configuration, Image<TPixel> image, int colorPlanes, int alphaChannel, bool alphaPremultiplied)
    {
        this.configuration = configuration;
        this.image = image;
        this.greenPlane = colorPlanes == 1 ? 0 : 1;
        this.bluePlane = colorPlanes == 1 ? 0 : 2;
        this.alphaPlane = alphaChannel < 0 ? -1 : colorPlanes + alphaChannel;
        this.unpremultiply = alphaChannel >= 0 && alphaPremultiplied;
    }

    public bool ConsumeFrame(Buffer2D<float>[] planes, int width, int height)
    {
        // Spot colours, CMYK and the sRGB transfer for XYB arrive with later slices.
        ImageFrame<TPixel> frame = this.framesWritten == 0 ? this.image.Frames.RootFrame : this.image.Frames.CreateFrame();
        this.framesWritten++;
        Buffer2D<TPixel> pixels = frame.PixelBuffer;
        using IMemoryOwner<Vector4> rowOwner = this.configuration.MemoryAllocator.Allocate<Vector4>(width);
        Span<Vector4> row = rowOwner.GetSpan();
        for (int y = 0; y < height; y++)
        {
            Span<float> r = planes[0].DangerousGetRowSpan(y);
            Span<float> g = planes[this.greenPlane].DangerousGetRowSpan(y);
            Span<float> b = planes[this.bluePlane].DangerousGetRowSpan(y);
            if (this.alphaPlane < 0)
            {
                for (int x = 0; x < width; x++)
                {
                    row[x] = Vector4.Clamp(new Vector4(r[x], g[x], b[x], 1F), Vector4.Zero, Vector4.One);
                }
            }
            else if (!this.unpremultiply)
            {
                Span<float> a = planes[this.alphaPlane].DangerousGetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    row[x] = Vector4.Clamp(new Vector4(r[x], g[x], b[x], a[x]), Vector4.Zero, Vector4.One);
                }
            }
            else
            {
                // As libjxl's UnpremultiplyAlpha (lib/jxl/alpha.cc), before the clamp.
                Span<float> a = planes[this.alphaPlane].DangerousGetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    float multiplier = 1F / MathF.Max(SmallAlpha, a[x]);
                    Vector4 v = new(r[x] * multiplier, g[x] * multiplier, b[x] * multiplier, a[x]);
                    row[x] = Vector4.Clamp(v, Vector4.Zero, Vector4.One);
                }
            }

            PixelOperations<TPixel>.Instance.FromVector4Destructive(this.configuration, row, pixels.DangerousGetRowSpan(y), PixelConversionModifiers.Scale);
        }

        return false;
    }
}
