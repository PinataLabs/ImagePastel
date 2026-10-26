// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jpeg;

/// <summary>
/// Image decoder for generating an image out of a jpg stream.
/// </summary>
public sealed class JpegDecoder : IImageDecoder, IJpegDecoderOptions, IImageInfoDetector
{
    /// <inheritdoc/>
    public bool IgnoreMetadata { get; set; }

    /// <summary>
    /// Gets or sets the conversion from the embedded ICC profile that runs after the pixels are
    /// decoded. Defaults to <see cref="ColorProfileConversion.None"/>. Nothing is converted when
    /// <see cref="IgnoreMetadata"/> is set, when the image has no embedded profile, or when the
    /// embedded profile is corrupt or not supported; the image then keeps its embedded profile.
    /// Adobe CMYK and YCCK images with an embedded CMYK profile are converted from their CMYK samples
    /// through that profile instead of through the CMYK-to-RGB formula; without one, the formula stays.
    /// A CMYK profile is never the pixel profile, since the decoded pixels are RGB; it is exposed as
    /// <see cref="JpegMetadata.EmbeddedIccProfile"/>, with or without a conversion.
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

        using var decoder = new JpegDecoderCore(configuration, this);
        Image<TPixel> image = decoder.Decode<TPixel>(configuration, stream, cancellationToken);
        return DecoderColorProfileConversion.Apply(image, this, cancellationToken);
    }

    /// <inheritdoc />
    public Image Decode(Configuration configuration, Stream stream, CancellationToken cancellationToken)
        => this.Decode<Rgb24>(configuration, stream, cancellationToken);

    /// <inheritdoc/>
    public IImageInfo Identify(Configuration configuration, Stream stream, CancellationToken cancellationToken)
    {
        Guard.NotNull(stream, nameof(stream));

        using var decoder = new JpegDecoderCore(configuration, this);
        return decoder.Identify(configuration, stream, cancellationToken);
    }
}
