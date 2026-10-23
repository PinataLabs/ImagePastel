// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Image encoder for writing an image to a stream as a JPEG XL codestream. Lossless encoding
/// (<see cref="Distance"/> 0) is supported at every <see cref="JxlEffort"/>. Lossy encoding is supported
/// at <see cref="JxlEffort.Fastest"/> and <see cref="JxlEffort.Default"/>, for any pixel type, with or
/// without alpha. Its colour space is sRGB, or the one an ICC profile describes when the encoder
/// recognises the profile as a JPEG XL colour encoding; the file then carries that encoding instead of the
/// profile. <see cref="Frame.JxlFrameEncoder"/> lists what each effort does. Lossy encoding at
/// <see cref="JxlEffort.Thorough"/>, or with a profile that is not recognised, throws
/// <see cref="System.NotSupportedException"/>.
/// </summary>
internal sealed class JxlEncoder : IImageEncoder, IJxlEncoderOptions
{
    /// <inheritdoc/>
    public float Distance { get; set; } = 1.0f;

    /// <inheritdoc/>
    public JxlEffort Effort { get; set; } = JxlEffort.Default;

    /// <summary>
    /// Gets or sets a value indicating whether a lossy frame keeps every coefficient order natural. For
    /// tests only; see <see cref="IJxlEncoderOptions.NaturalCoeffOrders"/>.
    /// </summary>
    internal bool NaturalCoeffOrders { get; set; }

    /// <inheritdoc/>
    bool IJxlEncoderOptions.NaturalCoeffOrders => this.NaturalCoeffOrders;

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
