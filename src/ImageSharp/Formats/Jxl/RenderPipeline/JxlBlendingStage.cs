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
    private readonly IMemoryOwner<float> zeros;
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
    public JxlBlendingStage(MemoryAllocator allocator, JxlFrameHeader frameHeader, JxlReferenceFrames references, int frameXSize)
    {
        JxlImageMetadata m = frameHeader.NonserializedMetadata.M;
        int numChannels = m.NumColorPlanes + m.NumExtraChannels;
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

        // An empty slot reads as zeros. libjxl checks the colour source for a frame saved before its
        // colour transform only when that slot is not empty.
        if (this.bg != null && this.bg.IsInXyb)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Trying to blend XYB reference frame {frameHeader.BlendingInfo.Source} and non-XYB frame");
        }

        this.VerifyBgSize(this.bg);
        foreach (JxlReferenceFrame ecBackground in this.ecBg)
        {
            this.VerifyBgSize(ecBackground);
        }

        // libjxl also rejects blending an XYB image whose output colour encoding is not the original
        // one ("Blending in unsupported color space"). XYB frames are a later slice.

        // As libjxl, the zeros are allocated only when a slot the frame blends onto is empty.
        if (this.bg == null || Array.IndexOf(this.ecBg, null) >= 0)
        {
            this.zeros = allocator.Allocate<float>(this.ImageXSize, AllocationOptions.Clean);
        }

        Memory<float> zeros = this.zeros?.Memory ?? Memory<float>.Empty;
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

        this.bgRows = new JxlBlendingRows(bgPlanes, zeros);
        this.fgRows = new JxlBlendingRows(new Buffer2D<float>[numChannels], zeros);
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

        this.fgRows.PointAt(offset, ypos, (int)size);
        this.bgRows.PointAt((int)bgXpos, (int)bgYpos, (int)size);
        JxlBlending.PerformBlending(this.bgRows, this.fgRows, this.numColorPlanes, this.frameHeader.BlendingInfo, this.frameHeader.ExtraChannelBlendingInfo, this.extraChannelInfo, this.tmp, (int)size);
    }

    /// <summary>Fills row <paramref name="ypos"/> of the image-sized planes from the background, or with zeros.</summary>
    /// <param name="output">The image-sized planes.</param>
    /// <param name="ypos">The row.</param>
    public void ProcessPaddingRow(Buffer2D<float>[] output, int ypos)
    {
        for (int c = 0; c < this.numColorPlanes; c++)
        {
            this.CopyBackgroundRow(this.bg, output, c, ypos);
        }

        for (int ec = 0; ec < this.extraChannelInfo.Count; ec++)
        {
            this.CopyBackgroundRow(this.ecBg[ec], output, this.numColorPlanes + ec, ypos);
        }
    }

    public void Dispose()
    {
        this.zeros?.Dispose();
        this.tmp.Dispose();
    }

    // Copies row ypos of channel c from the background, or clears it when the slot is empty.
    private void CopyBackgroundRow(JxlReferenceFrame background, Buffer2D<float>[] output, int c, int ypos)
    {
        Span<float> row = output[c].DangerousGetRowSpan(ypos).Slice(0, this.ImageXSize);
        if (background == null)
        {
            row.Clear();
        }
        else
        {
            background.Planes[c].DangerousGetRowSpan(ypos).Slice(0, this.ImageXSize).CopyTo(row);
        }
    }

    // verify_bg_size. A slot's frame always starts at the image origin here: libjxl sets an origin only
    // on the decoded frame, never on the frame it keeps for referencing.
    private void VerifyBgSize(JxlReferenceFrame background)
    {
        if (background != null && (background.Width < this.ImageXSize || background.Height < this.ImageYSize))
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Trying to use a {background.Width}x{background.Height} crop as a background");
        }
    }
}
