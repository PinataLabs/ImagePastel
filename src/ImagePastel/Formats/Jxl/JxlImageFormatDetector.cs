// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Detects JPEG XL images: a bare codestream by its <c>FF 0A</c> signature, and the container by its
/// 12-byte signature box.
/// </summary>
public sealed class JxlImageFormatDetector : IImageFormatDetector
{
    /// <inheritdoc />
    public int HeaderSize => JxlConstants.ContainerSignature.Length;

    /// <inheritdoc />
    public IImageFormat DetectFormat(ReadOnlySpan<byte> header)
        => header.StartsWith(JxlConstants.CodestreamSignature) || header.StartsWith(JxlConstants.ContainerSignature) ? JxlFormat.Instance : null;
}
