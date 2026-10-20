// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Icc;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The first frame of a bare VarDCT codestream, decoded up to its DC groups with the decoder's own steps
/// (<see cref="JxlFrameDecoder.ProcessDcGlobal"/> and <see cref="JxlFrameDecoder.ProcessDcGroup"/>), so
/// that a test can read what they fill: the AC metadata, the quant field and the chroma-from-luma maps.
/// </summary>
internal sealed class JxlDecodedDcGroups : IDisposable
{
    private readonly JxlModularFrameDecoder modularFrameDecoder;

    private JxlDecodedDcGroups(JxlFrameHeader frameHeader, JxlVarDctFrameState state, JxlModularFrameDecoder modularFrameDecoder)
    {
        this.FrameHeader = frameHeader;
        this.State = state;
        this.modularFrameDecoder = modularFrameDecoder;
    }

    /// <summary>Gets the frame header.</summary>
    public JxlFrameHeader FrameHeader { get; }

    /// <summary>Gets the VarDCT state, with every DC group decoded.</summary>
    public JxlVarDctFrameState State { get; }

    /// <summary>Decodes the DC global section and the DC groups of the first frame.</summary>
    /// <param name="codestream">A bare codestream whose first frame is VarDCT, with no patches.</param>
    /// <returns>The decoded DC groups.</returns>
    public static JxlDecodedDcGroups Decode(byte[] codestream)
    {
        Assert.Equal(JxlConstants.CodestreamSignature, codestream[..2]);

        // The same steps as JxlDecoderCore.ReadHeaders and JxlFrameDecoder.DecodeFrame.
        JxlCodecMetadata metadata = new();
        JxlBitReader reader = new(codestream, 2, codestream.Length - 2);
        JxlBundle.Read(reader, metadata.Size);
        JxlBundle.Read(reader, metadata.M);
        metadata.TransformData.NonserializedXybEncoded = metadata.M.XybEncoded;
        JxlBundle.Read(reader, metadata.TransformData);
        if (metadata.M.ColorEncoding.WantIcc)
        {
            JxlIccReader.Read(reader);
        }

        reader.JumpToByteBoundary();
        JxlFrameHeader frameHeader = new(metadata);
        JxlBundle.Read(reader, frameHeader);
        Assert.Equal(JxlFrameEncoding.VarDct, frameHeader.Encoding);
        Assert.Equal(JxlFrameFlags.None, frameHeader.Flags & JxlFrameFlags.Patches);
        JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();
        long tocEntries = JxlToc.NumTocEntries(frameDim.NumGroups, frameDim.NumDcGroups, frameHeader.Passes.NumPasses);
        uint[] sizes = JxlToc.ReadToc(tocEntries, reader);
        reader.EnsureAllReadsWithinBounds("frame header");
        int offset = 2 + (int)(reader.TotalBitsConsumed / 8);
        reader.Close();

        JxlBitReader[] sections = new JxlBitReader[tocEntries];
        for (long i = 0; i < tocEntries; i++)
        {
            sections[i] = new JxlBitReader(codestream, offset, (int)sizes[i]);
            offset += (int)sizes[i];
        }

        // A frame of one group and one pass has every section in one.
        bool singleSection = tocEntries == 1;
        JxlDequantMatrices matrices = new();
        JxlModularFrameDecoder modularFrameDecoder = new(Configuration.Default.MemoryAllocator, frameDim, matrices);
        JxlVarDctFrameState state = new(Configuration.Default.MemoryAllocator, frameHeader, frameDim, matrices);
        JxlFrameDecoder.ProcessDcGlobal(sections[0], frameHeader, frameDim, null, null, matrices, modularFrameDecoder, state, CancellationToken.None);
        for (long i = 0; i < frameDim.NumDcGroups; i++)
        {
            JxlFrameDecoder.ProcessDcGroup(i, singleSection ? sections[0] : sections[1 + i], frameHeader, frameDim, modularFrameDecoder, state);
        }

        return new JxlDecodedDcGroups(frameHeader, state, modularFrameDecoder);
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

    public void Dispose()
    {
        this.State.Dispose();
        this.modularFrameDecoder.Dispose();
    }
}
