// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_entropy_coder.cc (NumNonZeroExceptLLF, NumNonZero8x8ExceptDC and TokenizeCoefficients), for
// one lane and 4:4:4.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// Turns the quantised coefficients of a group into AC tokens, as <see cref="JxlVarDctGroup"/> reads them:
/// for each block and channel, the number of non-zeros in a context predicted from the blocks above and
/// to the left, then each coefficient in the coefficient order up to the last non-zero.
/// </summary>
/// <remarks>
/// The encoder never subsamples chroma, so libjxl's subsampled positions are not ported.
/// </remarks>
internal static class JxlAcTokenizer
{
    private const int BlockDim = 8;
    private const int DctBlockSize = BlockDim * BlockDim;

    // The group size in blocks (libjxl's kGroupDimInBlocks): VarDCT groups are 256 pixels.
    private const int GroupDimInBlocks = JxlFrameDimensions.GroupDim / BlockDim;

    // The channels in the order the decoder reads them: Y, X, B.
    private static readonly int[] ChannelOrder = { 1, 0, 2 };

    /// <summary>Tokenizes the coefficients of group <paramref name="groupIndex"/> into <see cref="JxlPassesEncoderState.AcTokens"/>.</summary>
    /// <param name="encState">The encoder state, with the group's coefficients and the coefficient orders.</param>
    /// <param name="groupIndex">The group.</param>
    public static void TokenizeCoefficients(JxlPassesEncoderState encState, long groupIndex)
    {
        JxlRect rect = encState.FrameDim.BlockGroupRect(groupIndex);
        int xsizeBlocks = (int)rect.XSize;
        int ysizeBlocks = (int)rect.YSize;
        int[][] acRows = encState.Coeffs[groupIndex];
        JxlBlockCtxMap blockCtxMap = encState.BlockCtxMap;
        ReadOnlySpan<uint> orders = encState.CoeffOrders;

        // TODO(user): update the estimate: usually less coefficients are used.
        List<JxlToken> output = new(3 * xsizeBlocks * ysizeBlocks * DctBlockSize);
        encState.AcTokens[groupIndex] = output;

        // The number of non-zeros of each block of the group, per channel (libjxl's EncCache).
        int[][] numNzeroes = { new int[GroupDimInBlocks * GroupDimInBlocks], new int[GroupDimInBlocks * GroupDimInBlocks], new int[GroupDimInBlocks * GroupDimInBlocks] };
        int[] offset = new int[3];
        for (int by = 0; by < ysizeBlocks; ++by)
        {
            for (int bx = 0; bx < xsizeBlocks; ++bx)
            {
                int blockIndex = encState.BlockIndex(rect.X0 + bx, rect.Y0 + by);
                JxlAcStrategy acs = encState.AcStrategy.Get((int)(rect.X0 + bx), (int)(rect.Y0 + by));
                if (!acs.IsFirstBlock)
                {
                    continue;
                }

                int cx = acs.CoveredBlocksX;
                int cy = acs.CoveredBlocksY;

                // = #LLF coefficients
                int coveredBlocks = cx * cy;
                int log2CoveredBlocks = JxlBits.Num0BitsBelowLs1BitNonzero((uint)coveredBlocks);
                int size = coveredBlocks * DctBlockSize;

                // swap cx/cy to canonical order
                int canonicalCx = cx;
                int canonicalCy = cy;
                JxlAcStrategy.CoefficientLayout(ref canonicalCy, ref canonicalCx);

                foreach (int c in ChannelOrder)
                {
                    ReadOnlySpan<int> block = acRows[c].AsSpan(offset[c], size);
                    Span<int> rowNzeros = numNzeroes[c].AsSpan(by * GroupDimInBlocks, GroupDimInBlocks);
                    ReadOnlySpan<int> rowNzerosTop = by == 0 ? Span<int>.Empty : numNzeroes[c].AsSpan((by - 1) * GroupDimInBlocks, GroupDimInBlocks);

                    int nzeros = coveredBlocks == 1
                        ? NumNonZero8x8ExceptDc(block, rowNzeros.Slice(bx))
                        : NumNonZeroExceptLlf(canonicalCx, canonicalCy, acs, coveredBlocks, log2CoveredBlocks, block, GroupDimInBlocks, numNzeroes[c].AsSpan((by * GroupDimInBlocks) + bx));

                    int ord = JxlCoeffOrder.StrategyOrder[acs.RawStrategy];
                    ReadOnlySpan<uint> order = orders.Slice(JxlCoeffOrder.Offset(ord, c));

                    int predictedNzeros = JxlBlockCtxMap.PredictFromTopAndLeft(rowNzerosTop, rowNzeros, bx, 32);

                    // libjxl passes the block's quantised-DC bucket (DequantDC's quant_dc). Neither the
                    // default block context map nor FindBestBlockEntropyModel's has DC thresholds, so
                    // every bucket is 0.
                    int blockCtx = blockCtxMap.Context(0, encState.RawQuantField[blockIndex], ord, c);
                    int nzeroCtx = blockCtxMap.NonZeroContext(predictedNzeros, blockCtx);

                    output.Add(new JxlToken((uint)nzeroCtx, (uint)nzeros));
                    int histoOffset = blockCtxMap.ZeroDensityContextsOffset(blockCtx);

                    // Skip LLF.
                    int prev = nzeros > size / 16 ? 0 : 1;
                    for (int k = coveredBlocks; k < size && nzeros != 0; ++k)
                    {
                        int coeff = block[(int)order[k]];
                        int ctx = histoOffset + JxlBlockCtxMap.ZeroDensityContext(nzeros, k, coveredBlocks, log2CoveredBlocks, prev);
                        output.Add(new JxlToken((uint)ctx, JxlBits.PackSigned(coeff)));
                        prev = coeff != 0 ? 1 : 0;
                        nzeros -= prev;
                    }

                    DebugGuard.IsTrue(nzeros == 0, "Every non-zero was tokenized");
                    offset[c] += size;
                }
            }
        }
    }

