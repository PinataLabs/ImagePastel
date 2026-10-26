// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Writes a small VarDCT codestream: an 8-bit RGB image, not XYB, with one VarDCT frame of one DC group
/// (up to 2048 x 2048) whose blocks are DCT8 unless <see cref="Strategies"/> says otherwise. The frame
/// has no colour transform, or YCbCr with the <see cref="ChromaSubsampling"/> given. Each field
/// under test is a writer of its own, so a test can put one value over its bound and keep the rest
/// valid.
/// </summary>
/// <remarks>
/// <para>
/// The Modular streams (the VarDCT DC and the AC metadata) use a global MA tree of one leaf, the zero
/// predictor, so each sample is its own token. Every token of a stream coded with <see cref="WriteFlatPrefixHistograms"/>
/// costs 5 bits plus its raw bits: one prefix code of 32 symbols of length 5, with split exponent 0,
/// codes any value below 2^31.
/// </para>
/// <para>
/// The defaults decode to a flat grey frame: every DC and AC coefficient is 0, and the DCT8
/// quantization table has one distance band, which needs no <c>FastPowf</c>.
/// </para>
/// </remarks>
internal sealed class JxlTestVarDct
{
    private const int BlockDim = 8;
    private const int GroupDimInBlocks = 32;

    // libjxl's kHShift and kVShift (frame_header.h): how many times the channel of each mode is wider and
    // higher than the most subsampled channel, as a shift.
    private static readonly int[] HShiftTable = { 0, 1, 1, 0 };
    private static readonly int[] VShiftTable = { 0, 1, 0, 1 };

