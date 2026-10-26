// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using PinataLabs.ImagePastel.Formats.Tiff.Constants;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Tiff.Compression.Compressors;

internal sealed class NoCompressor : TiffBaseCompressor
{
    public NoCompressor(Stream output, MemoryAllocator memoryAllocator, int width, int bitsPerPixel)
        : base(output, memoryAllocator, width, bitsPerPixel)
    {
    }

    /// <inheritdoc/>
    public override TiffCompression Method => TiffCompression.None;

    /// <inheritdoc/>
    public override void Initialize(int rowsPerStrip)
    {
    }

    /// <inheritdoc/>
    public override void CompressStrip(Span<byte> rows, int height) => this.Output.Write(rows);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
    }
}
