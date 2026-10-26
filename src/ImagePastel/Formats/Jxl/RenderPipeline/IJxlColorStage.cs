// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Structure after libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// RenderPipelineStage in lib/jxl/render_pipeline/render_pipeline_stage.h, for the colour-transform
// stages (XYBStage, YCbCrStage, FromLinearStage) that PassesDecoderState::PreparePipeline in
// lib/jxl/dec_cache.cc adds after the save before the colour transform and before the blending.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// A colour-transform stage of the render pipeline: it changes the colour planes of one row in place.
/// </summary>
internal interface IJxlColorStage
{
    /// <summary>Transforms row <paramref name="y"/> of the frame.</summary>
    /// <param name="planes">The frame planes, one per channel: the colour planes, then the extra channels.</param>
    /// <param name="xsize">The width of the frame, which may be less than the width of the planes.</param>
    /// <param name="y">The row.</param>
    void ProcessRow(Buffer2D<float>[] planes, int xsize, int y);
}
