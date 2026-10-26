// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Structure after libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// RenderPipelineStage and its Settings in lib/jxl/render_pipeline/render_pipeline_stage.h, for the stages
// with a border or a shift: GaborishStage, EPF0Stage, EPF1Stage and EPF2Stage, and from D10 the
// upsampling and chroma-upsampling stages, which PassesDecoderState::PreparePipeline in
// lib/jxl/dec_cache.cc adds around the patches.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// A neighbour stage of the render pipeline: a loop filter (Gaborish or one EPF pass), or a stage that
/// upsamples. It reads the pixels around the one it writes, so it reads its input through
/// <see cref="JxlNeighbourRows"/> and writes new planes, as libjxl's <c>kInOut</c> channels. A stage
/// with a shift writes planes <c>2^ShiftX</c> times as wide and <c>2^ShiftY</c> times as high as its input.
/// </summary>
/// <remarks>
/// libjxl has no such type: its stages all derive from <c>RenderPipelineStage</c>, and a stage's settings
/// say whether it reads neighbours or upsamples. This port runs those stages apart from the others
/// (<see cref="JxlRenderPipeline.Finish"/>), so they share this interface.
/// The channels are numbered as libjxl numbers them: three colour channels, then the extra channels. The
/// pipeline gives a grey image three colour planes for these stages, as libjxl does.
/// </remarks>
internal interface IJxlNeighbourStage
{
    /// <summary>
    /// Gets how far the stage reads past the pixel it writes, in rows and in columns: libjxl's
    /// <c>Settings::SymmetricBorderOnly</c>, 1 for Gaborish and 3, 2 and 1 for the EPF passes 0, 1 and 2.
    /// </summary>
    int Border { get; }

    /// <summary>
    /// Gets log2 of how much wider the stage makes the channels it changes: libjxl's
    /// <c>Settings::shift_x</c>, 0 (the default) for the loop filters and 1 for an upsampling stage.
    /// </summary>
    int ShiftX => 0;

    /// <summary>Gets log2 of how much higher the stage makes the channels it changes: libjxl's <c>Settings::shift_y</c>.</summary>
    int ShiftY => 0;

    /// <summary>
    /// Returns whether the stage changes channel <paramref name="c"/> (libjxl's <c>kInOut</c>) or leaves
    /// it as it is (<c>kIgnored</c>). Only the channels it changes can be read through the
    /// <see cref="JxlNeighbourRows"/> and have an output plane.
    /// </summary>
    /// <param name="c">The channel: the colour planes, then the extra channels.</param>
    /// <returns><see langword="true"/> if the stage changes the channel.</returns>
    bool ChangesChannel(int c);

    /// <summary>Processes input row <paramref name="y"/> of the channels the stage changes.</summary>
    /// <param name="input">
    /// The rows around row <paramref name="y"/> of each channel the stage changes, mirrored at the
    /// channel's current size.
    /// </param>
    /// <param name="output">
    /// The output planes, one per channel: <see langword="null"/> for the channels the stage does not
    /// change. The stage writes the first <c>xsize &lt;&lt; ShiftX</c> samples of rows
    /// <c>(y &lt;&lt; ShiftY) + i</c>, for <c>i</c> from 0 to <c>2^ShiftY - 1</c>.
    /// </param>
    /// <param name="xsize">
    /// The width of the channels the stage changes, at their current shift; it may be less than the
    /// width of the planes.
    /// </param>
    /// <param name="y">The input row.</param>
    void ProcessRow(JxlNeighbourRows input, Buffer2D<float>[] output, int xsize, int y);
}
