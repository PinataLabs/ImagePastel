// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.Icc;
using PinataLabs.ImagePastel.Formats.Jxl.Modular;
using PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using PinataLabs.ImagePastel.Tests.Memory;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The #112 bounds that <see cref="JxlBoundsTests"/> left untested (D14f), each through a crafted stream
/// where one can reach it: input over the bound throws, and input at the bound gets past it.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlBoundsCoverageTests
{
    // squeeze.cc MetaSqueeze: "Too many squeezes: shift > 30", checked before each step. A 1 x 1 channel
    // squeezed along one axis stays 1 x 1, and its shift grows by one per step, so the 32nd step sees a
    // shift of 31. At 31 steps the squeeze passes, and the decode goes on to the channel data, which the
    // stream does not have.
    [Theory]
    [InlineData(true, 31, false)]
    [InlineData(true, 32, true)]
    [InlineData(false, 31, false)]
    [InlineData(false, 32, true)]
    public void Squeeze_ShiftOver30_Throws(bool horizontal, int steps, bool throws)
    {
        JxlTransform squeeze = new() { Id = JxlTransformId.Squeeze };
        for (int i = 0; i < steps; i++)
        {
            squeeze.Squeezes.Add(new JxlSqueezeParams { Horizontal = horizontal, InPlace = true, BeginC = 0, NumC = 1 });
        }

        byte[] codestream = new JxlTestLayers(1, 1, alpha: false)
            .Add(JxlTestFrame.Last(GlobalModularSection(squeeze)))
            .ToArray();

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => DecodeToPlanes(codestream));
        Assert.Equal(throws, ex.Message == "Too many squeezes: shift > 30");
    }

    // The 2^28-pixel cap applies to each frame at its upsampled size, not only to the image: a frame with a
    // custom size larger than its 8 x 8 image is checked before anything is allocated for it. At the cap
    // the frame gets past the check and fails on its TOC, which is too short for 4162 sections. The
    // upsampled frame is coded at 2049 x 2048, far under the cap. The frames are RGB, so each is under the
    // sample budget (2^30 samples, checked after the pixel cap): the over-cap frames throw the pixel cap's
    // exception, and the frames at the cap pass both.
    [Theory]
    [InlineData(16385, 16384, 1u, true)]
    [InlineData(16384, 16385, 1u, true)]
    [InlineData(16385, 16384, 8u, true)]
    [InlineData(16384, 16384, 1u, false)]
    [InlineData(16384, 16384, 8u, false)]
    public void PixelCap_FrameOverTheCapInAnImageUnderIt_ThrowsBeforeAllocating(int xsize, int ysize, uint upsampling, bool throws)
    {
        Assert.True((long)xsize * ysize * 3 <= JxlSampleBudget.MaxSamples);
        byte[] codestream = new JxlTestLayers(8, 8, alpha: false)
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { Crop = true, XSize = xsize, YSize = ysize, Upsampling = upsampling })
            .ToArray();
        TestMemoryAllocator allocator = new();
        allocator.EnableNonThreadSafeLogging();
        Configuration configuration = Configuration.Default.Clone();
        configuration.MemoryAllocator = allocator;
        using MemoryStream stream = new(codestream);

        if (throws)
        {
            InvalidMemoryOperationException ex = Assert.Throws<InvalidMemoryOperationException>(() => new JxlDecoder().Decode<Rgb24>(configuration, stream, CancellationToken.None));
            Assert.Equal($"The JPEG XL frame is {xsize}x{ysize} pixels, more than the limit of {JxlFrameDecoder.MaxPixelsPerFrame}.", ex.Message);
        }
        else
        {
            Assert.Throws<InvalidImageContentException>(() => new JxlDecoder().Decode<Rgb24>(configuration, stream, CancellationToken.None));
        }

        // Only the 8 x 8 image and small buffers: no plane of the frame.
        Assert.All(allocator.AllocationLog, a => Assert.True(a.LengthInBytes < 1 << 16, $"{a.LengthInBytes} bytes of {a.ElementType.Name} were allocated."));
    }

    // dec_modular.cc DecodeGlobalInfo: the global MA tree has at most min(2^22, 1024 + xsize * ysize *
    // channels / 16) nodes, from the coded frame size and the colour and extra channels. DecodeTree checks
    // the limit before each node, so a tree of limit + 1 nodes is read and one of limit + 3 is not. Past
    // the limit, the tree is valid; the stream ends there, so the decode fails later.
    [Theory]
    [InlineData(8, 8, true, 1u, 1040)] // 1024 + 64 * 4 / 16
    [InlineData(8, 8, false, 1u, 1036)] // 1024 + 64 * 3 / 16
    [InlineData(16, 16, true, 1u, 1088)] // 1024 + 256 * 4 / 16
    [InlineData(16, 16, true, 2u, 1040)] // coded at 8 x 8
    public void MaTree_OverTheFrameSizeLimit_Throws(int width, int height, bool alpha, uint upsampling, int limit)
    {
        foreach (bool over in new[] { false, true })
        {
            int splits = (limit / 2) + (over ? 1 : 0);
            JxlTestBitWriter section = new JxlTestBitWriter().Bit(true).Bit(true); // all-default DC quantisation, a global tree
            WriteLeftChainTree(section, splits);
            byte[] codestream = new JxlTestLayers(width, height, alpha)
                .Add(JxlTestFrame.Last(section.ToArray()) with { Upsampling = upsampling })
                .ToArray();

            InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => DecodeToPlanes(codestream));
            if (over)
            {
                Assert.Equal($"Tree is too large: {limit + 1} nodes vs {limit} max nodes", ex.Message);
            }
            else
            {
                Assert.DoesNotContain("Tree is too large", ex.Message, StringComparison.Ordinal);
            }
        }
    }

    // dec_ma.cc ValidateTree: a tree no taller than 2048 levels ("Tree too tall"), checked as the walk
    // descends. A chain of n splits, each the left child of the last, is n + 1 levels deep.
    [Theory]
    [InlineData(2046, false)]
    [InlineData(2047, true)]
    public void MaTree_TallerThanTheHeightLimit_Throws(int splits, bool throws)
    {
        JxlTestBitWriter writer = new();
        WriteLeftChainTree(writer, splits);
        writer.Bits(64, false);

        if (throws)
        {
            InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => JxlMaTree.DecodeTree(writer.ToReader(), JxlMaTree.MaxTreeSize));
            Assert.Equal("Tree too tall", ex.Message);
        }
        else
        {
            Assert.Equal((2 * splits) + 1, JxlMaTree.DecodeTree(writer.ToReader(), JxlMaTree.MaxTreeSize).Count);
        }
    }

    // icc_codec.cc CheckPreamble: UnpredictICC inflates its input, so the output size it announces is at
    // least the encoded size less 65536 ("Malformed ICC").
    [Theory]
    [InlineData(100ul + 65536, null)]
    [InlineData(100ul + 65537, "Malformed ICC")]
    public void Icc_OutputSizeSmallerThanTheEncodedSize_Throws(ulong encodedSize, string message)
    {
        byte[] preamble = new byte[JxlIccReader.PreambleSize];
        preamble[0] = 100; // output size
        preamble[1] = 0; // commands size

        AssertPreamble(preamble, encodedSize, message);
    }

    // icc_codec.cc DecodeVarInt: a varint of at most 10 bytes whose value fits in 64 bits, in the input.
    [Theory]
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00 }, "DecodeVarInt: varint exceeds 10 bytes")]
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x02 }, "DecodeVarInt: value exceeds 2^64 - 1")]
    [InlineData(new byte[] { 0x80, 0x80, 0x80 }, "DecodeVarInt: truncated input")]
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 }, "DecodeVarInt: truncated input (10th byte)")]
    public void Icc_InvalidVarint_Throws(byte[] data, string message)
    {
        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => JxlIccReader.CheckPreamble(data, data.Length, 0));
        Assert.Equal(message, ex.Message);
    }

    // The 10th byte may carry bit 63: the varint is valid, and the 32-bit check on the size rejects it.
    [Fact]
    public void Icc_TenByteVarint_IsReadAndThen32BitChecked()
    {
        byte[] data = { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 };

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => JxlIccReader.CheckPreamble(data, data.Length, 0));
        Assert.DoesNotContain("DecodeVarInt", ex.Message, StringComparison.Ordinal);
    }

    // icc_codec.cc ICCReader::Process: at each 65536th byte, the bytes read so far are at most 256 times
    // the bits used ("Corrupted stream"). Every byte of this profile is 1, from a one-symbol prefix code
    // of 0 bits, so the first check, at byte 65536, fails. A profile of 65536 bytes never reaches it, and
    // fails in UnpredictICC instead. The preamble (output size 1, commands size 1) passes CheckPreamble.
    [Theory]
    [InlineData(65536u, false)]
    [InlineData(65537u, true)]
    public void Icc_MoreThan256BytesPerByteRead_Throws(uint encodedSize, bool throws)
    {
        JxlTestBitWriter writer = new JxlTestBitWriter().Write(8, 0xFF).Write(8, 0x0A);
        writer.Bit(true).Write(5, 0).Write(3, 1); // 8 x 8
        writer.Bit(false).Bit(false); // image metadata: not all default, no extra fields
        writer.Bit(false).Write(2, 0).Bit(true).Write(2, 0); // 8-bit integer samples, no extra channels
        writer.Bit(false).Bit(false).Bit(true).Write(2, 0); // not XYB; an embedded ICC, RGB
        writer.Write(2, 0).Bit(true); // no extensions; all-default transform data

        // The encoded size as a U64: selector 3, the low 12 bits, then 8 more.
        writer.Write(2, 3).Write(12, encodedSize & 0xFFF).Bit(true).Write(8, encodedSize >> 12).Bit(false);

        // 41 contexts, one histogram, a prefix code of the one symbol 1.
        writer.Bit(false); // no LZ77
        writer.Bit(true).Write(2, 0); // simple context map, 0 bits per entry
        writer.Bit(true); // prefix codes
        WriteUintConfigSplit4(writer);
        WriteVarLenUint16(writer, 1); // alphabet size 2
        writer.Write(2, 1).Write(2, 0).Write(1, 1); // simple prefix code with the symbol 1
        writer.Bits(64, false);

        using MemoryStream stream = new(writer.ToArray());
        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken.None));
        Assert.Equal(throws, ex.Message == "Corrupted stream");
    }

    private static void AssertPreamble(byte[] preamble, ulong encodedSize, string message)
    {
        if (message != null)
        {
            InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => JxlIccReader.CheckPreamble(preamble, preamble.Length, encodedSize));
            Assert.Equal(message, ex.Message);
        }
        else
        {
            JxlIccReader.CheckPreamble(preamble, preamble.Length, encodedSize);
        }
    }

    // An MA tree that is a chain of splits on property 0, each the left child of the last, at 1024, 1025
    // and so on, so each narrows the range its parent left. Every other child is a leaf. The split values
    // have a histogram of their own: tokens 23 and 24 with 11 and 12 raw bits, for packed values 2048 to
    // 8191. Every other token is one bit of a two-symbol prefix code. The nodes come breadth first: the
    // root, then each split with its parent's leaf, then the last split's two leaves.
    private static void WriteLeftChainTree(JxlTestBitWriter writer, int splits)
    {
        writer.Bit(false); // no LZ77
        writer.Bit(true).Write(2, 1); // simple context map, 1 bit per entry
        writer.Bit(true).Bits(5, false); // split values: histogram 1; the rest: histogram 0
        writer.Bit(true); // prefix codes
        WriteUintConfigSplit4(writer);
        WriteUintConfigSplit4(writer);
        WriteVarLenUint16(writer, 1); // alphabet size 2
        WriteVarLenUint16(writer, 24); // alphabet size 25
        writer.Write(2, 1).Write(2, 1).Write(1, 0).Write(1, 1); // simple prefix code: symbols 0 and 1
        writer.Write(2, 1).Write(2, 1).Write(5, 23).Write(5, 24); // simple prefix code: symbols 23 and 24

        for (int k = 0; k < splits; k++)
        {
            writer.Bit(true); // property 0 + 1
            ulong packed = 2 * (1024 + (ulong)k);
            if (packed < 4096)
            {
                writer.Bit(false).Write(11, packed - 2048);
            }
            else
            {
                writer.Bit(true).Write(12, packed - 4096);
            }

            if (k > 0)
            {
                WriteLeaf(writer);
            }
        }

        WriteLeaf(writer);
        WriteLeaf(writer);
    }

    // A leaf: the zero predictor, offset 0 and multiplier 1.
    private static void WriteLeaf(JxlTestBitWriter writer) => writer.Bits(5, false);

    // Split exponent 4, no msb or lsb in the token, for log_alpha_size 15.
    private static void WriteUintConfigSplit4(JxlTestBitWriter writer) => writer.Write(4, 4).Write(3, 0).Write(3, 0);

    private static void WriteVarLenUint16(JxlTestBitWriter writer, int value)
    {
        if (value == 0)
        {
            writer.Bit(false);
            return;
        }

        int nbits = JxlBits.FloorLog2Nonzero((uint)value);
        writer.Bit(true).Write(4, (ulong)nbits);
        if (nbits != 0)
        {
            writer.Write(nbits, (ulong)(value - (1 << nbits)));
        }
    }

    // frame_header.cc: an extra channel's upsampling, its coded value shifted by the channel's dim_shift,
    // is at least the colour's ("EC upsampling (%u) < color upsampling (%u)") and at most 8 ("EC upsampling
    // too large"). The frames within both bounds decode.
    [Theory]
    [InlineData(0u, 2u, 2u, null)]
    [InlineData(0u, 2u, 1u, "EC upsampling (1) < color upsampling (2), which is invalid.")]
    [InlineData(0u, 8u, 4u, "EC upsampling (4) < color upsampling (8), which is invalid.")]
    [InlineData(1u, 4u, 2u, null)] // 2 << 1 = 4
    [InlineData(1u, 4u, 1u, "EC upsampling (2) < color upsampling (4), which is invalid.")]
    [InlineData(1u, 1u, 4u, null)] // 4 << 1 = 8
    [InlineData(1u, 1u, 8u, "EC upsampling too large (16)")]
    [InlineData(3u, 1u, 1u, null)] // 1 << 3 = 8
    [InlineData(3u, 1u, 2u, "EC upsampling too large (16)")]
    public void ExtraChannelUpsampling_OutsideItsRange_Throws(uint dimShift, uint upsampling, uint codedExtraChannelUpsampling, string message)
    {
        byte[] codestream = DimShiftedAlphaCodestream(dimShift, upsampling, codedExtraChannelUpsampling);

        if (message != null)
        {
            InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => DecodeToPlanes(codestream));
            Assert.Equal(message, ex.Message);
        }
        else
        {
            using MemoryStream stream = new(codestream);
            using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
            Assert.Single(decoded.Frames);
        }
    }

    // libjxl rejects the same extra-channel upsampling, and decodes the frames within the bounds.
    [Theory]
    [InlineData(0u, 2u, 2u, false)]
    [InlineData(0u, 2u, 1u, true)]
    [InlineData(1u, 4u, 2u, false)]
    [InlineData(1u, 4u, 1u, true)]
    [InlineData(1u, 1u, 4u, false)]
    [InlineData(1u, 1u, 8u, true)]
    [InlineData(3u, 1u, 1u, false)]
    [InlineData(3u, 1u, 2u, true)]
    public async Task Djxl_RejectsTheSameExtraChannelUpsampling(uint dimShift, uint upsampling, uint codedExtraChannelUpsampling, bool throws)
    {
        byte[] codestream = DimShiftedAlphaCodestream(dimShift, upsampling, codedExtraChannelUpsampling);
        string input = Path.Combine(TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlBoundsCoverageTests)), $"ec_upsampling_{dimShift}_{upsampling}_{codedExtraChannelUpsampling}.jxl");
        await File.WriteAllBytesAsync(input, codestream, TestContext.Current.CancellationToken);

        (int exitCode, string output) = await JxlTools.RunForExitCodeAsync("djxl", TestContext.Current.CancellationToken, input, input + ".npy");

        Assert.True(throws == (exitCode != 0), $"djxl exited with code {exitCode}.{Environment.NewLine}{output}");
    }

    // A 16 x 16 RGBA image whose alpha has the given dim_shift, and one Modular frame (the ColourA layer)
    // with the given colour upsampling and coded extra-channel upsampling. JxlTestLayers writes only
    // all-default extra channels, so the header is written here, field by field in the same way.
    private static byte[] DimShiftedAlphaCodestream(uint dimShift, uint upsampling, uint codedExtraChannelUpsampling)
    {
        JxlTestBitWriter w = new JxlTestBitWriter().Write(8, 0xFF).Write(8, 0x0A);
        w.Bit(true).Write(5, 1).Write(3, 1); // small size header: 16 x 16
        w.Bit(false).Bit(false); // image metadata: not all default, no extra fields
        w.Bit(false).Write(2, 0).Bit(true); // 8-bit integer samples, 16-bit buffers suffice
        w.Write(2, 1); // one extra channel
        w.Bit(false).Write(2, 0).Bit(false).Write(2, 0); // not all default: alpha, 8-bit integer samples
        if (dimShift == 0)
        {
            w.Write(2, 0);
        }
        else
        {
            w.Write(2, 3).Write(3, dimShift - 1); // U32(Val(0), Val(3), Val(4), BitsOffset(3, 1))
        }

        w.Write(2, 0).Bit(false); // empty name, straight alpha
        w.Bit(false).Bit(true); // not XYB, sRGB
        w.Write(2, 0).Bit(true); // no extensions, all-default transform data
        w.ZeroPadToByte();

        w.Bit(false).Write(2, 0).Bit(true).Write(2, 0); // not all default, regular, Modular, no flags
        w.Bit(false); // no YCbCr
        w.Write(2, (ulong)JxlBits.FloorLog2Nonzero(upsampling)); // U32(Val(1), Val(2), Val(4), Val(8))
        w.Write(2, (ulong)JxlBits.FloorLog2Nonzero(codedExtraChannelUpsampling));
        w.Write(2, 1); // 256 x 256 groups
        w.Write(2, 0); // one pass
        w.Bit(false); // no crop
        w.Write(2, 0).Write(2, 0); // colour and alpha replace
        w.Bit(true); // last frame
        w.Write(2, 0); // no name
        w.Bit(false).Bit(false).Write(2, 0).Write(2, 0); // loop filter: no Gaborish, no EPF, no extensions
        w.Write(2, 0); // no extensions

        byte[] section = JxlTestLayers.ColourA;
        w.Bit(false).ZeroPadToByte(); // TOC: not permuted
        w.Write(2, 0).Write(10, (ulong)section.Length).ZeroPadToByte();
        foreach (byte b in section)
        {
            w.Write(8, b);
        }

        return w.ToArray();
    }

    // The DC global section of a one-group Modular frame, cut short after the global stream's group
    // header: the all-default DC quantisation, no global tree, then a header with the transforms. MetaApply
    // runs as soon as the header is read, before the tree or any pixel data.
    private static byte[] GlobalModularSection(params JxlTransform[] transforms)
    {
        JxlGroupHeader header = new();
        header.Transforms.AddRange(transforms);
        JxlBitWriter writer = new();
        writer.Write(1, 1); // all-default DC quantisation
        writer.Write(1, 0); // no global tree
        JxlBundle.Write(header, writer);
        writer.ZeroPadToByte();
        return writer.ToArray();
    }

    private static void DecodeToPlanes(byte[] codestream)
    {
        using MemoryStream stream = new(codestream);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
    }
}
