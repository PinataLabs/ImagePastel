// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/frame_header.h and lib/jxl/frame_header.cc (YCbCrChromaSubsampling, BlendingInfo,
// AnimationFrame, Passes, FrameHeader), and lib/jxl/frame_dimensions.h (FrameDimensions).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <summary>The dimensions of a frame and its groups.</summary>
internal readonly struct JxlFrameDimensions
{
    /// <summary>The size of a block.</summary>
    public const int BlockDim = 8;

    /// <summary>The default group size.</summary>
    public const int GroupDim = 256;

    public JxlFrameDimensions(long xsizePx, long ysizePx, int groupSizeShift, int maxHShift, int maxVShift, bool modularMode, int upsampling)
    {
        this.GroupDimension = (GroupDim >> 1) << groupSizeShift;
        this.DcGroupDim = this.GroupDimension * BlockDim;
        this.XSizeUpsampled = xsizePx;
        this.YSizeUpsampled = ysizePx;
        this.XSize = JxlBits.DivCeil(xsizePx, upsampling);
        this.YSize = JxlBits.DivCeil(ysizePx, upsampling);
        this.XSizeBlocks = JxlBits.DivCeil(this.XSize, BlockDim << maxHShift) << maxHShift;
        this.YSizeBlocks = JxlBits.DivCeil(this.YSize, BlockDim << maxVShift) << maxVShift;
        long xsizePadded = this.XSizeBlocks * BlockDim;
        long ysizePadded = this.YSizeBlocks * BlockDim;
        if (modularMode)
        {
            // Modular mode doesn't have any padding.
            xsizePadded = this.XSize;
            ysizePadded = this.YSize;
        }

        this.XSizePadded = xsizePadded;
        this.YSizePadded = ysizePadded;
        this.XSizeGroups = JxlBits.DivCeil(this.XSize, this.GroupDimension);
        this.YSizeGroups = JxlBits.DivCeil(this.YSize, this.GroupDimension);
        this.XSizeDcGroups = JxlBits.DivCeil(this.XSizeBlocks, this.GroupDimension);
        this.YSizeDcGroups = JxlBits.DivCeil(this.YSizeBlocks, this.GroupDimension);
        this.NumGroups = this.XSizeGroups * this.YSizeGroups;
        this.NumDcGroups = this.XSizeDcGroups * this.YSizeDcGroups;
    }

    // Image size without any upsampling, i.e. original_size / upsampling.
    public long XSize { get; }

    public long YSize { get; }

    // Original image size.
    public long XSizeUpsampled { get; }

    public long YSizeUpsampled { get; }

    // Image size after upsampling the padded image.
    public long XSizePadded { get; }

    public long YSizePadded { get; }

    // Size of the DCT blocks of the padded image.
    public long XSizeBlocks { get; }

    public long YSizeBlocks { get; }

    // Number of groups.
    public long XSizeGroups { get; }

    public long YSizeGroups { get; }

    // Number of DC groups.
    public long XSizeDcGroups { get; }

    public long YSizeDcGroups { get; }

    public long NumGroups { get; }

    public long NumDcGroups { get; }

    /// <summary>Gets the size of a group, from the frame's group size shift.</summary>
    public int GroupDimension { get; }

    /// <summary>Gets the size of a DC group.</summary>
    public int DcGroupDim { get; }

    /// <summary>Returns the rectangle of group <paramref name="groupIndex"/>, clipped to the frame.</summary>
    /// <param name="groupIndex">The group.</param>
    /// <returns>The rectangle.</returns>
    public JxlRect GroupRect(long groupIndex)
    {
        long gx = groupIndex % this.XSizeGroups;
        long gy = groupIndex / this.XSizeGroups;
        return new JxlRect(gx * this.GroupDimension, gy * this.GroupDimension, this.GroupDimension, this.GroupDimension, this.XSize, this.YSize);
    }
}
