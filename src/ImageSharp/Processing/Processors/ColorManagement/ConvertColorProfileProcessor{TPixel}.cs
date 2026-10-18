// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.Memory;
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

            // The converter carries CMYK in all four channels; no pixel type maps to that yet.
            if (this.transform.SourceColorSpace == IccColorSpaceType.Cmyk || this.transform.TargetColorSpace == IccColorSpaceType.Cmyk)
            {
                throw new NotSupportedException("Converting images from or to a CMYK profile is not supported yet.");
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnFrameApply(ImageFrame<TPixel> source)
    {
        if (this.transform is null)
        {
            return;
        }

        RowOperation operation = new(source.PixelBuffer, this.transform, this.Configuration);
        ParallelRowIterator.IterateRows<RowOperation, Vector4>(this.Configuration, source.Bounds(), in operation);
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

    /// <summary>
    /// Converts one row of pixels.
    /// </summary>
    private readonly struct RowOperation : IRowOperation<Vector4>
    {
        private readonly Buffer2D<TPixel> source;
        private readonly IccColorTransform transform;
        private readonly Configuration configuration;

        [MethodImpl(InliningOptions.ShortMethod)]
        public RowOperation(Buffer2D<TPixel> source, IccColorTransform transform, Configuration configuration)
        {
            this.source = source;
            this.transform = transform;
            this.configuration = configuration;
        }

        /// <inheritdoc/>
        public void Invoke(int y, Span<Vector4> span)
        {
            Span<TPixel> row = this.source.DangerousGetRowSpan(y).Slice(0, span.Length);
            PixelOperations<TPixel>.Instance.ToVector4(this.configuration, row, span, PixelConversionModifiers.Scale);

            this.transform.Transform(span);

            // Clamp to the scaled range of every pixel type; float pixel types skip it in bulk conversion.
            for (int i = 0; i < span.Length; i++)
            {
                ref Vector4 value = ref span[i];
                value = new Vector4(
                    Numerics.Clamp(value.X, 0, 1),
                    Numerics.Clamp(value.Y, 0, 1),
                    Numerics.Clamp(value.Z, 0, 1),
                    value.W);
            }

            PixelOperations<TPixel>.Instance.FromVector4Destructive(this.configuration, span, row, PixelConversionModifiers.Scale);
        }
    }
}
