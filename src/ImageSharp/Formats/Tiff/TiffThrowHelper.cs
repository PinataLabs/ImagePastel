// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Formats.Tiff;

/// <summary>
/// Cold path optimizations for throwing tiff format based exceptions.
/// </summary>
internal static class TiffThrowHelper
{
    /// <summary>
    /// Cold path optimization for throwing <see cref="ImageFormatException"/>-s
    /// </summary>
    /// <param name="errorMessage">The error message for the exception.</param>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Exception ThrowImageFormatException(string errorMessage) => throw new ImageFormatException(errorMessage);

    /// <summary>
    /// Cold path optimization for throwing <see cref="InvalidImageContentException"/>-s
    /// </summary>
    /// <param name="errorMessage">The error message for the exception.</param>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Exception ThrowInvalidImageContentException(string errorMessage) => throw new InvalidImageContentException(errorMessage);

    [DoesNotReturn]
    [MethodImpl(InliningOptions.ColdPath)]
    public static Exception NotSupportedDecompressor(string compressionType) => throw new NotSupportedException($"Not supported decoder compression method: {compressionType}");

    [DoesNotReturn]
    [MethodImpl(InliningOptions.ColdPath)]
    public static Exception NotSupportedCompressor(string compressionType) => throw new NotSupportedException($"Not supported encoder compression method: {compressionType}");

    [DoesNotReturn]
    [MethodImpl(InliningOptions.ColdPath)]
    public static Exception InvalidColorType(string colorType) => throw new NotSupportedException($"Invalid color type: {colorType}");

    [DoesNotReturn]
    [MethodImpl(InliningOptions.ColdPath)]
    public static Exception ThrowInvalidHeader() => throw new ImageFormatException("Invalid TIFF file header.");

    [DoesNotReturn]
    [MethodImpl(InliningOptions.ColdPath)]
    public static void ThrowNotSupported(string message) => throw new NotSupportedException(message);

    [DoesNotReturn]
    [MethodImpl(InliningOptions.ColdPath)]
    public static void ThrowArgumentException(string message) => throw new ArgumentException(message);
}
