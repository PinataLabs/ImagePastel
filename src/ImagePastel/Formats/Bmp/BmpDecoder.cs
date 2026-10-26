// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Bmp;

/// <summary>
/// Image decoder for generating an image out of a Windows bitmap stream.
/// </summary>
/// <remarks>
/// Does not support the following formats at the moment:
/// <list type="bullet">
///    <item>JPG</item>
///    <item>PNG</item>
///    <item>Some OS/2 specific subtypes like: Bitmap Array, Color Icon, Color Pointer, Icon, Pointer.</item>
/// </list>
/// Formats will be supported in a later releases. We advise always
/// to use only 24 Bit Windows bitmaps.
/// </remarks>
public sealed class BmpDecoder : IImageDecoder, IBmpDecoderOptions, IImageInfoDetector
{
    /// <summary>
    /// Gets or sets a value indicating how to deal with skipped pixels, which can occur during decoding run length encoded bitmaps.
    /// </summary>
    public RleSkippedPixelHandling RleSkippedPixelHandling { get; set; } = RleSkippedPixelHandling.Black;

    /// <summary>
    /// Gets or sets a value indicating whether the metadata should be ignored when the image is being decoded.
    /// When set, the embedded ICC profile of a V5 bitmap is not read.
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

        var decoder = new BmpDecoderCore(configuration, this);
        Image<TPixel> image = decoder.Decode<TPixel>(configuration, stream, cancellationToken);
        return DecoderColorProfileConversion.Apply(image, this, cancellationToken);
    }

    /// <inheritdoc />
    public Image Decode(Configuration configuration, Stream stream, CancellationToken cancellationToken)
        => this.Decode<Rgba32>(configuration, stream, cancellationToken);

    /// <inheritdoc/>
    public IImageInfo Identify(Configuration configuration, Stream stream, CancellationToken cancellationToken)
    {
        Guard.NotNull(stream, nameof(stream));

        return new BmpDecoderCore(configuration, this).Identify(configuration, stream, cancellationToken);
    }
}
