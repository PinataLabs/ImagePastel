// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_group.cc (DequantLane, DequantBlock, DecodeGroupImpl, DecodeACVarBlock,
// GetBlockFromBitstream, DecodeGroup), without the JPEG reconstruction branches and the encoder's
// GetBlockFromEncoder.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// Decodes the VarDCT coefficients of one AC group, dequantises them and transforms them into the
/// colour planes of the render pipeline.
/// </summary>
/// <remarks>
/// Deliberate departures from libjxl: the quantised coefficients are always <see cref="int"/>, where
/// libjxl picks <c>int16_t</c> when the tokens are narrow enough, which gives the same values; and the
/// non-zero counts are sized for the frame's group rather than a fixed 32x32 blocks. libjxl's
/// <c>MulAdd</c> is fused on AVX2; here each multiply and add is separate (ADR 0004).
/// </remarks>
internal static class JxlVarDctGroup
{
    private const int BlockDim = JxlFrameDimensions.BlockDim;
    private const int DctBlockSize = BlockDim * BlockDim;

    // libjxl visits Y, then X, then B.
    private static ReadOnlySpan<byte> ChannelOrder => new byte[] { 1, 0, 2 };

    /// <summary>Decodes AC group <paramref name="groupIdx"/> from one reader per pass.</summary>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="readers">The reader of each pass.</param>
    /// <param name="numPasses">The number of passes to decode.</param>
    /// <param name="groupIdx">The AC group.</param>
    /// <param name="state">The frame's VarDCT state.</param>
    /// <param name="pipeline">The render pipeline, whose padded colour planes receive the pixels.</param>
    public static void DecodeGroup(JxlFrameHeader frameHeader, JxlBitReader[] readers, int numPasses, long groupIdx, JxlVarDctFrameState state, JxlRenderPipeline pipeline)
    {
        if (state.NumHistograms <= 0)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("No histograms for the AC groups");
        }

        int histoSelectorBits = JxlBits.CeilLog2Nonzero((uint)state.NumHistograms);
        JxlRect blockRect = state.FrameDim.BlockGroupRect(groupIdx);

        // GetBlockFromBitstream::Init.
        JxlVarDctGroupCache cache = state.GroupCache;
        int[] ctxOffset = cache.CtxOffset;
        JxlAnsSymbolReader[] decoders = cache.Decoders;
        for (int pass = 0; pass < numPasses; pass++)
        {
            // Select which histogram set to use among those of the current pass.
            int curHistogram = 0;
            if (histoSelectorBits != 0)
            {
                curHistogram = (int)readers[pass].ReadBits(histoSelectorBits);
            }

            if (curHistogram >= state.NumHistograms)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid histogram selector");
            }

