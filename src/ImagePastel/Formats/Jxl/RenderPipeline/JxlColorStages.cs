// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Structure after libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// the colour-transform part of PassesDecoderState::PreparePipeline in lib/jxl/dec_cache.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// Chooses the colour-transform stages of a frame, which the render pipeline runs after the save before
/// the colour transform and before the blending.
/// </summary>
internal static class JxlColorStages
{
    /// <summary>
    /// Returns the colour-transform stages of the frame, in libjxl's order: the XYB (or YCbCr) stage, then
    /// the from-linear stage, which alone applies the output transfer function. libjxl puts the
    /// from-linear stage before the blending, before the save after the colour transform or before the
    /// output, whichever comes first. Before the output, libjxl's spot colour and tone mapping stages come
    /// between; this port has no tone mapping and renders spot colours in the sink, so the from-linear
    /// stage can follow the colour transform directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A frame without a colour transform has nothing to convert. A YCbCr frame has the YCbCr stage
    /// (<see cref="JxlYCbCrStage"/>) alone, on three colour planes even for a grey image
    /// (<see cref="JxlFrameHeader.DropsToGreyAfterColorTransform"/>): its output is already in the image's colour encoding, so no
    /// from-linear stage follows it. libjxl adds the from-linear stage only when the XYB stage does not output
    /// XYB itself, which this port never does (<see cref="JxlXybStage"/>). An XYB image that cannot render
    /// to its own encoding renders to linear sRGB, as djxl's default output, or to sRGB for a sink that
    /// takes display-encoded samples, as libjxl when it is asked for sRGB (<see cref="JxlOutputEncoding"/>).
    /// </para>
    /// <para>
    /// A departure from libjxl: with a spot colour channel and no blending or save after the colour
    /// transform, libjxl renders the spot colours of an XYB image on the linear samples, before the
    /// from-linear stage, where this port renders them in the sink, after it. Matching libjxl would put
    /// the spot colours before the sink's un-premultiplying, which issue #111 orders first so that a spot
    /// colour keeps its own colour, while libjxl un-premultiplies after the from-linear stage. With
    /// premultiplied alpha the three orders cannot all hold, and keeping both would need two paths for
    /// spot colours.
    /// </para>
    /// </remarks>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="displayEncoded">Whether the sink takes display-encoded samples (<see cref="IJxlRenderSink.DisplayEncoded"/>).</param>
    /// <returns>The stages, possibly none.</returns>
    public static IJxlColorStage[] Create(JxlFrameHeader frameHeader, bool displayEncoded)
    {
        if (frameHeader.ColorTransform == JxlColorTransform.YCbCr)
        {
            return new IJxlColorStage[] { new JxlYCbCrStage() };
        }

        if (frameHeader.ColorTransform != JxlColorTransform.Xyb)
        {
            return Array.Empty<IJxlColorStage>();
        }

        JxlOutputEncoding output = JxlOutputEncoding.Create(frameHeader.NonserializedMetadata, displayEncoded);
        JxlFromLinearStage fromLinear = JxlFromLinearStage.Create(output);
        return fromLinear == null
            ? new IJxlColorStage[] { new JxlXybStage(output) }
            : new IJxlColorStage[] { new JxlXybStage(output), fromLinear };
    }
}
