// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Modular;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// One test per structural bound ported from libjxl (issue #112): input over the bound throws
/// <see cref="InvalidImageContentException"/>, and input at the bound still decodes.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlBoundsTests
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    public void MaTree_OverTheNodeLimit_Throws(long limit, bool throws)
    {
        // A split on property 0 with two leaves: three nodes. The limit is checked before each node.
        JxlTestBitWriter writer = new();
        WriteTwoSymbolPrefixHistograms(writer, numContexts: 6);
        writer.Bit(true).Bit(false); // property 0 + 1, split value 0
        writer.Bits(10, false); // two leaves: property, predictor, offset, multiplier log and bits

        JxlBitReader reader = writer.ToReader();
        if (throws)
        {
            Assert.Throws<InvalidImageContentException>(() => JxlMaTree.DecodeTree(reader, limit));
        }
        else
        {
            Assert.Equal(3, JxlMaTree.DecodeTree(reader, limit).Count);
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void MaTree_SplitOutsideItsPropertyRange_Throws(int nestedSplitToken, bool throws)
    {
        // The root splits property 0 at 0, so its right subtree only sees values <= 0. A nested split
        // there at -1 (token 1) narrows the range; a split at 0 (token 0) cannot.
        JxlTestBitWriter writer = new();
        WriteTwoSymbolPrefixHistograms(writer, numContexts: 6);
        writer.Bit(true).Bit(false); // root: property 0, split value 0
        writer.Bits(5, false); // left child: leaf
        writer.Bit(true).Write(1, (ulong)nestedSplitToken); // right child: property 0
        writer.Bits(10, false); // two leaves

        JxlBitReader reader = writer.ToReader();
        if (throws)
        {
            Assert.Throws<InvalidImageContentException>(() => JxlMaTree.DecodeTree(reader, 1024));
        }
        else
        {
            Assert.Equal(5, JxlMaTree.DecodeTree(reader, 1024).Count);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(8, true)]
    public void ExtraChannel_DimShiftOver3_Throws(int dimShift, bool throws)
    {
        JxlTestBitWriter writer = new();
        writer.Bit(false).Write(2, 0); // not all default, alpha
        writer.Bit(false).Write(2, 0); // 8-bit integer samples
        if (dimShift == 0)
        {
            writer.Write(2, 0);
        }
        else
        {
            writer.Write(2, 3).Write(3, (ulong)(dimShift - 1)); // BitsOffset(3, 1)
        }

        writer.Write(2, 0).Bit(false); // empty name, not premultiplied

        JxlBitReader reader = writer.ToReader();
        if (throws)
        {
            Assert.Throws<InvalidImageContentException>(() => JxlBundle.Read(reader, new JxlExtraChannelInfo()));
        }
        else
        {
            JxlExtraChannelInfo info = new();
            JxlBundle.Read(reader, info);
            Assert.Equal((uint)dimShift, info.DimShift);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(20, true)]
    public void ContextMap_ClusterIdOver255_Throws(int token, bool throws)
    {
        // Token 20 with split exponent 4 carries 8 raw bits, which are zero: cluster 256.
        JxlTestBitWriter writer = new();
        writer.Bit(false).Bit(false); // not simple, no move-to-front
        writer.Bit(false).Bit(true); // nested histograms: no LZ77, prefix codes
        WriteUintConfigSplit4(writer);
        WriteVarLenUint16(writer, 20); // alphabet size 21
        writer.Write(2, 1).Write(2, 0).Write(5, (ulong)token); // simple prefix code with one symbol
        writer.Bits(3 * 8, false);

        byte[] contextMap = new byte[3];
        if (throws)
        {
            Assert.Throws<InvalidImageContentException>(() => JxlContextMap.DecodeContextMap(contextMap, writer.ToReader()));
        }
        else
        {
            Assert.Equal(1, JxlContextMap.DecodeContextMap(contextMap, writer.ToReader()));
        }
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(33, true)]
    public void AnsAlphabet_OverTheLimit_Throws(int alphabetSize, bool throws)
    {
        // log_alpha_size 5 allows 32 symbols; a flat histogram of alphabetSize symbols follows.
        JxlTestBitWriter writer = new();
        writer.Bit(false).Bit(false); // no LZ77, ANS
        writer.Write(2, 0); // log_alpha_size = 5
        writer.Write(3, 5); // split exponent = log_alpha_size: no msb or lsb fields
        writer.Bit(false).Bit(true); // not simple, flat
        WriteVarLenUint8(writer, alphabetSize - 1);

        if (throws)
        {
            Assert.Throws<InvalidImageContentException>(() => JxlEntropyDecoder.DecodeHistograms(writer.ToReader(), 1, new JxlAnsCode()));
        }
        else
        {
            JxlEntropyDecoder.DecodeHistograms(writer.ToReader(), 1, new JxlAnsCode());
        }
    }

    [Theory]
    [InlineData(1 << 15, false)]
    [InlineData((1 << 15) + 1, true)]
    [InlineData(1 << 16, true)]
    public void PrefixAlphabet_Over2Pow15_Throws(int alphabetSize, bool throws)
    {
        JxlTestBitWriter writer = new();
        writer.Bit(false).Bit(true); // no LZ77, prefix codes
        WriteUintConfigSplit4(writer);
        WriteVarLenUint16(writer, alphabetSize - 1);
        writer.Write(2, 1).Write(2, 0).Write(15, 0); // simple prefix code with symbol 0

        if (throws)
        {
            Assert.Throws<InvalidImageContentException>(() => JxlEntropyDecoder.DecodeHistograms(writer.ToReader(), 1, new JxlAnsCode()));
        }
        else
        {
            JxlEntropyDecoder.DecodeHistograms(writer.ToReader(), 1, new JxlAnsCode());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Lz77_CopyLengthThatWraps_Throws(bool lengthBitsAllOnes, bool throws)
    {
        // Every token is 40, a copy: length token 32 with split exponent 0 reads 31 more bits, so all
        // ones give 2^32 - 1, which wraps when the minimum length of 3 is added.
        JxlTestBitWriter writer = new();
        WriteLz77Params(writer, minSymbol: 8);
        writer.Write(4, 0); // length uint config: split exponent 0
        writer.Bit(true).Write(2, 0); // simple context map, 0 bits per entry
        writer.Bit(true); // prefix codes
        WriteUintConfigSplit4(writer);
        WriteVarLenUint16(writer, 40); // alphabet size 41
        writer.Write(2, 1).Write(2, 0).Write(6, 40); // simple prefix code with one symbol
        writer.Bits(31, lengthBitsAllOnes); // copy length
        writer.Bits(28, false); // distance
        writer.Bits(64, false);

        JxlBitReader br = writer.ToReader();
        JxlAnsCode code = new();
        byte[] contextMap = JxlEntropyDecoder.DecodeHistograms(br, 1, code);
        JxlAnsSymbolReader reader = new(code, br);

        Assert.Equal(0u, reader.ReadHybridUint(0, br, contextMap));
        if (throws)
        {
            Assert.Throws<InvalidImageContentException>(() => br.EnsureAllReadsWithinBounds("LZ77 copy"));
        }
        else
        {
            br.EnsureAllReadsWithinBounds("LZ77 copy");
        }
    }

    [Fact]
    public void Lz77_DistanceBeyondTheDecodedValues_IsClamped()
    {
        // Tokens 5 (a literal) and 8 (a copy of length 3), one bit each. The copy's distance token 5
        // is distance 6, but only one value has been decoded, so the copy repeats that value.
        JxlTestBitWriter writer = new();
        WriteLz77Params(writer, minSymbol: 8);
        writer.Write(4, 0); // length uint config: split exponent 0
        writer.Bit(true).Write(2, 0); // simple context map, 0 bits per entry
        writer.Bit(true); // prefix codes
        WriteUintConfigSplit4(writer);
        WriteVarLenUint16(writer, 8); // alphabet size 9
        writer.Write(2, 1).Write(2, 1).Write(4, 5).Write(4, 8); // simple prefix code: symbols 5 and 8
        writer.Bit(false); // literal 5
        writer.Bit(true).Bit(false); // copy, distance token 5
        writer.Bit(false); // literal 5
        writer.Bits(64, false);

        JxlBitReader br = writer.ToReader();
        JxlAnsCode code = new();
        byte[] contextMap = JxlEntropyDecoder.DecodeHistograms(br, 1, code);
        JxlAnsSymbolReader reader = new(code, br);

        uint[] values = new uint[5];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadHybridUint(0, br, contextMap);
        }

        Assert.Equal(new uint[] { 5, 5, 5, 5, 5 }, values);
        br.EnsureAllReadsWithinBounds("LZ77 copy");
    }

    [Fact]
    public void Lz77_DistanceBeyondTheWindow_IsClamped()
    {
        // Symbols 5, 6, 8 (a copy of length 3) and 32, with 2-bit codes. After 2^20 sixes and then a 5,
        // which overwrites window slot 0, a copy with distance token 32 asks for distance 2^21. That is
        // clamped to the 2^20 + 1 values decoded so far and then to the window of 2^20, so the copy
        // starts at slot 1, a 6. Without the window clamp it would start at slot 0, the 5.
        JxlTestBitWriter writer = new();
        WriteLz77Params(writer, minSymbol: 8);
        writer.Write(4, 0); // length uint config: split exponent 0
        writer.Bit(true).Write(2, 0); // simple context map, 0 bits per entry
        writer.Bit(true); // prefix codes
        WriteUintConfigSplit4(writer);
        WriteVarLenUint16(writer, 32); // alphabet size 33
        writer.Write(2, 1).Write(2, 3).Write(6, 5).Write(6, 6).Write(6, 8).Write(6, 32).Bit(false); // simple prefix code, four 2-bit codes
        for (int i = 0; i < 1 << 20; i++)
        {
            writer.Write(2, 2); // 6
        }

        writer.Write(2, 0); // 5

        writer.Write(2, 1); // copy
        writer.Write(2, 3).Bits(20, true); // distance token 32 and its 20 bits
        writer.Bits(64, false);

        JxlBitReader br = writer.ToReader();
        JxlAnsCode code = new();
        byte[] contextMap = JxlEntropyDecoder.DecodeHistograms(br, 1, code);
        JxlAnsSymbolReader reader = new(code, br);
        for (int i = 0; i < (1 << 20) + 1; i++)
        {
            reader.ReadHybridUint(0, br, contextMap);
        }

        Assert.Equal(6u, reader.ReadHybridUint(0, br, contextMap));
        br.EnsureAllReadsWithinBounds("LZ77 copy");
    }

    [Fact]
    public void Lz77_WhereDisallowed_Throws()
    {
        JxlTestBitWriter writer = new();
        WriteLz77Params(writer, minSymbol: 8);
        writer.Write(4, 0).Bits(64, false);

        Assert.Throws<InvalidImageContentException>(() => JxlEntropyDecoder.DecodeHistograms(writer.ToReader(), 1, new JxlAnsCode(), disallowLz77: true));
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(32, true)]
    public void BitDepth_IntegerOver31Bits_Throws(int bits, bool throws)
    {
        JxlTestBitWriter writer = new();
        writer.Bit(false).Write(2, 3).Write(6, (ulong)(bits - 1)); // integer samples
        writer.Bits(64, false);

        AssertBitDepth(writer, throws);
    }

    [Theory]
    [InlineData(16, 1, true)] // too few exponent bits
    [InlineData(16, 2, false)]
    [InlineData(16, 8, false)]
    [InlineData(16, 9, true)] // too many exponent bits
    [InlineData(32, 8, false)]
    [InlineData(32, 7, true)] // 24 mantissa bits
    [InlineData(11, 8, false)] // 2 mantissa bits, the least
    [InlineData(10, 8, true)] // 1 mantissa bit
    public void BitDepth_FloatOutsideTheExponentAndMantissaRanges_Throws(int bits, int expBits, bool throws)
    {
        JxlTestBitWriter writer = new();
        writer.Bit(true).Write(2, 3).Write(6, (ulong)(bits - 1)); // floating-point samples
        writer.Write(4, (ulong)(expBits - 1));
        writer.Bits(64, false);

        AssertBitDepth(writer, throws);
    }

    [Fact]
    public void Toc_OverTheEntryLimit_Throws()
        => Assert.Throws<InvalidImageContentException>(() => JxlToc.ReadToc(JxlToc.MaxTocEntries + 1, new JxlBitReader(new byte[16])));

    [Fact]
    public void Toc_WithinTheLimit_ReadsTheSizes()
    {
        JxlTestBitWriter writer = new();
        writer.Bit(false).ZeroPadToByte(); // no permutation
        writer.Write(2, 0).Write(10, 5).ZeroPadToByte(); // one entry of 5 bytes

        Assert.Equal(new uint[] { 5 }, JxlToc.ReadToc(1, writer.ToReader()));
    }

    [Fact]
    public void Header_SideOver2Pow30_Throws()
    {
        // ysize = 2^30 and ratio 2:1, so xsize = 2^31.
        JxlTestBitWriter writer = Codestream();
        writer.Bit(false).Write(2, 3).Write(30, (1 << 30) - 1).Write(3, 7);
        writer.Bit(true).Bit(true); // all-default image metadata and transform data

        Assert.Throws<InvalidImageContentException>(() => Identify(writer));
    }

    [Theory]
    [InlineData(256, false)]
    [InlineData(257, true)]
    public void Header_MoreThan256ExtraChannels_Throws(int count, bool throws)
    {
        JxlTestBitWriter writer = Codestream();
        writer.Bit(true).Write(5, 0).Write(3, 1); // 8 x 8
        writer.Bit(false).Bit(false); // image metadata: not all default, no extra fields
        writer.Bit(false).Write(2, 0).Bit(true); // 8-bit integer samples, 16-bit buffers suffice
        writer.Write(2, 3).Write(12, (ulong)(count - 1)); // extra channels
        writer.Bits(count, true); // each all default (8-bit alpha)
        writer.Bit(true).Bit(true).Write(2, 0); // XYB, default colour encoding, no extensions
        writer.Bit(true); // all-default transform data

        if (throws)
        {
            Assert.Throws<InvalidImageContentException>(() => Identify(writer));
        }
        else
        {
            Assert.Equal(8, Identify(writer).Width);
        }
    }

    [Fact]
    public void Icc_EncodedSizeOver2Pow28_Throws()
    {
        JxlTestBitWriter writer = Codestream();
        writer.Bit(true).Write(5, 0).Write(3, 1); // 8 x 8
        writer.Bit(false).Bit(false); // image metadata: not all default, no extra fields
        writer.Bit(false).Write(2, 0).Bit(true).Write(2, 0); // 8-bit integer samples, no extra channels
        writer.Bit(false).Bit(false).Bit(true).Write(2, 0); // not XYB; an embedded ICC, RGB
        writer.Write(2, 0).Bit(true); // no extensions; all-default transform data

        // The encoded ICC size, 2^28 + 1, as a U64: selector 3, then 12 + 8 + 8 + 8 bits.
        writer.Write(2, 3).Write(12, 1).Bit(true).Write(8, 0).Bit(true).Write(8, 0).Bit(true).Write(8, 1).Bit(false);
        writer.Bits(64, false);

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => Identify(writer));
        Assert.Contains("Too large encoded profile", ex.Message);
    }

    [Theory]
    [InlineData(329000, false)] // D65's y
    [InlineData(0, true)]
    public void ColorEncoding_WhitePointWithNoProfile_Throws(int whiteY, bool throws)
    {
        // libjxl builds an ICC profile from every colour encoding it reads (CreateICC), and a white point
        // with y = 0 has none.
        JxlTestBitWriter writer = Codestream();
        writer.Bit(true).Write(5, 0).Write(3, 1); // 8 x 8
        writer.Bit(false).Bit(false); // image metadata: not all default, no extra fields
        writer.Bit(false).Write(2, 0).Bit(true).Write(2, 0); // 8-bit integer samples, no extra channels
        writer.Bit(false).Bit(false).Bit(false).Write(2, 0); // not XYB; a colour encoding, no ICC; RGB
        writer.Write(2, 2).Write(4, 0); // custom white point
        writer.Write(2, 1).Write(19, 625400 - 524288); // x = 0.3127, packed signed
        if (whiteY == 0)
        {
            writer.Write(2, 0).Write(19, 0);
        }
        else
        {
            writer.Write(2, 1).Write(19, (ulong)((whiteY * 2) - 524288));
        }

        writer.Write(2, 1); // sRGB primaries
        writer.Bit(false).Write(2, 2).Write(4, 11); // the sRGB transfer function
        writer.Write(2, 1); // relative rendering intent
        writer.Write(2, 0).Bit(true); // no extensions; all-default transform data

        if (throws)
        {
            Assert.Throws<InvalidImageContentException>(() => Identify(writer));
        }
        else
        {
            Assert.Equal(8, Identify(writer).Width);
        }
    }

    [Fact]
    public void PixelCap_ThrowsInvalidMemoryOperationBeforeAllocating()
    {
        // 20000 x 20000 is over the 2^28-pixel cap.
        JxlTestBitWriter writer = Codestream();
        writer.Bit(false).Write(2, 2).Write(18, 19999).Write(3, 0).Write(2, 2).Write(18, 19999);
        writer.Bit(true).Bit(true);

        using MemoryStream stream = new(writer.ToArray());
        Assert.Throws<InvalidMemoryOperationException>(() => new JxlDecoder().Decode<Rgb24>(Configuration.Default, stream, CancellationToken.None));
    }

    [Fact]
    public void PixelCap_DoesNotApplyToIdentify()
    {
        JxlTestBitWriter writer = Codestream();
        writer.Bit(false).Write(2, 2).Write(18, 19999).Write(3, 0).Write(2, 2).Write(18, 19999);
        writer.Bit(true).Bit(true);

        IImageInfo info = Identify(writer);

        Assert.Equal(20000, info.Width);
        Assert.Equal(20000, info.Height);
    }

    // stage_blending.cc: the colour of a frame cannot blend onto a slot saved before the colour
    // transform ("Trying to blend XYB reference frame"), whatever the image's colour space.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Blending_ColourOntoASlotSavedBeforeTheColourTransform_Throws(bool saveBeforeColorTransform, bool throws)
        => AssertLayers(ColourOntoSlotSavedBeforeColourTransform(saveBeforeColorTransform), throws);

    // stage_blending.cc: a background smaller than the image ("Trying to use a WxH crop as a
    // background"). Only a reference-only frame saved before the colour transform can be cropped in its
    // slot, and only an extra channel can blend onto it, as the colour check above comes first.
    [Theory]
    [InlineData(24, 16, false)]
    [InlineData(23, 16, true)]
    [InlineData(24, 15, true)]
    [InlineData(30, 20, false)]
    public void Blending_OntoACropSmallerThanTheImage_Throws(int xsize, int ysize, bool throws)
        => AssertLayers(AlphaOntoCroppedSlot(xsize, ysize), throws);

    // frame_header.cc: a reference-only frame kept after the colour transform cannot be cropped.
    [Theory]
    [InlineData(true, 10, false)]
    [InlineData(false, 24, false)]
    [InlineData(false, 10, true)]
    public void ReferenceOnlyFrame_CroppedAndNotSavedBeforeTheColourTransform_Throws(bool saveBeforeColorTransform, int xsize, bool throws)
        => AssertLayers(CroppedReferenceOnly(saveBeforeColorTransform, xsize), throws);

    // frame_header.cc: a crop with no width or height.
    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void Crop_ZeroWidth_Throws(int xsize, bool throws)
        => AssertLayers(LayersWithCrop(xsize), throws);

    // The frame loop needs every frame up to the last one.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Layers_EndingBeforeTheLastFrame_Throw(bool truncate, bool throws)
    {
        byte[] codestream = new JxlTestLayers(24, 16)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(new JxlTestFrame(JxlTestLayers.ColourB) { IsLast = true })
            .ToArray();
        int firstFrameEnd = new JxlTestLayers(24, 16).Add(new JxlTestFrame(JxlTestLayers.ColourA)).ToArray().Length;
        AssertLayers(truncate ? codestream.AsSpan(0, firstFrameEnd).ToArray() : codestream, throws);
    }

    // libjxl rejects the same streams.
    [Theory]
    [InlineData("colour_onto_slot_saved_before_ct", true)]
    [InlineData("colour_onto_slot_saved_after_ct", false)]
    [InlineData("alpha_onto_23x16_crop", true)]
    [InlineData("alpha_onto_24x16_crop", false)]
    [InlineData("cropped_reference_only_after_ct", true)]
    [InlineData("zero_width_crop", true)]
    public async Task Djxl_RejectsTheSameLayers(string name, bool throws)
    {
        byte[] codestream = name switch
        {
            "colour_onto_slot_saved_before_ct" => ColourOntoSlotSavedBeforeColourTransform(true),
            "colour_onto_slot_saved_after_ct" => ColourOntoSlotSavedBeforeColourTransform(false),
            "alpha_onto_23x16_crop" => AlphaOntoCroppedSlot(23, 16),
            "alpha_onto_24x16_crop" => AlphaOntoCroppedSlot(24, 16),
            "cropped_reference_only_after_ct" => CroppedReferenceOnly(false, 10),
            _ => LayersWithCrop(0),
        };
        string input = Path.Combine(TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlBoundsTests)), name + ".jxl");
        await File.WriteAllBytesAsync(input, codestream, TestContext.Current.CancellationToken);

        (int exitCode, string output) = await JxlTools.RunForExitCodeAsync("djxl", TestContext.Current.CancellationToken, input, input + ".npy");

        Assert.True(throws == (exitCode != 0), $"djxl exited with code {exitCode}.{Environment.NewLine}{output}");
    }

    private static byte[] ColourOntoSlotSavedBeforeColourTransform(bool saveBeforeColorTransform)
        => new JxlTestLayers(24, 16)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { SaveAsReference = 1, SaveBeforeColorTransform = saveBeforeColorTransform })
            .Add(new JxlTestFrame(JxlTestLayers.ColourB) { IsLast = true, Color = new(JxlBlendMode.Add, Source: 1) })
            .ToArray();

    private static byte[] AlphaOntoCroppedSlot(int xsize, int ysize)
        => new JxlTestLayers(24, 16)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { Type = JxlFrameType.ReferenceOnly, XSize = xsize, YSize = ysize, SaveAsReference = 1, SaveBeforeColorTransform = true })
            .Add(new JxlTestFrame(JxlTestLayers.ColourB) { IsLast = true, ExtraChannel = new(JxlBlendMode.Add, Source: 1) })
            .ToArray();

    private static byte[] CroppedReferenceOnly(bool saveBeforeColorTransform, int xsize)
        => new JxlTestLayers(24, 16)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { Type = JxlFrameType.ReferenceOnly, Crop = true, XSize = xsize, YSize = 16, SaveAsReference = 1, SaveBeforeColorTransform = saveBeforeColorTransform })
            .Add(new JxlTestFrame(JxlTestLayers.ColourB) { IsLast = true })
            .ToArray();

    private static byte[] LayersWithCrop(int xsize)
        => new JxlTestLayers(24, 16)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(new JxlTestFrame(JxlTestLayers.ColourB) { IsLast = true, Crop = true, X0 = 2, Y0 = 2, XSize = xsize, YSize = 5 })
            .ToArray();

    private static void AssertLayers(byte[] codestream, bool throws)
    {
        using MemoryStream stream = new(codestream);
        if (throws)
        {
            Assert.Throws<InvalidImageContentException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None));
        }
        else
        {
            using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None);
            Assert.Single(decoded.Frames);
        }
    }

    private static void AssertBitDepth(JxlTestBitWriter writer, bool throws)
    {
        if (throws)
        {
            Assert.Throws<InvalidImageContentException>(() => JxlBundle.Read(writer.ToReader(), new JxlBitDepth()));
        }
        else
        {
            JxlBundle.Read(writer.ToReader(), new JxlBitDepth());
        }
    }

    private static JxlTestBitWriter Codestream() => new JxlTestBitWriter().Write(8, 0xFF).Write(8, 0x0A);

    private static IImageInfo Identify(JxlTestBitWriter writer)
    {
        using MemoryStream stream = new(writer.ToArray());
        return new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken.None);
    }

    // No LZ77, one histogram for every context, prefix codes with two 1-bit symbols: each bit is a token.
    private static void WriteTwoSymbolPrefixHistograms(JxlTestBitWriter writer, int numContexts)
    {
        writer.Bit(false);
        if (numContexts > 1)
        {
            writer.Bit(true).Write(2, 0); // simple context map, 0 bits per entry
        }

        writer.Bit(true);
        WriteUintConfigSplit4(writer);
        WriteVarLenUint16(writer, 1); // alphabet size 2
        writer.Write(2, 1).Write(2, 1).Write(1, 0).Write(1, 1); // simple prefix code: symbols 0 and 1
    }

    // LZ77 enabled, with a minimum symbol from 8 to 2^15 + 7 and the default minimum length of 3.
    private static void WriteLz77Params(JxlTestBitWriter writer, int minSymbol)
        => writer.Bit(true).Write(2, 3).Write(15, (ulong)(minSymbol - 8)).Write(2, 0);

    // Split exponent 4, no msb or lsb in the token, for log_alpha_size 15.
    private static void WriteUintConfigSplit4(JxlTestBitWriter writer) => writer.Write(4, 4).Write(3, 0).Write(3, 0);

    private static void WriteVarLenUint8(JxlTestBitWriter writer, int value) => WriteVarLen(writer, value, 3);

    private static void WriteVarLenUint16(JxlTestBitWriter writer, int value) => WriteVarLen(writer, value, 4);

    private static void WriteVarLen(JxlTestBitWriter writer, int value, int lengthBits)
    {
        if (value == 0)
        {
            writer.Bit(false);
            return;
        }

        writer.Bit(true);
        int nbits = 0;
        while ((value >> (nbits + 1)) != 0)
        {
            nbits++;
        }

        writer.Write(lengthBits, (ulong)nbits);
        if (nbits != 0)
        {
            writer.Write(nbits, (ulong)(value - (1 << nbits)));
        }
    }
}
