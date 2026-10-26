// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using PinataLabs.ImagePastel.ColorManagement;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats.Webp;

/// <summary>
/// Image decoder for generating an image out of a webp stream.
/// </summary>
public sealed class WebpDecoder : IImageDecoder, IWebpDecoderOptions, IImageInfoDetector
{
    /// <summary>
    /// Gets or sets a value indicating whether the metadata should be ignored when the image is being decoded.
    /// </summary>
    public bool IgnoreMetadata { get; set; }

    /// <summary>
    /// Gets or sets the conversion from the embedded ICC profile that runs after the pixels are
    /// decoded. Defaults to <see cref="ColorProfileConversion.None"/>. Nothing is converted when
    /// <see cref="IgnoreMetadata"/> is set, when the image has no embedded profile, or when the
    /// embedded profile is corrupt or not supported; the image then keeps its embedded profile.
    /// </summary>
    public ColorProfileConversion ColorProfileConversion { get; set; }

    /// <summary>
    /// Gets or sets the profile to convert to when <see cref="ColorProfileConversion"/> is
    /// <see cref="ColorProfileConversion.ToTargetProfile"/>. Decoding throws when it is missing,
    /// corrupt or not supported.
    /// </summary>
    public IccProfile ColorProfileConversionTarget { get; set; }

    /// <inheritdoc/>
    public Image<TPixel> Decode<TPixel>(Configuration configuration, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(stream, nameof(stream));

        var decoder = new WebpDecoderCore(configuration, this);
        Image<TPixel> image;

        try
        {
            image = decoder.Decode<TPixel>(configuration, stream, cancellationToken);
        }
        catch (InvalidMemoryOperationException ex)
        {
            Size dims = decoder.Dimensions;

            throw new InvalidImageContentException($"Cannot decode image. Failed to allocate buffers for possibly degenerate dimensions: {dims.Width}x{dims.Height}.", ex);
        }

        return DecoderColorProfileConversion.Apply(image, this, cancellationToken);
    }

    /// <inheritdoc />
    public Image Decode(Configuration configuration, Stream stream, CancellationToken cancellationToken)
        => this.Decode<Rgba32>(configuration, stream, cancellationToken);

    /// <inheritdoc/>
    public IImageInfo Identify(Configuration configuration, Stream stream, CancellationToken cancellationToken)
    {
        Guard.NotNull(stream, nameof(stream));

        return new WebpDecoderCore(configuration, this).Identify(configuration, stream, cancellationToken);
    }
}
