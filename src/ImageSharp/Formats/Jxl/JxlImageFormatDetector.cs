// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Detects JPEG XL codestreams by their <c>FF 0A</c> signature. The container comes with a later slice.
/// </summary>
internal sealed class JxlImageFormatDetector : IImageFormatDetector
{
    /// <inheritdoc />
    public int HeaderSize => 2;

    /// <inheritdoc />
    public IImageFormat DetectFormat(ReadOnlySpan<byte> header)
        => header.Length >= this.HeaderSize && header.Slice(0, 2).SequenceEqual(JxlConstants.CodestreamSignature) ? JxlFormat.Instance : null;
}
