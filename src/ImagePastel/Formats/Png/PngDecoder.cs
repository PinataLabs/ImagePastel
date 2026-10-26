// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using PinataLabs.ImagePastel.ColorManagement;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats.Png;

/// <summary>
/// Decoder for generating an image out of a png encoded stream.
/// </summary>
public sealed class PngDecoder : IImageDecoder, IPngDecoderOptions, IImageInfoDetector
{
    /// <inheritdoc/>
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
        PngDecoderCore decoder = new(configuration, this);
        Image<TPixel> image = decoder.Decode<TPixel>(configuration, stream, cancellationToken);
        return DecoderColorProfileConversion.Apply(image, this, cancellationToken);
    }

    /// <inheritdoc />
    public Image Decode(Configuration configuration, Stream stream, CancellationToken cancellationToken)
    {
        PngDecoderCore decoder = new(configuration, true);
        IImageInfo info = decoder.Identify(configuration, stream, cancellationToken);
        stream.Position = 0;

        PngMetadata meta = info.Metadata.GetPngMetadata();
        PngColorType color = meta.ColorType.GetValueOrDefault();
        PngBitDepth bits = meta.BitDepth.GetValueOrDefault();
        switch (color)
        {
            case PngColorType.Grayscale:
                if (bits == PngBitDepth.Bit16)
                {
                    return !meta.HasTransparency
                        ? this.Decode<L16>(configuration, stream, cancellationToken)
                        : this.Decode<La32>(configuration, stream, cancellationToken);
                }

                return !meta.HasTransparency
                    ? this.Decode<L8>(configuration, stream, cancellationToken)
                    : this.Decode<La16>(configuration, stream, cancellationToken);

            case PngColorType.Rgb:
                if (bits == PngBitDepth.Bit16)
                {
                    return !meta.HasTransparency
                        ? this.Decode<Rgb48>(configuration, stream, cancellationToken)
                        : this.Decode<Rgba64>(configuration, stream, cancellationToken);
                }

                return !meta.HasTransparency
                    ? this.Decode<Rgb24>(configuration, stream, cancellationToken)
                    : this.Decode<Rgba32>(configuration, stream, cancellationToken);

            case PngColorType.Palette:
                return this.Decode<Rgba32>(configuration, stream, cancellationToken);

            case PngColorType.GrayscaleWithAlpha:
                return (bits == PngBitDepth.Bit16)
                ? this.Decode<La32>(configuration, stream, cancellationToken)
                : this.Decode<La16>(configuration, stream, cancellationToken);

            case PngColorType.RgbWithAlpha:
                return (bits == PngBitDepth.Bit16)
                ? this.Decode<Rgba64>(configuration, stream, cancellationToken)
                : this.Decode<Rgba32>(configuration, stream, cancellationToken);

            default:
                return this.Decode<Rgba32>(configuration, stream, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public IImageInfo Identify(Configuration configuration, Stream stream, CancellationToken cancellationToken)
    {
        PngDecoderCore decoder = new(configuration, this);
        return decoder.Identify(configuration, stream, cancellationToken);
    }
}
