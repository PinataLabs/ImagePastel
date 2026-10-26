// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/modular_image.h, lib/jxl/modular/modular_image.cc and lib/jxl/modular/options.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.Modular;

/// <summary>
/// One channel of a Modular image: a plane of 32-bit samples, possibly downsampled.
/// </summary>
/// <remarks>
/// The samples live in a <see cref="Buffer2D{T}"/> from the configuration's allocator. libjxl's planes
/// are uninitialized; these are zeroed. An empty channel (zero width or height) has no buffer.
/// </remarks>
internal sealed class JxlModularChannel : IDisposable
{
    private readonly MemoryAllocator allocator;
    private Buffer2D<int> plane;

    private JxlModularChannel(MemoryAllocator allocator, int w, int h, int hshift, int vshift)
    {
        this.allocator = allocator;
        this.W = w;
        this.H = h;
        this.HShift = hshift;
        this.VShift = vshift;
        this.plane = Allocate(allocator, w, h);
    }

    /// <summary>Gets or sets the width; w is about image.w >> hshift.</summary>
    public int W { get; set; }

    /// <summary>Gets or sets the height; h is about image.h >> vshift.</summary>
    public int H { get; set; }

    /// <summary>Gets or sets the horizontal downsampling shift. -1 marks a meta channel such as a palette.</summary>
    public int HShift { get; set; }

    /// <summary>Gets or sets the vertical downsampling shift.</summary>
    public int VShift { get; set; }

    /// <summary>Gets or sets the colour or extra channel this channel came from, or -1.</summary>
    public int Component { get; set; } = -1;

    /// <summary>Gets the allocator of the plane.</summary>
    public MemoryAllocator Allocator => this.allocator;

    /// <summary>Creates a channel.</summary>
    /// <param name="allocator">The allocator.</param>
    /// <param name="w">The width.</param>
    /// <param name="h">The height.</param>
    /// <param name="hshift">The horizontal shift.</param>
    /// <param name="vshift">The vertical shift.</param>
    /// <returns>The channel.</returns>
    public static JxlModularChannel Create(MemoryAllocator allocator, int w, int h, int hshift = 0, int vshift = 0)
        => new(allocator, w, h, hshift, vshift);

    /// <summary>Returns row <paramref name="y"/>, <see cref="W"/> samples long.</summary>
    /// <param name="y">The row index.</param>
    /// <returns>The samples of the row.</returns>
    public Span<int> Row(int y) => this.plane.DangerousGetRowSpan(y).Slice(0, this.W);

    /// <summary>Reallocates the plane if its size no longer matches <see cref="W"/> and <see cref="H"/>.</summary>
    public void Shrink()
    {
        int planeW = this.plane?.Width ?? 0;
        int planeH = this.plane?.Height ?? 0;
        if (planeW == this.W && planeH == this.H)
        {
            return;
        }

        this.plane?.Dispose();
        this.plane = Allocate(this.allocator, this.W, this.H);
    }

    /// <summary>Sets a new size and reallocates the plane if needed.</summary>
    /// <param name="nw">The new width.</param>
    /// <param name="nh">The new height.</param>
    public void Shrink(int nw, int nh)
    {
        this.W = nw;
        this.H = nh;
        this.Shrink();
    }

    /// <summary>Sets every sample to zero.</summary>
    public void ZeroFill()
    {
        for (int y = 0; y < this.H; y++)
        {
            this.Row(y).Clear();
        }
    }

    /// <summary>Swaps the planes of two channels of the same plane size, as libjxl's <c>Plane::Swap</c>.</summary>
    /// <param name="other">The other channel.</param>
    public void SwapPlane(JxlModularChannel other) => (this.plane, other.plane) = (other.plane, this.plane);

    /// <summary>Releases the plane, keeping the channel's size and shifts.</summary>
    public void DropPlane()
    {
        this.plane?.Dispose();
        this.plane = null;
    }

    public void Dispose() => this.DropPlane();

    private static Buffer2D<int> Allocate(MemoryAllocator allocator, int w, int h)
        => w == 0 || h == 0 ? null : allocator.Allocate2D<int>(w, h, AllocationOptions.Clean);
}
