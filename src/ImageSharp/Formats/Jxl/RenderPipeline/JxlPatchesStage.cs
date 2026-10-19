// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// PatchDictionaryStage in lib/jxl/render_pipeline/stage_patches.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// Adds the patches of a frame to its planes, in place, one row at a time.
/// </summary>
internal sealed class JxlPatchesStage : IDisposable
{
    private readonly JxlPatchDictionary patches;
    private readonly List<JxlExtraChannelInfo> extraChannelInfo;
    private readonly int numColorPlanes;
    private readonly bool hasAlpha;
    private readonly Buffer2D<float> tmp;
    private readonly JxlBlendingRows rows;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlPatchesStage"/> class, before the frame's
    /// sections, and so the dictionary, are decoded, as libjxl builds its pipeline.
    /// </summary>
    /// <param name="allocator">The allocator.</param>
    /// <param name="patches">The patch dictionary of the frame.</param>
    /// <param name="metadata">The image metadata.</param>
    /// <param name="frameXSize">The width of the frame.</param>
    public JxlPatchesStage(MemoryAllocator allocator, JxlPatchDictionary patches, JxlImageMetadata metadata, int frameXSize)
    {
        this.patches = patches;
        this.extraChannelInfo = metadata.ExtraChannelInfo;
        this.numColorPlanes = metadata.NumColorPlanes;
        this.hasAlpha = JxlBlending.HasAlpha(metadata.ExtraChannelInfo);
        int numChannels = metadata.NumColorPlanes + metadata.NumExtraChannels;
        this.tmp = allocator.Allocate2D<float>(Math.Max(frameXSize, 1), numChannels);
        this.rows = new JxlBlendingRows(new Buffer2D<float>[numChannels], Memory<float>.Empty);
    }

    /// <summary>Adds the patches to row <paramref name="ypos"/> of the frame.</summary>
    /// <param name="planes">The frame planes, one per channel.</param>
    /// <param name="xsize">The width of the frame.</param>
    /// <param name="ypos">The row.</param>
    public void ProcessRow(Buffer2D<float>[] planes, int xsize, int ypos)
    {
        for (int c = 0; c < planes.Length; c++)
        {
            this.rows.SetPlane(c, planes[c]);
        }

        this.patches.AddOneRow(this.rows, ypos, 0, xsize, this.numColorPlanes, this.extraChannelInfo, this.hasAlpha, this.tmp);
    }

    public void Dispose() => this.tmp.Dispose();
}
