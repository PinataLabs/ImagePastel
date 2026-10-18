// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Processing.Processors.ColorManagement;

namespace SixLabors.ImageSharp.Processing;

/// <summary>
/// Defines extensions that convert an <see cref="Image"/> between ICC profiles using Mutate/Clone.
/// See <see cref="ConvertColorProfileProcessor"/> for what the conversion does to pixels and metadata,
/// and for how CMYK is stored in <c>Rgba32</c>, <c>Rgba64</c> and <c>RgbaVector</c> pixels.
/// </summary>
public static class ColorProfileExtensions
{
    /// <summary>
    /// Converts the image from its pixel profile (or sRGB, when it has none) to <paramref name="targetProfile"/>,
    /// which becomes the image's pixel profile.
    /// </summary>
    /// <param name="source">The image this method extends.</param>
    /// <param name="targetProfile">The profile to convert to.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext ConvertColorProfile(this IImageProcessingContext source, IccProfile targetProfile)
        => ConvertColorProfile(source, targetProfile, new IccColorTransformOptions());

    /// <summary>
    /// Converts the image from its pixel profile (or sRGB, when it has none) to <paramref name="targetProfile"/>,
    /// which becomes the image's pixel profile.
    /// </summary>
    /// <param name="source">The image this method extends.</param>
    /// <param name="targetProfile">The profile to convert to.</param>
    /// <param name="options">The conversion options.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext ConvertColorProfile(this IImageProcessingContext source, IccProfile targetProfile, IccColorTransformOptions options)
    {
        Guard.NotNull(targetProfile, nameof(targetProfile));
        return source.ApplyProcessor(new ConvertColorProfileProcessor(targetProfile, options));
    }

    /// <summary>
    /// Converts the image from its pixel profile to sRGB, and removes the pixel profile.
    /// An image without a pixel profile is sRGB already and is left as it is.
    /// </summary>
    /// <param name="source">The image this method extends.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext ConvertColorProfileToSrgb(this IImageProcessingContext source)
        => ConvertColorProfileToSrgb(source, new IccColorTransformOptions());

    /// <summary>
    /// Converts the image from its pixel profile to sRGB, and removes the pixel profile.
    /// An image without a pixel profile is sRGB already and is left as it is.
    /// </summary>
    /// <param name="source">The image this method extends.</param>
    /// <param name="options">The conversion options.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext ConvertColorProfileToSrgb(this IImageProcessingContext source, IccColorTransformOptions options)
        => source.ApplyProcessor(new ConvertColorProfileProcessor(null, options));
}
