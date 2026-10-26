// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: the loop-filter part of
// PassesDecoderState::PreparePipeline in lib/jxl/dec_cache.cc, GetGaborishStage in
// lib/jxl/render_pipeline/stage_gaborish.cc, GetEPFStage in lib/jxl/render_pipeline/stage_epf.cc, and the
// sigma fill of a Modular frame in FrameDecoder::ProcessDCGroup (lib/jxl/dec_frame.cc).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// Builds the loop-filter stages of a frame, for the loop-filter slot of <see cref="JxlRenderPipeline.Finish"/>.
/// Both filters decode on VarDCT and Modular frames, alone or together.
/// </summary>
internal static class JxlLoopFilterStages
{
    /// <summary>
    /// Returns the loop-filter stages of the frame in libjxl's order: Gaborish, then the EPF passes 0, 1
    /// and 2 (pass 0 only with three iterations, pass 2 only with two or more).
    /// </summary>
    /// <param name="context">What the stages are built from.</param>
    /// <returns>The stages, possibly none.</returns>
    public static IJxlNeighbourStage[] Create(JxlLoopFilterContext context)
    {
        List<IJxlNeighbourStage> stages = new();
        JxlLoopFilter lf = context.LoopFilter;

        // GetGaborishStage, for a frame with loop_filter.gab.
        if (lf.Gab)
        {
            stages.Add(new JxlGaborishStage(lf));
        }

        // GetEPFStage for the passes the frame's loop_filter.epf_iters asks for.
        if (lf.EpfIters == 0)
        {
            return stages.ToArray();
        }

        // The DC groups compute the sigma in libjxl: ComputeSigma in DecodeAcMetadata for a VarDCT frame,
        // and for a Modular frame, which has no quant field or sharpness, FillImage with
        // kInvSigmaNum / epf_sigma_for_modular. Here every DC group is decoded by now, so it is computed
        // for the whole frame at once.
        JxlEpfSigma sigma = context.VarDct != null
            ? JxlEpfSigma.Compute(lf, context.VarDct)
            : JxlEpfSigma.Constant(JxlEpfSigma.InvSigmaNum / lf.EpfSigmaForModular, (int)context.FrameDim.XSizeBlocks);
        if (lf.EpfIters >= 3)
        {
            stages.Add(new JxlEpf0Stage(lf, sigma));
        }

        if (lf.EpfIters >= 1)
        {
            stages.Add(new JxlEpf1Stage(lf, sigma));
        }

        if (lf.EpfIters >= 2)
        {
            stages.Add(new JxlEpf2Stage(lf, sigma));
        }

        return stages.ToArray();
    }
}
