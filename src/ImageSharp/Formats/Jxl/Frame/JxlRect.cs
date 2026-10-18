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

/// <summary>A rectangle; the constructor with an end clips the size to it, as libjxl's <c>Rect</c>.</summary>
internal readonly struct JxlRect
{
    public JxlRect(long x0, long y0, long xsize, long ysize)
    {
        this.X0 = x0;
        this.Y0 = y0;
        this.XSize = xsize;
        this.YSize = ysize;
    }

    public JxlRect(long x0, long y0, long xsizeMax, long ysizeMax, long xend, long yend)
        : this(x0, y0, ClampedSize(x0, xsizeMax, xend), ClampedSize(y0, ysizeMax, yend))
    {
    }

    public long X0 { get; }

    public long Y0 { get; }

    public long XSize { get; }

    public long YSize { get; }

    private static long ClampedSize(long begin, long sizeMax, long end)
        => begin + sizeMax <= end ? sizeMax : (end > begin ? end - begin : 0);
}
