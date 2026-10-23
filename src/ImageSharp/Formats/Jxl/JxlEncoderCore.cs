// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/encode.cc (JxlEncoderSetBasicInfo, JxlEncoderSetColorEncoding, JxlEncoderSetICCProfile,
// JxlEncoderSetFrameDistance, and the
// codestream header write of JxlEncoder::ProcessOneEnqueuedInput), lib/jxl/color_encoding_internal.h
// (ColorEncoding::SetICCRaw), lib/jxl/color_encoding_internal.cc (ColorEncoding::SRGB) and
// lib/jxl/luminance.cc (SetIntensityTarget).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Icc;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Writes an image as a bare JPEG XL codestream.
/// </summary>
internal sealed class JxlEncoderCore : IImageEncoderInternals
{
    // libjxl's largest distance (JxlEncoderSetFrameDistance).
    private const float MaxDistance = 25.0f;

    private readonly MemoryAllocator memoryAllocator;
    private readonly float distance;
    private readonly JxlEffort effort;
    private readonly JxlEncoderTestHooks testHooks;

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
        this.testHooks = options.TestHooks;
    }

    /// <inheritdoc/>
    public void Encode<TPixel>(Image<TPixel> image, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(image, nameof(image));
        Guard.NotNull(stream, nameof(stream));
        this.CheckOptions();
        cancellationToken.ThrowIfCancellationRequested();

        // Taken before the encode, so that a box over the decoder's cap throws before any work is done.
        JxlMetadataBoxes boxes = JxlMetadataBoxes.Create(image.Metadata);

        bool lossy = this.distance != 0;
        JxlCodecMetadata metadata = CreateMetadata<TPixel>(image, lossy);

        // Lossless embeds a matching profile verbatim; lossy writes the colour encoding it recognises.
        byte[] icc = lossy
            ? SetLossyColorEncoding(metadata.M.ColorEncoding, image.Metadata.IccProfile)
            : SetIccProfile(metadata.M.ColorEncoding, image.Metadata.IccProfile);

        // Both modes: libjxl's JxlEncoderSetBasicInfo takes intensity_target whether or not the frame is
        // lossless, and JxlEncoderSetColorEncoding / JxlEncoderSetICCProfile fall back to SetIntensityTarget.
        SetIntensityTarget(metadata.M, image.Metadata.GetFormatMetadata(JxlFormat.Instance).IntensityTarget);

        // The whole codestream is built in memory, so nothing reaches the stream if encoding fails.
        JxlBitWriter writer = new();
        JxlBundle.WriteCodestreamHeaders(metadata, writer);

        // Only send ICC (at least several hundred bytes) if fields aren't enough.
        if (metadata.M.ColorEncoding.WantIcc)
        {
            JxlIccWriter.WriteIcc(icc, writer);
        }

        writer.ZeroPadToByte();

        // In a container, the codestream splits here into the header's jxlp box and the frame's.
        int headerSize = (int)(writer.BitsWritten / 8);

        if (lossy)
        {
            JxlFrameEncoder.EncodeLossyFrame(metadata, image, ToSpeedTier(this.effort), this.distance, this.testHooks, this.memoryAllocator, writer);
        }
        else
        {
            JxlFrameEncoder.EncodeFrame(metadata, image, ToSpeedTier(this.effort), this.memoryAllocator, writer);
        }

        JxlContainerWriter.Write(stream, writer.GetSpan(), headerSize, boxes);
    }

    // libjxl's JxlEncoderSetBasicInfo and JxlEncoderSetColorEncoding: the image's own colour space,
    // XYB-encoded for a lossy image (libjxl's uses_original_profile is off), the sample depth, and
    // straight alpha at the colour depth when the pixel type has alpha. The depth is the one JxlMetadata
    // gives, as cjxl keeps the depth of the file it reads, when it holds every sample losslessly;
    // otherwise it is the depth of the pixel type (see JxlEncoderInput.ChooseBitDepth). For a lossy
    // image the depth is only metadata.
    private static JxlCodecMetadata CreateMetadata<TPixel>(Image<TPixel> image, bool lossy)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        JxlCodecMetadata metadata = new();
        metadata.Size.Set((ulong)image.Width, (ulong)image.Height);

        JxlMetadata jxlMetadata = image.Metadata.GetFormatMetadata(JxlFormat.Instance);
        JxlBitDepth requested = jxlMetadata.BitsPerSample == 0 ? null : CreateRequestedBitDepth<TPixel>(jxlMetadata);
        JxlBitDepth bitDepth = JxlEncoderInput<TPixel>.ChooseBitDepth(image, requested);

        JxlImageMetadata m = metadata.M;
        SetBitDepth(m.BitDepth, bitDepth);

        // Lossless sets libjxl's uses_original_profile (the samples are not XYB-encoded), so a 16-bit
        // buffer is enough up to 12 bits. XYB samples always fit, so for lossy only alpha counts.
        m.Modular16BitBufferSufficient = lossy || bitDepth.BitsPerSample <= 12;
        if (JxlEncoderInput<TPixel>.HasAlpha)
        {
            JxlExtraChannelInfo alpha = new()
            {
                Type = JxlExtraChannelType.Alpha,
                AlphaAssociated = false,
            };
            SetBitDepth(alpha.BitDepth, bitDepth);
            m.ExtraChannelInfo.Add(alpha);
            m.Modular16BitBufferSufficient &= bitDepth.BitsPerSample <= 12;
        }

        m.XybEncoded = lossy;
        SetColorEncoding(m.ColorEncoding, JxlEncoderInput<TPixel>.IsGray);
        return metadata;
    }

    // The public depth is an int (issue #108), and the bundle's a uint, as libjxl's. A negative depth is
    // invalid, as one that CheckValidBitdepth rejects is.
    private static JxlBitDepth CreateRequestedBitDepth<TPixel>(JxlMetadata jxlMetadata)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int bits = jxlMetadata.BitsPerSample;
        int exponentBits = jxlMetadata.ExponentBitsPerSample;
        if (bits < 0 || exponentBits < 0)
        {
            JxlThrowHelper.ThrowImageFormatException($"Invalid JPEG XL bit depth: {bits} bits with {exponentBits} exponent bits per sample.");
        }

        return JxlEncoderInput<TPixel>.CreateBitDepth((uint)bits, (uint)exponentBits);
    }

    private static void SetBitDepth(JxlBitDepth bitDepth, JxlBitDepth source)
    {
        bitDepth.BitsPerSample = source.BitsPerSample;
        bitDepth.ExponentBitsPerSample = source.ExponentBitsPerSample;
        bitDepth.FloatingPointSample = source.FloatingPointSample;
    }

    // The pixel profile, as libjxl's JxlEncoderSetICCProfile: the only colour input on encode (#115),
    // embedded verbatim when its data colour space matches the pixels, RGB for colour and grey for grey.
    // Returns the profile bytes, or null when the colour encoding keeps the sRGB enum.
    // libjxl has two paths here. With a CMS, SetICC parses the profile and sets the colour fields from
    // it; without one, SetICCRaw keeps the fields it has. Lossless takes the second (#115): the fields keep
    // the enum of SetColorEncoding, which already gives the colour space of a matching profile, and the
    // intensity target is the SDR default that libjxl's SetIntensityTarget gives those fields. Lossless
    // never turns the profile back into an enum (DecideIfWantICC runs only without uses_original_profile).
    // Departure (ADR 0001): libjxl returns an error for a profile that does not match the channel
    // count; this port ignores such a profile and writes the enum (#115).
    private static byte[] SetIccProfile(JxlColorEncodingHeader colorEncoding, IccProfile profile)
    {
        if (!IsPixelProfile(colorEncoding, profile))
        {
            return null;
        }

        colorEncoding.WantIcc = true;
        return profile.ToByteArray();
    }

    // The lossy colour rules of #115, libjxl's JxlEncoderSetICCProfile with a CMS and then DecideIfWantICC:
    // a matching profile that TryFromIccProfile recognises is written as its colour encoding, never as the
    // ICC, and the pixels reach XYB through it. Unlike libjxl, which keeps an ICC it cannot describe and
    // encodes through its CMS, a profile that is not recognised throws: this library has no CMS (#115).
    // Returns null: a lossy file never has an ICC.
    private static byte[] SetLossyColorEncoding(JxlColorEncodingHeader colorEncoding, IccProfile profile)
    {
        if (!IsPixelProfile(colorEncoding, profile))
        {
            return null;
        }

        if (!JxlColorEncodingHeader.TryFromIccProfile(profile, out JxlColorEncodingHeader recognised))
        {
            throw new NotSupportedException("lossy JPEG XL needs an ICC profile the encoder can describe; use Distance = 0 or convert to sRGB first");
        }

        colorEncoding.CopyFrom(recognised);
        return null;
    }

    // Whether the profile describes the pixels: it is there, and its data colour space is RGB for colour and
    // grey for grey. A profile that is not valid throws ImageFormatException, the same as one that libjxl's
    // ICC reader would refuse (JxlIccWriter): libjxl's JxlEncoderSetICCProfile returns an error for a
    // profile its CMS cannot parse, and a profile that fails IccProfile.CheckIsValid counts as one.
    private static bool IsPixelProfile(JxlColorEncodingHeader colorEncoding, IccProfile profile)
    {
        if (profile is null)
        {
            return false;
        }

        if (!profile.CheckIsValid())
        {
            JxlThrowHelper.ThrowImageFormatException("The ICC profile cannot be embedded: it is not a valid ICC profile.");
        }

        IccColorSpaceType pixelColorSpace = colorEncoding.ColorSpace == JxlColorSpace.Gray ? IccColorSpaceType.Gray : IccColorSpaceType.Rgb;
        return profile.Header.DataColorSpace == pixelColorSpace;
    }

    // libjxl's JxlEncoderSetBasicInfo with an intensity target, or else SetIntensityTarget: 10,000 nits
    // for PQ (the peak of SMPTE ST 2084), 1,000 for HLG (BT.2100's nominal display peak) and 255 for SDR.
    // #115 leaves HLG out; this follows libjxl. The header stores it as a half float.
    private static void SetIntensityTarget(JxlImageMetadata m, float requested)
    {
        if (requested != 0)
        {
            if (!(requested > 0 && requested <= 65504))
            {
                JxlThrowHelper.ThrowImageFormatException($"JPEG XL intensity target {requested} is out of range: it has to be in (0..65504].");
            }

            m.ToneMapping.IntensityTarget = requested;
            return;
        }

        JxlCustomTransferFunction tf = m.ColorEncoding.Tf;
        m.ToneMapping.IntensityTarget = tf.IsPq ? 10000 : tf.IsHlg ? 1000 : 255;
    }

    // The colour encoding: sRGB, or grey with the sRGB white point and transfer function, as libjxl's
    // ColorEncoding::SRGB(is_gray). SetIccProfile then adds a matching pixel profile.
    private static void SetColorEncoding(JxlColorEncodingHeader colorEncoding, bool isGray)
    {
        colorEncoding.WantIcc = false;
        colorEncoding.ColorSpace = isGray ? JxlColorSpace.Gray : JxlColorSpace.Rgb;
        colorEncoding.WhitePoint = JxlWhitePoint.D65;
        colorEncoding.Primaries = JxlPrimaries.Srgb;
        colorEncoding.Tf.HaveGamma = false;
        colorEncoding.Tf.TransferFunction = JxlTransferFunction.Srgb;
        colorEncoding.RenderingIntent = JxlRenderingIntent.Relative;
    }

    /// <summary>
    /// Returns the libjxl speed tier of <paramref name="effort"/>, the one place the encoder maps the
    /// two: Fastest is libjxl's effort 3, Default effort 5 and Thorough effort 7. Every stage is then
    /// gated by the speed tier, as libjxl gates it.
    /// </summary>
    /// <param name="effort">The effort.</param>
    /// <returns>The speed tier.</returns>
    internal static JxlSpeedTier ToSpeedTier(JxlEffort effort) => effort switch
    {
        JxlEffort.Fastest => JxlSpeedTier.Falcon,
        JxlEffort.Default => JxlSpeedTier.Hare,
        JxlEffort.Thorough => JxlSpeedTier.Squirrel,
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, "The effort is not one that JxlEffort defines."),
    };

    // Lossless and lossy are ported at every effort. The distance must be in libjxl's range
    // (JxlEncoderSetFrameDistance), and the effort one that JxlEffort defines: libjxl's
    // JXL_ENC_FRAME_SETTING_EFFORT rejects an effort outside its range too.
    private void CheckOptions()
    {
        if (!(this.distance >= 0 && this.distance <= MaxDistance))
        {
            JxlThrowHelper.ThrowImageFormatException($"JPEG XL distance {this.distance} is out of range: it has to be in [0.0..25.0].");
        }

        if (this.effort is < JxlEffort.Fastest or > JxlEffort.Thorough)
        {
            JxlThrowHelper.ThrowImageFormatException($"JPEG XL effort {(int)this.effort} is not defined: it has to be Fastest, Default or Thorough.");
        }
    }
}
