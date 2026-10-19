// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Structure after libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// RenderPipelineStage in lib/jxl/render_pipeline/render_pipeline_stage.h, for the stages with a border
// (GaborishStage, EPF0Stage, EPF1Stage, EPF2Stage) that PassesDecoderState::PreparePipeline in
// lib/jxl/dec_cache.cc adds before the patches.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// A loop-filter stage of the render pipeline: Gaborish or one EPF pass. It reads the pixels around the
/// one it writes, so it reads its input through <see cref="JxlNeighbourRows"/> and writes new planes,
/// as libjxl's <c>kInOut</c> channels.
/// </summary>
internal interface IJxlLoopFilterStage
{
    /// <summary>
    /// Gets how far the stage reads past the pixel it writes, in rows and in columns: libjxl's
    /// <c>Settings::SymmetricBorderOnly</c>, 1 for Gaborish and 3, 2 and 1 for the EPF passes 0, 1 and 2.
    /// </summary>
    int Border { get; }

    /// <summary>
    /// Returns whether the stage changes channel <paramref name="c"/> (libjxl's <c>kInOut</c>) or leaves
    /// it as it is (<c>kIgnored</c>). Only the channels it changes can be read through the
    /// <see cref="JxlNeighbourRows"/> and have an output plane.
    /// </summary>
    /// <param name="c">The channel: the colour planes, then the extra channels.</param>
    /// <returns><see langword="true"/> if the stage changes the channel.</returns>
    bool ChangesChannel(int c);

    /// <summary>Filters row <paramref name="y"/> of the frame.</summary>
    /// <param name="input">The rows around row <paramref name="y"/> of each channel the stage changes.</param>
    /// <param name="output">
    /// The output planes, one per channel: <see langword="null"/> for the channels the stage does not
    /// change. The stage writes the first <paramref name="xsize"/> samples of row <paramref name="y"/>.
    /// </param>
    /// <param name="xsize">The width of the frame, which may be less than the width of the planes.</param>
    /// <param name="y">The row.</param>
    void ProcessRow(JxlNeighbourRows input, Buffer2D<float>[] output, int xsize, int y);
}
