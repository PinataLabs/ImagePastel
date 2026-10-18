// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using SixLabors.ImageSharp.IO;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Image decoder for JPEG XL codestreams.
/// </summary>
internal sealed class JxlDecoder : IImageDecoder, IImageInfoDetector
{
    /// <inheritdoc/>
    /// <remarks>
    /// A frame over the 2^28-pixel cap throws <see cref="InvalidMemoryOperationException"/>. This bypasses
    /// <see cref="ImageDecoderUtilities"/>, which would turn it into an <see cref="InvalidImageContentException"/>.
    /// </remarks>
    public Image<TPixel> Decode<TPixel>(Configuration configuration, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(stream, nameof(stream));

        JxlDecoderCore decoder = new(configuration);
        BufferedReadStream bufferedReadStream = stream as BufferedReadStream ?? new BufferedReadStream(configuration, stream);
        try
        {
            return decoder.Decode<TPixel>(bufferedReadStream, cancellationToken);
        }
        finally
        {
            if (bufferedReadStream != stream)
            {
                bufferedReadStream.Dispose();
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>The pixel type is <see cref="Rgba32"/> until a later slice picks it from the bit depth and channels.</remarks>
    public Image Decode(Configuration configuration, Stream stream, CancellationToken cancellationToken)
        => this.Decode<Rgba32>(configuration, stream, cancellationToken);

    /// <inheritdoc/>
    public IImageInfo Identify(Configuration configuration, Stream stream, CancellationToken cancellationToken)
    {
        Guard.NotNull(stream, nameof(stream));

        return new JxlDecoderCore(configuration).Identify(configuration, stream, cancellationToken);
    }
}
