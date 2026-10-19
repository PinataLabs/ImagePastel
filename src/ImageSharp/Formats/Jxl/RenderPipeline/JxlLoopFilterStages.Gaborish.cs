// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: GetGaborishStage in
// lib/jxl/render_pipeline/stage_gaborish.cc, as PassesDecoderState::PreparePipeline in lib/jxl/dec_cache.cc
// calls it.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <content>Gaborish: its stage, on VarDCT and Modular frames alike.</content>
internal static partial class JxlLoopFilterStages
{
    static partial void AddGaborishStage(List<IJxlLoopFilterStage> stages, JxlLoopFilterContext context)
    {
        if (context.LoopFilter.Gab)
        {
            stages.Add(new JxlGaborishStage(context.LoopFilter));
        }
    }
}
