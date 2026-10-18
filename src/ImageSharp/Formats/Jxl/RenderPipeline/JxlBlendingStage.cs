// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// BlendingStage in lib/jxl/render_pipeline/stage_blending.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// Blends a frame onto its reference slots and switches the pipeline to the image size: each frame
/// row is blended in place over the background under it, then the image-sized planes are filled
/// from the background and the frame is copied in at its origin.
/// </summary>
internal sealed class JxlBlendingStage : IDisposable
{
    private readonly JxlFrameHeader frameHeader;
    private readonly List<JxlExtraChannelInfo> extraChannelInfo;
    private readonly int numColorPlanes;
    private readonly JxlReferenceFrame bg;
    private readonly JxlReferenceFrame[] ecBg;
    private readonly IMemoryOwner<float> zeroes;
    private readonly Buffer2D<float> tmp;
    private readonly JxlBlendingRows bgRows;
    private readonly JxlBlendingRows fgRows;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlBlendingStage"/> class, before the frame's
    /// sections are decoded, as libjxl builds its pipeline.
    /// </summary>
    /// <param name="allocator">The allocator.</param>
    /// <param name="frameHeader">The header of the frame.</param>
    /// <param name="references">The reference slots.</param>
    /// <param name="frameXSize">The width of the frame.</param>
    /// <param name="numChannels">The number of channels: the colour planes plus the extra channels.</param>
    public JxlBlendingStage(MemoryAllocator allocator, JxlFrameHeader frameHeader, JxlReferenceFrames references, int frameXSize, int numChannels)
    {
        JxlImageMetadata m = frameHeader.NonserializedMetadata.M;
        this.frameHeader = frameHeader;
        this.ImageXSize = (int)frameHeader.NonserializedMetadata.XSize;
        this.ImageYSize = (int)frameHeader.NonserializedMetadata.YSize;
        this.extraChannelInfo = m.ExtraChannelInfo;
        this.numColorPlanes = m.NumColorPlanes;

        List<JxlBlendingInfo> ecInfo = frameHeader.ExtraChannelBlendingInfo;
        this.bg = references[frameHeader.BlendingInfo.Source];
        this.ecBg = new JxlReferenceFrame[ecInfo.Count];
        for (int i = 0; i < ecInfo.Count; i++)
        {
            this.ecBg[i] = references[ecInfo[i].Source];
        }

        // libjxl allocates its zeros only when a slot is empty; they are always allocated here.
        this.zeroes = allocator.Allocate<float>(this.ImageXSize, AllocationOptions.Clean);
        this.tmp = allocator.Allocate2D<float>(Math.Max(frameXSize, 1), numChannels);

        Buffer2D<float>[] bgPlanes = new Buffer2D<float>[numChannels];
        for (int c = 0; c < this.numColorPlanes; c++)
        {
            bgPlanes[c] = this.bg?.Planes[c];
        }

        for (int i = 0; i < this.ecBg.Length; i++)
        {
            bgPlanes[this.numColorPlanes + i] = this.ecBg[i]?.Planes[this.numColorPlanes + i];
        }

        this.bgRows = new JxlBlendingRows(bgPlanes, this.zeroes.Memory);
        this.fgRows = new JxlBlendingRows(new Buffer2D<float>[numChannels], this.zeroes.Memory);
    }

    /// <summary>Gets the width of the image, which the pipeline switches to.</summary>
    public int ImageXSize { get; }

    /// <summary>Gets the height of the image, which the pipeline switches to.</summary>
    public int ImageYSize { get; }

    /// <summary>Gets the column of the image where the frame starts.</summary>
    public int FrameOriginX0 => this.frameHeader.FrameOriginX0;

    /// <summary>Gets the row of the image where the frame starts.</summary>
    public int FrameOriginY0 => this.frameHeader.FrameOriginY0;

    /// <summary>
    /// Blends row <paramref name="ypos"/> of the frame, in place, over the background under it. Rows
    /// and columns outside the image are left as they are; they are not copied into the image.
    /// </summary>
    /// <param name="planes">The frame planes.</param>
    /// <param name="xsize">The width of the frame.</param>
    /// <param name="ypos">The row.</param>
    public void ProcessRow(Buffer2D<float>[] planes, int xsize, int ypos)
    {
        long x0 = this.frameHeader.FrameOriginX0;
        long bgXpos = x0;
        long bgYpos = this.frameHeader.FrameOriginY0 + (long)ypos;
        long size = xsize;
        int offset = 0;
        if (bgXpos + size <= 0 || x0 >= this.ImageXSize || bgYpos < 0 || bgYpos >= this.ImageYSize)
        {
            return;
        }

        if (bgXpos < 0)
        {
            offset -= (int)bgXpos;
            size += bgXpos;
            bgXpos = 0;
        }

        if (bgXpos + size > this.ImageXSize)
        {
            size = Math.Max(0, this.ImageXSize - bgXpos);
        }

        for (int c = 0; c < planes.Length; c++)
        {
            this.fgRows.SetPlane(c, planes[c]);
        }

        this.fgRows.Set(offset, ypos, (int)size);
        this.bgRows.Set((int)bgXpos, (int)bgYpos, (int)size);
        JxlBlending.PerformBlending(this.bgRows, this.fgRows, this.numColorPlanes, this.frameHeader.BlendingInfo, this.frameHeader.ExtraChannelBlendingInfo, this.extraChannelInfo, this.tmp, (int)size);
    }

    /// <summary>Fills row <paramref name="ypos"/> of the image-sized planes from the background, or with zeros.</summary>
    /// <param name="output">The image-sized planes.</param>
    /// <param name="ypos">The row.</param>
    public void ProcessPaddingRow(Buffer2D<float>[] output, int ypos)
    {
        int xsize = this.ImageXSize;
        for (int c = 0; c < this.numColorPlanes; c++)
        {
            Span<float> row = output[c].DangerousGetRowSpan(ypos).Slice(0, xsize);
            if (this.bg == null)
            {
                row.Clear();
            }
            else
            {
                this.bg.Planes[c].DangerousGetRowSpan(ypos).Slice(0, xsize).CopyTo(row);
            }
        }

        for (int ec = 0; ec < this.extraChannelInfo.Count; ec++)
        {
            int c = this.numColorPlanes + ec;
            Span<float> row = output[c].DangerousGetRowSpan(ypos).Slice(0, xsize);
            JxlReferenceFrame ecBackground = this.ecBg[ec];
            if (ecBackground == null)
            {
                row.Clear();
            }
            else
            {
                ecBackground.Planes[c].DangerousGetRowSpan(ypos).Slice(0, xsize).CopyTo(row);
            }
        }
    }

    public void Dispose()
    {
        this.zeroes.Dispose();
        this.tmp.Dispose();
    }
}