    // Returns number of non-zero coefficients (but skip LLF): the LLF are the first cx coefficients of the
    // first cy rows, in the canonical dimensions. Also writes the per-8x8 block nzeros, rounded up over the
    // covered blocks, to every block the strategy covers, in its non-canonical dimensions.
    private static int NumNonZeroExceptLlf(int cx, int cy, JxlAcStrategy acs, int coveredBlocks, int log2CoveredBlocks, ReadOnlySpan<int> block, int nzerosStride, Span<int> nzerosPos)
    {
        int nzeros = 0;
        for (int y = 0; y < cy * BlockDim; y++)
        {
            for (int x = 0; x < cx * BlockDim; x++)
            {
                // LLF counts as zero so we don't include it in nzeros.
                bool isLlf = y < cy && x < cx;
                if (!isLlf && block[(y * cx * BlockDim) + x] != 0)
                {
                    nzeros++;
                }
            }
        }

        int shiftedNzeros = (nzeros + coveredBlocks - 1) >> log2CoveredBlocks;

        // Need non-canonicalized dimensions!
        for (int y = 0; y < acs.CoveredBlocksY; y++)
        {
            for (int x = 0; x < acs.CoveredBlocksX; x++)
            {
                nzerosPos[x + (y * nzerosStride)] = shiftedNzeros;
            }
        }

        return nzeros;
    }

    // Specialization for 8x8, where only top-left is LLF/DC. The DC counts as zero.
    private static int NumNonZero8x8ExceptDc(ReadOnlySpan<int> block, Span<int> nzerosPos)
    {
        int nzeros = 0;
        for (int k = 1; k < DctBlockSize; k++)
        {
            if (block[k] != 0)
            {
                nzeros++;
            }
        }

        nzerosPos[0] = nzeros;
        return nzeros;
    }
}
