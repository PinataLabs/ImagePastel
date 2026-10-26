// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Compression.Zlib;
using SixLabors.ImageSharp.Formats.Tiff.Constants;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace SixLabors.ImageSharp.Formats.Tiff;

/// <summary>
/// Encoder for writing the data image to a stream in TIFF format.
/// </summary>
public class TiffEncoder : IImageEncoder, ITiffEncoderOptions
{
    /// <inheritdoc/>
    public TiffBitsPerPixel? BitsPerPixel { get; set; }

    /// <inheritdoc/>
    public TiffCompression? Compression { get; set; }

    /// <inheritdoc/>
    public DeflateCompressionLevel? CompressionLevel { get; set; }

    /// <inheritdoc/>
    public TiffPhotometricInterpretation? PhotometricInterpretation { get; set; }

    /// <inheritdoc/>
    public TiffPredictor? HorizontalPredictor { get; set; }

    /// <inheritdoc/>
    public IQuantizer Quantizer { get; set; }

    /// <inheritdoc/>
    public void Encode<TPixel>(Image<TPixel> image, Stream stream)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var encode = new TiffEncoderCore(this, image.GetMemoryAllocator());
        if (stream.CanSeek)
        {
            encode.Encode(image, stream);
            return;
        }

        // The IFD offsets are written after the image data, so a non-seekable stream is buffered, as EncodeAsync does.
        using var seekableStream = new MemoryStream();
        encode.Encode(image, seekableStream);
        seekableStream.Position = 0;
        seekableStream.CopyTo(stream, image.GetConfiguration().StreamProcessingBufferSize);
    }

    /// <inheritdoc/>
    public Task EncodeAsync<TPixel>(Image<TPixel> image, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var encoder = new TiffEncoderCore(this, image.GetMemoryAllocator());
        return encoder.EncodeAsync(image, stream, cancellationToken);
    }
}
