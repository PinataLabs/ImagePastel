// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// The codestream header steps follow libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// JxlDecoderReadBasicInfo and JxlDecoderReadAllHeaders in lib/jxl/decode.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.IO;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Performs the JPEG XL decoding operation.
/// </summary>
internal sealed class JxlDecoderCore : IImageDecoderInternals
{
    // Level 10 limits, which libjxl only checks in its encoder.
    private const long MaxSideLength = 1L << 30;
    private const int MaxExtraChannels = 256;

    private JxlCodecMetadata metadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlDecoderCore"/> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    public JxlDecoderCore(Configuration configuration) => this.Configuration = configuration;

    /// <inheritdoc/>
    public Configuration Configuration { get; }

    /// <inheritdoc/>
    public Size Dimensions => this.metadata == null ? default : new Size((int)this.metadata.XSize, (int)this.metadata.YSize);

    /// <inheritdoc/>
    public Image<TPixel> Decode<TPixel>(BufferedReadStream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = ReadCodestream(stream);
        int frameOffset = this.ReadHeaders(data);

        // The pixel cap is checked from the header, before the image is allocated.
        CheckPixelCap(this.metadata.XSize, this.metadata.YSize);

        Image<TPixel> image = new(this.Configuration, (int)this.metadata.XSize, (int)this.metadata.YSize, new ImageMetadata());
        try
        {
            int alpha = this.metadata.M.Find(JxlExtraChannelType.Alpha);
            bool premultiplied = alpha >= 0 && this.metadata.M.ExtraChannelInfo[alpha].AlphaAssociated;
            JxlImageFrameSink<TPixel> sink = new(this.Configuration, image.Frames.RootFrame, alpha, premultiplied);
            new JxlFrameDecoder(this.Configuration, this.metadata).DecodeFrame(data, frameOffset, sink, cancellationToken);
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
        this.ReadHeaders(ReadCodestream(stream));
        JxlImageMetadata m = this.metadata.M;
        int bitsPerPixel = (int)m.BitDepth.BitsPerSample * (m.ColorEncoding.IsGray ? 1 : 3);
        foreach (JxlExtraChannelInfo eci in m.ExtraChannelInfo)
        {
            bitsPerPixel += (int)eci.BitDepth.BitsPerSample;
        }

        return new ImageInfo(new PixelTypeInfo(bitsPerPixel), (int)this.metadata.XSize, (int)this.metadata.YSize, new ImageMetadata());
    }

    /// <summary>
    /// Decodes the image into float planes, without clamping, for the conformance tests.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decoded planes. The caller disposes them.</returns>
    public JxlDecodedImage DecodeToPlanes(Stream stream, CancellationToken cancellationToken)
    {
        using BufferedReadStream bufferedStream = new(this.Configuration, stream);
        byte[] data = ReadCodestream(bufferedStream);
        int frameOffset = this.ReadHeaders(data);
        CheckPixelCap(this.metadata.XSize, this.metadata.YSize);

        JxlPlanesSink sink = new();
        try
        {
            new JxlFrameDecoder(this.Configuration, this.metadata).DecodeFrame(data, frameOffset, sink, cancellationToken);
            return sink.Image;
        }
        catch
        {
            sink.Image.Dispose();
            throw;
        }
    }

    private static void CheckPixelCap(ulong xsize, ulong ysize)
    {
        if (xsize * ysize > JxlFrameDecoder.MaxPixelsPerFrame)
        {
            throw new InvalidMemoryOperationException($"The JPEG XL image is {xsize}x{ysize} pixels, more than the limit of {JxlFrameDecoder.MaxPixelsPerFrame}.");
        }
    }

    // Reads the whole file, and takes the codestream out of a container.
    private static byte[] ReadCodestream(BufferedReadStream stream)
    {
        byte[] data = ReadAll(stream);
        return data.AsSpan().StartsWith(JxlConstants.ContainerSignature) ? JxlContainer.ExtractCodestream(data) : data;
    }

    // libjxl reads the codestream incrementally. This decoder decodes from memory, so it reads the
    // whole stream first; its size bounds the buffer.
    private static byte[] ReadAll(BufferedReadStream stream)
    {
        long length = stream.Length - stream.Position;
        if (length > int.MaxValue)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("The JPEG XL codestream is too large");
        }

        byte[] data = new byte[length];
        int read = 0;
        while (read < data.Length)
        {
            int n = stream.Read(data, read, data.Length - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        if (read != data.Length)
        {
            Array.Resize(ref data, read);
        }

        return data;
    }

    // Reads the signature, size header, image metadata, transform data and ICC profile, and returns
    // the offset of the first frame.
    private int ReadHeaders(byte[] data)
    {
        if (data.Length < 2 || data[0] != JxlConstants.CodestreamSignature[0] || data[1] != JxlConstants.CodestreamSignature[1])
        {
            JxlThrowHelper.ThrowInvalidImageContentException("invalid signature");
        }

        const int signatureLength = 2;
        JxlCodecMetadata codecMetadata = new();
        JxlBitReader reader = new(data, signatureLength, data.Length - signatureLength);

        // Basic info.
        JxlBundle.Read(reader, codecMetadata.Size);
        JxlBundle.Read(reader, codecMetadata.M);
        this.metadata = codecMetadata;

        if (codecMetadata.XSize > MaxSideLength || codecMetadata.YSize > MaxSideLength)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Image too large: {codecMetadata.XSize}x{codecMetadata.YSize}");
        }

        if (codecMetadata.M.NumExtraChannels > MaxExtraChannels)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Too many extra channels: {codecMetadata.M.NumExtraChannels}");
        }

        // All headers.
        codecMetadata.TransformData.NonserializedXybEncoded = codecMetadata.M.XybEncoded;
        JxlBundle.Read(reader, codecMetadata.TransformData);

        if (codecMetadata.M.ColorEncoding.WantIcc)
        {
            // libjxl decodes the entropy-coded ICC profile here.
            JxlThrowHelper.ThrowNotSupportedException("ICC profiles");
        }

        reader.JumpToByteBoundary();
        reader.EnsureAllReadsWithinBounds("codestream headers");
        long headerBits = reader.TotalBitsConsumed;
        reader.Close();

        if (codecMetadata.M.HavePreview)
        {
            // libjxl skips the preview frame, which comes first.
            JxlThrowHelper.ThrowNotSupportedException("Preview frames");
        }

        return signatureLength + (int)(headerBits / 8);
    }
}
