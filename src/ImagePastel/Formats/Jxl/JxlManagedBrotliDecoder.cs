// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using PinataLabs.ImagePastel.Compression.Brotli;

namespace PinataLabs.ImagePastel.Formats.Jxl;

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
