// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Structure after libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: an entry of
// RenderPipeline::channel_shifts_ in lib/jxl/render_pipeline/render_pipeline.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// The shift of one channel of the render pipeline: log2 of how much the stages still to run will
/// upsample it, horizontally and vertically. At shift <c>s</c> the channel is the upsampled frame size
/// divided by <c>2^s</c>, rounded up.
/// </summary>
internal readonly struct JxlChannelShift
{
    public JxlChannelShift(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    /// <summary>Gets the horizontal shift.</summary>
    public int X { get; }

    /// <summary>Gets the vertical shift.</summary>
    public int Y { get; }
}
