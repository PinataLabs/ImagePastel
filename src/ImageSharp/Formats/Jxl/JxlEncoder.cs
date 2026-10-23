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
/// Image encoder for writing an image to a stream in the JPEG XL format. The defaults write a lossy image
/// that is visually lossless (<see cref="Distance"/> 1); set <see cref="Distance"/> to 0 for a lossless image.
/// </summary>
/// <remarks>
/// <para>
/// Lossless and lossy encoding work for any pixel type, with or without alpha, at every
/// <see cref="JxlEffort"/>. The options are checked when the encode starts: a value out of range throws
/// <see cref="ImageFormatException"/>, and nothing is written to the stream.
/// </para>
/// <para>
/// Lossless encoding embeds the image's ICC profile when it describes the pixels. Lossy encoding codes the
/// colour as sRGB, or as the colour space that the ICC profile describes when the encoder recognises it as
/// a JPEG XL colour encoding; the file then carries that encoding instead of the profile. Lossy encoding
/// with an ICC profile that the encoder cannot describe throws <see cref="NotSupportedException"/>: save
/// it lossless, or convert the image to sRGB first.
/// </para>
/// <para>
/// The image's <see cref="Metadata.ImageMetadata.ExifProfile"/> and
/// <see cref="Metadata.ImageMetadata.XmpProfile"/> are written in <c>Exif</c> and <c>xml </c> boxes of a
/// JPEG XL container, uncompressed. An Exif profile without values and an empty XMP profile are not
/// written. An Exif or XMP profile over 64 MiB, the most the JPEG XL decoder reads, throws
/// <see cref="ImageFormatException"/>. The ICC profile is part of the codestream, not a box. IPTC, JUMBF
/// and gain maps are not written.
/// </para>
/// <para>
/// The output is a bare codestream unless it needs a container, as in libjxl. It needs one when there is
/// Exif or XMP to store, or when the codestream needs level 10. Level 10 is needed for lossless samples
/// or alpha above 12 bits (for example a 16-bit image saved lossless, or 16-bit alpha at any
/// <see cref="Distance"/>), and for a side over 2^18 pixels or more than 2^28 pixels. The container then
/// has a <c>jxll</c> box that gives the level. A side over 2^30 pixels throws
/// <see cref="ImageFormatException"/>. The JPEG XL decoder of this library refuses a frame over 2^28
/// pixels, although such a file is a valid level-10 file.
/// </para>
/// <para>
/// Only the root frame is written. From <see cref="JxlMetadata"/> the encoder reads only
/// <see cref="JxlMetadata.BitsPerSample"/>, <see cref="JxlMetadata.ExponentBitsPerSample"/> and
/// <see cref="JxlMetadata.IntensityTarget"/>; it ignores the other members.
/// </para>
/// </remarks>
public sealed class JxlEncoder : IImageEncoder, IJxlEncoderOptions
{
    /// <summary>
    /// Gets or sets the target for lossy error, in butteraugli units. It has to be in [0, 25]. 0 means
    /// lossless, about 1 is visually lossless, and larger values give smaller files with more visible loss.
    /// Values above 0 and below 0.05 are encoded as 0.05, as libjxl does. Defaults to 1.
    /// </summary>
    public float Distance { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets how much search the encoder spends to reach <see cref="Distance"/>: more effort gives
    /// smaller files and takes longer. It does not change the target error. Defaults to
    /// <see cref="JxlEffort.Default"/>.
    /// </summary>
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
