// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The sample budget (issue #292): a frame's pixels times its colour and extra channels must not pass
/// 2^30 samples. Each stream is a hand-built header, so the budget throws before anything is allocated;
/// a stream inside the budget fails later, on its missing TOC or frame.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlSampleBudgetTests
{
    // An 8192 x 8192 frame is 2^26 pixels, inside the 2^28-pixel cap. With three colour planes, 13 extra
    // channels make exactly 2^30 samples and 14 make one plane too many.
    [Theory]
    [InlineData(13, false)]
    [InlineData(14, true)]
    public void Decode_FrameOverTheSampleBudget_ThrowsInvalidMemoryOperation(int numExtraChannels, bool throws)
    {
        byte[] codestream = FrameCodestream(numExtraChannels, dimShift: 0, extraChannelUpsampling: 1);

        AssertBudget(throws, () => Decode(codestream));
        AssertBudget(throws, () => DecodeToPlanes(codestream));
    }

    // An extra channel counts at its own size: halved on each axis, by its dim_shift or by its frame
    // upsampling, it is 2^24 samples. 3 * 2^26 + 52 * 2^24 is exactly 2^30.
    [Theory]
    [InlineData(52, 1, 1u, false)]
    [InlineData(53, 1, 1u, true)]
    [InlineData(52, 0, 2u, false)]
    [InlineData(53, 0, 2u, true)]
    [InlineData(256, 3, 1u, false)]
    [InlineData(256, 0, 8u, false)]
    public void Decode_ExtraChannelsCountAtTheirOwnSize(int numExtraChannels, int dimShift, uint extraChannelUpsampling, bool throws)
    {
        byte[] codestream = FrameCodestream(numExtraChannels, dimShift, extraChannelUpsampling);

        AssertBudget(throws, () => Decode(codestream));
    }

    // The image header is checked too, as the pixel cap is, before the image is allocated: an 8192 x
    // 8192 image with 14 extra channels throws with no frame at all.
    [Theory]
    [InlineData(13, false)]
    [InlineData(14, true)]
    public void DecodeToPlanes_ImageOverTheSampleBudget_ThrowsInvalidMemoryOperation(int numExtraChannels, bool throws)
        => AssertBudget(throws, () => DecodeToPlanes(ImageCodestream(numExtraChannels)));

    [Fact]
    public void Decode_ImageOverTheSampleBudget_ThrowsBeforeAllocating()
    {
        byte[] codestream = ImageCodestream(14);

        Assert.Throws<InvalidMemoryOperationException>(() => Decode(codestream));
        Assert.Throws<InvalidMemoryOperationException>(() =>
        {
            using MemoryStream stream = new(codestream);
            using Image image = new JxlDecoder().Decode(Configuration.Default, stream, CancellationToken.None);
        });
    }

    [Fact]
    public void SampleBudget_DoesNotApplyToIdentify()
    {
        using MemoryStream stream = new(ImageCodestream(14));

        IImageInfo info = new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken.None);

        Assert.Equal(8192, info.Width);
        Assert.Equal(8192, info.Height);
    }

    // An 8192 x 8192 image header with no frame.
    private static byte[] ImageCodestream(int numExtraChannels)
    {
        JxlTestBitWriter writer = new JxlTestBitWriter().Write(8, 0xFF).Write(8, 0x0A);
        writer.Bit(false).Write(2, 1).Write(13, 8191).Write(3, 1); // 8192 high, BitsOffset(13, 1); ratio 1:1
        WriteImageMetadata(writer, numExtraChannels, dimShift: 0);
        return writer.ToArray();
    }

    // Each test writes an 8 x 8 XYB-encoded image with 8-bit alpha extra channels, then the header of one
    // Modular frame of the given size, with no TOC.
    private static byte[] FrameCodestream(int numExtraChannels, int dimShift, uint extraChannelUpsampling, int frameSize = 8192)
    {
        JxlTestBitWriter writer = new JxlTestBitWriter().Write(8, 0xFF).Write(8, 0x0A);
        writer.Bit(true).Write(5, 0).Write(3, 1); // 8 x 8
        WriteImageMetadata(writer, numExtraChannels, dimShift);

        writer.Bit(false).Write(2, 0).Bit(true).Write(2, 0); // not all default, regular, Modular, no flags
        writer.Write(2, 0); // colour upsampling 1
        ulong extraChannelSelector = extraChannelUpsampling switch { 1 => 0UL, 2 => 1UL, 4 => 2UL, _ => 3UL };
        for (int i = 0; i < numExtraChannels; i++)
        {
            writer.Write(2, extraChannelSelector);
        }

        writer.Write(2, 1).Write(2, 0); // 256 x 256 groups, one pass
        writer.Bit(true).Write(2, 0).Write(8, 0).Write(2, 0).Write(8, 0); // custom size, origin (0, 0)
        WriteCrop(writer, (uint)frameSize);
        WriteCrop(writer, (uint)frameSize);
        for (int i = 0; i <= numExtraChannels; i++)
        {
            writer.Write(2, 0); // replace, for the colour and each extra channel
        }

        writer.Bit(true).Write(2, 0); // last, no name
        writer.Bit(false).Bit(false).Write(2, 0).Write(2, 0); // loop filter: no Gaborish, no EPF, no extensions
        writer.Write(2, 0); // no extensions
        return writer.ToArray();
    }

    private static void WriteImageMetadata(JxlTestBitWriter writer, int numExtraChannels, int dimShift)
    {
        writer.Bit(false).Bit(false); // image metadata: not all default, no extra fields
        writer.Bit(false).Write(2, 0).Bit(true); // 8-bit integer samples, 16-bit buffers suffice
        writer.Write(2, 3).Write(12, (ulong)(numExtraChannels - 1));
        for (int i = 0; i < numExtraChannels; i++)
        {
            if (dimShift == 0)
            {
                writer.Bit(true); // all default: 8-bit straight alpha
            }
            else
            {
                writer.Bit(false).Write(2, 0).Bit(false).Write(2, 0); // alpha, 8-bit integer samples
                writer.Write(2, 3).Write(3, (ulong)(dimShift - 1)); // dim_shift, BitsOffset(3, 1)
                writer.Write(2, 0).Bit(false); // empty name, not premultiplied
            }
        }

        writer.Bit(true).Bit(true).Write(2, 0); // XYB, default colour encoding, no extensions
        writer.Bit(true); // all-default transform data
        writer.ZeroPadToByte();
    }

    // U32(Bits(8), BitsOffset(11, 256), BitsOffset(14, 2304), BitsOffset(30, 18688)).
    private static void WriteCrop(JxlTestBitWriter writer, uint value)
    {
        if (value < 256)
        {
            writer.Write(2, 0).Write(8, value);
        }
        else if (value < 2304)
        {
            writer.Write(2, 1).Write(11, value - 256);
        }
        else if (value < 18688)
        {
            writer.Write(2, 2).Write(14, value - 2304);
        }
        else
        {
            writer.Write(2, 3).Write(30, value - 18688);
        }
    }

    // Over the budget, the decode throws InvalidMemoryOperationException. Inside it, the stream still
    // ends too soon, so the decode throws InvalidImageContentException.
    private static void AssertBudget(bool throws, Action decode)
    {
        if (throws)
        {
            Assert.Throws<InvalidMemoryOperationException>(decode);
        }
        else
        {
            Assert.Throws<InvalidImageContentException>(decode);
        }
    }

    private static void Decode(byte[] codestream)
    {
        using MemoryStream stream = new(codestream);
        using Image<Rgb24> image = new JxlDecoder().Decode<Rgb24>(Configuration.Default, stream, CancellationToken.None);
    }

    private static void DecodeToPlanes(byte[] codestream)
    {
        using MemoryStream stream = new(codestream);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
    }
}
