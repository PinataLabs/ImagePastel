// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: the EPF part of
// PassesDecoderState::PreparePipeline in lib/jxl/dec_cache.cc, and GetEPFStage in
// lib/jxl/render_pipeline/stage_epf.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <content>The edge-preserving filter (EPF): its supported-feature check and its passes.</content>
internal static partial class JxlLoopFilterStages
{
    // EPF on a VarDCT frame decodes. On a Modular frame, whose sigma is one value for the whole frame
    // (epf_sigma_for_modular), it still throws until D9d.
    private static void CheckEpfSupported(JxlFrameHeader frameHeader)
    {
        if (frameHeader.LoopFilter.EpfIters > 0 && frameHeader.Encoding != JxlFrameEncoding.VarDct)
        {
            JxlThrowHelper.ThrowNotSupportedException("Loop filters (EPF on a Modular frame)");
        }
    }

    static partial void AddEpfStages(List<IJxlLoopFilterStage> stages, JxlLoopFilterContext context)
    {
        JxlLoopFilter lf = context.LoopFilter;
        if (lf.EpfIters == 0)
        {
            return;
        }

        // The DC groups compute the sigma in libjxl (ComputeSigma in DecodeAcMetadata); here every DC
        // group is decoded by now, so it is computed for the whole frame at once.
        JxlEpfSigma sigma = JxlEpfSigma.Compute(lf, context.VarDct);
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
