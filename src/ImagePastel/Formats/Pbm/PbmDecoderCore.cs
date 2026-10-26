// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading;
using PinataLabs.ImagePastel.IO;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats.Pbm;

/// <summary>
/// Performs the PBM decoding operation.
/// </summary>
internal sealed class PbmDecoderCore : IImageDecoderInternals
{
    private int maxPixelValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="PbmDecoderCore" /> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    public PbmDecoderCore(Configuration configuration) => this.Configuration = configuration ?? Configuration.Default;

    /// <inheritdoc />
    public Configuration Configuration { get; }

    /// <summary>
    /// Gets the colortype to use
    /// </summary>
    public PbmColorType ColorType { get; private set; }

    /// <summary>
    /// Gets the size of the pixel array
    /// </summary>
    public Size PixelSize { get; private set; }

    /// <summary>
    /// Gets the component data type
    /// </summary>
    public PbmComponentType ComponentType { get; private set; }

    /// <summary>
    /// Gets the Encoding of pixels
    /// </summary>
    public PbmEncoding Encoding { get; private set; }

    /// <summary>
    /// Gets the <see cref="ImageMetadata"/> decoded by this decoder instance.
    /// </summary>
    public ImageMetadata Metadata { get; private set; }

    /// <inheritdoc/>
    Size IImageDecoderInternals.Dimensions => this.PixelSize;

    /// <inheritdoc/>
    public Image<TPixel> Decode<TPixel>(BufferedReadStream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ProcessHeader(stream);

        var image = new Image<TPixel>(this.Configuration, this.PixelSize.Width, this.PixelSize.Height, this.Metadata);
        try
        {
            Buffer2D<TPixel> pixels = image.GetRootFramePixelBuffer();

            this.ProcessPixels(stream, pixels);

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public IImageInfo Identify(BufferedReadStream stream, CancellationToken cancellationToken)
    {
        this.ProcessHeader(stream);

        // BlackAndWhite pixels are encoded into a byte.
        int bitsPerPixel = this.ComponentType == PbmComponentType.Short ? 16 : 8;
        return new ImageInfo(new PixelTypeInfo(bitsPerPixel), this.PixelSize.Width, this.PixelSize.Height, this.Metadata);
    }

    /// <summary>
    /// Processes the ppm header.
    /// </summary>
    /// <param name="stream">The input stream.</param>
    /// <exception cref="InvalidImageContentException">An EOF marker has been read before the image has been decoded.</exception>
    private void ProcessHeader(BufferedReadStream stream)
    {
        Span<byte> buffer = stackalloc byte[2];

        int bytesRead = stream.Read(buffer);
        if (bytesRead != 2 || buffer[0] != 'P')
        {
            throw new InvalidImageContentException("Empty or not an PPM image.");
        }

        switch ((char)buffer[1])
        {
            case '1':
                // Plain PBM format: 1 component per pixel, boolean value ('0' or '1').
                this.ColorType = PbmColorType.BlackAndWhite;
                this.Encoding = PbmEncoding.Plain;
                break;
            case '2':
                // Plain PGM format: 1 component per pixel, in decimal text.
                this.ColorType = PbmColorType.Grayscale;
                this.Encoding = PbmEncoding.Plain;
                break;
            case '3':
                // Plain PPM format: 3 components per pixel, in decimal text.
                this.ColorType = PbmColorType.Rgb;
                this.Encoding = PbmEncoding.Plain;
                break;
            case '4':
                // Binary PBM format: 1 component per pixel, 8 pixels per byte.
                this.ColorType = PbmColorType.BlackAndWhite;
                this.Encoding = PbmEncoding.Binary;
                break;
            case '5':
                // Binary PGM format: 1 components per pixel, in binary integers.
                this.ColorType = PbmColorType.Grayscale;
                this.Encoding = PbmEncoding.Binary;
                break;
            case '6':
                // Binary PPM format: 3 components per pixel, in binary integers.
                this.ColorType = PbmColorType.Rgb;
                this.Encoding = PbmEncoding.Binary;
                break;
            case '7':
            // PAM image: sequence of images.
            // Not implemented yet
            default:
                throw new InvalidImageContentException("Unknown of not implemented image type encountered.");
        }

        if (!stream.SkipWhitespaceAndComments() ||
            !stream.ReadDecimal(out int width) ||
            !stream.SkipWhitespaceAndComments() ||
            !stream.ReadDecimal(out int height))
        {
            ThrowPrematureEof();
        }

        if (width == 0 || height == 0)
        {
            throw new InvalidImageContentException($"Invalid image dimensions {width}x{height}; the width and height must be greater than zero.");
        }

        // ReadDecimal reads the single white space byte that ends the last header field. In the binary formats
        // the raster starts right after it, and its first bytes can look like white space or a comment,
        // so only the plain formats skip more white space and comments.
        if (this.ColorType != PbmColorType.BlackAndWhite)
        {
            if (!stream.SkipWhitespaceAndComments() ||
                !stream.ReadDecimal(out this.maxPixelValue))
            {
                ThrowPrematureEof();
            }

            // The Netpbm specification requires 0 < maxval < 65536.
            if (this.maxPixelValue is <= 0 or > 65535)
            {
                throw new InvalidImageContentException($"Invalid maximum pixel value {this.maxPixelValue}; it must be between 1 and 65535.");
            }

            if (this.maxPixelValue > 255)
            {
                this.ComponentType = PbmComponentType.Short;
            }
            else
            {
                this.ComponentType = PbmComponentType.Byte;
            }

            if (this.Encoding == PbmEncoding.Plain)
            {
                stream.SkipWhitespaceAndComments();
            }
        }
        else
        {
            this.ComponentType = PbmComponentType.Bit;
            if (this.Encoding == PbmEncoding.Plain && !stream.SkipWhitespaceAndComments())
            {
                ThrowPrematureEof();
            }
        }

        this.PixelSize = new Size(width, height);
        this.Metadata = new ImageMetadata();
        PbmMetadata meta = this.Metadata.GetPbmMetadata();
        meta.Encoding = this.Encoding;
        meta.ColorType = this.ColorType;
        meta.ComponentType = this.ComponentType;

        static void ThrowPrematureEof() => throw new InvalidImageContentException("Reached EOF while reading the header.");
    }

    private void ProcessPixels<TPixel>(BufferedReadStream stream, Buffer2D<TPixel> pixels)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (this.Encoding == PbmEncoding.Binary)
        {
            BinaryDecoder.Process(this.Configuration, pixels, stream, this.ColorType, this.ComponentType, this.maxPixelValue);
        }
        else
        {
            PlainDecoder.Process(this.Configuration, pixels, stream, this.ColorType, this.ComponentType, this.maxPixelValue);
        }
    }
}
