// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace PinataLabs.ImagePastel.Compression.Brotli;

internal static class BrotliThrowHelper
{
    [DoesNotReturn]
    [MethodImpl(InliningOptions.ColdPath)]
    public static void ThrowCorrupt(string reason) => throw new InvalidImageContentException($"Invalid Brotli stream: {reason}.");

    [DoesNotReturn]
    [MethodImpl(InliningOptions.ColdPath)]
    public static void ThrowTruncated() => throw new InvalidImageContentException("Invalid Brotli stream: the stream is truncated.");
}
