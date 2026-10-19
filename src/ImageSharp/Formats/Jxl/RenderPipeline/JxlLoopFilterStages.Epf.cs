// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: the EPF part of
// PassesDecoderState::PreparePipeline in lib/jxl/dec_cache.cc, GetEPFStage in
// lib/jxl/render_pipeline/stage_epf.cc, and the sigma fill of a Modular frame in
// FrameDecoder::ProcessDCGroup (lib/jxl/dec_frame.cc).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <content>The edge-preserving filter (EPF): its passes, on VarDCT and Modular frames alike.</content>
internal static partial class JxlLoopFilterStages
{
    static partial void AddEpfStages(List<IJxlLoopFilterStage> stages, JxlLoopFilterContext context)
    {
        JxlLoopFilter lf = context.LoopFilter;
        if (lf.EpfIters == 0)
        {
            return;
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
    }
}
