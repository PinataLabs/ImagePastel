// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Processing.Processors.ColorManagement;

/// <summary>
/// Converts an image from its pixel profile to a target ICC profile.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal sealed class ConvertColorProfileProcessor<TPixel> : ImageProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly IccProfile targetProfile;

    // True when the frames carry their own pixel profiles (as TIFF decodes them); the image's profile is then not used.
    private readonly bool perFrame;

    // The transform of each frame. A frame is missing when there is nothing to convert (sRGB to sRGB).
    private readonly Dictionary<ImageFrame<TPixel>, IccColorTransform> transforms = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ConvertColorProfileProcessor{TPixel}"/> class.
    /// The transforms are built here, so a corrupt or unsupported profile fails before any pixel changes.
    /// </summary>
    /// <param name="configuration">The configuration which allows altering default behaviour or extending the library.</param>
    /// <param name="definition">The <see cref="ConvertColorProfileProcessor"/>.</param>
    /// <param name="source">The source <see cref="Image{TPixel}"/> for the current processor instance.</param>
    /// <param name="sourceRectangle">The source area to process for the current processor instance.</param>
    public ConvertColorProfileProcessor(Configuration configuration, ConvertColorProfileProcessor definition, Image<TPixel> source, Rectangle sourceRectangle)
        : base(configuration, source, sourceRectangle)
    {
        this.targetProfile = definition.TargetProfile;
        foreach (ImageFrame<TPixel> frame in source.Frames)
        {
            this.perFrame |= frame.Metadata.IccProfile is not null;
        }

        // Frames with equal profiles (separate instances in a TIFF) share one transform.
        IccTransformCache built = new();
        foreach (ImageFrame<TPixel> frame in source.Frames)
        {
            // When the frames have their own profiles, a frame without one holds sRGB.
            IccProfile sourceProfile = this.perFrame ? frame.Metadata.IccProfile : source.Metadata.IccProfile;
            if (sourceProfile is null && this.targetProfile is null)
            {
                continue;
            }

            this.transforms[frame] = built.GetOrCreate(
                sourceProfile?.ToByteArray(),
                () => CreateTransform(sourceProfile, this.targetProfile, definition.Options));
        }
    }

    /// <inheritdoc/>
    protected override void OnFrameApply(ImageFrame<TPixel> source)
    {
        if (this.transforms.TryGetValue(source, out IccColorTransform transform))
        {
            IccPixelConversion.Convert(this.Configuration, source.PixelBuffer, transform, CancellationToken.None);
        }
    }

    /// <inheritdoc/>
    protected override void AfterImageApply()
    {
        this.Source.Metadata.IccProfile = this.targetProfile?.DeepClone();
        if (this.perFrame)
        {
            // Every frame now holds the target's colors, also the frames that held sRGB.
            foreach (ImageFrame<TPixel> frame in this.Source.Frames)
            {
                frame.Metadata.IccProfile = this.targetProfile?.DeepClone();
            }
        }

        base.AfterImageApply();
    }

    private static IccColorTransform CreateTransform(IccProfile sourceProfile, IccProfile targetProfile, IccColorTransformOptions options)
    {
        IccColorTransform transform = new(sourceProfile, targetProfile, options);

        // CMYK fills all four components, so it needs a pixel type whose four components can hold any value.
        if ((transform.SourceColorSpace == IccColorSpaceType.Cmyk || transform.TargetColorSpace == IccColorSpaceType.Cmyk)
            && !CanHoldCmyk())
        {
            throw new NotSupportedException(
                $"CMYK pixels are stored as C, M, Y and K in the R, G, B and A components of {nameof(Rgba32)}, {nameof(Rgba64)} or {nameof(RgbaVector)}; "
                + $"{typeof(TPixel).Name} cannot hold them. Convert the image to one of those pixel types first.");
        }

        return transform;
    }

    // One RGBA pixel type per precision (8-bit, 16-bit, float), so CMYK sits in a fixed component order.
    private static bool CanHoldCmyk()
        => typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Rgba64) || typeof(TPixel) == typeof(RgbaVector);
}
