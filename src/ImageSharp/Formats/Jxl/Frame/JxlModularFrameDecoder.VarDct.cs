// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_modular.cc (ModularFrameDecoder: DecodeVarDCTDC, DecodeAcMetadata, DecodeQuantTable), with
// ConvertPlaneAndClamp from lib/jxl/image_ops.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Modular;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <content>
/// The VarDCT streams that use the global MA tree and histograms: the DC and the AC metadata of the DC
/// groups, and the raw quantization tables of the AC global section.
/// </content>
internal sealed partial class JxlModularFrameDecoder
{
    private const int EpfSharpEntries = 8;
    private const float AlmostZero = 1e-8f;

    /// <summary>
    /// Reads the VarDCT DC of one DC group: the extra-precision bits, then the Y, X and B channels,
    /// which are dequantised into the frame's DC.
    /// </summary>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="groupId">The DC group.</param>
    /// <param name="reader">The reader.</param>
    /// <param name="state">The frame's VarDCT state.</param>
    public void DecodeVarDctDc(JxlFrameHeader frameHeader, long groupId, JxlBitReader reader, JxlVarDctFrameState state)
    {
        JxlRect r = this.frameDim.DcGroupRect(groupId);

        using JxlModularImage image = JxlModularImage.Create(this.allocator, (int)r.XSize, (int)r.YSize, this.fullImage.BitDepth, 3);
        long streamId = JxlModularStreamId.VarDctDc(this.frameDim, groupId);
        reader.Refill();
        int extraPrecision = (int)reader.ReadBits(2);
        float mul = 1.0f / (1 << extraPrecision);
        for (int c = 0; c < 3; c++)
        {
            JxlModularChannel ch = image.Channel[c < 2 ? c ^ 1 : c];
            ch.Shrink(ch.W >> frameHeader.ChromaSubsampling.HShift(c), ch.H >> frameHeader.ChromaSubsampling.VShift(c));
        }

        JxlModularOptions options = new();
        JxlModularDecoder.ModularGenericDecompress(reader, image, null, (int)streamId, options, undoTransforms: true, this.tree, this.code, this.contextMap, this.cancellationToken);
        JxlCompressedDc.DequantDc(r, state.Dc, state.QuantDc, image, state.Quantizer.MulDc, mul, state.Cmap.DcFactors, frameHeader.ChromaSubsampling, state.BlockCtxMap);
    }

    /// <summary>
    /// Reads the AC metadata of one DC group: the chroma-from-luma maps, the strategy and quant field
    /// of each varblock, and the EPF sharpness of each block.
    /// </summary>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="groupId">The DC group.</param>
    /// <param name="reader">The reader.</param>
    /// <param name="state">The frame's VarDCT state.</param>
    public void DecodeAcMetadata(JxlFrameHeader frameHeader, long groupId, JxlBitReader reader, JxlVarDctFrameState state)
    {
        JxlRect r = this.frameDim.DcGroupRect(groupId);
        int rx0 = (int)r.X0;
        int ry0 = (int)r.Y0;
        int rxsize = (int)r.XSize;
        int rysize = (int)r.YSize;
        long upperBound = r.XSize * r.YSize;
        reader.Refill();
        int count = (int)reader.ReadBits(JxlBits.CeilLog2Nonzero((uint)upperBound)) + 1;
        long streamId = JxlModularStreamId.AcMetadata(this.frameDim, groupId);

        // YToX, YToB, ACS + QF, EPF
        using JxlModularImage image = JxlModularImage.Create(this.allocator, rxsize, rysize, this.fullImage.BitDepth, 4);
        JxlRect cr = new(r.X0 >> 3, r.Y0 >> 3, (r.XSize + 7) >> 3, (r.YSize + 7) >> 3);
        ReplaceChannel(image, 0, JxlModularChannel.Create(this.allocator, (int)cr.XSize, (int)cr.YSize, 3, 3));
        ReplaceChannel(image, 1, JxlModularChannel.Create(this.allocator, (int)cr.XSize, (int)cr.YSize, 3, 3));
        ReplaceChannel(image, 2, JxlModularChannel.Create(this.allocator, count, 2, 0, 0));
        JxlModularOptions options = new();
        JxlModularDecoder.ModularGenericDecompress(reader, image, null, (int)streamId, options, undoTransforms: true, this.tree, this.code, this.contextMap, this.cancellationToken);

        JxlColorCorrelation cmap = state.Cmap;
        ConvertPlaneAndClamp(image.Channel[0], cr, cmap.YtoXMap, cmap.XSizeTiles);
        ConvertPlaneAndClamp(image.Channel[1], cr, cmap.YtoBMap, cmap.XSizeTiles);

        int num = 0;
        bool is444 = JxlCompressedDc.Is444(frameHeader.ChromaSubsampling);
        JxlAcStrategyImage acStrategy = state.AcStrategy;
        int xlim = Math.Min(acStrategy.XSize, rx0 + rxsize);
        int ylim = Math.Min(acStrategy.YSize, ry0 + rysize);
        uint localUsedAcs = 0;
        ReadOnlySpan<int> rowIn1 = image.Channel[2].Row(0);
        ReadOnlySpan<int> rowIn2 = image.Channel[2].Row(1);
        for (int iy = 0; iy < rysize; iy++)
        {
            int y = ry0 + iy;
            Span<int> rowQf = state.RawQuantField.DangerousGetRowSpan(y).Slice(rx0, rxsize);
            Span<byte> rowEpf = state.EpfSharpness.DangerousGetRowSpan(y).Slice(rx0, rxsize);
            ReadOnlySpan<int> rowIn3 = image.Channel[3].Row(iy);
            for (int ix = 0; ix < rxsize; ix++)
            {
                int x = rx0 + ix;
                int sharpness = rowIn3[ix];
                if (sharpness is < 0 or >= EpfSharpEntries)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Corrupted sharpness field");
                }

                rowEpf[ix] = (byte)sharpness;
                if (acStrategy.IsValid(x, y))
                {
                    continue;
                }

                if (num >= count)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Corrupted stream");
                }

                if (!JxlAcStrategy.IsRawStrategyValid(rowIn1[num]))
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid AC strategy");
                }