            ctxOffset[pass] = curHistogram * state.BlockCtxMap.NumAcContexts;
            decoders[pass] = new JxlAnsSymbolReader(state.Code[pass], readers[pass]);
        }

        DecodeGroupImpl(frameHeader, readers, numPasses, blockRect, state, pipeline, cache);

        for (int pass = 0; pass < numPasses; pass++)
        {
            if (!decoders[pass].CheckAnsFinalState)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("ANS checksum failure.");
            }
        }
    }

    private static void DecodeGroupImpl(JxlFrameHeader frameHeader, JxlBitReader[] readers, int numPasses, JxlRect blockRect, JxlVarDctFrameState state, JxlRenderPipeline pipeline, JxlVarDctGroupCache cache)
    {
        int[] ctxOffset = cache.CtxOffset;
        JxlAnsSymbolReader[] decoders = cache.Decoders;
        int bx0 = (int)blockRect.X0;
        int by0 = (int)blockRect.Y0;
        int xsizeBlocks = (int)blockRect.XSize;
        int ysizeBlocks = (int)blockRect.YSize;
        float invGlobalScale = state.Quantizer.InvGlobalScale;
        JxlYCbCrChromaSubsampling cs = frameHeader.ChromaSubsampling;
        JxlColorCorrelation colorCorrelation = state.Cmap;
        JxlBlockCtxMap blockCtxMap = state.BlockCtxMap;

        Span<int> hshift = stackalloc int[] { cs.HShift(0), cs.HShift(1), cs.HShift(2) };
        Span<int> vshift = stackalloc int[] { cs.VShift(0), cs.VShift(1), cs.VShift(2) };
        Span<int> rx0 = stackalloc int[3];
        Span<int> ry0 = stackalloc int[3];
        for (int i = 0; i < 3; i++)
        {
            rx0[i] = bx0 >> hshift[i];
            ry0[i] = by0 >> vshift[i];
            int rxs = xsizeBlocks >> hshift[i];
            int rys = ysizeBlocks >> vshift[i];

            // The DC planes are not subsampled here, so they are XSizeBlocks x YSizeBlocks.
            if (rx0[i] + rxs > state.XSizeBlocks || ry0[i] + rys > state.YSizeBlocks)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Frame dimensions are too big for the image.");
            }
        }

        // GroupDecCache: the non-zero counts of each pass and channel, and the block scratch space.
        int nzerosStride = cache.NzerosStride;
        int[][][] numNzeroes = cache.NumNzeroes;
        int[][] qblock = cache.QBlock;
        float[] block = cache.Block;

        // The pixel origin of the group in the padded colour planes.
        int px0 = bx0 * BlockDim;
        int py0 = by0 * BlockDim;
        Buffer2D<float>[] planes = { pipeline.GetPlane(0), pipeline.GetPlane(1), pipeline.GetPlane(2) };

        Span<int> sbx = stackalloc int[3];
        Span<int> sby = stackalloc int[3];
        for (int by = 0; by < ysizeBlocks; ++by)
        {
            sby[0] = by >> vshift[0];
            sby[1] = by >> vshift[1];
            sby[2] = by >> vshift[2];
            ReadOnlySpan<int> rowQuant = state.RawQuantField.DangerousGetRowSpan(by0 + by).Slice(bx0, xsizeBlocks);
            ReadOnlySpan<byte> quantDcRow = state.QuantDc.DangerousGetRowSpan(by0 + by).Slice(bx0, xsizeBlocks);

            int ty = (by0 + by) / JxlColorCorrelation.ColorTileDimInBlocks;
            ReadOnlySpan<sbyte> rowCmapX = colorCorrelation.YtoXMap.AsSpan(ty * colorCorrelation.XSizeTiles, colorCorrelation.XSizeTiles);
            ReadOnlySpan<sbyte> rowCmapB = colorCorrelation.YtoBMap.AsSpan(ty * colorCorrelation.XSizeTiles, colorCorrelation.XSizeTiles);

            int bx = 0;
            for (int tx = 0; tx < JxlBits.DivCeil(xsizeBlocks, JxlColorCorrelation.ColorTileDimInBlocks); tx++)
            {
                int absTx = tx + (bx0 / JxlColorCorrelation.ColorTileDimInBlocks);
                float xCcMul = colorCorrelation.YtoXRatio(rowCmapX[absTx]);
                float bCcMul = colorCorrelation.YtoBRatio(rowCmapB[absTx]);

                // Increment bx by llf_x because those iterations would otherwise immediately continue
                // (!IsFirstBlock). Reduces mispredictions.
                while (bx < xsizeBlocks && bx < (tx + 1) * JxlColorCorrelation.ColorTileDimInBlocks)
                {
                    sbx[0] = bx >> hshift[0];
                    sbx[1] = bx >> hshift[1];
                    sbx[2] = bx >> hshift[2];
                    JxlAcStrategy acs = state.AcStrategy.Get(bx0 + bx, by0 + by);
                    int llfX = acs.CoveredBlocksX;

                    // Can only happen in the second or lower rows of a varblock.
                    if (!acs.IsFirstBlock)
                    {
                        bx += llfX;
                        continue;
                    }

                    int log2CoveredBlocks = acs.Log2CoveredBlocks;
                    int coveredBlocks = 1 << log2CoveredBlocks;
                    int size = coveredBlocks * DctBlockSize;

                    // No point in reading from bitstream without accumulating and not drawing.
                    for (int c = 0; c < 3; c++)
                    {
                        qblock[c].AsSpan(0, size).Clear();
                    }

                    // GetBlockFromBitstream::LoadBlock.
                    foreach (int c in ChannelOrder)
                    {
                        int csbx = bx >> hshift[c];
                        int csby = by >> vshift[c];
                        if ((csbx << hshift[c]) != bx || (csby << vshift[c]) != by)
                        {
                            continue;
                        }

                        for (int pass = 0; pass < numPasses; pass++)
                        {
                            Span<int> rowNzeros = numNzeroes[pass][c].AsSpan(csby * nzerosStride);
                            ReadOnlySpan<int> rowNzerosTop = csby == 0 ? ReadOnlySpan<int>.Empty : numNzeroes[pass][c].AsSpan((csby - 1) * nzerosStride, nzerosStride);
                            DecodeAcVarBlock(
                                ctxOffset[pass],
                                log2CoveredBlocks,
                                rowNzeros,
                                rowNzerosTop,
                                nzerosStride,
                                c,
                                csbx,
                                csby,
                                bx,
                                acs,
                                state.CoeffOrders.AsSpan(pass * state.CoeffOrderSize),
                                readers[pass],
                                decoders[pass],
                                state.ContextMap[pass],
                                quantDcRow,
                                rowQuant,
                                blockCtxMap,
                                qblock[c],
                                (int)frameHeader.Passes.Shift[pass]);
                        }
                    }

                    // Dequantize and add predictions.
                    DequantBlock(invGlobalScale, rowQuant[bx], state.XDmMultiplier, state.BDmMultiplier, xCcMul, bCcMul, acs.Strategy, size, state.Matrices, acs.CoveredBlocksY * acs.CoveredBlocksX, state.QuantBiases, qblock, block);
                    for (int c = 0; c < 3; c++)
                    {
                        // LowestFrequenciesFromDC reads the DC values of the blocks the varblock covers,
                        // from a copy whose rows are CoveredBlocksX apart.
                        int dcStride = acs.CoveredBlocksX;
                        for (int iy = 0; iy < acs.CoveredBlocksY; iy++)
                        {
                            state.Dc[c].DangerousGetRowSpan(ry0[c] + sby[c] + iy).Slice(rx0[c] + sbx[c], dcStride).CopyTo(cache.Dc.AsSpan(iy * dcStride));
                        }

                        JxlDct.LowestFrequenciesFromDc(acs.Strategy, cache.Dc, dcStride, block.AsSpan(c * size, size), cache.Scratch);
                    }

                    foreach (int c in ChannelOrder)
                    {
                        if ((sbx[c] << hshift[c]) != bx || (sby[c] << vshift[c]) != by)
                        {
                            continue;
                        }

                        // IDCT
                        Buffer2D<float> plane = planes[c];
                        int x = (px0 >> hshift[c]) + (sbx[c] * BlockDim);
                        int y = (py0 >> vshift[c]) + (sby[c] * BlockDim);
                        TransformToPixels(acs, block.AsSpan(c * size, size), plane, x, y, cache);
                    }

                    bx += llfX;
                }
            }
        }
    }

    // TransformToPixels, into the pixels of the varblock at (x, y) of the plane.
    private static void TransformToPixels(JxlAcStrategy acs, Span<float> coefficients, Buffer2D<float> plane, int x, int y, JxlVarDctGroupCache cache)
    {
        int width = acs.CoveredBlocksX * BlockDim;
        int height = acs.CoveredBlocksY * BlockDim;
        Span<float> pixels = cache.Pixels.AsSpan(0, width * height);
        JxlDct.TransformToPixels(acs.Strategy, coefficients, pixels, width, cache.Scratch);
        for (int iy = 0; iy < height; iy++)
        {
            pixels.Slice(iy * width, width).CopyTo(plane.DangerousGetRowSpan(y + iy).Slice(x, width));
        }
    }

    private static void DequantBlock(float invGlobalScale, int quant, float xDmMultiplier, float bDmMultiplier, float xCcMul, float bCcMul, JxlAcStrategyType kind, int size, JxlDequantMatrices matrices, int coveredBlocks, float[] biases, int[][] qblock, float[] block)
    {
        float scaledDequantS = invGlobalScale / quant;
        float scaledDequantX = scaledDequantS * xDmMultiplier;
        float scaledDequantY = scaledDequantS;
        float scaledDequantB = scaledDequantS * bDmMultiplier;

        ReadOnlySpan<float> dequantX = matrices.Matrix(kind, 0);
        ReadOnlySpan<float> dequantY = matrices.Matrix(kind, 1);
        ReadOnlySpan<float> dequantB = matrices.Matrix(kind, 2);
        int[] qx = qblock[0];
        int[] qy = qblock[1];
        int[] qb = qblock[2];

        // DequantLane, one coefficient at a time.
        for (int k = 0; k < coveredBlocks * DctBlockSize; k++)
        {
            float xMul = dequantX[k] * scaledDequantX;
            float yMul = dequantY[k] * scaledDequantY;
            float bMul = dequantB[k] * scaledDequantB;

            float dequantXCc = JxlQuantizer.AdjustQuantBias(0, qx[k], biases) * xMul;
            float dequantYk = JxlQuantizer.AdjustQuantBias(1, qy[k], biases) * yMul;
            float dequantBCc = JxlQuantizer.AdjustQuantBias(2, qb[k], biases) * bMul;

            block[k] = (xCcMul * dequantYk) + dequantXCc;
            block[size + k] = dequantYk;
            block[(2 * size) + k] = (bCcMul * dequantYk) + dequantBCc;
        }
    }

    // Decode quantized AC coefficients of DCT blocks. LLF components in the output block will not be
    // modified.
    private static void DecodeAcVarBlock(
        int ctxOffset,
        int log2CoveredBlocks,
        Span<int> rowNzeros,
        ReadOnlySpan<int> rowNzerosTop,
        int nzerosStride,
        int c,
        int bx,
        int by,
        int lbx,
        JxlAcStrategy acs,
        ReadOnlySpan<uint> coeffOrder,
        JxlBitReader br,
        JxlAnsSymbolReader decoder,
        byte[] contextMap,
        ReadOnlySpan<byte> qdcRow,
        ReadOnlySpan<int> qfRow,
        JxlBlockCtxMap blockCtxMap,
        int[] block,
        int shift)
    {
        // Equal to number of LLF coefficients.
        int coveredBlocks = 1 << log2CoveredBlocks;
        int size = coveredBlocks * DctBlockSize;
        int predictedNzeros = JxlBlockCtxMap.PredictFromTopAndLeft(rowNzerosTop, rowNzeros, bx, 32);

        int ord = JxlCoeffOrder.StrategyOrder[acs.RawStrategy];
        ReadOnlySpan<uint> order = coeffOrder.Slice(JxlCoeffOrder.Offset(ord, c));

        int blockCtx = blockCtxMap.Context(qdcRow[lbx], qfRow[bx], ord, c);
        int nzeroCtx = blockCtxMap.NonZeroContext(predictedNzeros, blockCtx) + ctxOffset;

        uint nzeros = decoder.ReadHybridUint(nzeroCtx, br, contextMap);
        if (nzeros > size - coveredBlocks)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Invalid AC: nzeros {nzeros} too large for {coveredBlocks} 8x8 blocks");
        }

        for (int y = 0; y < acs.CoveredBlocksY; y++)
        {
            for (int x = 0; x < acs.CoveredBlocksX; x++)
            {
                rowNzeros[bx + x + (y * nzerosStride)] = (int)((nzeros + coveredBlocks - 1) >> log2CoveredBlocks);
            }
        }

        int histoOffset = ctxOffset + blockCtxMap.ZeroDensityContextsOffset(blockCtx);

        int prev = nzeros > size / 16 ? 0 : 1;
        for (int k = coveredBlocks; k < size && nzeros != 0; ++k)
        {
            int ctx = histoOffset + JxlBlockCtxMap.ZeroDensityContext((int)nzeros, k, coveredBlocks, log2CoveredBlocks, prev);
            uint uCoeff = decoder.ReadHybridUint(ctx, br, contextMap);

            // Hand-rolled version of UnpackSigned, shifting before the conversion to signed integer to
            // avoid undefined behavior of shifting negative numbers.
            uint magnitude = uCoeff >> 1;
            uint negSign = ~uCoeff & 1;
            int coeff = unchecked((int)((magnitude ^ (negSign - 1)) << shift));
            block[order[k]] = unchecked(block[order[k]] + coeff);
            prev = uCoeff != 0 ? 1 : 0;
            nzeros -= (uint)prev;
        }

        if (nzeros != 0)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Invalid AC: nzeros at end of block is {nzeros}, should be 0. Block ({bx}, {by}), channel {c}");
        }
    }
}
