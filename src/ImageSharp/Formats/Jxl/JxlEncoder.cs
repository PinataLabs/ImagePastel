// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Image encoder for writing an image to a stream as a JPEG XL codestream. Only lossless encoding
/// (<see cref="Distance"/> 0) is supported so far, at every <see cref="JxlEffort"/>; a lossy distance
/// throws <see cref="System.NotSupportedException"/>.
/// </summary>
internal sealed class JxlEncoder : IImageEncoder, IJxlEncoderOptions
{
    /// <inheritdoc/>
    public float Distance { get; set; } = 1.0f;

    /// <inheritdoc/>
    public JxlEffort Effort { get; set; } = JxlEffort.Default;

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
