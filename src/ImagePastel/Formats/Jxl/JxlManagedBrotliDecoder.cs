// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using PinataLabs.ImagePastel.Compression.Brotli;

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>
/// Reads <c>brob</c> boxes with <see cref="ManagedBrotliDecoder"/>: on netstandard2.1, which has no
/// <c>System.IO.Compression.BrotliDecoder</c>, and in the tests on every target.
/// </summary>
internal struct JxlManagedBrotliDecoder : IJxlBrotliDecoder
{
    private ManagedBrotliDecoder decoder;

    /// <inheritdoc/>
    public OperationStatus Decompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten)
    {
        this.decoder ??= new ManagedBrotliDecoder();
        try
        {
            return this.decoder.Decompress(source, destination, out bytesConsumed, out bytesWritten);
        }
        catch (InvalidImageContentException)
        {
            // The container drops a box that cannot be read, so it wants a status here, as BrotliDecoder gives.
            bytesConsumed = 0;
            bytesWritten = 0;
            return OperationStatus.InvalidData;
        }
    }

    /// <inheritdoc/>
    public readonly void Dispose()
    {
    }
}
