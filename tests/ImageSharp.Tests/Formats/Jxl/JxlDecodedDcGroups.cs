// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The first frame of a bare VarDCT codestream, decoded up to its DC groups with the decoder's own steps
/// (<see cref="JxlFrameDecoder.ProcessDcGlobal"/> and <see cref="JxlFrameDecoder.ProcessDcGroup"/>), so
/// that a test can read what they fill: the AC metadata, the quant field and the chroma-from-luma maps.
/// <see cref="DecodeAcGlobal"/> then reads the AC global section, for the coefficient orders.
/// </summary>
internal sealed class JxlDecodedDcGroups : IDisposable
{
    private readonly JxlModularFrameDecoder modularFrameDecoder;

    private readonly JxlBitReader acGlobal;

    private JxlDecodedDcGroups(JxlFrameHeader frameHeader, JxlVarDctFrameState state, JxlModularFrameDecoder modularFrameDecoder, JxlBitReader acGlobal)
    {
        this.FrameHeader = frameHeader;
        this.State = state;
        this.modularFrameDecoder = modularFrameDecoder;
        this.acGlobal = acGlobal;
    }

    /// <summary>Gets the frame header.</summary>
    public JxlFrameHeader FrameHeader { get; }

    /// <summary>Gets the VarDCT state, with every DC group decoded.</summary>
    public JxlVarDctFrameState State { get; }

    /// <summary>Gets the extra DC precision of each DC group, as its section codes it.</summary>
    public int[] ExtraDcPrecision { get; private init; }

    /// <summary>Gets the transforms of the global Modular stream, which codes the extra channels.</summary>
    public IReadOnlyList<JxlTransform> GlobalTransforms => this.modularFrameDecoder.GlobalTransforms;

    /// <summary>Decodes the DC global section and the DC groups of the first frame.</summary>
    /// <param name="codestream">A bare codestream whose first frame is VarDCT, with no patches.</param>
    /// <returns>The decoded DC groups.</returns>
    public static JxlDecodedDcGroups Decode(byte[] codestream)
    {
        // The same steps as JxlDecoderCore.ReadHeaders and JxlFrameDecoder.DecodeFrame.
        JxlCodecMetadata metadata = JxlCodestreamFrames.ReadImageHeader(codestream, out int frameOffset);
        JxlBitReader reader = new(codestream, frameOffset, codestream.Length - frameOffset);
        JxlFrameHeader frameHeader = new(metadata);
        JxlBundle.Read(reader, frameHeader);
        Assert.Equal(JxlFrameEncoding.VarDct, frameHeader.Encoding);
        Assert.Equal(JxlFrameFlags.None, frameHeader.Flags & JxlFrameFlags.Patches);
        JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();
        long tocEntries = JxlToc.NumTocEntries(frameDim.NumGroups, frameDim.NumDcGroups, frameHeader.Passes.NumPasses);
        JxlGroupOffsets groupOffsets = JxlToc.ReadGroupOffsets(tocEntries, reader);
        reader.EnsureAllReadsWithinBounds("frame header");
        int offset = frameOffset + (int)(reader.TotalBitsConsumed / 8);
        reader.Close();

        JxlBitReader[] sections = new JxlBitReader[tocEntries];
        for (long i = 0; i < tocEntries; i++)
        {
            sections[i] = new JxlBitReader(codestream, offset + (int)groupOffsets.Offsets[i], (int)groupOffsets.Sizes[i]);
        }

        // A frame of one group and one pass has every section in one.
        bool singleSection = tocEntries == 1;
        JxlDequantMatrices matrices = new();
        JxlModularFrameDecoder modularFrameDecoder = new(Configuration.Default.MemoryAllocator, frameDim, matrices);
        JxlVarDctFrameState state = new(Configuration.Default.MemoryAllocator, frameHeader, frameDim, matrices);
        JxlFrameDecoder.ProcessDcGlobal(sections[0], frameHeader, frameDim, JxlImageFeatures.None, state.Cmap, matrices, modularFrameDecoder, state, CancellationToken.None);
        int[] extraDcPrecision = new int[frameDim.NumDcGroups];
        for (long i = 0; i < frameDim.NumDcGroups; i++)
        {
            // Each DC group starts with its 2-bit extra DC precision, which the decoder reads first.
            JxlBitReader dcGroup = singleSection ? sections[0] : sections[1 + i];
            dcGroup.Refill();
            extraDcPrecision[i] = (int)dcGroup.PeekBits(2);
            JxlFrameDecoder.ProcessDcGroup(i, dcGroup, frameHeader, frameDim, modularFrameDecoder, state);
        }

        // The AC global section follows the DC groups; in a frame of one section it is the rest of it.
        JxlBitReader acGlobal = singleSection ? sections[0] : sections[1 + frameDim.NumDcGroups];
        return new JxlDecodedDcGroups(frameHeader, state, modularFrameDecoder, acGlobal) { ExtraDcPrecision = extraDcPrecision };
    }

    /// <summary>
    /// Reads the AC global section with the decoder's own steps (<see cref="JxlVarDctFrameState.InitForAc"/>
    /// and <see cref="JxlVarDctFrameState.ProcessAcGlobal"/>): the dequantization matrices, the number of
    /// histogram sets, and the coefficient orders and histograms of each pass.
    /// </summary>
    public void DecodeAcGlobal()
    {
        int numPasses = (int)this.FrameHeader.Passes.NumPasses;
        this.State.InitForAc(numPasses);
        this.State.ProcessAcGlobal(this.acGlobal, numPasses, this.modularFrameDecoder);
    }

    /// <summary>
    /// Returns the smallest and the largest quant field value over the first blocks of the varblocks:
    /// the blocks whose value the decoder reads.
    /// </summary>
    /// <returns>The range.</returns>
    public (int Min, int Max) QuantFieldRange()
    {
        int min = int.MaxValue;
        int max = int.MinValue;
        for (int by = 0; by < this.State.YSizeBlocks; by++)
        {
            Span<int> row = this.State.RawQuantField.DangerousGetRowSpan(by);
            for (int bx = 0; bx < this.State.XSizeBlocks; bx++)
            {
                if (this.State.AcStrategy.Get(bx, by).IsFirstBlock)
                {
                    min = Math.Min(min, row[bx]);
                    max = Math.Max(max, row[bx]);
                }
            }
        }

        return (min, max);
    }

    // How many first blocks have a raw quant field value above and below `value`, and how many first
    // blocks there are.
    public (int Above, int Below, int Blocks) QuantFieldAround(int value)
    {
        int above = 0;
        int below = 0;
        int blocks = 0;
        for (int by = 0; by < this.State.YSizeBlocks; by++)
        {
            Span<int> row = this.State.RawQuantField.DangerousGetRowSpan(by);
            for (int bx = 0; bx < this.State.XSizeBlocks; bx++)
            {
                if (this.State.AcStrategy.Get(bx, by).IsFirstBlock)
                {
                    blocks++;
                    above += row[bx] > value ? 1 : 0;
                    below += row[bx] < value ? 1 : 0;
                }
            }
        }

        return (above, below, blocks);
    }

    public void Dispose()
    {
        this.State.Dispose();
        this.modularFrameDecoder.Dispose();
    }
}
