// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Icc;
using SixLabors.ImageSharp.Formats.Jxl.Modular;
using SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Slice D14c: the resource caps of #112 that libjxl does not have, or has at a higher value. Input over
/// a cap throws <see cref="InvalidImageContentException"/> before the decoder allocates or loops over it.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlResourceCapsTests
{
    private const int IccCap = 1 << 22;

    private const int BoxCap = 64 << 20;

    private const int ChannelCap = 1 << 14;

    [Theory]
    [InlineData(IccCap, false)]
    [InlineData(IccCap + 1, true)]
    public void Icc_DecodedSizeOver2Pow22_Throws(int decodedSize, bool throws)
    {
        // Only the preamble: the decoded size, no commands, then zeros. The decoded size is checked once
        // the preamble is read; a profile under the cap fails later, as this one has no data for it.
        List<byte> enc = new();
        EncodeVarInt((ulong)decodedSize, enc);
        EncodeVarInt(0, enc);
        enc.AddRange(new byte[64 - enc.Count]);

        JxlBitWriter writer = new();
        WriteIccStream(enc, writer);
        JxlBitReader reader = new(writer.ToArray());

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => JxlIccReader.Read(reader, TestContext.Current.CancellationToken));
        Assert.Equal(throws, ex.Message.Contains("Decoded ICC is too large"));
    }

    [Fact]
    public void Icc_DecodedSizeOver2Pow22_ThrowsFromIdentify()
    {
        List<byte> enc = new();
        EncodeVarInt(IccCap + 1, enc);
        EncodeVarInt(0, enc);
        enc.AddRange(new byte[64 - enc.Count]);

        JxlBitWriter writer = new();
        writer.Write(8, 0xFF);
        writer.Write(8, 0x0A);
        writer.Write(1, 1); // size header: small
        writer.Write(5, 0); // height 8
        writer.Write(3, 1); // ratio 1: width 8
        writer.Write(1, 0); // image metadata: not all default
        writer.Write(1, 0); // no extra fields
        writer.Write(1, 0); // integer samples
        writer.Write(2, 0); // 8 bits
        writer.Write(1, 1); // 16-bit buffers suffice
        writer.Write(2, 0); // no extra channels
        writer.Write(1, 0); // not XYB
        writer.Write(1, 0); // colour encoding not all default
        writer.Write(1, 1); // an embedded ICC profile
        writer.Write(2, 0); // RGB
        writer.Write(2, 0); // no extensions
        writer.Write(1, 1); // all-default transform data
        WriteIccStream(enc, writer);

        using MemoryStream stream = new(writer.ToArray());
        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(
            () => new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken.None));
        Assert.Contains("Decoded ICC is too large", ex.Message);
    }

    [Fact]
    public void Icc_ProfileOfExactly2Pow22Bytes_Decodes()
    {
        // Arbitrary bytes: the ICC codec takes any byte string. Random bytes keep the stream above the
        // reader's 256:1 ratio check, which a run of zeros coded in no bits would fail. The tag count is
        // zero, so the commands fit in the first 1024 encoded bytes, where the preamble check reads them.
        byte[] profile = new byte[IccCap];
        new Random(22).NextBytes(profile);
        profile.AsSpan(128, 4).Clear();

        JxlBitWriter writer = new();
        JxlIccWriter.WriteIcc(profile, writer);

        byte[] decoded = JxlIccReader.Read(new JxlBitReader(writer.ToArray()), TestContext.Current.CancellationToken);

        Assert.Equal(profile, decoded);
    }

    [Theory]
    [InlineData("Exif", false)]
    [InlineData("xml ", false)]
    [InlineData("Exif", true)]
    [InlineData("xml ", true)]
    public void MetadataBox_Over64MiB_ThrowsWithoutAllocatingTheBox(string type, bool brotli)
    {
        byte[] file = WithMetadataBox(type, BoxCap + 1, brotli);

        // The file itself is in memory already; the check must come before the box is copied out of
        // it, or decompressed in full.
        long before = GC.GetAllocatedBytesForCurrentThread();
        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => JxlContainer.Read(file, readMetadata: true, CancellationToken.None));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Contains("too large", ex.Message);
        Assert.True(allocated < 4 << 20, $"Allocated {allocated} bytes");
    }

    [Theory]
    [InlineData("Exif", false)]
    [InlineData("xml ", true)]
    public void MetadataBox_Over64MiB_ThrowsFromIdentify(string type, bool brotli)
    {
        using MemoryStream stream = new(WithMetadataBox(type, BoxCap + 1, brotli));

        Assert.Throws<InvalidImageContentException>(() => new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken.None));
    }

    [Theory]
    [InlineData("Exif", false)]
    [InlineData("xml ", true)]
    public void MetadataBox_Of64MiB_IsRead(string type, bool brotli)
    {
        JxlContainerContents contents = JxlContainer.Read(WithMetadataBox(type, BoxCap, brotli), readMetadata: true, CancellationToken.None);

        // A box of zeros: the Exif box has no TIFF header, so it gives no profile.
        if (type == "xml ")
        {
            Assert.Equal(BoxCap, contents.Xmp.Length);
        }
        else
        {
            Assert.Null(contents.Exif);
        }
    }

    [Fact]
    public void MetadataBox_Over64MiB_IsIgnoredWhenMetadataIsSkipped()
    {
        JxlContainerContents contents = JxlContainer.Read(WithMetadataBox("xml ", BoxCap + 1, brotli: true), readMetadata: false, CancellationToken.None);

        Assert.Null(contents.Xmp);
    }

    [Fact]
    public async Task Modular_RepeatedSqueezesOfMetaChannels_StopAtTheChannelCap()
    {
        // #161: every step of 272 transforms of 296 steps, which would make about 1.5 million channels.
        // The cap's message shows that the squeezes stopped at 2^14 channels, which bounds the work; the
        // test does not time it, as a time limit can fail at random in a parallel run.
        byte[] codestream = MetaSqueezeCodestream(int.MaxValue);

        // Run aside, so that a decoder without the cap fails the test instead of hanging the run. The
        // limit is a hang guard only: the decode takes about 0.3 s in Debug.
        Task<Exception> decode = Task.Run(() =>
        {
            using MemoryStream stream = new(codestream);
            return Record.Exception(() => new JxlDecoder().Decode(Configuration.Default, stream, CancellationToken.None).Dispose());
        });

        Task finished = await Task.WhenAny(decode, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.True(finished == decode, "The decode did not finish in 30 s");
        InvalidImageContentException ex = Assert.IsType<InvalidImageContentException>(await decode);
        Assert.Contains("Too many Modular channels", ex.Message);
    }

    [Theory]
    [InlineData(ChannelCap, false)]
    [InlineData(ChannelCap + 1, true)]
    public void Modular_ChannelsOverTheCap_Throw(int channels, bool throws)
    {
        // The frame has three channels, and the palette makes four. The stream ends after the transforms,
        // so a frame under the cap fails later, for want of its MA tree.
        using MemoryStream stream = new(MetaSqueezeCodestream(channels - 4));

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => new JxlDecoder().Decode(Configuration.Default, stream, CancellationToken.None));
        Assert.Equal(throws, ex.Message.Contains("Too many Modular channels"));
    }

    // An 8 x 8 RGB Modular frame whose global stream ends after its transforms (#161). A palette of
    // channel 0 makes a meta channel 65536 wide. Meta channels keep a shift of -1, so the shift > 30
    // check never stops squeezing them, and each step adds a residual channel for each channel it
    // squeezes. The steps double the meta channels up to 19 at a time (the most one step can name),
    // then squeeze the first 19 again and again, until they have added `added` channels or every step
    // of 272 transforms of 296 steps is used.
    private static byte[] MetaSqueezeCodestream(int added)
    {
        JxlGroupHeader header = new();
        header.Transforms.Add(new JxlTransform { Id = JxlTransformId.Palette, BeginC = 0, NumC = 1, NbColors = 1 << 16 });
        long metaChannels = 1;
        long left = added;
        for (int t = 0; t < 272 && left > 0; t++)
        {
            JxlTransform squeeze = new() { Id = JxlTransformId.Squeeze };
            for (int s = 0; s < 296 && left > 0; s++)
            {
                uint numC = (uint)Math.Min(Math.Min(metaChannels, 19), left);
                squeeze.Squeezes.Add(new JxlSqueezeParams { Horizontal = true, InPlace = true, BeginC = 0, NumC = numC });
                metaChannels += numC;
                left -= numC;
            }

            header.Transforms.Add(squeeze);
        }

        // The DC global section of a Modular frame: default DC dequantisation, no global tree, then the
        // global stream's group header. MetaApply runs as soon as the header is read.
        JxlBitWriter section = new();
        section.Write(1, 1);
        section.Write(1, 0);
        JxlBundle.Write(header, section);
        section.ZeroPadToByte();
        return new JxlTestLayers(8, 8, alpha: false).Add(JxlTestFrame.Last(section.ToArray())).ToArray();
    }

    // A container of the bare 8 x 8 codestream of JxlTestLayers, then an Exif or XMP box whose contents
    // are `size` zero bytes, plain or Brotli-compressed in a brob box.
    private static byte[] WithMetadataBox(string type, int size, bool brotli)
    {
        byte[] codestream = new JxlTestLayers(8, 8, alpha: false).Add(JxlTestFrame.Last(JxlTestLayers.ColourA)).ToArray();
        byte[] contents;
        string boxType = type;
        if (brotli)
        {
            using MemoryStream compressed = new();
            compressed.Write(Encoding.ASCII.GetBytes(type));
            using (BrotliStream brotliStream = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                byte[] zeros = new byte[1 << 20];
                for (int left = size; left > 0; left -= zeros.Length)
                {
                    brotliStream.Write(zeros, 0, Math.Min(left, zeros.Length));
                }
            }

            contents = compressed.ToArray();
            boxType = "brob";
        }
        else
        {
            contents = new byte[size];
        }

        using MemoryStream file = new();
        file.Write(JxlConstants.ContainerSignature);
        WriteBox(file, "ftyp", Encoding.ASCII.GetBytes("jxl \0\0\0\0jxl "));
        WriteBox(file, "jxlc", codestream);
        WriteBox(file, boxType, contents);
        return file.ToArray();
    }

    private static void WriteBox(Stream stream, string type, byte[] contents)
    {
        byte[] header = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)(8 + contents.Length));
        Encoding.ASCII.GetBytes(type).CopyTo(header, 4);
        stream.Write(header);
        stream.Write(contents);
    }

    // A base-128 varint, low group first, as the ICC codec stores its sizes.
    private static void EncodeVarInt(ulong value, List<byte> data)
    {
        while (value > 127)
        {
            data.Add((byte)(128 | (value & 127)));
            value >>= 7;
        }

        data.Add((byte)value);
    }

    // The ICC stream as JxlIccWriter.WriteIcc writes it, for encoded bytes that the writer would refuse:
    // the encoded size, prefix-coded histograms, then one token per encoded byte.
    private static void WriteIccStream(List<byte> enc, JxlBitWriter writer)
    {
        List<JxlToken>[] tokens = { new(enc.Count) };
        JxlFieldCoders.WriteU64((ulong)enc.Count, writer);
        for (int i = 0; i < enc.Count; i++)
        {
            uint ctx = (uint)JxlIccCodecCommon.IccAnsContext(i, i > 0 ? enc[i - 1] : 0, i > 1 ? enc[i - 2] : 0);
            tokens[0].Add(new JxlToken(ctx, enc[i]));
        }

        JxlHistogramParams parameters = new() { Lz77 = JxlHistogramParams.Lz77Method.None, ForceHuffman = true };
        JxlEntropyEncodingData code = new();
        JxlEntropyEncoder.BuildAndEncodeHistograms(parameters, JxlIccCodecCommon.NumIccContexts, tokens, code, writer);
        JxlEntropyEncoder.WriteTokens(tokens[0], code, 0, writer);
    }
}
