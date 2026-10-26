// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Processing.Processors.ColorManagement;

/// <summary>
/// Converts an image from its pixel profile to a target ICC profile with an <see cref="IccColorTransform"/>.
/// </summary>
/// <remarks>
/// <para>
/// The source is the image's pixel profile (<c>Metadata.IccProfile</c>), or sRGB when the image
/// has none. When frames have their own pixel profiles (<c>ImageFrameMetadata.IccProfile</c>, as a
/// TIFF decodes them), each frame is converted from its own profile instead, and a frame without
/// one holds sRGB. Every pixel of every frame is converted, whatever source rectangle the processor
/// is given, because the pixel profile describes the whole image. Alpha is not changed, and results
/// outside the target gamut are clamped to the range of the pixel type.
/// </para>
/// <para>
/// Afterwards the image's pixel profile, and the profile of each frame when the frames had their
/// own, is a copy of the target profile, or none when the target is sRGB
/// (<see cref="TargetProfile"/> is <see langword="null"/>).
/// </para>
/// <para>
/// ImageSharp has no CMYK pixel type. An image whose pixel profile is CMYK holds cyan, magenta,
/// yellow and black in the first, second, third and fourth components (R, G, B and A) of an
/// <see cref="Rgba32"/>, <see cref="Rgba64"/> or <see cref="RgbaVector"/> pixel, each from 0 (no ink)
/// to the component's maximum (full ink). Converting to a CMYK profile writes that layout and drops
/// alpha; converting from one reads it and gives opaque pixels, so CMYK to RGB reverses RGB to CMYK.
/// The pixel profile is what marks the components as CMYK, so an image must only carry a CMYK
/// pixel profile when its pixels hold CMYK in this layout. Any other pixel type throws
/// <see cref="System.NotSupportedException"/> when either profile is CMYK.
/// </para>
/// <para>
/// Decoders never give an image a CMYK pixel profile. A CMYK JPEG or TIFF decoded without a color
/// profile conversion has RGB pixels from a simple CMYK formula and no pixel profile, so this
/// processor treats them as sRGB; the file's CMYK profile stays reachable as the embedded ICC
/// profile (<c>JpegMetadata.EmbeddedIccProfile</c>, <c>TiffMetadata.EmbeddedIccProfile</c>). To get
/// the colors that profile defines, decode with the decoder's <c>ColorProfileConversion</c> option,
/// which converts the CMYK samples themselves.
/// </para>
/// </remarks>
public sealed class ConvertColorProfileProcessor : IImageProcessor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConvertColorProfileProcessor"/> class with the default options.
    /// </summary>
    /// <param name="targetProfile">The profile to convert to, or <see langword="null"/> for sRGB.</param>
    public ConvertColorProfileProcessor(IccProfile targetProfile)
        : this(targetProfile, new IccColorTransformOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConvertColorProfileProcessor"/> class.
    /// </summary>
    /// <param name="targetProfile">The profile to convert to, or <see langword="null"/> for sRGB.</param>
    /// <param name="options">The conversion options.</param>
    public ConvertColorProfileProcessor(IccProfile targetProfile, IccColorTransformOptions options)
    {
        Guard.NotNull(options, nameof(options));
        this.TargetProfile = targetProfile;
        this.Options = options;
    }

    /// <summary>
    /// Gets the profile to convert to, or <see langword="null"/> for sRGB.
    /// </summary>
    public IccProfile TargetProfile { get; }

    /// <summary>
    /// Gets the conversion options.
    /// </summary>
    public IccColorTransformOptions Options { get; }

    /// <inheritdoc />
    /// <exception cref="InvalidImageContentException">The image's pixel profile or the target profile is corrupt.</exception>
    /// <exception cref="System.ArgumentOutOfRangeException">The options' rendering intent is not one of the four ICC intents.</exception>
    /// <exception cref="System.NotSupportedException">
    /// The engine does not support a profile, or a profile is CMYK and
    /// <typeparamref name="TPixel"/> is not <see cref="Rgba32"/>, <see cref="Rgba64"/> or <see cref="RgbaVector"/>.
    /// </exception>
    public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle)
        where TPixel : unmanaged, IPixel<TPixel>
        => new ConvertColorProfileProcessor<TPixel>(configuration, this, source, sourceRectangle);
}
