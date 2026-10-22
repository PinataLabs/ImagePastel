// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// The codestream header steps follow libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// JxlDecoderReadBasicInfo and JxlDecoderReadAllHeaders in lib/jxl/decode.cc, and the frame loop of
// JxlDecoderProcessCodestream there, with its preview skip.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Icc;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.IO;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
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

    private readonly bool ignoreMetadata;
    private readonly uint maxFrames;

    private JxlCodecMetadata metadata;

    // The Exif and XMP of the file, read with the codestream.
    private ImageMetadata imageMetadata;

    // Whether the image is CMYK: it has a Black extra channel and its embedded ICC profile is CMYK.
    private bool isCmyk;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlDecoderCore"/> class with the default options.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    public JxlDecoderCore(Configuration configuration)
        : this(configuration, new JxlDecoder())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlDecoderCore"/> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="options">The options.</param>
    public JxlDecoderCore(Configuration configuration, IJxlDecoderOptions options)
    {
        this.Configuration = configuration;
        this.ignoreMetadata = options.IgnoreMetadata;
        this.maxFrames = options.MaxFrames;
    }

    /// <inheritdoc/>
    public Configuration Configuration { get; }

    /// <inheritdoc/>
    public Size Dimensions => this.metadata == null ? default : this.OrientedSize;

    // The size of the image once its orientation is undone: the size of the decoded image.
    private Size OrientedSize => new((int)this.metadata.OrientedXSize, (int)this.metadata.OrientedYSize);

    /// <inheritdoc/>
    public Image<TPixel> Decode<TPixel>(BufferedReadStream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        byte[] data = this.ReadCodestream(stream, cancellationToken);
        int frameOffset = this.ReadHeaders(data, cancellationToken);
        return this.Decode<TPixel>(data, frameOffset, cancellationToken);
    }

    /// <summary>
    /// Decodes the image into the pixel type that <see cref="ChoosePixelType(JxlImageMetadata)"/> picks
    /// from its header.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The image.</returns>
    public Image Decode(BufferedReadStream stream, CancellationToken cancellationToken)
    {
        byte[] data = this.ReadCodestream(stream, cancellationToken);
        int frameOffset = this.ReadHeaders(data, cancellationToken);
        Type pixelType = ChoosePixelType(this.metadata.M);
        return pixelType == typeof(L8) ? this.Decode<L8>(data, frameOffset, cancellationToken)
            : pixelType == typeof(L16) ? this.Decode<L16>(data, frameOffset, cancellationToken)
            : pixelType == typeof(La16) ? this.Decode<La16>(data, frameOffset, cancellationToken)
            : pixelType == typeof(La32) ? this.Decode<La32>(data, frameOffset, cancellationToken)
            : pixelType == typeof(Rgb24) ? this.Decode<Rgb24>(data, frameOffset, cancellationToken)
            : pixelType == typeof(Rgb48) ? this.Decode<Rgb48>(data, frameOffset, cancellationToken)
            : pixelType == typeof(Rgba32) ? this.Decode<Rgba32>(data, frameOffset, cancellationToken)
            : pixelType == typeof(Rgba64) ? this.Decode<Rgba64>(data, frameOffset, cancellationToken)
            : this.Decode<RgbaVector>(data, frameOffset, cancellationToken);
    }

    /// <summary>
    /// Picks the pixel type for an image with this header, as decided in issue #111. The bit depth is
    /// the larger of the colour's and the alpha's. XYB images use the bit depth they signal, and CMYK
    /// images count as RGB because they are converted.
    /// </summary>
    /// <param name="metadata">The image metadata.</param>
    /// <returns>The pixel type.</returns>
    public static Type ChoosePixelType(JxlImageMetadata metadata)
    {
        uint bits = metadata.BitDepth.BitsPerSample;
        bool floatingPoint = metadata.BitDepth.FloatingPointSample;
        int alpha = metadata.Find(JxlExtraChannelType.Alpha);
        if (alpha >= 0)
        {
            JxlBitDepth alphaBitDepth = metadata.ExtraChannelInfo[alpha].BitDepth;
            bits = Math.Max(bits, alphaBitDepth.BitsPerSample);
            floatingPoint |= alphaBitDepth.FloatingPointSample;
        }

        return ChoosePixelType(metadata.ColorEncoding.IsGray, alpha >= 0, bits, floatingPoint);
    }

    /// <summary>
    /// Picks the pixel type for an image with these channels and samples. ImageSharp has no float grey
    /// pixel, so float images are <see cref="RgbaVector"/>.
    /// </summary>
    /// <param name="gray">Whether the image is grey.</param>
    /// <param name="alpha">Whether the image has alpha.</param>
    /// <param name="bits">The bits per sample.</param>
    /// <param name="floatingPoint">Whether the samples are floats.</param>
    /// <returns>The pixel type.</returns>
    public static Type ChoosePixelType(bool gray, bool alpha, uint bits, bool floatingPoint)
    {
        if (floatingPoint || bits > 16)
        {
            return typeof(RgbaVector);
        }

        bool wide = bits > 8;
        if (gray)
        {
            return alpha ? (wide ? typeof(La32) : typeof(La16)) : (wide ? typeof(L16) : typeof(L8));
        }

        return alpha ? (wide ? typeof(Rgba64) : typeof(Rgba32)) : (wide ? typeof(Rgb48) : typeof(Rgb24));
    }

    private Image<TPixel> Decode<TPixel>(byte[] data, int frameOffset, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // The pixel cap is checked from the header, before the image is allocated.
        CheckPixelCap(this.metadata.XSize, this.metadata.YSize);
        JxlSampleBudget.CheckImage(this.metadata);

        // The image has the oriented size, as libjxl's output buffer when it undoes the orientation.
        Size size = this.OrientedSize;
        Image<TPixel> image = new(this.Configuration, size.Width, size.Height, this.imageMetadata);
        try
        {
            JxlImageFrameSink<TPixel> imageSink = new(this.Configuration, image, this.metadata.M, this.isCmyk);
            IJxlRenderSink sink = JxlOrientingSink.Create(this.Configuration.MemoryAllocator, this.metadata.M.GetOrientation(), imageSink);
            List<JxlFrameHeader> displayed = this.DecodeFrames(data, frameOffset, sink, cancellationToken);

            // JxlDecoderGetFrameHeader and JxlDecoderGetFrameName: the duration is 0 unless the image is
            // animated, as the frame header only stores it then. libjxl hands the name over as raw
            // bytes; as a string, bytes that are not UTF-8 become U+FFFD.
            for (int i = 0; i < displayed.Count; i++)
            {
                JxlFrameMetadata frameMetadata = image.Frames[i].Metadata.GetFormatMetadata(JxlFormat.Instance);
                frameMetadata.Duration = displayed[i].AnimationFrame.Duration;
                frameMetadata.Name = Encoding.UTF8.GetString(displayed[i].Name);
            }

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
        this.ReadHeaders(this.ReadCodestream(stream, cancellationToken), cancellationToken);
        JxlImageMetadata m = this.metadata.M;
        int bitsPerPixel = (int)m.BitDepth.BitsPerSample * (m.ColorEncoding.IsGray ? 1 : 3);
        foreach (JxlExtraChannelInfo eci in m.ExtraChannelInfo)
        {
            bitsPerPixel += (int)eci.BitDepth.BitsPerSample;
        }

        // The oriented size, as libjxl's JxlDecoderGetBasicInfo gives when it does not keep the orientation.
        Size size = this.OrientedSize;
        return new ImageInfo(new PixelTypeInfo(bitsPerPixel), size.Width, size.Height, this.imageMetadata);
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
        byte[] data = this.ReadCodestream(bufferedStream, cancellationToken);
        int frameOffset = this.ReadHeaders(data, cancellationToken);
        CheckPixelCap(this.metadata.XSize, this.metadata.YSize);
        JxlSampleBudget.CheckImage(this.metadata);

        // djxl orients its output, so the planes are oriented too.
        JxlPlanesSink planesSink = new();
        IJxlRenderSink sink = JxlOrientingSink.Create(this.Configuration.MemoryAllocator, this.metadata.M.GetOrientation(), planesSink);
        try
        {
            this.DecodeFrames(data, frameOffset, sink, cancellationToken);
            return planesSink.Image;
        }
        catch
        {
            planesSink.Image.Dispose();
            throw;
        }
    }

    // The frame loop of JxlDecoderProcessCodestream: frame after frame, each from where the previous
    // frame's sections end, until the last one. Frames are coalesced, as djxl does by default, so only
    // the displayed frames reach the sink. Returns the header of each displayed frame, which libjxl
    // reports with JXL_DEC_FRAME.
    //
    // MaxFrames has no libjxl counterpart. As in the GIF decoder, the loop stops once that many frames
    // are displayed, so a value of 0 never matches and every frame is decoded. The frames after the
    // last one decoded are not read at all.
    private List<JxlFrameHeader> DecodeFrames(byte[] data, int offset, IJxlRenderSink sink, CancellationToken cancellationToken)
    {
        using JxlReferenceFrames references = new();
        JxlFrameDecoder frameDecoder = new(this.Configuration, this.metadata, references);
        List<JxlFrameHeader> displayed = new();

        // The preview frame comes first. libjxl skips it when the preview is not wanted, as here: this
        // decoder has no output for the preview.
        if (this.metadata.M.HavePreview)
        {
            cancellationToken.ThrowIfCancellationRequested();
            offset = frameDecoder.SkipPreviewFrame(data, offset);
        }

        uint frameCount = 0;
        JxlFrameHeader frameHeader;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            frameHeader = frameDecoder.DecodeFrame(data, offset, sink, cancellationToken, out offset);
            if (JxlFrameDecoder.IsLastOfStill(frameHeader))
            {
                displayed.Add(frameHeader);
                if (++frameCount == this.maxFrames)
                {
                    break;
                }
            }
        }
        while (!frameHeader.IsLast);

        return displayed;
    }

    private static void CheckPixelCap(ulong xsize, ulong ysize)
    {
        if (xsize * ysize > JxlFrameDecoder.MaxPixelsPerFrame)
        {
            throw new InvalidMemoryOperationException($"The JPEG XL image is {xsize}x{ysize} pixels, more than the limit of {JxlFrameDecoder.MaxPixelsPerFrame}.");
        }
    }

    // Reads the whole file, and takes the codestream and the Exif and XMP out of a container. The
    // container read checks the token per box, and per buffer of a Brotli-compressed box (#293).
    private byte[] ReadCodestream(BufferedReadStream stream, CancellationToken cancellationToken)
    {
        byte[] data = ReadAll(stream);
        this.imageMetadata = new ImageMetadata();
        if (!data.AsSpan().StartsWith(JxlConstants.ContainerSignature))
        {
            return data;
        }

        JxlContainerContents contents = JxlContainer.Read(data, !this.ignoreMetadata, cancellationToken);
        if (contents.Exif != null)
        {
            // The decoder always undoes the codestream orientation, which overrides the Exif one, so
            // djxl's reset applies to every image. Identify gets it too, as it reports the oriented size.
            JxlExif.ResetOrientation(contents.Exif);
            this.imageMetadata.ExifProfile = new ExifProfile(contents.Exif);
        }

        if (contents.Xmp != null)
        {
            this.imageMetadata.XmpProfile = new XmpProfile(contents.Xmp);
        }

        return contents.Codestream;
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

    // The pixel-profile rules of issue #105, with #111's CMYK exception: the embedded ICC is always the
    // original profile, and it is the pixel profile only where the pixels are the coded samples. XYB
    // images decode to sRGB, and CMYK images are converted naively to RGB.
    private void SetIccProfile(IccProfile original)
    {
        if (this.ignoreMetadata)
        {
            return;
        }

        this.imageMetadata.GetFormatMetadata(JxlFormat.Instance).OriginalIccProfile = original;
        if (!this.metadata.M.XybEncoded && original.CheckIsValid() && !IsCmykProfile(original))
        {
            // Its own copy, so the two profiles share no bytes.
            this.imageMetadata.IccProfile = original.DeepClone();
        }
    }

    // libjxl's ColorEncoding::IsCMYK: the profile's data colour space is CMYK.
    private static bool IsCmykProfile(IccProfile profile)
        => profile.CheckIsValid() && profile.Header.DataColorSpace == IccColorSpaceType.Cmyk;

    // Whether the samples are CMYK, which the image sink converts to RGB. libjxl's IsCMYK looks at the
    // profile alone, and its CMS takes K from the first extra channel. This decoder also requires the
    // Black channel the spec gives such an image (lib/jxl/cms/color_encoding_cms.h), and takes K from
    // it wherever it is. libjxl has no check that rejects a CMYK profile without one, so neither has
    // this decoder: the samples are then the three colour channels.
    private static bool IsCmyk(JxlImageMetadata metadata, IccProfile profile)
        => IsCmykProfile(profile) && metadata.Find(JxlExtraChannelType.Black) >= 0;

    // The rest of #105's rules: sRGB, and grey with the sRGB curve, get no pixel profile; every other
    // colour encoding gets a built one. libjxl builds a profile for every colour encoding as it reads the header and
    // rejects the image when it cannot, so the profile is built, and checked, even when it is not kept.
    // An XYB image whose encoding the XYB stage cannot output renders to linear sRGB, which the image
    // sink encodes as sRGB, so its pixels get no profile either: as for an ICC profile, the pixel profile
    // only ever describes the pixels (ADR 0002).
    private void SetColorEncodingProfile(JxlColorEncodingHeader c)
    {
        IccProfile profile = JxlIccBuilder.Create(c);
        bool srgbCurve = !c.Tf.HaveGamma && c.Tf.TransferFunction == JxlTransferFunction.Srgb && c.WhitePoint == JxlWhitePoint.D65;
        bool srgb = srgbCurve && (c.IsGray || (c.ColorSpace == JxlColorSpace.Rgb && c.Primaries == JxlPrimaries.Srgb));
        if (!this.ignoreMetadata && !srgb && JxlOutputEncoding.IsOriginalOutput(this.metadata.M))
        {
            this.imageMetadata.IccProfile = profile;
        }
    }

    // The description of every extra channel, which issue #111 adds to JxlMetadata. libjxl hands the
    // name over as raw bytes (JxlDecoderGetExtraChannelName); as a string, bytes that are not UTF-8
    // become U+FFFD.
    private static JxlExtraChannel[] DescribeExtraChannels(List<JxlExtraChannelInfo> extraChannelInfo)
    {
        JxlExtraChannel[] extraChannels = new JxlExtraChannel[extraChannelInfo.Count];
        for (int i = 0; i < extraChannels.Length; i++)
        {
            JxlExtraChannelInfo eci = extraChannelInfo[i];
            JxlBitDepth bitDepth = eci.BitDepth;
            extraChannels[i] = new JxlExtraChannel(eci.Type, Encoding.UTF8.GetString(eci.Name), bitDepth.BitsPerSample, bitDepth.ExponentBitsPerSample, bitDepth.FloatingPointSample);
        }

        return extraChannels;
    }

    // Reads the signature, size header, image metadata, transform data and ICC profile, and returns
    // the offset of the first frame. libjxl has no cancellation; the token is ImageSharp's (#293). It is
    // checked once here, so Identify honours it too, and the ICC decode checks it as it goes.
    private int ReadHeaders(byte[] data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        JxlMetadata jxlMetadata = this.imageMetadata.GetFormatMetadata(JxlFormat.Instance);
        jxlMetadata.IsXybEncoded = codecMetadata.M.XybEncoded;
        jxlMetadata.BitsPerSample = codecMetadata.M.BitDepth.BitsPerSample;
        jxlMetadata.ExponentBitsPerSample = codecMetadata.M.BitDepth.ExponentBitsPerSample;
        jxlMetadata.IntensityTarget = codecMetadata.M.ToneMapping.IntensityTarget;
        jxlMetadata.ExtraChannels = DescribeExtraChannels(codecMetadata.M.ExtraChannelInfo);
        if (codecMetadata.M.HaveAnimation)
        {
            // As JxlDecoderGetBasicInfo, which leaves the animation fields 0 for a still image. A departure:
            // LoopCount is an int (issue #108), so a loop count past int.MaxValue, which libjxl keeps as a
            // uint32, saturates at int.MaxValue.
            JxlAnimationHeader animation = codecMetadata.M.Animation;
            jxlMetadata.LoopCount = (int)Math.Min(animation.NumLoops, int.MaxValue);
            jxlMetadata.TicksPerSecondNumerator = animation.TpsNumerator;
            jxlMetadata.TicksPerSecondDenominator = animation.TpsDenominator;
        }

        if (codecMetadata.M.ColorEncoding.WantIcc)
        {
            IccProfile original = new(JxlIccReader.Read(reader, cancellationToken));
            this.isCmyk = IsCmyk(codecMetadata.M, original);
            this.SetIccProfile(original);
        }
        else
        {
            this.isCmyk = false;
            jxlMetadata.ColorEncoding = new JxlColorEncoding(codecMetadata.M.ColorEncoding);
            this.SetColorEncodingProfile(codecMetadata.M.ColorEncoding);
        }

        reader.JumpToByteBoundary();
        reader.EnsureAllReadsWithinBounds("codestream headers");
        long headerBits = reader.TotalBitsConsumed;
        reader.Close();

        return signatureLength + (int)(headerBits / 8);
    }
}
