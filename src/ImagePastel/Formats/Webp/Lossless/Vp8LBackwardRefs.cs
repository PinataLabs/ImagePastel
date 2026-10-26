// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.Memory.Internals;

namespace PinataLabs.ImagePastel.Formats.Webp.Lossless;

internal class Vp8LBackwardRefs : IDisposable
{
    private readonly IMemoryOwner<PixOrCopy> refs;

    private int count;

    /// <summary>
    /// Initializes a new instance of the <see cref="Vp8LBackwardRefs"/> class.
    /// </summary>
    /// <param name="memoryAllocator">The memory allocator.</param>
    /// <param name="pixels">The number of pixels. Each reference covers at least one pixel.</param>
    public Vp8LBackwardRefs(MemoryAllocator memoryAllocator, int pixels)
    {
        // A very large image can need more than the allocator gives out in one buffer.
        // Keep its references in a managed array then, as they were before.
        long bytes = (long)pixels * Unsafe.SizeOf<PixOrCopy>();
        this.refs = bytes <= memoryAllocator.SingleBufferAllocationLimitBytes
            ? memoryAllocator.Allocate<PixOrCopy>(pixels)
            : new BasicArrayBuffer<PixOrCopy>(new PixOrCopy[pixels]);
    }

    /// <summary>
    /// Gets or sets the common block-size.
    /// </summary>
    public int BlockSize { get; set; }

    /// <summary>
    /// Gets the backward references.
    /// </summary>
    public Span<PixOrCopy> Refs => this.refs.GetSpan().Slice(0, this.count);

    public void Add(PixOrCopy pixOrCopy) => this.refs.GetSpan()[this.count++] = pixOrCopy;

    public void Clear() => this.count = 0;

    /// <inheritdoc/>
    public void Dispose() => this.refs.Dispose();
}
