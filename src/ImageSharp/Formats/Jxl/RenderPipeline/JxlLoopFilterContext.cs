// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// What the loop-filter stages of a frame are built from (<see cref="JxlLoopFilterStages.Create"/>):
/// the parts of libjxl's <c>PassesDecoderState</c> that <c>PreparePipeline</c> hands to
/// <c>GetGaborishStage</c> and <c>GetEPFStage</c>.
/// </summary>
internal sealed class JxlLoopFilterContext
{
    /// <summary>Initializes a new instance of the <see cref="JxlLoopFilterContext"/> class.</summary>
    /// <param name="allocator">The allocator.</param>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="frameDim">The frame dimensions.</param>
    /// <param name="varDct">The state of a VarDCT frame, or <see langword="null"/> for a Modular frame.</param>
    public JxlLoopFilterContext(MemoryAllocator allocator, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlVarDctFrameState varDct)
    {
        this.Allocator = allocator;
        this.FrameHeader = frameHeader;
        this.FrameDim = frameDim;
        this.VarDct = varDct;
    }

    /// <summary>Gets the allocator.</summary>
    public MemoryAllocator Allocator { get; }

    /// <summary>Gets the frame header, with the loop-filter fields.</summary>
    public JxlFrameHeader FrameHeader { get; }

    /// <summary>Gets the frame dimensions.</summary>
    public JxlFrameDimensions FrameDim { get; }

    /// <summary>
    /// Gets the state of a VarDCT frame, with the quant field (<see cref="JxlVarDctFrameState.RawQuantField"/>),
    /// the quantiser and the EPF sharpness of each block (<see cref="JxlVarDctFrameState.EpfSharpness"/>);
    /// or <see langword="null"/> for a Modular frame.
    /// </summary>
    public JxlVarDctFrameState VarDct { get; }

    /// <summary>Gets the loop-filter fields of the frame header.</summary>
    public JxlLoopFilter LoopFilter => this.FrameHeader.LoopFilter;
}
