// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

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

    // Null when there is nothing to convert: the image has no pixel profile and the target is sRGB.
    private readonly IccColorTransform transform;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConvertColorProfileProcessor{TPixel}"/> class.
    /// The transform is built here, so a corrupt or unsupported profile fails before any pixel changes.
    /// </summary>
    /// <param name="configuration">The configuration which allows altering default behaviour or extending the library.</param>
    /// <param name="definition">The <see cref="ConvertColorProfileProcessor"/>.</param>
    /// <param name="source">The source <see cref="Image{TPixel}"/> for the current processor instance.</param>
    /// <param name="sourceRectangle">The source area to process for the current processor instance.</param>
    public ConvertColorProfileProcessor(Configuration configuration, ConvertColorProfileProcessor definition, Image<TPixel> source, Rectangle sourceRectangle)
        : base(configuration, source, sourceRectangle)
    {
        this.targetProfile = definition.TargetProfile;
        IccProfile sourceProfile = source.Metadata.IccProfile;
        if (sourceProfile is not null || this.targetProfile is not null)
        {
            this.transform = new IccColorTransform(sourceProfile, this.targetProfile, definition.Options);
        }
    }

    /// <inheritdoc/>
    protected override void OnFrameApply(ImageFrame<TPixel> source)
    {
        if (this.transform is null)
        {
            return;
        }

        IccPixelConversion.Convert(this.Configuration, source.PixelBuffer, this.transform, CancellationToken.None);
    }

    /// <inheritdoc/>
    protected override void AfterImageApply()
    {
        this.Source.Metadata.IccProfile = this.targetProfile?.DeepClone();
        foreach (ImageFrame<TPixel> frame in this.Source.Frames)
        {
            if (frame.Metadata.IccProfile is not null)
            {
                frame.Metadata.IccProfile = this.targetProfile?.DeepClone();
            }
        }

        base.AfterImageApply();
    }
}