                localUsedAcs |= 1u << rowIn1[num];
                JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(rowIn1[num]);
                if ((acs.CoveredBlocksX > 1 || acs.CoveredBlocksY > 1) && !is444)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("AC strategy not compatible with chroma subsampling");
                }

                // Ensure that blocks do not overflow *AC* groups.
                const int groupDimInBlocks = JxlFrameDimensions.GroupDim / JxlFrameDimensions.BlockDim;
                int nextXAcBlock = ((x / groupDimInBlocks) + 1) * groupDimInBlocks;
                int nextYAcBlock = ((y / groupDimInBlocks) + 1) * groupDimInBlocks;
                int nextXDctBlock = x + acs.CoveredBlocksX;
                int nextYDctBlock = y + acs.CoveredBlocksY;
                if (nextXDctBlock > nextXAcBlock || nextXDctBlock > xlim)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid AC strategy, x overflow");
                }

                if (nextYDctBlock > nextYAcBlock || nextYDctBlock > ylim)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid AC strategy, y overflow");
                }

                acStrategy.SetRejectingOverlap(x, y, acs.Strategy);
                rowQf[ix] = 1 + Math.Max(0, Math.Min(JxlQuantizer.QuantMax - 1, rowIn2[num]));
                num++;
            }
        }

        state.UsedAcs |= localUsedAcs;
    }

    /// <summary>
    /// Reads a raw quantization table: its F16 denominator, then its entries as a Modular stream of three
    /// channels that uses the frame's global tree, histograms and context map.
    /// </summary>
    /// <remarks>
    /// libjxl's <c>DecodeQuantTable</c> is static and takes the frame's Modular decoder, which may be
    /// <see langword="null"/>: then the stream has id 0 and a tree of its own. Only the encoder passes
    /// <see langword="null"/>, when it round-trips its tables through the decoder
    /// (<c>DequantMatricesRoundtrip</c> in lib/jxl/enc_quant_weights.cc); the decoder always passes its
    /// own (<c>FrameDecoder::ProcessACGlobal</c>). This port's encoder writes no VarDCT frames, so the
    /// branch is left out, and the method is an instance method of that decoder.
    /// </remarks>
    /// <param name="requiredSizeX">The width of the table in coefficients.</param>
    /// <param name="requiredSizeY">The height of the table in coefficients.</param>
    /// <param name="br">The reader.</param>
    /// <param name="encoding">The encoding to fill.</param>
    /// <param name="idx">The table, which gives the stream id.</param>
    public void DecodeQuantTable(int requiredSizeX, int requiredSizeY, JxlBitReader br, JxlQuantEncoding encoding, int idx)
    {
        encoding.QTableDen = JxlFieldCoders.ReadF16OrThrow(br);
        if (encoding.QTableDen < AlmostZero)
        {
            // qtable[] values are already checked for <= 0 so the denominator may not be negative.
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid qtable_den: value too small");
        }

        using JxlModularImage image = JxlModularImage.Create(this.allocator, requiredSizeX, requiredSizeY, 8, 3);
        JxlModularOptions options = new();
        long streamId = JxlModularStreamId.QuantTable(this.frameDim, idx);
        JxlModularDecoder.ModularGenericDecompress(br, image, null, (int)streamId, options, undoTransforms: true, this.tree, this.code, this.contextMap, this.cancellationToken);

        int[] qtable = new int[requiredSizeX * requiredSizeY * 3];
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < requiredSizeY; y++)
            {
                ReadOnlySpan<int> row = image.Channel[c].Row(y);
                for (int x = 0; x < requiredSizeX; x++)
                {
                    qtable[(c * requiredSizeX * requiredSizeY) + (y * requiredSizeX) + x] = row[x];
                    if (row[x] <= 0)
                    {
                        JxlThrowHelper.ThrowInvalidImageContentException("Invalid raw quantization table");
                    }
                }
            }
        }

        encoding.QTable = qtable;
    }

    private static void ReplaceChannel(JxlModularImage image, int c, JxlModularChannel channel)
    {
        image.Channel[c].Dispose();
        image.Channel[c] = channel;
    }

    // ConvertPlaneAndClamp from the whole channel into the tile map at cr, clamped to int8.
    private static void ConvertPlaneAndClamp(JxlModularChannel from, JxlRect cr, sbyte[] to, int toStride)
    {
        // libjxl's JXL_ENSURE(SameSize(rect_from, rect_to)).
        if (from.W != cr.XSize || from.H != cr.YSize)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Chroma-from-luma map size mismatch");
        }

        for (int y = 0; y < cr.YSize; y++)
        {
            ReadOnlySpan<int> rowFrom = from.Row(y);
            Span<sbyte> rowTo = to.AsSpan((int)(((cr.Y0 + y) * toStride) + cr.X0), (int)cr.XSize);
            for (int x = 0; x < rowTo.Length; x++)
            {
                rowTo[x] = (sbyte)Math.Clamp(rowFrom[x], sbyte.MinValue, sbyte.MaxValue);
            }
        }
    }
}