    // The order of the code length code lengths: kCodeLengthCodeOrder in lib/jxl/dec_huffman.cc.
    private static readonly int[] CodeLengthCodeOrder = { 1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

    public JxlTestVarDct(int width, int height)
    {
        this.Width = width;
        this.Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the writer of the quantiser: global_scale 1024 and quant_dc 16 by default.</summary>
    public Action<JxlTestBitWriter> Quantizer { get; init; } = w => WriteQuantizer(w, 1024, 16);

    /// <summary>Gets the writer of the block context map: the default map by default.</summary>
    public Action<JxlTestBitWriter> BlockCtxMap { get; init; } = w => w.Bit(true);

    /// <summary>Gets the writer of the chroma-from-luma DC: all default by default.</summary>
    public Action<JxlTestBitWriter> ColorCorrelationDc { get; init; } = w => w.Bit(true);

    /// <summary>
    /// Gets the raw strategy of each varblock, in the order of their first blocks; by default every
    /// block is DCT8.
    /// </summary>
    public int[] Strategies { get; init; }

    /// <summary>
    /// Gets the chroma subsampling mode of each channel, in Cb, Y, Cr order, for a frame with the YCbCr
    /// colour transform; <see langword="null"/>, the default, for a frame with no colour transform. A mode
    /// is the channel's JPEG sampling factors: 0 is 1 x 1, 1 is 2 x 2, 2 is 2 across and 3 is 2 down. So
    /// 4:2:0 is (0, 1, 0), and (1, 0, 1) subsamples Y.
    /// </summary>
    public int[] ChromaSubsampling { get; init; }

    /// <summary>
    /// Gets a value indicating whether the frame has the skip-adaptive-DC-smoothing flag. By default only a
    /// chroma-subsampled frame has it, as every JPEG transcode has: libjxl v0.12.0 rejects a subsampled
    /// VarDCT frame without it, which libjxl ef67fde2, the port's source, decodes.
    /// </summary>
    public bool? SkipAdaptiveDcSmoothing { get; init; }

    /// <summary>Gets the number of AC metadata entries, when it is not the number of varblocks.</summary>
    public int? AcMetadataCount { get; init; }

    /// <summary>Gets the EPF sharpness of every block.</summary>
    public int Sharpness { get; init; }

    /// <summary>Gets the writer of the dequantization matrices: a one-band DCT table 0, library tables otherwise.</summary>
    public Action<JxlTestBitWriter> DequantMatrices { get; init; } = w => WriteDequantMatrices(w, 0, t => WriteOneBandDct(t, 0x3C00));

    /// <summary>Gets the number of histogram sets of the AC groups.</summary>
    public int NumHistograms { get; init; } = 1;

    /// <summary>Gets the writer of the used orders and the coefficient orders: none by default.</summary>
    public Action<JxlTestBitWriter> CoeffOrders { get; init; } = w => w.Write(2, 2);

    /// <summary>
    /// Gets the initial ANS state of the AC groups. When set, the AC histograms are one ANS histogram of
    /// the single symbol 0, which never changes the state, so every block is empty and the final-state
    /// check passes only for 0x130000. Otherwise they are the flat prefix code.
    /// </summary>
    public uint? AcAnsState { get; init; }

    /// <summary>Gets the histogram selector of each AC group, 0 by default.</summary>
    public int[] HistogramSelectors { get; init; }

    /// <summary>
    /// Gets the tokens of the Y channel of the first block of AC group 0: its non-zero count, then its
    /// coefficient symbols in scan order. Every other block has no non-zeros.
    /// </summary>
    public uint[] FirstBlockTokens { get; init; }

    /// <summary>Gets the writer of the TOC's permutation, as <see cref="JxlTestFrame.TocPermutation"/>; none by default.</summary>
    public Action<JxlTestBitWriter> TocPermutation { get; init; }

    /// <summary>
    /// Gets the order the sections are stored in: entry k is the section, in decoding order, stored k-th.
    /// By default they are stored in decoding order.
    /// </summary>
    public int[] StoredSections { get; init; }

    // FrameDimensions::Set: the blocks are rounded up to whole blocks of the most subsampled channel.
    private int XSizeBlocks => ((this.Width + (BlockDim << this.MaxHShift) - 1) / (BlockDim << this.MaxHShift)) << this.MaxHShift;

    private int YSizeBlocks => ((this.Height + (BlockDim << this.MaxVShift) - 1) / (BlockDim << this.MaxVShift)) << this.MaxVShift;

    private int MaxHShift => this.ChromaSubsampling?.Max(m => HShiftTable[m]) ?? 0;

    private int MaxVShift => this.ChromaSubsampling?.Max(m => VShiftTable[m]) ?? 0;

    private int XSizeGroups => (this.XSizeBlocks + GroupDimInBlocks - 1) / GroupDimInBlocks;

    private int NumGroups => this.XSizeGroups * ((this.YSizeBlocks + GroupDimInBlocks - 1) / GroupDimInBlocks);

    /// <summary>Writes the quantiser: global_scale up to 2048 and quant_dc from 1 to 32.</summary>
    public static void WriteQuantizer(JxlTestBitWriter w, int globalScale, int quantDc)
    {
        w.Write(2, 0).Write(11, (ulong)(globalScale - 1)); // BitsOffset(11, 1)
        if (quantDc == 16)
        {
            w.Write(2, 0); // Val(16)
        }
        else
        {
            w.Write(2, 1).Write(5, (ulong)(quantDc - 1)); // BitsOffset(5, 1)
        }
    }

    /// <summary>
    /// Writes the dequantization matrices, not all default: <paramref name="table"/> is written by
    /// <paramref name="encoding"/>, table 0 (DCT8) has one distance band unless it is that table, and the
    /// others are library tables.
    /// </summary>
    public static void WriteDequantMatrices(JxlTestBitWriter w, int table, Action<JxlTestBitWriter> encoding)
    {
        w.Bit(false);
        for (int i = 0; i < JxlDequantMatrices.NumQuantTables; i++)
        {
            if (i == table)
            {
                encoding(w);
            }
            else if (i == 0)
            {
                WriteOneBandDct(w, 0x3C00);
            }
            else
            {
                w.Write(3, 0); // kQuantModeLibrary, predefined table 0 in 0 bits
            }
        }
    }

    /// <summary>Writes a kQuantModeDCT encoding with one distance band, whose seed is an F16 for every channel.</summary>
    public static void WriteOneBandDct(JxlTestBitWriter w, ushort seed)
    {
        w.Write(3, 6); // kQuantModeDCT
        WriteDctParams(w, seed);
    }

    /// <summary>
    /// Writes a kQuantModeRAW encoding of a table of <paramref name="blocks"/> 8x8 blocks (libjxl's
    /// <c>required_size_x * required_size_y</c>, one by default): the F16 denominator, then a Modular stream
    /// on the global tree of the 3 x 64 x <paramref name="blocks"/> entries, channel by channel and row by
    /// row, entry i being <paramref name="entry"/>(i).
    /// </summary>
    public static void WriteRawTable(JxlTestBitWriter w, ushort denominator, Func<int, int> entry, int blocks = 1)
    {
        w.Write(3, 7); // kQuantModeRAW
        w.Write(16, denominator);
        WriteModularStream(w, Enumerable.Range(0, 3 * 64 * blocks).Select(entry));
    }

    /// <summary>Writes the distance bands of one band, whose seed is an F16 for every channel.</summary>
    public static void WriteDctParams(JxlTestBitWriter w, params ushort[] bands)
    {
        w.Write(4, (ulong)(bands.Length - 1));
        for (int c = 0; c < 3; c++)
        {
            foreach (ushort band in bands)
            {
                w.Write(16, band);
            }
        }
    }

    /// <summary>
    /// Writes histograms for <paramref name="numContexts"/> contexts: no LZ77, every context on one
    /// prefix code of 32 symbols of length 5, with split exponent 0. <see cref="WriteToken"/> then
    /// writes each value.
    /// </summary>
    public static void WriteFlatPrefixHistograms(JxlTestBitWriter w, int numContexts)
    {
        w.Bit(false); // no LZ77
        if (numContexts > 1)
        {
            w.Bit(true).Write(2, 0); // simple context map, 0 bits per entry
        }

        w.Bit(true); // prefix codes
        w.Write(4, 0); // split exponent 0, so no msb or lsb fields
        WriteVarLenUint16(w, 31); // alphabet size 32

        // A complex code whose only code length code is 5, so every symbol has length 5.
        w.Write(2, 0);
        foreach (int codeLength in CodeLengthCodeOrder)
        {
            w.Write(2, codeLength == 5 ? 1UL : 0UL); // code length code length 4, or 0
        }
    }

    /// <summary>Writes one value with the code of <see cref="WriteFlatPrefixHistograms"/>.</summary>
    public static void WriteToken(JxlTestBitWriter w, uint value)
    {
        int token = 0;
        int rawBits = 0;
        if (value != 0)
        {
            rawBits = JxlBits.FloorLog2Nonzero(value);
            token = rawBits + 1;
        }

        // A canonical code is read most significant bit first.
        ulong code = 0;
        for (int i = 0; i < 5; i++)
        {
            code |= (ulong)((token >> i) & 1) << (4 - i);
        }

        w.Write(5, code).Write(rawBits, value - (value == 0 ? 0 : 1u << rawBits));
    }

    /// <summary>
    /// Writes histograms for <paramref name="numContexts"/> contexts: no LZ77, every context on one ANS
    /// histogram of the single symbol 0. The 32-bit initial ANS state follows where the reader starts.
    /// </summary>
    public static void WriteAnsZeroHistograms(JxlTestBitWriter w, int numContexts)
    {
        w.Bit(false); // no LZ77
        if (numContexts > 1)
        {
            w.Bit(true).Write(2, 0); // simple context map, 0 bits per entry
        }

        w.Bit(false).Write(2, 0); // ANS, log alpha size 5
        w.Write(3, 0); // split exponent 0
        w.Bit(true).Bit(false).Bit(false); // simple histogram of the one symbol 0
    }

    /// <summary>Writes a block context map that is not the default, with the thresholds given and a simple context map of one context.</summary>
    public static void WriteBlockCtxMap(JxlTestBitWriter w, int xThresholds, int yThresholds, int bThresholds, int qfThresholds, Action<JxlTestBitWriter> contextMap = null)
    {
        w.Bit(false);
        foreach (int n in new[] { xThresholds, yThresholds, bThresholds })
        {
            w.Write(4, (ulong)n);
            for (int i = 0; i < n; i++)
            {
                w.Write(2, 0).Write(4, 0); // threshold 0
            }
        }

        w.Write(4, (ulong)qfThresholds);
        for (int i = 0; i < qfThresholds; i++)
        {
            w.Write(2, 0).Write(2, 0); // threshold 1
        }

        if (contextMap != null)
        {
            contextMap(w);
        }
        else
        {
            w.Bit(true).Write(2, 0); // simple context map, 0 bits per entry
        }
    }

    public byte[] ToArray()
    {
        JxlTestBitWriter dcGlobal = new();
        this.WriteDcGlobal(dcGlobal);
        JxlTestBitWriter dcGroup = this.NumGroups == 1 ? dcGlobal : new JxlTestBitWriter();
        this.WriteDcGroup(dcGroup);
        JxlTestBitWriter acGlobal = this.NumGroups == 1 ? dcGlobal : new JxlTestBitWriter();
        this.WriteAcGlobal(acGlobal);

        List<byte[]> sections = new();
        if (this.NumGroups == 1)
        {
            this.WriteAcGroup(dcGlobal, 0);
            sections.Add(dcGlobal.ZeroPadToByte().ToArray());
        }
        else
        {
            sections.Add(dcGlobal.ZeroPadToByte().ToArray());
            sections.Add(dcGroup.ZeroPadToByte().ToArray());
            sections.Add(acGlobal.ZeroPadToByte().ToArray());
            for (int g = 0; g < this.NumGroups; g++)
            {
                JxlTestBitWriter acGroup = new();
                this.WriteAcGroup(acGroup, g);
                sections.Add(acGroup.ZeroPadToByte().ToArray());
            }
        }

        if (this.StoredSections != null)
        {
            sections = this.StoredSections.Select(i => sections[i]).ToList();
        }

        JxlTestFrame frame = new(sections[0])
        {
            Encoding = JxlFrameEncoding.VarDct,
            IsLast = true,
            Sections = sections.Count > 1 ? sections.ToArray() : null,
            TocPermutation = this.TocPermutation,
            Flags = (this.SkipAdaptiveDcSmoothing ?? (this.MaxHShift != 0 || this.MaxVShift != 0)) ? 128UL : 0,
            YCbCr = this.ChromaSubsampling != null,
            ChromaSubsampling = this.ChromaSubsampling,
        };
        return new JxlTestLayers(this.Width, this.Height, alpha: false).Add(frame).ToArray();
    }

    private static void WriteVarLenUint16(JxlTestBitWriter w, int value)
    {
        if (value == 0)
        {
            w.Bit(false);
            return;
        }

        int nbits = JxlBits.FloorLog2Nonzero((uint)value);
        w.Bit(true).Write(4, (ulong)nbits);
        w.Write(nbits, (ulong)(value - (1 << nbits)));
    }

    // A Modular sub-stream that uses the global tree, with default weighted predictor parameters and no
    // transforms, and then its samples.
    private static void WriteModularStream(JxlTestBitWriter w, IEnumerable<int> samples)
    {
        w.Bit(true).Bit(true).Write(2, 0);
        foreach (int sample in samples)
        {
            WriteToken(w, JxlBits.PackSigned(sample));
        }
    }

    private static IEnumerable<int> Repeat(int value, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return value;
        }
    }

    // The number of blocks of an xsize x ysize area that colour channel c (0 is X or Cb, 1 is Y, 2 is B or
    // Cr) codes: one in 2^shift each way. The area is whole blocks of the most subsampled channel.
    private int ChannelBlocks(int c, int xsize, int ysize)
    {
        if (this.ChromaSubsampling == null)
        {
            return xsize * ysize;
        }

        int mode = this.ChromaSubsampling[c];
        return (xsize >> (this.MaxHShift - HShiftTable[mode])) * (ysize >> (this.MaxVShift - VShiftTable[mode]));
    }

    private int AllChannelBlocks(int xsize, int ysize)
        => this.ChannelBlocks(0, xsize, ysize) + this.ChannelBlocks(1, xsize, ysize) + this.ChannelBlocks(2, xsize, ysize);

    private void WriteDcGlobal(JxlTestBitWriter w)
    {
        w.Bit(true); // default DC quantization
        this.Quantizer(w);
        this.BlockCtxMap(w);
        this.ColorCorrelationDc(w);

        // The global MA tree: tree histograms of the single symbol 0, so the tree is one leaf with the
        // zero predictor, then the histograms of its one context. The global Modular image has no
        // channels, so nothing else follows.
        w.Bit(true);
        w.Bit(false).Bit(true).Write(2, 0); // no LZ77, simple context map of 6 contexts on histogram 0
        w.Bit(true).Write(4, 0).Bit(false); // prefix codes, split exponent 0, alphabet size 1
        WriteFlatPrefixHistograms(w, 1);
    }

    private void WriteDcGroup(JxlTestBitWriter w)
    {
        int numBlocks = this.XSizeBlocks * this.YSizeBlocks;

        // VarDCT DC: no extra precision, then the Y, X and B channels, all 0, each at its subsampled size.
        w.Write(2, 0);
        WriteModularStream(w, Repeat(0, this.AllChannelBlocks(this.XSizeBlocks, this.YSizeBlocks)));

        // AC metadata: the count, then the YtoX and YtoB maps of the colour tiles, the strategy and quant
        // field of each varblock, and the EPF sharpness of each block.
        int[] strategies = this.Strategies ?? new int[numBlocks];
        int count = this.AcMetadataCount ?? strategies.Length;
        w.Write(JxlBits.CeilLog2Nonzero((uint)numBlocks), (ulong)(count - 1));
        int numTiles = ((this.XSizeBlocks + 7) / 8) * ((this.YSizeBlocks + 7) / 8);
        List<int> samples = new();
        samples.AddRange(Repeat(0, 2 * numTiles));
        for (int i = 0; i < count; i++)
        {
            samples.Add(i < strategies.Length ? strategies[i] : 0);
        }

        samples.AddRange(Repeat(0, count)); // quant field 1
        samples.AddRange(Repeat(this.Sharpness, numBlocks));
        WriteModularStream(w, samples);
    }

    private void WriteAcGlobal(JxlTestBitWriter w)
    {
        this.DequantMatrices(w);
        w.Write(JxlBits.CeilLog2Nonzero((uint)this.NumGroups), (ulong)(this.NumHistograms - 1));
        this.CoeffOrders(w);

        // The AC histograms: the context map has no bits, so their number does not matter.
        if (this.AcAnsState != null)
        {
            WriteAnsZeroHistograms(w, 2);
        }
        else
        {
            WriteFlatPrefixHistograms(w, 2);
        }
    }

    private void WriteAcGroup(JxlTestBitWriter w, int group)
    {
        int selector = this.HistogramSelectors?[group] ?? 0;
        w.Write(JxlBits.CeilLog2Nonzero((uint)this.NumHistograms), (ulong)selector);
        if (this.AcAnsState is uint state)
        {
            w.Write(32, state);
            return;
        }

        int gx = group % this.XSizeGroups;
        int gy = group / this.XSizeGroups;
        int xsize = Math.Min(GroupDimInBlocks, this.XSizeBlocks - (gx * GroupDimInBlocks));
        int ysize = Math.Min(GroupDimInBlocks, this.YSizeBlocks - (gy * GroupDimInBlocks));
        for (int i = 0; i < this.AllChannelBlocks(xsize, ysize); i++)
        {
            if (group == 0 && i == 0 && this.FirstBlockTokens != null)
            {
                foreach (uint token in this.FirstBlockTokens)
                {
                    WriteToken(w, token);
                }
            }
            else
            {
                WriteToken(w, 0); // no non-zeros
            }
        }
    }
}
