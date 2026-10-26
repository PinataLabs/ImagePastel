// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using PinataLabs.ImagePastel.ColorManagement;
using PinataLabs.ImagePastel.IO;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>
/// Image decoder for JPEG XL images, as a bare codestream or in the ISO BMFF container.
/// </summary>
/// <remarks>
/// An animation decodes to one image frame per displayed frame, with the frames already blended. The
/// format metadata is in <see cref="JxlMetadata"/> and <see cref="JxlFrameMetadata"/>.
/// </remarks>
public sealed class JxlDecoder : IImageDecoder, IImageInfoDetector, IJxlDecoderOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the metadata should be ignored when the image is being decoded.
    /// </summary>
    public bool IgnoreMetadata { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of displayed frames to decode. Decoding stops once that many
    /// frames are out. Defaults to <see cref="uint.MaxValue"/>, which decodes them all.
    /// </summary>
    public uint MaxFrames { get; set; } = uint.MaxValue;

    /// <summary>
    /// Gets or sets the conversion from the embedded ICC profile that runs after the pixels are
    /// decoded. Defaults to <see cref="ColorProfileConversion.None"/>. Nothing is converted when
    /// <see cref="IgnoreMetadata"/> is set, when the image has no embedded profile, or when the
    /// embedded profile is corrupt or not supported; the image then keeps its embedded profile.
    /// The conversion starts from the pixel profile: an XYB image with an embedded ICC profile decodes
    /// to sRGB with no pixel profile, so it is not converted.
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
    /// An image larger than 2^28 pixels, or than 2^30 samples (pixels times colour and extra channels),
    /// throws <see cref="InvalidMemoryOperationException"/>. So does an animation whose displayed frames
    /// pass 2^34 samples in total, or a decode that runs out of memory.
    /// </remarks>
    public Image<TPixel> Decode<TPixel>(Configuration configuration, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(stream, nameof(stream));

        // The limits stay InvalidMemoryOperationException because this bypasses ImageDecoderUtilities, which
        // would turn it into an InvalidImageContentException.
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
    /// <remarks>
    /// The pixel type follows the channels and the bit depth of the image, the larger of the colour's and
    /// the alpha's: <see cref="L8"/>, <see cref="La16"/>, <see cref="Rgb24"/> or <see cref="Rgba32"/> up to
    /// 8 bits; <see cref="L16"/>, <see cref="La32"/>, <see cref="Rgb48"/> or <see cref="Rgba64"/> up to 16
    /// bits; and <see cref="RgbaVector"/> for floating-point samples or more than 16 bits. A CMYK image
    /// decodes to RGB.
    /// </remarks>
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
