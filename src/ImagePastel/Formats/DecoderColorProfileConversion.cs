// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading;
using PinataLabs.ImagePastel.Advanced;
using PinataLabs.ImagePastel.ColorManagement;
using PinataLabs.ImagePastel.Metadata.Profiles.Exif;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats;

/// <summary>
/// Runs the <see cref="ColorProfileConversion"/> decoder option. A decoder calls
/// <see cref="Apply{TPixel}"/> on the image it is about to return; a decoder that converts samples
/// itself before they become pixels (CMYK) uses <see cref="TryCreateCmykTransform"/> and
/// <see cref="GetConvertedProfile"/>.
/// </summary>
internal static class DecoderColorProfileConversion
{
    // Marks a frame whose pixels need no change; only its profile is replaced.
    private static readonly IccColorTransform NoPixelChange = new(null, null);

    /// <summary>
    /// Converts a decoded image from its embedded profile, as the options ask, and returns it.
    /// Each frame is converted with its own profile when the frames have them (TIFF), and with the
    /// image's profile otherwise. Afterwards the converted frames and the image have the target
    /// profile (a copy), or none for sRGB.
    /// </summary>
    /// <remarks>
    /// Nothing changes when the option is <see cref="ColorProfileConversion.None"/>, when metadata
    /// is ignored, when the image has no RGB or gray profile, when a profile is already the target,
    /// or when the engine cannot use a profile (corrupt or not supported yet). A profile equivalent
    /// to sRGB (<see cref="IccProfile.IsSrgb"/>) converted to sRGB keeps the pixels as they are and
    /// only removes the profile. The image is disposed when this method throws.
    /// </remarks>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="image">The decoded image.</param>
    /// <param name="options">The decoder options.</param>
    /// <param name="cancellationToken">The decoder's cancellation token, checked between bands of rows.</param>
    /// <returns>The same image.</returns>
    /// <exception cref="InvalidOperationException">The target profile is missing.</exception>
    /// <exception cref="InvalidImageContentException">The target profile is corrupt.</exception>
    /// <exception cref="NotSupportedException">The engine does not support the target profile.</exception>
    public static Image<TPixel> Apply<TPixel>(Image<TPixel> image, IColorProfileDecoderOptions options, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        try
        {
            Convert(image, options, cancellationToken);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Converts a decoded image of any pixel type, as <see cref="Apply{TPixel}"/> does, and returns it.
    /// For decoders that pick the pixel type themselves.
    /// </summary>
    /// <param name="image">The decoded image.</param>
    /// <param name="options">The decoder options.</param>
    /// <param name="cancellationToken">The decoder's cancellation token, checked between bands of rows.</param>
    /// <returns>The same image.</returns>
    /// <exception cref="InvalidOperationException">The target profile is missing.</exception>
    /// <exception cref="InvalidImageContentException">The target profile is corrupt.</exception>
    /// <exception cref="NotSupportedException">The engine does not support the target profile.</exception>
    public static Image Apply(Image image, IColorProfileDecoderOptions options, CancellationToken cancellationToken)
    {
        try
        {
            image.AcceptVisitor(new ConvertVisitor(options, cancellationToken));
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Builds the transform from a source profile to the target the options ask for.
    /// </summary>
    /// <param name="options">The decoder options.</param>
    /// <param name="source">The embedded profile, or <see langword="null"/>.</param>
    /// <param name="transform">The transform, or <see langword="null"/> when this returns <see langword="false"/>.</param>
    /// <returns>
    /// <see langword="false"/> when there is nothing to convert: the option is
    /// <see cref="ColorProfileConversion.None"/>, metadata is ignored, there is no source profile,
    /// the source profile is already the target, or the engine cannot use the source profile.
    /// </returns>
    /// <exception cref="InvalidOperationException">The target profile is missing.</exception>
    /// <exception cref="InvalidImageContentException">The target profile is corrupt.</exception>
    /// <exception cref="NotSupportedException">The engine does not support the target profile.</exception>
    public static bool TryCreateTransform(IColorProfileDecoderOptions options, IccProfile source, out IccColorTransform transform)
    {
        transform = null;
        if (options.IgnoreMetadata || options.ColorProfileConversion == ColorProfileConversion.None || source is null)
        {
            return false;
        }

        IccProfile target = GetTarget(options);
        if (target is not null && IsTarget(source.ToByteArray(), target.ToByteArray()))
        {
            return false;
        }

        return TryBuild(source, target, out transform);
    }

    /// <summary>
    /// Builds the transform from the embedded profile of CMYK samples to the target the options ask
    /// for. A decoder of CMYK samples uses it to replace the CMYK-to-RGB formula; afterwards the
    /// image's pixel profile is <see cref="GetConvertedProfile"/>.
    /// </summary>
    /// <param name="options">The decoder options.</param>
    /// <param name="source">The embedded profile, or <see langword="null"/>.</param>
    /// <param name="transform">
    /// The transform, from ink amounts (0 = no ink, 1 = full ink) to the target's colors, or
    /// <see langword="null"/> when this returns <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when the formula stays: as for <see cref="TryCreateTransform"/>, or
    /// when the source profile is not a CMYK profile.
    /// </returns>
    /// <exception cref="InvalidOperationException">The target profile is missing.</exception>
    /// <exception cref="InvalidImageContentException">The target profile is corrupt.</exception>
    /// <exception cref="NotSupportedException">The engine does not support the target profile.</exception>
    public static bool TryCreateCmykTransform(IColorProfileDecoderOptions options, IccProfile source, out IccColorTransform transform)
    {
        if (TryCreateTransform(options, source, out transform) && transform.SourceColorSpace == IccColorSpaceType.Cmyk)
        {
            return true;
        }

        transform = null;
        return false;
    }

    /// <summary>
    /// Gets whether an embedded profile can be the pixel profile of the decoded pixels: an RGB
    /// profile for a color image, a gray profile for a grayscale one. Any other profile (CMYK, or
    /// gray on color) describes samples the pixels do not hold, so a decoder does not make it the
    /// pixel profile.
    /// </summary>
    /// <param name="profile">The embedded profile.</param>
    /// <param name="gray">Whether the file holds a grayscale image.</param>
    /// <returns><see langword="true"/> when the profile's data color space matches the image.</returns>
    public static bool CanBePixelProfile(IccProfile profile, bool gray)
        => profile.DataColorSpace == (gray ? IccColorSpaceType.Gray : IccColorSpaceType.Rgb);

    /// <summary>
    /// Gets the pixel profile of a converted image: a copy of the target, or <see langword="null"/> for sRGB.
    /// </summary>
    /// <param name="options">The decoder options.</param>
    /// <returns>The profile.</returns>
    public static IccProfile GetConvertedProfile(IColorProfileDecoderOptions options) => GetTarget(options)?.DeepClone();

    private static IccProfile GetTarget(IColorProfileDecoderOptions options)
    {
        if (options.ColorProfileConversion != ColorProfileConversion.ToTargetProfile)
        {
            return null;
        }

        return options.ColorProfileConversionTarget
            ?? throw new InvalidOperationException($"{nameof(ColorProfileConversion)} is {nameof(ColorProfileConversion.ToTargetProfile)}, but {nameof(IColorProfileDecoderOptions.ColorProfileConversionTarget)} is null.");
    }

    // A profile with the target's bytes needs no conversion. This also skips images whose samples a
    // decoder has already converted (CMYK) and given the target profile.
    private static bool IsTarget(byte[] source, byte[] target)
        => target is not null && source.AsSpan().SequenceEqual(target);

    // Builds the transform from a source profile that is not the target. The source is the problem
    // when this fails, unless the target is.
    private static bool TryBuild(IccProfile source, IccProfile target, out IccColorTransform transform)
    {
        try
        {
            transform = new IccColorTransform(source, target);
        }
        catch (Exception ex) when (ex is InvalidImageContentException or NotSupportedException or InvalidIccProfileException)
        {
            // The image keeps its profile when that profile is the problem. A target the engine
            // cannot use is the caller's mistake, so that is reported. A decode stops at the first
            // profile that fails, so this check runs at most once per decode.
            if (target is not null)
            {
                CheckTarget(new IccColorTransform(null, target));
            }

            transform = null;
            return false;
        }

        CheckTarget(transform);
        return true;
    }

    // Decoded pixels hold RGB or gray. Converting to CMYK (all four components) is left to the
    // ConvertColorProfile processor, which checks the pixel type.
    private static void CheckTarget(IccColorTransform transform)
    {
        if (transform.TargetColorSpace is not (IccColorSpaceType.Rgb or IccColorSpaceType.Gray))
        {
            throw new NotSupportedException($"Decoding to a {transform.TargetColorSpace} profile is not supported; the target must be an RGB or gray profile.");
        }
    }

    // The transform for a frame's profile: null when the profile cannot be used, or NoPixelChange
    // for an sRGB-equivalent profile on the way to sRGB, whose pixels already hold the target's colors.
    private static IccColorTransform CreateFrameTransform(IccProfile source, IccProfile target)
    {
        // Decoded pixels are RGB or gray; a profile of another color space (CMYK) describes
        // samples that are gone, so a decoder must convert those before they become pixels.
        if (source.DataColorSpace is not (IccColorSpaceType.Rgb or IccColorSpaceType.Gray))
        {
            return null;
        }

        if (!TryBuild(source, target, out IccColorTransform transform))
        {
            return null;
        }

        // The transform reads the profile first, so a corrupt profile has been rejected before IsSrgb reads its tags.
        return target is null && source.IsSrgb() ? NoPixelChange : transform;
    }

    private static void Convert<TPixel>(Image<TPixel> image, IColorProfileDecoderOptions options, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (options.IgnoreMetadata || options.ColorProfileConversion == ColorProfileConversion.None)
        {
            return;
        }

        // TIFF gives each frame its own profile (the image's is the first frame's); other formats
        // set only the image's profile.
        bool perFrame = false;
        foreach (ImageFrame<TPixel> frame in image.Frames)
        {
            perFrame |= frame.Metadata.IccProfile is not null;
        }

        // Build every transform before any pixel changes, so a profile the engine cannot use leaves the whole image as it is.
        IccProfile target = GetTarget(options);
        int count = image.Frames.Count;
        IccColorTransform[] transforms = new IccColorTransform[count];

        // TIFF frames hold separate but usually equal profiles, so transforms are shared by profile bytes.
        IccTransformCache built = new();
        byte[] targetBytes = target?.ToByteArray();
        bool any = false;
        for (int i = 0; i < count; i++)
        {
            IccProfile source = perFrame ? image.Frames[i].Metadata.IccProfile : image.Metadata.IccProfile;
            if (source is null)
            {
                continue;
            }

            byte[] sourceBytes = source.ToByteArray();
            if (IsTarget(sourceBytes, targetBytes))
            {
                continue;
            }

            IccColorTransform transform = built.GetOrCreate(sourceBytes, () => CreateFrameTransform(source, target));
            if (transform is null)
            {
                return;
            }

            transforms[i] = transform;
            any = true;
        }

        if (!any)
        {
            return;
        }

        Configuration configuration = image.GetConfiguration();
        for (int i = 0; i < count; i++)
        {
            if (transforms[i] is null)
            {
                continue;
            }

            ImageFrame<TPixel> frame = image.Frames[i];
            if (!ReferenceEquals(transforms[i], NoPixelChange))
            {
                IccPixelConversion.Convert(configuration, frame.PixelBuffer, transforms[i], cancellationToken);
            }

            if (perFrame)
            {
                frame.Metadata.IccProfile = GetConvertedProfile(options);
                frame.Metadata.ExifProfile?.RemoveValue(ExifTag.IccProfile);
            }
        }

        image.Metadata.IccProfile = perFrame ? image.Frames.RootFrame.Metadata.IccProfile : GetConvertedProfile(options);
    }

    private sealed class ConvertVisitor : IImageVisitor
    {
        private readonly IColorProfileDecoderOptions options;
        private readonly CancellationToken cancellationToken;

        public ConvertVisitor(IColorProfileDecoderOptions options, CancellationToken cancellationToken)
        {
            this.options = options;
            this.cancellationToken = cancellationToken;
        }

        public void Visit<TPixel>(Image<TPixel> image)
            where TPixel : unmanaged, IPixel<TPixel>
            => Convert(image, this.options, this.cancellationToken);
    }
}
