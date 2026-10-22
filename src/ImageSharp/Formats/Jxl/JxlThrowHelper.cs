// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Throws the exceptions the JPEG XL decoder and encoder may raise. libjxl returns a <c>Status</c> from every call
/// (<c>JXL_FAILURE</c>, <c>JXL_RETURN_IF_ERROR</c>); the port throws instead.
/// </summary>
internal static class JxlThrowHelper
{
    /// <summary>
    /// Throws for a broken or truncated codestream, where libjxl returns <c>JXL_FAILURE</c>.
    /// </summary>
    /// <param name="errorMessage">The error message for the exception.</param>
    [DoesNotReturn]
    [MethodImpl(InliningOptions.ColdPath)]
    public static void ThrowInvalidImageContentException(string errorMessage) => throw new InvalidImageContentException(errorMessage);

    /// <summary>
    /// Throws when the encoder is given a value that the codestream cannot hold, where libjxl's
    /// <c>CanEncode</c> returns <c>JXL_FAILURE</c>. It is thrown before anything is written.
    /// </summary>
    /// <param name="errorMessage">The error message for the exception.</param>
    /// <param name="innerException">The exception that caused it, if any.</param>
    [DoesNotReturn]
    [MethodImpl(InliningOptions.ColdPath)]
    public static void ThrowImageFormatException(string errorMessage, Exception innerException = null)
        => throw new ImageFormatException(errorMessage, innerException);

    /// <summary>
    /// Throws for a valid codestream feature that the port does not handle: a feature of a later encoder
    /// slice, or a commented departure of the decoder from libjxl.
    /// </summary>
    /// <param name="feature">The feature that is not handled.</param>
    [DoesNotReturn]
    [MethodImpl(InliningOptions.ColdPath)]
    public static void ThrowNotSupportedException(string feature) => throw new NotSupportedException($"JPEG XL feature not supported yet: {feature}.");
}
