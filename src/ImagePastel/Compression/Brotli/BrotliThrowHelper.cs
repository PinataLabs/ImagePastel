// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace PinataLabs.ImagePastel.Compression.Brotli;

/// <summary>
/// Throws for a Brotli stream that cannot be decoded. The C decoder returns a
/// <c>BrotliDecoderErrorCode</c> from every call; the port throws instead.
/// </summary>
internal static class BrotliThrowHelper
{
    /// <summary>Throws for a corrupt stream.</summary>
    /// <param name="reason">What is wrong with the stream.</param>
    [DoesNotReturn]
    [MethodImpl(InliningOptions.ColdPath)]
    public static void ThrowCorrupt(string reason) => throw new InvalidImageContentException($"Invalid Brotli stream: {reason}.");

    /// <summary>Throws for a stream that ends before its last meta-block does.</summary>
    [DoesNotReturn]
    [MethodImpl(InliningOptions.ColdPath)]
    public static void ThrowTruncated() => throw new InvalidImageContentException("Invalid Brotli stream: the stream is truncated.");
}
