// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <summary>
/// The sample budget (#292): a frame's pixels times its colour and extra channels must not pass
/// <see cref="MaxSamples"/>. Colour planes count at the upsampled frame size, and each extra channel at
/// its own size, as the Modular decoder sizes it. Each check comes with the pixel cap, before anything is
/// allocated, so the count cannot overflow.
/// </summary>
/// <remarks>
/// A departure from libjxl, which has no such cap: an image can have 256 extra channels, so the pixel cap
/// alone lets a frame need far more memory than any machine has, and the
/// <see cref="OutOfMemoryException"/> would escape. Issue #112 allows only
/// <see cref="InvalidImageContentException"/>, <see cref="InvalidMemoryOperationException"/> and
/// <see cref="OperationCanceledException"/> to escape.
/// </remarks>
internal static class JxlSampleBudget
{
    /// <summary>The largest frame, in samples: the pixel cap times four (RGBA).</summary>
    public const long MaxSamples = JxlFrameDecoder.MaxPixelsPerFrame * 4;

    /// <summary>
    /// Checks a frame at its own size: the colour planes it decodes at the upsampled frame size, and each
    /// extra channel at that size divided by its upsampling, which includes its <c>dim_shift</c>.
    /// </summary>
    /// <param name="frameDim">The frame dimensions, inside the pixel cap.</param>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="numExtraChannels">The number of extra channels of the image.</param>
    public static void CheckFrame(JxlFrameDimensions frameDim, JxlFrameHeader frameHeader, int numExtraChannels)
    {
        long xsize = frameDim.XSizeUpsampled;
        long ysize = frameDim.YSizeUpsampled;
        List<uint> extraChannelUpsampling = frameHeader.ExtraChannelUpsampling;
        long samples = xsize * ysize * frameHeader.NumDecodedColorPlanes;
        for (int i = 0; i < numExtraChannels; i++)
        {
            // A missing entry counts as 1.
            samples += ExtraChannelSamples(xsize, ysize, i < extraChannelUpsampling.Count ? extraChannelUpsampling[i] : 1);
        }

        ThrowIfOver(samples, "frame");
    }

    /// <summary>
    /// Checks a full-size frame from the image header, as the pixel cap is, so that the image is not
    /// allocated for a frame that would fail the budget. Each extra channel counts at the image size shrunk
    /// by its <c>dim_shift</c>. <see cref="CheckFrame"/> checks each frame again at its own size.
    /// </summary>
    /// <param name="metadata">The codec metadata, inside the pixel cap.</param>
    public static void CheckImage(JxlCodecMetadata metadata)
    {
        long xsize = (long)metadata.XSize;
        long ysize = (long)metadata.YSize;
        List<JxlExtraChannelInfo> extraChannels = metadata.M.ExtraChannelInfo;
        long samples = xsize * ysize * metadata.M.NumColorPlanes;
        foreach (JxlExtraChannelInfo extraChannel in extraChannels)
        {
            samples += ExtraChannelSamples(xsize, ysize, 1u << (int)extraChannel.DimShift);
        }

        ThrowIfOver(samples, "image");
    }

    private static long ExtraChannelSamples(long xsize, long ysize, uint upsampling)
        => JxlBits.DivCeil(xsize, upsampling) * JxlBits.DivCeil(ysize, upsampling);

    // what names the checked thing in the message: "frame" or "image".
    private static void ThrowIfOver(long samples, string what)
    {
        if (samples > MaxSamples)
        {
            throw new InvalidMemoryOperationException($"The JPEG XL {what} has {samples} samples, more than the limit of {MaxSamples}.");
        }
    }
}
