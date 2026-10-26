// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Structure after libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: the parts of
// PassesDecoderState (lib/jxl/dec_cache.h) that PassesDecoderState::PreparePipeline in
// lib/jxl/dec_cache.cc hands to GetGaborishStage and GetEPFStage.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.VarDct;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// What the loop-filter stages of a frame are built from (<see cref="JxlLoopFilterStages.Create"/>):
/// the parts of libjxl's <c>PassesDecoderState</c> that <c>PreparePipeline</c> hands to
/// <c>GetGaborishStage</c> and <c>GetEPFStage</c>.
/// </summary>
internal sealed class JxlLoopFilterContext
{
    /// <summary>Initializes a new instance of the <see cref="JxlLoopFilterContext"/> class.</summary>
    /// <param name="loopFilter">The loop-filter fields of the frame header.</param>
    /// <param name="frameDim">The frame dimensions.</param>
    /// <param name="varDct">The state of a VarDCT frame, or <see langword="null"/> for a Modular frame.</param>
    public JxlLoopFilterContext(JxlLoopFilter loopFilter, JxlFrameDimensions frameDim, JxlVarDctFrameState varDct)
    {
        this.LoopFilter = loopFilter;
        this.FrameDim = frameDim;
        this.VarDct = varDct;
    }

    /// <summary>Gets the loop-filter fields of the frame header.</summary>
    public JxlLoopFilter LoopFilter { get; }

    /// <summary>Gets the frame dimensions.</summary>
    public JxlFrameDimensions FrameDim { get; }

    /// <summary>
    /// Gets the state of a VarDCT frame, with the quant field (<see cref="JxlVarDctFrameState.RawQuantField"/>),
    /// the quantiser and the EPF sharpness of each block (<see cref="JxlVarDctFrameState.EpfSharpness"/>);
    /// or <see langword="null"/> for a Modular frame.
    /// </summary>
    public JxlVarDctFrameState VarDct { get; }
}
