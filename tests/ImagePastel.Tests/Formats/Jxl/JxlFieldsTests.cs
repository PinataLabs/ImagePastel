// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

[Trait("Format", "Jxl")]
public class JxlFieldsTests
{
    [Fact]
    public void ImageMetadata_Defaults()
    {
        JxlImageMetadata metadata = new();

        Assert.Equal(1u, metadata.Orientation);
        Assert.False(metadata.BitDepth.FloatingPointSample);
        Assert.Equal(8u, metadata.BitDepth.BitsPerSample);
        Assert.Equal(0u, metadata.BitDepth.ExponentBitsPerSample);
        Assert.True(metadata.Modular16BitBufferSufficient);
        Assert.True(metadata.XybEncoded);
        Assert.Equal(0, metadata.NumExtraChannels);
        Assert.False(metadata.HavePreview);
        Assert.False(metadata.HaveAnimation);
        Assert.Equal(255f, metadata.ToneMapping.IntensityTarget);
        AssertSrgb(metadata.ColorEncoding);
    }

    [Fact]
    public void ImageMetadata_ReadFromAllDefaultBit_HasDefaults()
    {
        JxlImageMetadata metadata = new();

        JxlBundle.Read(new JxlTestBitWriter().Bit(true).ToReader(), metadata);

        Assert.True(metadata.XybEncoded);
        Assert.Equal(8u, metadata.BitDepth.BitsPerSample);
        AssertSrgb(metadata.ColorEncoding);
    }

    [Fact]
    public void FrameHeader_Defaults()
    {
        JxlFrameHeader header = new(null);

        Assert.Equal(JxlFrameType.Regular, header.FrameType);
        Assert.Equal(JxlFrameEncoding.VarDct, header.Encoding);
        Assert.Equal(JxlColorTransform.Xyb, header.ColorTransform);
        Assert.Equal(JxlFrameFlags.None, header.Flags);
        Assert.Equal(1u, header.Upsampling);
        Assert.Equal(1u, header.GroupSizeShift);
        Assert.Equal(1u, header.Passes.NumPasses);
        Assert.Equal(JxlBlendMode.Replace, header.BlendingInfo.Mode);
        Assert.True(header.IsLast);
        Assert.False(header.CustomSizeOrOrigin);
        Assert.True(header.LoopFilter.Gab);
        Assert.Equal(2u, header.LoopFilter.EpfIters);
    }

    [Fact]
    public void ExtraChannelInfo_Defaults()
    {
        JxlExtraChannelInfo info = new();

        Assert.Equal(JxlExtraChannelType.Alpha, info.Type);
        Assert.Equal(8u, info.BitDepth.BitsPerSample);
        Assert.Equal(0u, info.DimShift);
        Assert.False(info.AlphaAssociated);
        Assert.Equal(0, info.Name.Length);
    }

    [Fact]
    public void SizeHeader_SmallSize_ReadsMultiplesOf8()
    {
        // small = 1, ysize_div8_minus_1 = 3, ratio = 0, xsize_div8_minus_1 = 1.
        JxlBitReader reader = new JxlTestBitWriter().Bit(true).Write(5, 3).Write(3, 0).Write(5, 1).ToReader();
        JxlSizeHeader size = new();

        JxlBundle.Read(reader, size);

        Assert.Equal(16UL, size.XSize);
        Assert.Equal(32UL, size.YSize);
    }

    [Fact]
    public void SizeHeader_Ratio_DerivesTheWidth()
    {
        // small = 0, ysize = BitsOffset(9, 1) with 299, ratio = 5 (16:9).
        JxlBitReader reader = new JxlTestBitWriter().Bit(false).Write(2, 0).Write(9, 299).Write(3, 5).ToReader();
        JxlSizeHeader size = new();

        JxlBundle.Read(reader, size);

        Assert.Equal(300UL, size.YSize);
        Assert.Equal(533UL, size.XSize);
    }

    [Fact]
    public void Read_InvalidEnumValue_ThrowsInvalidImageContent()
    {
        // ColorEncoding: not all default, no ICC, colour space 2 + 15 = 17 (BitsOffset(4, 2)), which is undefined.
        JxlBitReader reader = new JxlTestBitWriter().Bit(false).Bit(false).Write(2, 2).Write(4, 15).ToReader();

        Assert.Throws<InvalidImageContentException>(() => JxlBundle.Read(reader, new JxlColorEncodingHeader()));
    }

    [Fact]
    public void Read_Truncated_ThrowsInvalidImageContent()
    {
        // small = 0 and a selector, then nothing.
        JxlBitReader reader = new(new byte[] { 0b0000_0110 });

        Assert.Throws<InvalidImageContentException>(() => JxlBundle.Read(reader, new JxlImageMetadata()));
    }

    [Theory]
    [InlineData(0x3C00, 1.0f)]
    [InlineData(0xC000, -2.0f)]
    [InlineData(0x0000, 0.0f)]
    [InlineData(0x0001, 5.9604645E-08f)]
    [InlineData(0x7BFF, 65504.0f)]
    public void F16_DecodesHalfPrecision(int bits, float expected)
    {
        Assert.True(JxlFieldCoders.TryReadF16(new JxlTestBitWriter().Write(16, (ulong)bits).ToReader(), out float value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(0x7C00)]
    [InlineData(0x7E00)]
    public void F16_InfinityOrNaN_IsInvalid(int bits)
        => Assert.False(JxlFieldCoders.TryReadF16(new JxlTestBitWriter().Write(16, (ulong)bits).ToReader(), out _));

    [Theory]
    [InlineData(0, 0UL)]
    [InlineData(1, 1UL + 9)]
    [InlineData(2, 17UL + 200)]
    public void U64_ReadsEachSelector(int selector, ulong expected)
    {
        JxlTestBitWriter writer = new JxlTestBitWriter().Write(2, (ulong)selector);
        if (selector == 1)
        {
            writer.Write(4, 9);
        }
        else if (selector == 2)
        {
            writer.Write(8, 200);
        }

        Assert.Equal(expected, JxlFieldCoders.ReadU64(writer.ToReader()));
    }

    [Fact]
    public void U64_ReadsTheVarint()
    {
        // Selector 3: 12 bits, then a continuation bit and 8 more bits, then a stop bit.
        JxlBitReader reader = new JxlTestBitWriter().Write(2, 3).Write(12, 0xABC).Bit(true).Write(8, 0x5D).Bit(false).ToReader();

        Assert.Equal(0x5DABCUL, JxlFieldCoders.ReadU64(reader));
    }

    private static void AssertSrgb(JxlColorEncodingHeader colorEncoding)
    {
        Assert.False(colorEncoding.WantIcc);
        Assert.Equal(JxlColorSpace.Rgb, colorEncoding.ColorSpace);
        Assert.Equal(JxlWhitePoint.D65, colorEncoding.WhitePoint);
        Assert.Equal(JxlPrimaries.Srgb, colorEncoding.Primaries);
        Assert.False(colorEncoding.Tf.HaveGamma);
        Assert.Equal(JxlTransferFunction.Srgb, colorEncoding.Tf.TransferFunction);
        Assert.Equal(JxlRenderingIntent.Relative, colorEncoding.RenderingIntent);
    }
}
