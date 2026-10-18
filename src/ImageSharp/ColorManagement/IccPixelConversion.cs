// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.ColorManagement;

/// <summary>
/// Runs an <see cref="IccColorTransform"/> over a buffer of pixels, for the
/// <c>ConvertColorProfile</c> processor and the decoders' color profile conversion.
/// </summary>
internal static class IccPixelConversion
{
    // Rows converted between two cancellation checks.
    private const int RowsPerCancellationCheck = 64;

    /// <summary>
    /// Converts every pixel of a buffer in place. Alpha is not changed, and results outside the
    /// target gamut are clamped to the range of the pixel type.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">The configuration, for parallelism and pixel conversion.</param>
    /// <param name="pixels">The pixels.</param>
    /// <param name="transform">The transform.</param>
    /// <param name="cancellationToken">Checked between bands of rows.</param>
    public static void Convert<TPixel>(Configuration configuration, Buffer2D<TPixel> pixels, IccColorTransform transform, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        RowOperation<TPixel> operation = new(pixels, transform, configuration);
        int band = cancellationToken.CanBeCanceled ? RowsPerCancellationCheck : pixels.Height;
        for (int y = 0; y < pixels.Height; y += band)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Rectangle rows = new(0, y, pixels.Width, Math.Min(band, pixels.Height - y));
            ParallelRowIterator.IterateRows<RowOperation<TPixel>, Vector4>(configuration, rows, in operation);
        }
    }

    /// <summary>
    /// Converts one row of pixels.
    /// </summary>
    private readonly struct RowOperation<TPixel> : IRowOperation<Vector4>
        where TPixel : unmanaged, IPixel<TPixel>
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
