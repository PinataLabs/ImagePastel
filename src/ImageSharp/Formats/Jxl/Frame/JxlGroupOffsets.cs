// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/toc.cc (the offsets and sizes of ReadGroupOffsets).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <summary>Where each section of a frame is, from <see cref="JxlToc.ReadGroupOffsets"/>.</summary>
internal sealed class JxlGroupOffsets
{
    /// <summary>Initializes a new instance of the <see cref="JxlGroupOffsets"/> class.</summary>
    /// <param name="offsets">The first byte of each section after the TOC, in the order the sections are decoded in.</param>
    /// <param name="sizes">The size of each section, in bytes, in the same order.</param>
    /// <param name="totalSize">The size of all the sections, in bytes.</param>
    /// <param name="permutation">The TOC's permutation, or null if it is not permuted.</param>
    public JxlGroupOffsets(long[] offsets, uint[] sizes, long totalSize, uint[] permutation)
    {
        this.Offsets = offsets;
        this.Sizes = sizes;
        this.TotalSize = totalSize;
        this.Permutation = permutation;
    }

    /// <summary>Gets the first byte of each section after the TOC, in the order the sections are decoded in.</summary>
    public long[] Offsets { get; }

    /// <summary>Gets the size of each section, in bytes, in the same order.</summary>
    public uint[] Sizes { get; }

    /// <summary>Gets the size of all the sections, in bytes.</summary>
    public long TotalSize { get; }

    /// <summary>Gets the TOC's permutation, or null if it is not permuted.</summary>
    public uint[] Permutation { get; }
}
