// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Icc;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Writes an image as a bare JPEG XL codestream.
/// </summary>
internal sealed class JxlEncoderCore : IImageEncoderInternals
{
    private readonly MemoryAllocator memoryAllocator;
    private readonly float distance;
    private readonly JxlEffort effort;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlEncoderCore"/> class.
    /// </summary>
    /// <param name="options">The encoder options.</param>
    /// <param name="memoryAllocator">The memory allocator.</param>
    public JxlEncoderCore(IJxlEncoderOptions options, MemoryAllocator memoryAllocator)
    {
        this.memoryAllocator = memoryAllocator;
        this.distance = options.Distance;
        this.effort = options.Effort;
    }

    /// <inheritdoc/>
    public void Encode<TPixel>(Image<TPixel> image, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(image, nameof(image));
        Guard.NotNull(stream, nameof(stream));
        this.CheckOptions();
        cancellationToken.ThrowIfCancellationRequested();

        JxlCodecMetadata metadata = CreateMetadata<TPixel>(image);
        byte[] icc = SetIccProfile(metadata.M.ColorEncoding, image.Metadata.IccProfile);

        // The whole codestream is built in memory, so nothing reaches the stream if encoding fails.
        JxlBitWriter writer = new();
        JxlBundle.WriteCodestreamHeaders(metadata, writer);

        // Only send ICC (at least several hundred bytes) if fields aren't enough.
        if (metadata.M.ColorEncoding.WantIcc)
        {
            JxlIccWriter.WriteIcc(icc, writer);
        }

        writer.ZeroPadToByte();

        JxlFrameEncoder.EncodeFrame(metadata, image, this.memoryAllocator, writer);
        stream.Write(writer.GetSpan());
    }

    // libjxl's JxlEncoderSetBasicInfo and JxlEncoderSetColorEncoding for a lossless image: the
    // original colour space (no XYB), the sample depth, and straight alpha at the colour depth when the
    // pixel type has alpha. The depth is the one JxlMetadata gives, as cjxl keeps the depth of the file
    // it reads, or else the depth of the pixel type.
    private static JxlCodecMetadata CreateMetadata<TPixel>(Image<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        JxlCodecMetadata metadata = new();
        metadata.Size.Set((ulong)image.Width, (ulong)image.Height);

        JxlMetadata jxlMetadata = image.Metadata.GetFormatMetadata(JxlFormat.Instance);
        uint bitsPerSample = JxlEncoderInput<TPixel>.BitsPerSample;
        uint exponentBitsPerSample = JxlEncoderInput<TPixel>.ExponentBitsPerSample;
        if (jxlMetadata.BitsPerSample != 0)
        {
            bitsPerSample = jxlMetadata.BitsPerSample;
            exponentBitsPerSample = jxlMetadata.ExponentBitsPerSample;
        }

        JxlEncoderInput<TPixel>.CheckValidBitdepth(bitsPerSample, exponentBitsPerSample);

        JxlImageMetadata m = metadata.M;
        SetBitDepth(m.BitDepth, bitsPerSample, exponentBitsPerSample);

        // Lossless uses the original profile, so a 16-bit buffer is enough up to 12 bits.
        m.Modular16BitBufferSufficient = bitsPerSample <= 12;
        if (JxlEncoderInput<TPixel>.HasAlpha)
        {
            JxlExtraChannelInfo alpha = new()
            {
                Type = JxlExtraChannelType.Alpha,
                AlphaAssociated = false,
            };
            SetBitDepth(alpha.BitDepth, bitsPerSample, exponentBitsPerSample);
            m.ExtraChannelInfo.Add(alpha);
            m.Modular16BitBufferSufficient &= bitsPerSample <= 12;
        }

        m.XybEncoded = false;
        SetColorEncoding(m.ColorEncoding, JxlEncoderInput<TPixel>.IsGray);
        return metadata;
    }

    private static void SetBitDepth(JxlBitDepth bitDepth, uint bitsPerSample, uint exponentBitsPerSample)
    {
        bitDepth.BitsPerSample = bitsPerSample;
        bitDepth.ExponentBitsPerSample = exponentBitsPerSample;
        bitDepth.FloatingPointSample = exponentBitsPerSample != 0;
    }

    // The pixel profile, as libjxl's JxlEncoderSetICCProfile: the only colour input on encode (#115),
    // embedded verbatim when its data colour space matches the pixels, RGB for colour and grey for grey.
    // Returns the profile bytes, or null when the colour encoding keeps the sRGB enum.
    // libjxl has two paths here. With a CMS, SetICC parses the profile and sets the colour fields from
    // it; without one, SetICCRaw keeps the fields it has. This port has no CMS (#105), so it takes the
    // second: the fields keep the enum of SetColorEncoding, which already gives the colour space of a
    // matching profile, and the intensity target stays the SDR default that libjxl's SetIntensityTarget
    // gives those fields. Lossless never turns the profile back into an enum (DecideIfWantICC runs only
    // without uses_original_profile). Departure (ADR 0001): libjxl returns an error for a profile
    // that does not match the channel count, or that its CMS cannot parse; this port ignores such a
    // profile and writes the enum (#115). A profile that fails IccProfile.CheckIsValid counts as one
    // that does not parse.
    private static byte[] SetIccProfile(JxlColorEncoding colorEncoding, IccProfile profile)
    {
        IccColorSpaceType pixelColorSpace = colorEncoding.ColorSpace == JxlColorSpace.Gray ? IccColorSpaceType.Gray : IccColorSpaceType.Rgb;
        if (profile is null || !profile.CheckIsValid() || profile.Header.DataColorSpace != pixelColorSpace)
        {
            return null;
        }

        colorEncoding.WantIcc = true;
        return profile.ToByteArray();
    }

    // The colour encoding: sRGB, or grey with the sRGB white point and transfer function, as libjxl's
    // ColorEncoding::SRGB(is_gray). SetIccProfile then adds a matching pixel profile.
    private static void SetColorEncoding(JxlColorEncoding colorEncoding, bool isGray)
    {
        colorEncoding.WantIcc = false;
        colorEncoding.ColorSpace = isGray ? JxlColorSpace.Gray : JxlColorSpace.Rgb;
        colorEncoding.WhitePoint = JxlWhitePoint.D65;
        colorEncoding.Primaries = JxlPrimaries.Srgb;
        colorEncoding.Tf.HaveGamma = false;
        colorEncoding.Tf.TransferFunction = JxlTransferFunction.Srgb;
        colorEncoding.RenderingIntent = JxlRenderingIntent.Relative;
    }

    // Only lossless at Fastest is ported. Each other combination names the slice of the spec that adds it.
    private void CheckOptions()
    {
        if (this.distance != 0)
        {
            throw new NotSupportedException($"JPEG XL lossy encoding (distance {this.distance}) is not supported yet; slice E3 adds it.");
        }

        if (this.effort != JxlEffort.Fastest)
        {
            throw new NotSupportedException($"JPEG XL lossless encoding at effort {this.effort} is not supported yet; slice E2 adds it.");
        }
    }
}
