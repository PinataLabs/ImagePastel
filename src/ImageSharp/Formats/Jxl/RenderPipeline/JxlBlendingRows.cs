// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Stands in for the row pointer arrays (const float* const* bg, fg) that libjxl
// (https://github.com/libjxl/libjxl, commit ef67fde2) passes to PerformBlending in lib/jxl/blending.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// One row of each channel, from column <c>x</c> for <c>xsize</c> pixels. A channel with no plane reads
/// as zeros, as libjxl points the rows of an empty reference slot at a zero buffer.
/// </summary>
internal sealed class JxlBlendingRows
{
    private readonly Buffer2D<float>[] planes;
    private readonly Memory<float> zeros;
    private int x;
    private int y;
    private int xsize;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlBlendingRows"/> class.
    /// </summary>
    /// <param name="planes">The plane of each channel, or <see langword="null"/> for zeros.</param>
    /// <param name="zeros">Zeros, at least as long as any row; empty when every channel has a plane.</param>
    public JxlBlendingRows(Buffer2D<float>[] planes, Memory<float> zeros)
    {
        this.planes = planes;
        this.zeros = zeros;
    }

    /// <summary>Returns the row of channel <paramref name="c"/>.</summary>
    /// <param name="c">The channel.</param>
    public Span<float> this[int c]
        => this.planes[c] == null ? this.zeros.Span.Slice(0, this.xsize) : this.planes[c].DangerousGetRowSpan(this.y).Slice(this.x, this.xsize);

    /// <summary>Sets the plane of channel <paramref name="c"/>.</summary>
    /// <param name="c">The channel.</param>
    /// <param name="plane">The plane, or <see langword="null"/> for zeros.</param>
    public void SetPlane(int c, Buffer2D<float> plane) => this.planes[c] = plane;

    /// <summary>Points the rows at row <paramref name="y"/>, columns <paramref name="x"/> to <paramref name="x"/> + <paramref name="xsize"/>.</summary>
    /// <param name="x">The first column.</param>
    /// <param name="y">The row.</param>
    /// <param name="xsize">The number of pixels.</param>
    public void PointAt(int x, int y, int xsize)
    {
        this.x = x;
        this.y = y;
        this.xsize = xsize;
    }
}
