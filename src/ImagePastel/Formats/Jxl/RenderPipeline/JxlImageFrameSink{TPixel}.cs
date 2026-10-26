// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Un-premultiplying follows libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// UnpremultiplyAlpha in lib/jxl/alpha.cc and kSmallAlpha in lib/jxl/alpha.h. Spot colours are
// rendered as SpotColorStage in lib/jxl/render_pipeline/stage_spot.cc, which the decoder adds for every
// spot colour channel in PassesDecoderState::PreparePipeline (lib/jxl/dec_cache.cc). CMYK is not from
// libjxl, which converts it through a CMS: see ConvertCmyk.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// Writes each rendered frame into a frame of an <see cref="Image{TPixel}"/>, clamped to [0, 1]: the
/// first into the root frame, each later one into a new frame. The first alpha channel becomes the
/// pixel alpha, un-premultiplied if it is premultiplied, spot colours are rendered over the colour, and
/// a CMYK image is converted to RGB; other extra channels are dropped (issue #111). A grey plane goes
/// into every colour component.
/// </summary>
/// <typeparam name="TPixel">The pixel type.</typeparam>
internal sealed class JxlImageFrameSink<TPixel> : IJxlRenderSink
    where TPixel : unmanaged, IPixel<TPixel>
{
    // libjxl's kSmallAlpha: un-premultiplying divides by at least this, so alpha 0 gives a defined colour.
    private const float SmallAlpha = 1F / (1u << 26);

    private readonly Configuration configuration;
    private readonly Image<TPixel> image;
    private readonly bool gray;
    private readonly int greenPlane;
    private readonly int bluePlane;
    private readonly int alphaPlane;
    private readonly bool unpremultiply;

    // The spot colour channels, in stream order.
    private readonly SpotChannel[] spotChannels;

    // The plane of the Black channel of a CMYK image, or -1.
    private readonly int blackPlane;
    private int framesWritten;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlImageFrameSink{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="image">The image whose frames are written.</param>
    /// <param name="metadata">The image metadata, which gives the colour planes and the extra channels.</param>
    /// <param name="cmyk">Whether the image is CMYK: its colour planes are C, M and Y, with K in its Black channel.</param>
    public JxlImageFrameSink(Configuration configuration, Image<TPixel> image, JxlImageMetadata metadata, bool cmyk)
    {
        this.configuration = configuration;
        this.image = image;
        int colorPlanes = metadata.NumOutputColorPlanes;
        this.gray = colorPlanes == 1;
        this.greenPlane = this.gray ? 0 : 1;
        this.bluePlane = this.gray ? 0 : 2;

        int alphaChannel = metadata.Find(JxlExtraChannelType.Alpha);
        this.alphaPlane = alphaChannel < 0 ? -1 : colorPlanes + alphaChannel;
        this.unpremultiply = alphaChannel >= 0 && metadata.ExtraChannelInfo[alphaChannel].AlphaAssociated;

        // As PreparePipeline, which does not use Find because there may be several spot colour channels.
        List<SpotChannel> spotChannels = new();
        for (int i = 0; i < metadata.NumExtraChannels; i++)
        {
            JxlExtraChannelInfo eci = metadata.ExtraChannelInfo[i];
            if (eci.Type == JxlExtraChannelType.SpotColor)
            {
                spotChannels.Add(new SpotChannel(colorPlanes + i, eci.SpotColor));
            }
        }

        this.spotChannels = spotChannels.ToArray();
        this.blackPlane = cmyk ? colorPlanes + metadata.Find(JxlExtraChannelType.Black) : -1;
    }

    /// <summary>
    /// Gets a value indicating whether the sink takes display-encoded samples. It does: an XYB image that
    /// cannot render to its own colour encoding, such as one with an ICC profile, reaches it as sRGB,
    /// and its pixels get no pixel profile (ADR 0002).
    /// </summary>
    public bool DisplayEncoded => true;

    public bool ConsumeFrame(Buffer2D<float>[] planes, int width, int height)
    {
        // Issue #111's order: un-premultiply, render the spot colours, convert CMYK to RGB, clamp, then
        // quantise. The output transfer function is the from-linear stage's (JxlColorStages).
        ImageFrame<TPixel> frame = this.framesWritten == 0 ? this.image.Frames.RootFrame : this.image.Frames.CreateFrame();
        this.framesWritten++;
        Buffer2D<TPixel> pixels = frame.PixelBuffer;
        using IMemoryOwner<Vector4> rowOwner = this.configuration.MemoryAllocator.Allocate<Vector4>(width);
        Span<Vector4> row = rowOwner.GetSpan().Slice(0, width);
        for (int y = 0; y < height; y++)
        {
            this.ReadColor(planes, y, row);
            foreach (SpotChannel spot in this.spotChannels)
            {
                this.RenderSpotColor(planes[spot.Plane].DangerousGetRowSpan(y), spot.Color, row);
            }

            if (this.blackPlane >= 0)
            {
                ConvertCmyk(planes[this.blackPlane].DangerousGetRowSpan(y), row);
            }

            for (int x = 0; x < row.Length; x++)
            {
                row[x] = Vector4.Clamp(row[x], Vector4.Zero, Vector4.One);
            }

            PixelOperations<TPixel>.Instance.FromVector4Destructive(this.configuration, row, pixels.DangerousGetRowSpan(y), PixelConversionModifiers.Scale);
        }

        return false;
    }

    // The colour and alpha of row y, unclamped, with the colour un-premultiplied if it is premultiplied.
    private void ReadColor(Buffer2D<float>[] planes, int y, Span<Vector4> row)
    {
        Span<float> r = planes[0].DangerousGetRowSpan(y);
        Span<float> g = planes[this.greenPlane].DangerousGetRowSpan(y);
        Span<float> b = planes[this.bluePlane].DangerousGetRowSpan(y);
        if (this.alphaPlane < 0)
        {
            for (int x = 0; x < row.Length; x++)
            {
                row[x] = new Vector4(r[x], g[x], b[x], 1F);
            }
        }
        else if (!this.unpremultiply)
        {
            Span<float> a = planes[this.alphaPlane].DangerousGetRowSpan(y);
            for (int x = 0; x < row.Length; x++)
            {
                row[x] = new Vector4(r[x], g[x], b[x], a[x]);
            }
        }
        else
        {
            // As libjxl's UnpremultiplyAlpha (lib/jxl/alpha.cc). A departure: libjxl un-premultiplies as it
            // writes the output, after it renders the spot colours, so it divides them by alpha too. Issue
            // #111 un-premultiplies first, so a spot colour keeps its own RGB.
            Span<float> a = planes[this.alphaPlane].DangerousGetRowSpan(y);
            for (int x = 0; x < row.Length; x++)
            {
                float multiplier = 1F / MathF.Max(SmallAlpha, a[x]);
                row[x] = new Vector4(r[x] * multiplier, g[x] * multiplier, b[x] * multiplier, a[x]);
            }
        }
    }

    // The naive conversion of issue #111, as the JPEG decoder's FromCmykScalar converts Adobe CMYK:
    // R = C K, G = M K, B = Y K. JPEG XL stores ink as 1 - amount, so 0 is full ink and the product is
    // the colour left. There is no ICC transform; the CMYK profile stays in JxlMetadata.EmbeddedIccProfile.
    private static void ConvertCmyk(Span<float> k, Span<Vector4> row)
    {
        for (int x = 0; x < row.Length; x++)
        {
            ref Vector4 p = ref row[x];
            p.X *= k[x];
            p.Y *= k[x];
            p.Z *= k[x];
        }
    }

    // SpotColorStage::ProcessRow: each colour channel moves towards the spot colour by the spot sample
    // times its solidity. libjxl's pipeline always has three colour channels, and the grey output takes
    // the first, so a grey image is rendered with the spot colour's red.
    private void RenderSpotColor(Span<float> s, float[] spotColor, Span<Vector4> row)
    {
        float scale = spotColor[3];
        for (int x = 0; x < row.Length; x++)
        {
            float mix = scale * s[x];
            ref Vector4 p = ref row[x];
            p.X = (mix * spotColor[0]) + ((1.0f - mix) * p.X);
            if (this.gray)
            {
                p.Y = p.X;
                p.Z = p.X;
            }
            else
            {
                p.Y = (mix * spotColor[1]) + ((1.0f - mix) * p.Y);
                p.Z = (mix * spotColor[2]) + ((1.0f - mix) * p.Z);
            }
        }
    }

    // A spot colour channel: its plane, and its RGB and solidity from the header.
    private readonly struct SpotChannel
    {
        public SpotChannel(int plane, float[] color)
        {
            this.Plane = plane;
            this.Color = color;
        }

        public int Plane { get; }

        public float[] Color { get; }
    }
}
