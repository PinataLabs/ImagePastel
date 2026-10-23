// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Image encoder for writing an image to a stream as a JPEG XL codestream. Lossless encoding
/// (<see cref="Distance"/> 0) and lossy encoding are supported at every <see cref="JxlEffort"/>, for any
/// pixel type, with or without alpha. The lossy colour space is sRGB, or the one an ICC profile describes
/// when the encoder recognises the profile as a JPEG XL colour encoding; the file then carries that
/// encoding instead of the profile. <see cref="Frame.JxlFrameEncoder"/> lists what each effort does,
/// <see cref="JxlEffort.Thorough"/> among them. Lossy encoding with a profile that is not recognised throws
/// <see cref="System.NotSupportedException"/>.
/// </summary>
internal sealed class JxlEncoder : IImageEncoder, IJxlEncoderOptions
{
    /// <inheritdoc/>
    public float Distance { get; set; } = 1.0f;

    /// <inheritdoc/>
    public JxlEffort Effort { get; set; } = JxlEffort.Default;

    /// <summary>
    /// Gets or sets the encoder's hooks for tests, or <see langword="null"/> for none. For tests only; see
    /// <see cref="JxlEncoderTestHooks"/>.
    /// </summary>
    internal JxlEncoderTestHooks TestHooks { get; set; }

    /// <inheritdoc/>
    JxlEncoderTestHooks IJxlEncoderOptions.TestHooks => this.TestHooks;

    /// <inheritdoc/>
    public void Encode<TPixel>(Image<TPixel> image, Stream stream)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        JxlEncoderCore encoder = new(this, image.GetMemoryAllocator());
        encoder.Encode(image, stream);
    }

    /// <inheritdoc/>
    public Task EncodeAsync<TPixel>(Image<TPixel> image, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        JxlEncoderCore encoder = new(this, image.GetMemoryAllocator());
        return encoder.EncodeAsync(image, stream, cancellationToken);
    }
}
