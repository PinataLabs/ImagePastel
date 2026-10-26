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
    private readonly bool hasAlpha;
    private readonly JxlPatchBlending colorBlending;
    private readonly JxlPatchBlending[] ecBlending;
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
        this.hasAlpha = JxlBlending.HasAlpha(m.ExtraChannelInfo);

        List<JxlBlendingInfo> ecInfo = frameHeader.ExtraChannelBlendingInfo;
        this.bg = references[frameHeader.BlendingInfo.Source];
        this.ecBg = new JxlReferenceFrame[ecInfo.Count];
        for (int i = 0; i < ecInfo.Count; i++)
        {
            this.ecBg[i] = references[ecInfo[i].Source];
        }

        this.colorBlending = MakeBlending(frameHeader.BlendingInfo);
        this.ecBlending = new JxlPatchBlending[ecInfo.Count];
        for (int i = 0; i < ecInfo.Count; i++)
        {
            this.ecBlending[i] = MakeBlending(ecInfo[i]);
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

        // An XYB image that renders to linear sRGB, rather than its own encoding, is not blended.
        if (m.XybEncoded && !JxlOutputEncoding.IsOriginalOutput(m))
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Blending in unsupported color space");
        }

        // As libjxl, the zeros are allocated only when a slot the frame blends onto is empty.
        if (this.bg == null || Array.IndexOf(this.ecBg, null) >= 0)
        {
            this.zeros = allocator.Allocate<float>(this.ImageXSize, AllocationOptions.Clean);
        }

        Memory<float> zeros = this.zeros?.Memory ?? Memory<float>.Empty;
        try
        {
            this.tmp = allocator.Allocate2D<float>(Math.Max(frameXSize, 1), numChannels);
        }
        catch
        {
            // The constructor throws, so no caller can dispose the zeros.
            this.zeros?.Dispose();
            throw;
        }

        Buffer2D<float>[] bgPlanes = new Buffer2D<float>[numChannels];
        for (int c = 0; c < this.numColorPlanes; c++)
        {
            bgPlanes[c] = this.bg?.Planes[c];
        }

        for (int i = 0; i < this.ecBg.Length; i++)
        {
            bgPlanes[this.numColorPlanes + i] = ExtraChannelPlane(this.ecBg[i], i, m.NumExtraChannels);
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
        JxlBlending.PerformBlending(this.bgRows, this.fgRows, this.fgRows, this.numColorPlanes, this.colorBlending, this.ecBlending, this.extraChannelInfo, this.hasAlpha, this.tmp, (int)size);
    }

    /// <summary>Fills row <paramref name="ypos"/> of the image-sized planes from the background, or with zeros.</summary>
    /// <param name="output">The image-sized planes.</param>
    /// <param name="ypos">The row.</param>
    public void ProcessPaddingRow(Buffer2D<float>[] output, int ypos)
    {
        for (int c = 0; c < this.numColorPlanes; c++)
        {
            CopyBackgroundRow(this.bg?.Planes[c], output[c], this.ImageXSize, ypos);
        }

        for (int ec = 0; ec < this.extraChannelInfo.Count; ec++)
        {
            CopyBackgroundRow(ExtraChannelPlane(this.ecBg[ec], ec, this.extraChannelInfo.Count), output[this.numColorPlanes + ec], this.ImageXSize, ypos);
        }
    }

    public void Dispose()
    {
        this.zeros?.Dispose();
        this.tmp.Dispose();
    }

    // make_blending: a frame blend mode as the patch blend mode that does the same. Frames never use
    // None or the Below modes.
    private static JxlPatchBlending MakeBlending(JxlBlendingInfo info)
    {
        JxlPatchBlendMode mode = info.Mode switch
        {
            JxlBlendMode.Add => JxlPatchBlendMode.Add,
            JxlBlendMode.Mul => JxlPatchBlendMode.Mul,
            JxlBlendMode.Blend => JxlPatchBlendMode.BlendAbove,
            JxlBlendMode.AlphaWeightedAdd => JxlPatchBlendMode.AlphaWeightedAddAbove,
            _ => JxlPatchBlendMode.Replace,
        };

        return new JxlPatchBlending(mode, info.AlphaChannel, info.Clamp);
    }

    // Copies row ypos of a background plane, or clears it when the slot is empty.
    private static void CopyBackgroundRow(Buffer2D<float> background, Buffer2D<float> output, int xsize, int ypos)
    {
        Span<float> row = output.DangerousGetRowSpan(ypos).Slice(0, xsize);
        if (background == null)
        {
            row.Clear();
        }
        else
        {
            background.DangerousGetRowSpan(ypos).Slice(0, xsize).CopyTo(row);
        }
    }

    // Extra channel i of a slot, or null for an empty slot, which reads as zeros. libjxl's slot keeps its
    // extra channels apart from its three colour channels; here they follow the slot's colour planes,
    // which number one for a grey image that is not XYB saved after blending, but three when the frame was
    // saved before its colour transform. So the extra channels are counted from the end, and an extra
    // channel may blend from such a slot, as in libjxl.
    private static Buffer2D<float> ExtraChannelPlane(JxlReferenceFrame frame, int i, int numExtraChannels)
        => frame?.Planes[frame.Planes.Length - numExtraChannels + i];

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
