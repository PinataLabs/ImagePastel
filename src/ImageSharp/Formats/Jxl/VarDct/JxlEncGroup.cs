// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_group.cc (QuantizeBlockAC, QuantizeRoundtripYBlockAC and ComputeCoefficients), for one lane
// and one pass.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// Transforms and quantises the blocks of one group: the forward DCT of each channel, the Y channel
/// quantised and dequantised again, the chroma-from-luma prediction from that Y taken out of X and B,
/// then X and B quantised. It also sets each block's DC.
/// </summary>
/// <remarks>
/// libjxl's <c>NegMulAdd</c> in the chroma-from-luma step is fused on AVX2; here the multiply and the
/// subtraction are rounded on their own (ADR 0004).
/// </remarks>
internal static class JxlEncGroup
{
    private const int BlockDim = 8;
    private const int DctBlockSize = BlockDim * BlockDim;

    /// <summary>
    /// Computes the quantised coefficients of group <paramref name="groupIdx"/> into
    /// <see cref="JxlPassesEncoderState.Coeffs"/>, and the DC of each of its blocks into <paramref name="dc"/>,
    /// as libjxl's <c>ComputeCoefficients</c>.
    /// </summary>
    /// <param name="groupIdx">The group.</param>
    /// <param name="encState">The encoder state.</param>
    /// <param name="opsin">The XYB planes, padded to whole blocks.</param>
    /// <param name="dc">The DC planes, one value per block.</param>
    public static void ComputeCoefficients(long groupIdx, JxlPassesEncoderState encState, Buffer2D<float>[] opsin, float[][] dc)
    {
        JxlRect blockGroupRect = encState.FrameDim.BlockGroupRect(groupIdx);
        JxlRect groupRect = encState.FrameDim.GroupRect(groupIdx);
        int xsizeBlocks = (int)blockGroupRect.XSize;
        int ysizeBlocks = (int)blockGroupRect.YSize;
        JxlQuantizer quantizer = encState.Quantizer;
        JxlColorCorrelation cmap = encState.Cmap;

        // libjxl passes error_diffusion (Squirrel mode or slower) to QuantizeBlockAC, which no longer reads it.

        // One pass, so libjxl's progressive splitter copies each block's coefficients as they are.
        int[][] coeffs = new int[3][];
        for (int c = 0; c < 3; c++)
        {
            coeffs[c] = new int[xsizeBlocks * ysizeBlocks * DctBlockSize];
        }

        encState.Coeffs[groupIdx] = coeffs;
        int coeffOffset = 0;

        float[] coeffsIn = new float[3 * DctBlockSize];
        int[] quantized = new int[3 * DctBlockSize];
        float[] scratch = new float[JxlDct.ScratchSize];
        float[] block = new float[DctBlockSize];
        float[] thres = new float[4];

        for (int by = 0; by < ysizeBlocks; ++by)
        {
            long gby = blockGroupRect.Y0 + by;
            int ty = by / JxlColorCorrelation.ColorTileDimInBlocks;
            int cmapRow = (int)(((blockGroupRect.Y0 / JxlColorCorrelation.ColorTileDimInBlocks) + ty) * cmap.XSizeTiles);
            for (int tx = 0; tx < JxlBits.DivCeil(xsizeBlocks, JxlColorCorrelation.ColorTileDimInBlocks); tx++)
            {
                int tile = cmapRow + (int)(blockGroupRect.X0 / JxlColorCorrelation.ColorTileDimInBlocks) + tx;
                float xFactor = cmap.YtoXRatio(cmap.YtoXMap[tile]);
                float bFactor = cmap.YtoBRatio(cmap.YtoBMap[tile]);
                for (int bx = tx * JxlColorCorrelation.ColorTileDimInBlocks; bx < xsizeBlocks && bx < (tx + 1) * JxlColorCorrelation.ColorTileDimInBlocks; ++bx)
                {
                    long gbx = blockGroupRect.X0 + bx;
                    JxlAcStrategy acs = encState.AcStrategy.Get((int)gbx, (int)gby);
                    if (!acs.IsFirstBlock)
                    {
                        continue;
                    }

                    int xblocks = acs.CoveredBlocksX;
                    int yblocks = acs.CoveredBlocksY;
                    JxlAcStrategy.CoefficientLayout(ref yblocks, ref xblocks);
                    int size = DctBlockSize * xblocks * yblocks;
                    DebugGuard.IsTrue(size == DctBlockSize, "The encoder only chooses DCT8");

                    // DCT Y channel, roundtrip-quantize it and set DC.
                    int blockIndex = encState.BlockIndex(gbx, gby);
                    int quantAc = encState.RawQuantField[blockIndex];
                    for (int c = 0; c < 3; c++)
                    {
                        // The pixels of the block, rows BlockDim apart.
                        for (int iy = 0; iy < BlockDim; iy++)
                        {
                            opsin[c].DangerousGetRowSpan((int)(groupRect.Y0 + (by * BlockDim) + iy))
                                .Slice((int)(groupRect.X0 + (bx * BlockDim)), BlockDim)
                                .CopyTo(block.AsSpan(iy * BlockDim, BlockDim));
                        }

                        JxlDct.TransformFromPixels(acs.Strategy, block, BlockDim, coeffsIn.AsSpan(c * size, size), scratch);
                    }

                    // DCFromLowestFrequencies: a DCT8 block's DC is its first coefficient.
                    dc[1][blockIndex] = coeffsIn[size];

                    QuantizeRoundtripYBlockAc(encState, size, quantizer, acs.Strategy, xblocks, yblocks, ref quantAc, coeffsIn, quantized);

                    // Unapply color correlation
                    for (int k = 0; k < size; k++)
                    {
                        float inX = coeffsIn[k];
                        float inY = coeffsIn[size + k];
                        float inB = coeffsIn[(2 * size) + k];
                        coeffsIn[k] = inX - (xFactor * inY);
                        coeffsIn[(2 * size) + k] = inB - (bFactor * inY);
                    }

                    // Quantize X and B channels and set DC.
                    for (int c = 0; c <= 2; c += 2)
                    {
                        thres[0] = 0.58f;
                        thres[1] = 0.62f;
                        thres[2] = 0.62f;
                        thres[3] = 0.62f;
                        QuantizeBlockAc(quantizer, c, c == 0 ? encState.XQmMultiplier : encState.BQmMultiplier, acs.Strategy, xblocks, yblocks, thres, coeffsIn.AsSpan(c * size, size), quantAc, quantized.AsSpan(c * size, size));
                        dc[c][blockIndex] = coeffsIn[c * size];
                    }

                    encState.RawQuantField[blockIndex] = quantAc;
                    for (int c = 0; c < 3; c++)
                    {
                        quantized.AsSpan(c * size, size).CopyTo(coeffs[c].AsSpan(coeffOffset, size));
                    }

                    coeffOffset += size;
                }
            }
        }
    }

