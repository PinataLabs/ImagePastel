// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>
/// The Brotli decoder that <see cref="JxlContainer"/> reads <c>brob</c> boxes with. The decoders are
/// structs and the container takes one as a type argument, so a trimmed application keeps only the
/// decoder that its target uses.
/// </summary>
internal interface IJxlBrotliDecoder : IDisposable
{
    /// <summary>
    /// Decodes the next output of a Brotli stream into <paramref name="destination"/>, as
    /// <c>System.IO.Compression.BrotliDecoder.Decompress</c> does.
    /// </summary>
    /// <param name="source">
    /// The rest of the stream: all of it on the first call, and then what is left after the bytes that
    /// the calls before consumed.
    /// </param>
    /// <param name="destination">The buffer for the next output.</param>
    /// <param name="bytesConsumed">The number of bytes of <paramref name="source"/> that are used.</param>
    /// <param name="bytesWritten">The number of bytes written to <paramref name="destination"/>.</param>
    /// <returns>
    /// <see cref="OperationStatus.Done"/> when the stream has ended and all its output is written, and
    /// <see cref="OperationStatus.DestinationTooSmall"/> when there is more output. Any other status
    /// means that the stream is corrupt or truncated.
    /// </returns>
    OperationStatus Decompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten);
}
