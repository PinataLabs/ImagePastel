// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>
/// Decompresses the Brotli stream of a JPEG XL <c>brob</c> box.
/// </summary>
internal interface IJxlBrotliDecoder : IDisposable
{
    /// <summary>
    /// Decodes as <c>System.IO.Compression.BrotliDecoder.Decompress</c> does.
    /// </summary>
    /// <param name="source">The rest of the stream, after the bytes that earlier calls consumed.</param>
    /// <param name="destination">The buffer that receives decoded bytes.</param>
    /// <param name="bytesConsumed">The number of bytes read from <paramref name="source"/>.</param>
    /// <param name="bytesWritten">The number of bytes written to <paramref name="destination"/>.</param>
    /// <returns><see cref="OperationStatus.Done"/> at the end of the stream, or why decoding stopped.</returns>
    OperationStatus Decompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten);
}
