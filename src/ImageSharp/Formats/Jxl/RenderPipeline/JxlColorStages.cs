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
    /// the from-linear stage. libjxl puts the from-linear stage before the blending, before the save after
    /// the colour transform or before the output, whichever comes first. Before the output, libjxl's
    /// spot colour and tone mapping stages come between; this port has no tone mapping and renders spot
    /// colours in the sink, so the from-linear stage can follow the colour transform directly.
    /// </summary>
    /// <remarks>
    /// Empty for now: the frame decoder rejects every frame with a colour transform, and a frame with none
    /// has nothing to convert.
    /// </remarks>
    /// <param name="frameHeader">The frame header.</param>
    /// <returns>The stages, possibly none.</returns>
    public static IJxlColorStage[] Create(JxlFrameHeader frameHeader) => Array.Empty<IJxlColorStage>();
}
