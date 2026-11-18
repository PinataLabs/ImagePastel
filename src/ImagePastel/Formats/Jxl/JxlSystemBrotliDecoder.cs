// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

#if NETCOREAPP3_0_OR_GREATER
using System;
using System.Buffers;
using System.IO.Compression;

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>
/// Reads <c>brob</c> boxes with <see cref="BrotliDecoder"/>, on the targets that have it.
/// </summary>
internal struct JxlSystemBrotliDecoder : IJxlBrotliDecoder
{
    private BrotliDecoder decoder;

    /// <inheritdoc/>
    public OperationStatus Decompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten)
        => this.decoder.Decompress(source, destination, out bytesConsumed, out bytesWritten);

    /// <inheritdoc/>
    public void Dispose() => this.decoder.Dispose();
}
#endif
