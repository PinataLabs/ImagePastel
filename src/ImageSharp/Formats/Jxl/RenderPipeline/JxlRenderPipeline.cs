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
/// The serial, full-frame render pipeline: libjxl's <c>SimpleRenderPipeline</c>, its reference path.
/// It holds one float plane per channel; groups write their rectangles into them, and once every
/// group is in, the stages run over the whole frame and the sink receives the planes.
/// </summary>
/// <remarks>
/// Deliberate departures from libjxl: the planes carry no border padding, because no stage needs
/// neighbours yet; and stages are to run in place rather than allocate new planes each, as decided
/// in issue #113. The first slice has no stages, only the sink.
/// </remarks>
internal sealed class JxlRenderPipeline : IDisposable
{
    private readonly JxlFrameDimensions frameDim;
    private Buffer2D<float>[] planes;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlRenderPipeline"/> class, allocating one plane per channel.
    /// </summary>
    /// <param name="allocator">The allocator.</param>
    /// <param name="frameDim">The frame dimensions.</param>
    /// <param name="numChannels">The number of channels: 3 colour channels plus the extra channels.</param>
    public JxlRenderPipeline(MemoryAllocator allocator, JxlFrameDimensions frameDim, int numChannels)
    {
        this.frameDim = frameDim;
        this.planes = new Buffer2D<float>[numChannels];
        for (int c = 0; c < numChannels; c++)
        {
            this.planes[c] = allocator.Allocate2D<float>((int)frameDim.XSizeUpsampled, (int)frameDim.YSizeUpsampled);
        }
    }

    /// <summary>Gets the number of channels.</summary>
    public int NumChannels => this.planes.Length;

    /// <summary>Returns the plane of channel <paramref name="c"/>.</summary>
    /// <param name="c">The channel.</param>
    /// <returns>The plane.</returns>
    public Buffer2D<float> GetPlane(int c) => this.planes[c];

    /// <summary>
    /// Returns the rectangle of group <paramref name="groupId"/>, clipped to the frame, as libjxl's
    /// <c>MakeChannelRect</c>. libjxl takes a channel too, for channel shifts and upsampling; those
    /// arrive with later stages, so every channel is full size here.
    /// </summary>
    /// <param name="groupId">The group.</param>
    /// <returns>The rectangle.</returns>
    public JxlRect GetGroupRect(long groupId)
    {
        long gx = groupId % this.frameDim.XSizeGroups;
        long gy = groupId / this.frameDim.XSizeGroups;
        long groupDim = this.frameDim.GroupDimension;
        return new JxlRect(gx * groupDim, gy * groupDim, groupDim, groupDim, this.frameDim.XSizeUpsampled, this.frameDim.YSizeUpsampled);
    }

    /// <summary>Runs the stages over the whole frame and hands the planes to <paramref name="sink"/>.</summary>
    /// <param name="sink">The sink.</param>
    public void Finish(IJxlRenderSink sink)
    {
        if (sink.ConsumeFrame(this.planes, (int)this.frameDim.XSizeUpsampled, (int)this.frameDim.YSizeUpsampled))
        {
            this.planes = Array.Empty<Buffer2D<float>>();
        }
    }

    public void Dispose()
    {
        foreach (Buffer2D<float> plane in this.planes)
        {
            plane?.Dispose();
        }

        this.planes = Array.Empty<Buffer2D<float>>();
    }
}
