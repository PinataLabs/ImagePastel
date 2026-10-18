// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.IO;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Image decoder for JPEG XL codestreams.
/// </summary>
internal sealed class JxlDecoder : IImageDecoder, IImageInfoDetector, IJxlDecoderOptions
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
    /// The conversion starts from the pixel profile: an XYB image with an embedded ICC profile decodes
    /// to sRGB with no pixel profile (ADR 0002), so it is not converted.
    /// </summary>
    public ColorProfileConversion ColorProfileConversion { get; set; }

    /// <summary>
    /// Gets or sets the profile to convert to when <see cref="ColorProfileConversion"/> is
    /// <see cref="ColorProfileConversion.ToTargetProfile"/>. Decoding throws when it is missing,
    /// corrupt or not supported.
    /// </summary>
    public IccProfile ColorProfileConversionTarget { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// A frame over the 2^28-pixel cap throws <see cref="InvalidMemoryOperationException"/>. This bypasses
    /// <see cref="ImageDecoderUtilities"/>, which would turn it into an <see cref="InvalidImageContentException"/>.
    /// </remarks>
    public Image<TPixel> Decode<TPixel>(Configuration configuration, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(stream, nameof(stream));

        JxlDecoderCore decoder = new(configuration, this);
        BufferedReadStream bufferedReadStream = stream as BufferedReadStream ?? new BufferedReadStream(configuration, stream);
        Image<TPixel> image;
        try
        {
            image = decoder.Decode<TPixel>(bufferedReadStream, cancellationToken);
        }
        finally
        {
            if (bufferedReadStream != stream)
            {
                bufferedReadStream.Dispose();
            }
        }

        return DecoderColorProfileConversion.Apply(image, this, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>The pixel type follows the bit depth and channels of the image; see <see cref="JxlDecoderCore.ChoosePixelType(JxlImageMetadata)"/>.</remarks>
    public Image Decode(Configuration configuration, Stream stream, CancellationToken cancellationToken)
    {
        Guard.NotNull(stream, nameof(stream));

        JxlDecoderCore decoder = new(configuration, this);
        BufferedReadStream bufferedReadStream = stream as BufferedReadStream ?? new BufferedReadStream(configuration, stream);
        Image image;
        try
        {
            image = decoder.Decode(bufferedReadStream, cancellationToken);
        }
        finally
        {
            if (bufferedReadStream != stream)
            {
                bufferedReadStream.Dispose();
            }
        }

        return DecoderColorProfileConversion.Apply(image, this, cancellationToken);
    }

    /// <inheritdoc/>
    public IImageInfo Identify(Configuration configuration, Stream stream, CancellationToken cancellationToken)
    {
        Guard.NotNull(stream, nameof(stream));

        return new JxlDecoderCore(configuration, this).Identify(configuration, stream, cancellationToken);
    }
}
