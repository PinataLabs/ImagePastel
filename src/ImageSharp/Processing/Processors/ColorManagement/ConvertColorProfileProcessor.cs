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
/// has none. Every pixel of every frame is converted, whatever source rectangle the processor is
/// given, because the pixel profile describes the whole image. Alpha is not changed, and results
/// outside the target gamut are clamped to the range of the pixel type.
/// </para>
/// <para>
/// Afterwards the image's pixel profile, and the profile of each frame that had one, is a copy of
/// the target profile, or none when the target is sRGB (<see cref="TargetProfile"/> is <see langword="null"/>).
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
    /// <exception cref="System.NotSupportedException">The engine does not support a profile or an option yet.</exception>
    public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle)
        where TPixel : unmanaged, IPixel<TPixel>
        => new ConvertColorProfileProcessor<TPixel>(configuration, this, source, sourceRectangle);
}
