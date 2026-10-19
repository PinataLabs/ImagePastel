// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Structure after libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/render_pipeline/simple_render_pipeline.h and simple_render_pipeline.cc, and RenderPipelineInput
// from lib/jxl/render_pipeline/render_pipeline.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// The final stage of the render pipeline: it receives the finished full-frame planes.
/// </summary>
internal interface IJxlRenderSink
{
    /// <summary>
    /// Gets a value indicating whether the sink takes display-encoded samples. An XYB image that cannot
    /// render to its own colour encoding then reaches the sink as sRGB, as libjxl's output when it is
    /// asked for sRGB; otherwise it stays linear sRGB, as djxl's default output (<see cref="JxlOutputEncoding"/>).
    /// </summary>
    bool DisplayEncoded { get; }

    /// <summary>
    /// Consumes a rendered frame: one plane per channel (colour channels, then extra channels).
    /// </summary>
    /// <param name="planes">
    /// The planes, at least <paramref name="width"/> by <paramref name="height"/>: the colour planes of a
    /// VarDCT frame keep the padding to whole blocks past the frame.
    /// </param>
    /// <param name="width">The frame width.</param>
    /// <param name="height">The frame height.</param>
    /// <returns><see langword="true"/> if the sink keeps the planes and will dispose them.</returns>
    bool ConsumeFrame(Buffer2D<float>[] planes, int width, int height);
}
