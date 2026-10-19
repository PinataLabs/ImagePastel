// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: GetGaborishStage in
// lib/jxl/render_pipeline/stage_gaborish.cc, as PassesDecoderState::PreparePipeline in lib/jxl/dec_cache.cc
// calls it.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <content>Gaborish: its supported-feature check and its stage.</content>
internal static partial class JxlLoopFilterStages
{
    // Gaborish decodes on VarDCT frames. A Modular frame with Gaborish waits for D9d.
    private static void CheckGaborishSupported(JxlFrameHeader frameHeader)
    {
        if (frameHeader.LoopFilter.Gab && frameHeader.Encoding == JxlFrameEncoding.Modular)
        {
            JxlThrowHelper.ThrowNotSupportedException("Loop filters (Gaborish) on a Modular frame");
        }
    }

    static partial void AddGaborishStage(List<IJxlLoopFilterStage> stages, JxlLoopFilterContext context)
    {
        if (context.LoopFilter.Gab)
        {
            stages.Add(new JxlGaborishStage(context.LoopFilter));
        }
    }
}
