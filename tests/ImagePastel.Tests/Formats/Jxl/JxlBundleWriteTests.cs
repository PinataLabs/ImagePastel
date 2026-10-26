// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Entropy;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.Formats.Jxl.Modular;
using PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms;
using Xunit;
using static PinataLabs.ImagePastel.Formats.Jxl.Fields.JxlU32Distr;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Writes header bundles through <see cref="JxlBundle.Write"/> and reads them back through the decoder's
/// <see cref="JxlBundle.Read"/>.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlBundleWriteTests
{
    [Fact]
    public void SizeHeader_SmallSize_WritesTheSmallForm()
    {
        JxlSizeHeader size = new();
        size.Set(16, 32);

        byte[] bytes = Write(size);

        // small = 1, ysize_div8_minus_1 = 3, ratio = 0, xsize_div8_minus_1 = 1.
        Assert.Equal(new JxlTestBitWriter().Bit(true).Write(5, 3).Write(3, 0).Write(5, 1).ToArray(), bytes);
    }

    [Theory]
    [InlineData(8, 8)] // small, ratio 1:1
    [InlineData(256, 128)] // small, ratio 2:1
    [InlineData(1, 1)] // ratio 1:1, ysize selector 0
    [InlineData(5, 3)] // ysize selector 0, xsize selector 0
    [InlineData(600, 5000)] // selector 1
    [InlineData(10000, 200000)] // selector 2
    [InlineData(1u << 30, 300000)] // selector 3
    [InlineData(1u << 30, 1u << 30)] // ratio 1:1, the largest ysize
    [InlineData(533, 300)] // ratio 16:9
    public void SizeHeader_WriteThenRead_KeepsTheSize(uint xsize, uint ysize)
    {
        JxlSizeHeader size = new();
        size.Set(xsize, ysize);

        JxlSizeHeader read = RoundTrip(size, new JxlSizeHeader());

        Assert.Equal(xsize, read.XSize);
        Assert.Equal(ysize, read.YSize);
    }

    [Fact]
    public void ImageMetadata_AllDefault_WritesOneBit()
    {
        JxlBitWriter writer = new();

        JxlBundle.Write(new JxlImageMetadata(), writer);

        Assert.Equal(1, writer.BitsWritten);
        Assert.Equal(new byte[] { 0x01 }, writer.ToArray());
    }

    [Fact]
    public void ImageMetadata_NonDefault_RoundTrips()
    {
        JxlImageMetadata metadata = new()
        {
            Orientation = 6,
            HaveIntrinsicSize = true,
            HavePreview = true,
            HaveAnimation = true,
            Modular16BitBufferSufficient = false,
            XybEncoded = false,
        };
        metadata.IntrinsicSize.Set(640, 480);
        metadata.PreviewSize.Set(64, 48);
        metadata.Animation.TpsNumerator = 30000;
        metadata.Animation.TpsDenominator = 1001;
        metadata.Animation.NumLoops = 3;
        metadata.Animation.HaveTimecodes = true;
        metadata.BitDepth.FloatingPointSample = true;
        metadata.BitDepth.BitsPerSample = 16;
        metadata.BitDepth.ExponentBitsPerSample = 5;
        metadata.ColorEncoding.ColorSpace = JxlColorSpace.Gray;
        metadata.ColorEncoding.WhitePoint = JxlWhitePoint.Dci;
        metadata.ColorEncoding.Tf.TransferFunction = JxlTransferFunction.Linear;
        metadata.ColorEncoding.RenderingIntent = JxlRenderingIntent.Perceptual;
        metadata.ToneMapping.IntensityTarget = 1000;
        metadata.ToneMapping.MinNits = 0.5f;
        metadata.ToneMapping.RelativeToMaxDisplay = true;
        metadata.ToneMapping.LinearBelow = 0.25f;
        metadata.ExtraChannelInfo.Add(new JxlExtraChannelInfo { AlphaAssociated = true });
        metadata.ExtraChannelInfo.Add(new JxlExtraChannelInfo { Type = JxlExtraChannelType.Depth, DimShift = 2, Name = "depth"u8 });
        metadata.Extensions = 5;

        JxlImageMetadata read = RoundTrip(metadata, new JxlImageMetadata());

        Assert.Equal(6u, read.Orientation);
        Assert.Equal(640UL, read.IntrinsicSize.XSize);
        Assert.Equal(48UL, read.PreviewSize.YSize);
        Assert.Equal(1001u, read.Animation.TpsDenominator);
        Assert.Equal(5u, read.BitDepth.ExponentBitsPerSample);
        Assert.Equal(JxlColorSpace.Gray, read.ColorEncoding.ColorSpace);
        Assert.Equal(0.25f, read.ToneMapping.LinearBelow);
        Assert.Equal(2, read.NumExtraChannels);
        Assert.Equal("depth"u8.ToArray(), read.ExtraChannelInfo[1].Name.ToArray());
        Assert.Equal(5UL, read.Extensions);
        Assert.Equal(JxlFieldRecorder.Record(metadata), JxlFieldRecorder.Record(read));
    }

    [Fact]
    public void ImageMetadata_OnlyToneMappingDiffers_WritesTheExtraFields()
    {
        JxlImageMetadata metadata = new();
        metadata.ToneMapping.IntensityTarget = 4000;

        JxlImageMetadata read = RoundTrip(metadata, new JxlImageMetadata());

        Assert.Equal(4000f, read.ToneMapping.IntensityTarget);
    }

    [Fact]
    public void ColorEncoding_CustomWhiteAndPrimariesWithGamma_RoundTrips()
    {
        JxlColorEncodingHeader encoding = new()
        {
            WhitePoint = JxlWhitePoint.Custom,
            Primaries = JxlPrimaries.Custom,
            RenderingIntent = JxlRenderingIntent.Absolute,
        };

        // Packed as signed, these values reach every selector of the xy encoding.
        SetXy(encoding.White, 312700, 329000); // selector 1
        SetXy(encoding.Red, 640000, 330000); // selector 2
        SetXy(encoding.Green, -100000, 1500000); // selectors 0 and 3
        SetXy(encoding.Blue, 150000, 60000);
        encoding.Tf.HaveGamma = true;
        encoding.Tf.Gamma = 4545455;

        JxlColorEncodingHeader read = RoundTrip(encoding, new JxlColorEncodingHeader());

        Assert.Equal(-100000, read.Green.X);
        Assert.Equal(1500000, read.Green.Y);
        Assert.Equal(4545455u, read.Tf.Gamma);
        Assert.Equal(JxlFieldRecorder.Record(encoding), JxlFieldRecorder.Record(read));
    }

    [Fact]
    public void ColorEncoding_WantIcc_RoundTrips()
    {
        JxlColorEncodingHeader encoding = new() { WantIcc = true, ColorSpace = JxlColorSpace.Unknown };

        JxlColorEncodingHeader read = RoundTrip(encoding, new JxlColorEncodingHeader());

        Assert.True(read.WantIcc);
        Assert.Equal(JxlColorSpace.Unknown, read.ColorSpace);
    }

    [Fact]
    public void ColorEncoding_Xyb_RoundTrips()
    {
        JxlColorEncodingHeader encoding = new() { ColorSpace = JxlColorSpace.Xyb, RenderingIntent = JxlRenderingIntent.Perceptual };

        JxlColorEncodingHeader read = RoundTrip(encoding, new JxlColorEncodingHeader());

        Assert.Equal(JxlColorSpace.Xyb, read.ColorSpace);
        Assert.True(read.Tf.HaveGamma);
        Assert.Equal(JxlFieldRecorder.Record(encoding), JxlFieldRecorder.Record(read));
    }

    [Theory]
    [InlineData(1)] // Bt709, enum selector 1
    [InlineData(8)] // Linear, enum selector 2
    [InlineData(18)] // Hlg, enum selector 3
    public void ColorEncoding_EnumTransferFunction_RoundTrips(int tfValue)
    {
        JxlTransferFunction tf = (JxlTransferFunction)tfValue;
        JxlColorEncodingHeader encoding = new() { Primaries = JxlPrimaries.Bt2100 };
        encoding.Tf.TransferFunction = tf;

        JxlColorEncodingHeader read = RoundTrip(encoding, new JxlColorEncodingHeader());

        Assert.Equal(tf, read.Tf.TransferFunction);
        Assert.Equal(JxlPrimaries.Bt2100, read.Primaries);
    }

    [Fact]
    public void ExtraChannelInfo_SpotColorAndCfa_RoundTrip()
    {
        JxlExtraChannelInfo spot = new() { Type = JxlExtraChannelType.SpotColor, Name = "Pantone 300"u8 };
        spot.SpotColor[0] = 0.25f;
        spot.SpotColor[1] = 0.5f;
        spot.SpotColor[2] = 1.0f;
        spot.SpotColor[3] = 0.75f;
        spot.BitDepth.BitsPerSample = 12;
        JxlExtraChannelInfo cfa = new() { Type = JxlExtraChannelType.Cfa, CfaChannel = 40 };

        JxlExtraChannelInfo readSpot = RoundTrip(spot, new JxlExtraChannelInfo());
        JxlExtraChannelInfo readCfa = RoundTrip(cfa, new JxlExtraChannelInfo());

        Assert.Equal(spot.SpotColor, readSpot.SpotColor);
        Assert.Equal(12u, readSpot.BitDepth.BitsPerSample);
        Assert.Equal(40u, readCfa.CfaChannel);
        Assert.Equal(JxlFieldRecorder.Record(spot), JxlFieldRecorder.Record(readSpot));
        Assert.Equal(JxlFieldRecorder.Record(cfa), JxlFieldRecorder.Record(readCfa));
    }

    [Fact]
    public void CustomTransformData_XybWithCustomWeights_RoundTrips()
    {
        JxlCustomTransformData data = new() { NonserializedXybEncoded = true, CustomWeightsMask = 0b101 };
        data.OpsinInverseMatrix.InverseMatrix[4] = 2.5f;
        data.OpsinInverseMatrix.QuantBiases[3] = 0.125f;
        data.Upsampling2Weights[14] = -0.5f;
        data.Upsampling8Weights[209] = 0.75f;

        JxlCustomTransformData read = RoundTrip(data, new JxlCustomTransformData { NonserializedXybEncoded = true });

        Assert.Equal(0b101u, read.CustomWeightsMask);
        Assert.Equal(2.5f, read.OpsinInverseMatrix.InverseMatrix[4]);
        Assert.Equal(-0.5f, read.Upsampling2Weights[14]);
        Assert.Equal(0.75f, read.Upsampling8Weights[209]);

        // The other written weights keep their defaults, which F16 holds only approximately. The 4x
        // weights are not written, so they keep their defaults exactly.
        Assert.Equal(data.Upsampling2Weights[0], read.Upsampling2Weights[0], 0.0001f);
        Assert.Equal(data.Upsampling4Weights, read.Upsampling4Weights);
    }

    [Fact]
    public void FrameHeader_AllDefault_WritesOneBit()
    {
        JxlBitWriter writer = new();

        JxlBundle.Write(new JxlFrameHeader(null), writer);

        Assert.Equal(1, writer.BitsWritten);
    }

    [Fact]
    public void FrameHeader_NonDefaultWithLoopFilterAndBlending_RoundTrips()
    {
        JxlCodecMetadata metadata = CreateMetadata();
        JxlFrameHeader header = new(metadata)
        {
            Encoding = JxlFrameEncoding.Modular,
            Flags = JxlFrameFlags.Noise | JxlFrameFlags.Patches,
            ColorTransform = JxlColorTransform.None,
            Upsampling = 2,
            GroupSizeShift = 3,
            CustomSizeOrOrigin = true,
            FrameOriginX0 = -5,
            FrameOriginY0 = 300,
            FrameSizeX = 3000,
            FrameSizeY = 20000,
            IsLast = false,
            SaveAsReference = 3,
            Name = "frame"u8,
            Extensions = 2,
        };
        header.ExtraChannelUpsampling[0] = 4;
        header.Passes.NumPasses = 3;
        header.Passes.NumDownsample = 2;
        header.Passes.Shift[0] = 1;
        header.Passes.Shift[1] = 2;
        header.Passes.Downsample[0] = 4;
        header.Passes.Downsample[1] = 2;
        header.Passes.LastPass[0] = 0;
        header.Passes.LastPass[1] = 1;
        header.BlendingInfo.Mode = JxlBlendMode.Blend;
        header.BlendingInfo.Clamp = true;
        header.BlendingInfo.Source = 2;
        header.ExtraChannelBlendingInfo[0].Mode = JxlBlendMode.Mul;
        header.ExtraChannelBlendingInfo[0].Clamp = true;
        header.ExtraChannelBlendingInfo[0].Source = 1;
        header.AnimationFrame.Duration = 1000;
        header.AnimationFrame.Timecode = 0x01020304;
        SetLoopFilter(header.LoopFilter);

        JxlFrameHeader read = RoundTrip(header, new JxlFrameHeader(metadata));

        Assert.Equal(JxlFrameEncoding.Modular, read.Encoding);
        Assert.Equal(-5, read.FrameOriginX0);
        Assert.Equal(20000u, read.FrameSizeY);
        Assert.Equal(4u, read.ExtraChannelUpsampling[0]);
        Assert.Equal(2u, read.Passes.Downsample[1]);
        Assert.Equal(JxlBlendMode.Blend, read.BlendingInfo.Mode);
        Assert.Equal(JxlBlendMode.Mul, read.ExtraChannelBlendingInfo[0].Mode);
        Assert.Equal(0x01020304u, read.AnimationFrame.Timecode);
        Assert.Equal(3u, read.SaveAsReference);
        Assert.Equal("frame"u8.ToArray(), read.Name.ToArray());
        Assert.Equal(0.0625f, read.LoopFilter.GabYWeight2);
        Assert.Equal(2.0f, read.LoopFilter.EpfSigmaForModular);
        Assert.Equal(1UL, read.LoopFilter.Extensions);
        Assert.Equal(2UL, read.Extensions);
        Assert.Equal(JxlFieldRecorder.Record(header), JxlFieldRecorder.Record(read));
    }

    [Theory]
    [InlineData(1)] // DC frame
    [InlineData(2)] // reference only
    [InlineData(3)] // skip progressive
    public void FrameHeader_FrameType_RoundTrips(int frameType)
    {
        JxlCodecMetadata metadata = CreateMetadata();
        JxlFrameHeader header = new(metadata)
        {
            FrameType = (JxlFrameType)frameType,
            Encoding = JxlFrameEncoding.Modular,
            DcLevel = 2,
            SaveBeforeColorTransform = true,
        };

        JxlFrameHeader read = RoundTrip(header, new JxlFrameHeader(metadata));

        Assert.Equal((JxlFrameType)frameType, read.FrameType);
        Assert.Equal(JxlFieldRecorder.Record(header), JxlFieldRecorder.Record(read));
    }

    [Fact]
    public void FrameHeader_ExtraChannelBlendingInfo_FollowsTheExtraChannels()
    {
        JxlCodecMetadata metadata = CreateMetadata();
        metadata.M.ExtraChannelInfo.Add(new JxlExtraChannelInfo());
        JxlFrameHeader header = new(metadata) { Encoding = JxlFrameEncoding.Modular };

        JxlFrameHeader read = RoundTrip(header, new JxlFrameHeader(metadata));

        Assert.Equal(2, header.ExtraChannelBlendingInfo.Count);
        Assert.Equal(2, read.ExtraChannelBlendingInfo.Count);
    }

    [Fact]
    public void FrameHeader_BlendAlphaChannelOutOfRange_ThrowsBeforeWriting()
    {
        JxlCodecMetadata metadata = CreateMetadata();
        JxlFrameHeader header = new(metadata);
        header.BlendingInfo.Mode = JxlBlendMode.Blend;
        header.BlendingInfo.AlphaChannel = 1;

        AssertRejected(header);
    }

    [Fact]
    public void GroupHeader_WithTransforms_RoundTrips()
    {
        JxlGroupHeader header = new() { UseGlobalTree = true };
        header.WpHeader.P1C = 3;
        header.WpHeader.P3Ce = 31;
        header.WpHeader.W[2] = 5;
        header.Transforms.Add(new JxlTransform { Id = JxlTransformId.Rct, BeginC = 8, RctType = 10 });
        header.Transforms.Add(new JxlTransform { Id = JxlTransformId.Palette, NumC = 1, NbColors = 300, NbDeltas = 5, Predictor = JxlPredictor.Gradient });
        JxlTransform squeeze = new() { Id = JxlTransformId.Squeeze };
        squeeze.Squeezes.Add(new JxlSqueezeParams { Horizontal = true, BeginC = 100, NumC = 7 });
        squeeze.Squeezes.Add(new JxlSqueezeParams { InPlace = true, BeginC = 2000 });
        header.Transforms.Add(squeeze);

        JxlGroupHeader read = RoundTrip(header, new JxlGroupHeader());

        Assert.True(read.UseGlobalTree);
        Assert.Equal(31, read.WpHeader.P3Ce);
        Assert.Equal(3, read.Transforms.Count);
        Assert.Equal(10u, read.Transforms[0].RctType);
        Assert.Equal(JxlPredictor.Gradient, read.Transforms[1].Predictor);
        Assert.Equal(2000u, read.Transforms[2].Squeezes[1].BeginC);
        Assert.Equal(JxlFieldRecorder.Record(header), JxlFieldRecorder.Record(read));
    }

    [Fact]
    public void GroupHeader_Default_RoundTrips()
    {
        JxlGroupHeader read = RoundTrip(new JxlGroupHeader(), new JxlGroupHeader());

        Assert.Equal(JxlFieldRecorder.Record(new JxlGroupHeader()), JxlFieldRecorder.Record(read));
    }

    [Fact]
    public void Lz77Params_Enabled_RoundTrips()
    {
        JxlLz77Params lz77 = new() { Enabled = true, MinSymbol = 600, MinLength = 7 };

        JxlLz77Params read = RoundTrip(lz77, new JxlLz77Params());

        Assert.Equal(JxlFieldRecorder.Record(lz77), JxlFieldRecorder.Record(read));
    }

    [Theory]
    [InlineData(7u, 2, 0)] // the direct value
    [InlineData(5u, 6, 1)] // 4 bits, the shortest of three that hold it
    [InlineData(100u, 10, 2)] // 8 bits
    [InlineData(1000u, 34, 3)] // 32 bits
    public void U32_ChoosesTheShortestSelector(uint value, int expectedBits, int expectedSelector)
    {
        U32Bundle bundle = new() { Value = value };
        JxlBitWriter writer = new();

        JxlBundle.Write(bundle, writer);
        U32Bundle read = RoundTrip(bundle, new U32Bundle());

        Assert.Equal(expectedBits, writer.BitsWritten);
        Assert.Equal((byte)expectedSelector, (byte)(writer.ToArray()[0] & 3));
        Assert.Equal(value, read.Value);
    }

    [Theory]
    [InlineData(0f, 0x0000)]
    [InlineData(1f, 0x3C00)]
    [InlineData(-2.5f, 0xC100)]
    [InlineData(65504f, 0x7BFF)] // the largest
    [InlineData(-65504f, 0xFBFF)]
    [InlineData(6.1035156E-05f, 0x0400)] // the smallest normal, 2^-14
    [InlineData(3.0517578E-05f, 0x0200)] // subnormal, 2^-15
    [InlineData(5.9604645E-08f, 0x0001)] // the smallest subnormal, 2^-24
    [InlineData(0.0009765625f, 0x1400)] // 2^-10
    public void F16_WritesHalfPrecisionAndRoundTrips(float value, int expectedBits)
    {
        F16Bundle bundle = new() { Value = value };

        byte[] bytes = Write(bundle);
        F16Bundle read = RoundTrip(bundle, new F16Bundle());

        Assert.Equal(new JxlTestBitWriter().Write(16, (ulong)expectedBits).ToArray(), bytes);
        Assert.Equal(value, read.Value);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(65520f)]
    [InlineData(-1e6f)]
    public void F16_ValueF16CannotHold_ThrowsBeforeWriting(float value)
        => AssertRejected(new F16Bundle { Value = value });

    [Fact]
    public void Bits_ValueTooLarge_ThrowsBeforeWriting()
    {
        JxlWeightedHeader header = new() { P1C = 32 };

        AssertRejected(header);
    }

    [Fact]
    public void U32_NoSelectorHoldsTheValue_ThrowsBeforeWriting()
    {
        JxlSizeHeader size = new();
        size.Set(1, (1u << 30) + 1);

        AssertRejected(size);
    }

    [Fact]
    public void Enum_UndefinedValue_ThrowsBeforeWriting()
        => AssertRejected(new JxlColorEncodingHeader { ColorSpace = (JxlColorSpace)7 });

    [Fact]
    public void Name_TooLong_ThrowsBeforeWriting()
        => AssertRejected(new JxlExtraChannelInfo { Name = new byte[1072] });

    [Fact]
    public void FieldCheck_InvalidToneMapping_ThrowsBeforeWriting()
    {
        JxlImageMetadata metadata = new();
        metadata.ToneMapping.IntensityTarget = -1;

        AssertRejected(metadata);
    }

    [Fact]
    public void FieldCheck_InvalidBitDepth_ThrowsBeforeWriting()
    {
        JxlImageMetadata metadata = new();
        metadata.BitDepth.BitsPerSample = 40;

        AssertRejected(metadata);
    }

    [Fact]
    public void SizeHeader_EmptySize_Throws()
        => Assert.Throws<ImageFormatException>(() => new JxlSizeHeader().Set(0, 8));

    [Fact]
    public void Extensions_WriteTheirFieldsAndSizes()
    {
        // Bit 3 is an extension with no fields.
        ExtendedBundle bundle = new() { Before = 9, Extensions = 0b1101, First = 1000, Second = 0.5f };
        JxlBitWriter writer = new();

        JxlBundle.Write(bundle, writer);
        JxlBundle.CanEncode(bundle, out long totalBits);

        Assert.Equal(totalBits, writer.BitsWritten);
        ExtendedBundle read = RoundTrip(bundle, new ExtendedBundle());
        Assert.Equal(0b1101UL, read.Extensions);
        Assert.Equal(1000u, read.First);
        Assert.Equal(0.5f, read.Second);
        Assert.Equal(JxlFieldRecorder.Record(bundle), JxlFieldRecorder.Record(read));
    }

    [Fact]
    public void Extensions_AnOlderReaderSkipsThem()
    {
        ExtendedBundle bundle = new() { Before = 9, Extensions = 0b1101, First = 1000, Second = 0.5f };
        JxlSizeHeader after = new();
        after.Set(123, 45);
        JxlBitWriter writer = new();
        JxlBundle.Write(bundle, writer);
        JxlBundle.Write(after, writer);

        JxlBitReader reader = new(writer.ToArray());
        OldBundle old = new();
        JxlSizeHeader readAfter = new();
        JxlBundle.Read(reader, old);
        JxlBundle.Read(reader, readAfter);

        Assert.Equal(9u, old.Before);
        Assert.Equal(0b1101UL, old.Extensions);
        Assert.Equal(123UL, readAfter.XSize);
        Assert.Equal(45UL, readAfter.YSize);
    }

    private static JxlCodecMetadata CreateMetadata()
    {
        JxlCodecMetadata metadata = new();
        metadata.Size.Set(4000, 30000);
        metadata.M.XybEncoded = false;
        metadata.M.HaveAnimation = true;
        metadata.M.Animation.HaveTimecodes = true;
        metadata.M.ExtraChannelInfo.Add(new JxlExtraChannelInfo());
        return metadata;
    }

    private static void SetLoopFilter(JxlLoopFilter filter)
    {
        // Values that F16 holds exactly, so the fields read back equal.
        filter.Gab = true;
        filter.GabCustom = true;
        filter.GabXWeight1 = 0.125f;
        filter.GabXWeight2 = 0.0625f;
        filter.GabYWeight1 = 0.25f;
        filter.GabYWeight2 = 0.0625f;
        filter.GabBWeight1 = 0.5f;
        filter.GabBWeight2 = -0.03125f;
        filter.EpfIters = 1;
        filter.EpfWeightCustom = true;
        filter.EpfChannelScale[0] = 40;
        filter.EpfChannelScale[1] = 5;
        filter.EpfChannelScale[2] = 3.5f;
        filter.EpfPass1Zeroflush = 0.5f;
        filter.EpfPass2Zeroflush = 0.625f;
        filter.EpfSigmaCustom = true;
        filter.EpfPass0SigmaScale = 0.875f;
        filter.EpfPass2SigmaScale = 6.5f;
        filter.EpfBorderSadMul = 0.75f;
        filter.EpfSigmaForModular = 2.0f;
        filter.Extensions = 1;
    }

    private static void SetXy(JxlCustomXy xy, int x, int y)
    {
        xy.X = x;
        xy.Y = y;
    }

    private static void AssertRejected(JxlFields fields)
    {
        JxlBitWriter writer = new();
        writer.Write(3, 0b101);

        Assert.Throws<ImageFormatException>(() => JxlBundle.Write(fields, writer));

        Assert.Equal(3, writer.BitsWritten);
        Assert.False(JxlBundle.CanEncode(fields, out _));
    }

    private static byte[] Write(JxlFields fields)
    {
        JxlBitWriter writer = new();
        JxlBundle.Write(fields, writer);
        writer.ZeroPadToByte();
        return writer.ToArray();
    }

    private static T RoundTrip<T>(JxlFields fields, T target)
        where T : JxlFields
    {
        JxlBitReader reader = new(Write(fields));
        JxlBundle.Read(reader, target);
        return target;
    }

    private sealed class U32Bundle : JxlFields
    {
        public U32Bundle() => JxlBundle.Init(this);

        public uint Value { get; set; }

        public override void VisitFields(JxlVisitor visitor)
            => this.Value = visitor.U32(Val(7), Bits(4), Bits(8), Bits(32), 0, this.Value);
    }

    private sealed class F16Bundle : JxlFields
    {
        public F16Bundle() => JxlBundle.Init(this);

        public float Value { get; set; }

        public override void VisitFields(JxlVisitor visitor) => this.Value = visitor.F16(0, this.Value);
    }

    /// <summary>A bundle with two extensions that have fields, a U32 and an F16.</summary>
    private sealed class ExtendedBundle : JxlFields
    {
        public ExtendedBundle() => JxlBundle.Init(this);

        public uint Before { get; set; }

        public ulong Extensions { get; set; }

        public uint First { get; set; }

        public float Second { get; set; }

        public override void VisitFields(JxlVisitor visitor)
        {
            this.Before = visitor.Bits(4, 0, this.Before);
            this.Extensions = visitor.BeginExtensions(this.Extensions);
            if (visitor.Conditional((this.Extensions & 1) != 0))
            {
                this.First = visitor.U32(Val(0), Bits(8), Bits(16), Bits(32), 0, this.First);
            }

            if (visitor.Conditional((this.Extensions & 4) != 0))
            {
                this.Second = visitor.F16(0, this.Second);
            }

            visitor.EndExtensions();
        }
    }

    /// <summary>The fields of <see cref="ExtendedBundle"/> that a reader without its extensions knows.</summary>
    private sealed class OldBundle : JxlFields
    {
        public OldBundle() => JxlBundle.Init(this);

        public uint Before { get; set; }

        public ulong Extensions { get; set; }

        public override void VisitFields(JxlVisitor visitor)
        {
            this.Before = visitor.Bits(4, 0, this.Before);
            this.Extensions = visitor.BeginExtensions(this.Extensions);
            visitor.EndExtensions();
        }
    }
}