    // NOTE: caller takes care of extracting quant from rect of RawQuantField.
    private static void QuantizeBlockAc(JxlQuantizer quantizer, int c, float qmMultiplier, JxlAcStrategyType quantKind, int xsize, int ysize, Span<float> thresholds, ReadOnlySpan<float> blockIn, int quant, Span<int> blockOut)
    {
        ReadOnlySpan<float> qm = quantizer.InvDequantMatrix(quantKind, c);
        float qac = quantizer.Scale * quant;

        // Not SIMD-ified for now.
        if (c != 1 && xsize * ysize >= 4)
        {
            for (int i = 0; i < 4; ++i)
            {
                thresholds[i] -= 0.00744f * xsize * ysize;
                if (thresholds[i] < 0.5)
                {
                    thresholds[i] = 0.5f;
                }
            }
        }

        float quantv = qac * qmMultiplier;
        for (int y = 0; y < ysize * BlockDim; y++)
        {
            int yfix = (y >= ysize * BlockDim / 2 ? 1 : 0) * 2;
            int off = y * BlockDim * xsize;
            for (int x = 0; x < xsize * BlockDim; x++)
            {
                // With one block across, the threshold of the right half is the next one; otherwise each
                // half of the row takes its own.
                float threshold = xsize == 1
                    ? thresholds[yfix + (x >= 4 ? 1 : 0)]
                    : thresholds[yfix + (x >= xsize * BlockDim / 2 ? 1 : 0)];
                float q = qm[off + x] * quantv;
                float val = q * blockIn[off + x];
                blockOut[off + x] = MathF.Abs(val) >= threshold ? (int)MathF.Round(val, MidpointRounding.ToEven) : 0;
            }
        }
    }

    // Quantises the Y channel at Falcon's thresholds, then dequantises it in place, so that X and B can
    // take out the chroma-from-luma prediction from the Y the decoder will see. libjxl's AdjustQuantBlockAC
    // runs here from Hare down; it comes with Default (E4).
    private static void QuantizeRoundtripYBlockAc(JxlPassesEncoderState encState, int size, JxlQuantizer quantizer, JxlAcStrategyType quantKind, int xsize, int ysize, ref int quant, float[] inout, int[] quantized)
    {
        if (encState.SpeedTier <= JxlSpeedTier.Hare)
        {
            JxlThrowHelper.ThrowNotSupportedException("Adjusting the AC quantisation per block (libjxl effort 5 and up; slice E4)");
        }

        float[] thresY = { 0.56f, 0.62f, 0.62f, 0.62f };
        QuantizeBlockAc(quantizer, 1, 1.0f, quantKind, xsize, ysize, thresY, inout.AsSpan(size, size), quant, quantized.AsSpan(size, size));

        ReadOnlySpan<float> dequantMatrix = quantizer.DequantMatrix(quantKind, 1);
        float invQac = quantizer.InvQuantAc(quant);
        float[] biases = JxlQuantizer.DefaultQuantBias;
        for (int k = 0; k < DctBlockSize * xsize * ysize; k++)
        {
            float adjQuant = JxlQuantizer.AdjustQuantBias(1, quantized[size + k], biases);
            inout[size + k] = adjQuant * dequantMatrix[k] * invQac;
        }
    }
}
