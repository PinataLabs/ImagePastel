// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Structure after libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: the loop-filter part of
// PassesDecoderState::PreparePipeline in lib/jxl/dec_cache.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// Builds the loop-filter stages of a frame, for the loop-filter slot of <see cref="JxlRenderPipeline.Finish"/>,
/// and rejects the loop filters this port does not decode yet.
/// </summary>
/// <remarks>
/// Each filter lives in a part of its own: Gaborish in <c>JxlLoopFilterStages.Gaborish.cs</c> and EPF in
/// <c>JxlLoopFilterStages.Epf.cs</c>. The part holds the filter's check and, once the filter is ported,
/// the implementation of the partial method that adds its stages, so the two can be ported apart. Until
/// a part implements its partial method, the call to it is compiled away and the filter adds no stage;
/// its check rejects every frame that uses it.
/// </remarks>
internal static partial class JxlLoopFilterStages
{
    /// <summary>
    /// Throws <see cref="System.NotSupportedException"/> if the frame uses a loop filter that this port
    /// does not decode yet. Gaborish and EPF have a check each, so that each can be lifted on its own.
    /// </summary>
    /// <param name="frameHeader">The frame header.</param>
    public static void CheckSupported(JxlFrameHeader frameHeader)
    {
        CheckGaborishSupported(frameHeader);
        CheckEpfSupported(frameHeader);
    }

    /// <summary>
    /// Returns the loop-filter stages of the frame in libjxl's order: Gaborish, then the EPF passes 0, 1
    /// and 2 (pass 0 only with three iterations, pass 2 only with two or more).
    /// </summary>
    /// <param name="context">What the stages are built from.</param>
    /// <returns>The stages, possibly none.</returns>
    public static IJxlLoopFilterStage[] Create(JxlLoopFilterContext context)
    {
        List<IJxlLoopFilterStage> stages = new();
        AddGaborishStage(stages, context);
        AddEpfStages(stages, context);
        return stages.ToArray();
    }

    // GetGaborishStage, for a frame with loop_filter.gab.
    static partial void AddGaborishStage(List<IJxlLoopFilterStage> stages, JxlLoopFilterContext context);

    // GetEPFStage for the passes the frame's loop_filter.epf_iters asks for.
    static partial void AddEpfStages(List<IJxlLoopFilterStage> stages, JxlLoopFilterContext context);
}
