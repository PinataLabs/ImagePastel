// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_group.cc (QuantizeBlockAC, AdjustQuantBlockAC, QuantizeRoundtripYBlockAC and ComputeCoefficients), for one lane
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

    // AdjustQuantBlockAC's kMul1 and kMul2, [ix][c] row-major: ix 0 for DCT16X16, 1 for DCT32X16 and
    // DCT16X32, 2 for DCT32X32 and 3 for the other strategies of at least DCT16X16's raw value.
    private static readonly double[] AdjustQuantMul1 =
    {
        0.22080615753848404, 0.45797479824262011, 0.29859235095977965,
        0.70109486510286834, 0.16185281305512639, 0.14387691730035473,
        0.114985964456218638, 0.44656840441027695, 0.10587658215149048,
        0.46849665264409396, 0.41239077937781954, 0.088667407767185444,
    };

    private static readonly double[] AdjustQuantMul2 =
    {
        0.27450281941822197, 1.1255766549984996, 0.98950459134128388,
        0.4652168675598285, 0.40945807983455818, 0.36581899811751367,
        0.28034972424715715, 0.9182653201929738, 1.5581531543057416,
        0.26873118114033728, 0.68863712390392484, 1.2082185408666786,
    };

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

        // The largest block the encoder chooses is DCT64X64 (libjxl sizes these for kMaxCoeffArea, 256x256).
        float[] coeffsIn = new float[3 * JxlAcsScratch.MaxCoeffArea];
        int[] quantized = new int[3 * JxlAcsScratch.MaxCoeffArea];
        float[] scratch = new float[JxlDct.ScratchSize];
        float[] block = new float[JxlAcsScratch.MaxCoeffArea];
        float[] thres = new float[4];
        int dcStride = encState.XSizeBlocks;

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

                    // DCT Y channel, roundtrip-quantize it and set DC.
                    int blockIndex = encState.BlockIndex(gbx, gby);
                    int quantAc = encState.RawQuantField[blockIndex];
                    int pixelsStride = acs.CoveredBlocksX * BlockDim;
                    for (int c = 0; c < 3; c++)
                    {
                        // The pixels of the block, all the blocks it covers, rows pixelsStride apart.
                        for (int iy = 0; iy < acs.CoveredBlocksY * BlockDim; iy++)
                        {
                            opsin[c].DangerousGetRowSpan((int)(groupRect.Y0 + (by * BlockDim) + iy))
                                .Slice((int)(groupRect.X0 + (bx * BlockDim)), pixelsStride)
                                .CopyTo(block.AsSpan(iy * pixelsStride, pixelsStride));
                        }

                        JxlDct.TransformFromPixels(acs.Strategy, block, pixelsStride, coeffsIn.AsSpan(c * size, size), scratch);
                    }

                    // The DC of every block the strategy covers.
                    JxlDct.DcFromLowestFrequencies(acs.Strategy, coeffsIn.AsSpan(size, size), dc[1].AsSpan(blockIndex), dcStride, scratch);

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
                        JxlDct.DcFromLowestFrequencies(acs.Strategy, coeffsIn.AsSpan(c * size, size), dc[c].AsSpan(blockIndex), dcStride, scratch);
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

    /// <summary>
    /// Adjusts a block's quant and dead-zone thresholds to its coefficients in channel <paramref name="c"/>,
    /// as libjxl's <c>AdjustQuantBlockAC</c>: it raises the quant of flat blocks and of blocks with lone
    /// high-frequency detail, and lowers it in busy blocks.
    /// </summary>
    /// <param name="quantizer">The quantiser.</param>
    /// <param name="c">The channel: 0 for X, 1 for Y, 2 for B.</param>
    /// <param name="qmMultiplier">The channel's quant matrix multiplier.</param>
    /// <param name="quantKind">The block's strategy.</param>
    /// <param name="xsize">The block's width in 8x8 blocks, after <see cref="JxlAcStrategy.CoefficientLayout"/>.</param>
    /// <param name="ysize">The block's height in 8x8 blocks, after the layout.</param>
    /// <param name="thresholds">The four dead-zone thresholds, one per quarter of the block; updated.</param>
    /// <param name="blockIn">The block's coefficients, before the chroma-from-luma prediction is taken out.</param>
    /// <param name="quant">The block's quant; updated.</param>
    /// <remarks>
    /// The mixed precision is libjxl's: the threshold, limit and step expressions are in double, and
    /// <c>*quant += float</c> adds in float and truncates. The <c>orig_quant + 1</c> raise is not capped,
    /// so a quant of 256 can become 257 when no later step caps it; the decoder clamps it to 256.
    /// </remarks>
    internal static void AdjustQuantBlockAc(JxlQuantizer quantizer, int c, float qmMultiplier, JxlAcStrategyType quantKind, int xsize, int ysize, Span<float> thresholds, ReadOnlySpan<float> blockIn, ref int quant)
    {
        // No quantization adjusting for these small blocks.
        // Quantization adjusting attempts to fix some known issues
        // with larger blocks and on the 8x8 dct's emerging 8x8 blockiness
        // when there are not many non-zeros.
        const uint partialBlockKinds =
            (1u << (int)JxlAcStrategyType.Identity) |
            (1u << (int)JxlAcStrategyType.Dct2X2) |
            (1u << (int)JxlAcStrategyType.Dct4X4) |
            (1u << (int)JxlAcStrategyType.Dct4X8) |
            (1u << (int)JxlAcStrategyType.Dct8X4) |
            (1u << (int)JxlAcStrategyType.Afv0) |
            (1u << (int)JxlAcStrategyType.Afv1) |
            (1u << (int)JxlAcStrategyType.Afv2) |
            (1u << (int)JxlAcStrategyType.Afv3);
        if (((1u << (int)quantKind) & partialBlockKinds) != 0)
        {
            return;
        }

        ReadOnlySpan<float> qm = quantizer.InvDequantMatrix(quantKind, c);
        float qac = quantizer.Scale * quant;
        if (xsize > 1 || ysize > 1)
        {
            for (int i = 0; i < 4; ++i)
            {
                thresholds[i] -= Math.Clamp(0.003f * xsize * ysize, 0.0f, 0.08f);
                if (thresholds[i] < 0.54)
                {
                    thresholds[i] = 0.54f;
                }
            }
        }

        float sumOfHighestFreqRowAndColumn = 0;
        float sumOfError = 0;
        float sumOfVals = 0;
        Span<float> hfNonZeros = stackalloc float[4];
        Span<float> hfMaxError = stackalloc float[4];
        hfNonZeros.Clear();
        hfMaxError.Clear();

        for (int y = 0; y < ysize * BlockDim; y++)
        {
            for (int x = 0; x < xsize * BlockDim; x++)
            {
                int pos = (y * BlockDim * xsize) + x;
                if (x < xsize && y < ysize)
                {
                    continue;
                }

                int hfix = ((y >= ysize * BlockDim / 2 ? 1 : 0) * 2) + (x >= xsize * BlockDim / 2 ? 1 : 0);
                float val = blockIn[pos] * (qm[pos] * qac * qmMultiplier);
                float v = MathF.Abs(val) < thresholds[hfix] ? 0 : MathF.Round(val, MidpointRounding.ToEven);
                float error = MathF.Abs(val - v);
                sumOfError += error;
                sumOfVals += MathF.Abs(v);
                if (c == 1 && v == 0)
                {
                    if (hfMaxError[hfix] < error)
                    {
                        hfMaxError[hfix] = error;
                    }
                }

                if (v != 0.0f)
                {
                    hfNonZeros[hfix] += MathF.Abs(v);
                    bool inCorner = y >= 7 * ysize && x >= 7 * xsize;
                    bool onBorder = y == (ysize * BlockDim) - 1 || x == (xsize * BlockDim) - 1;
                    bool inLargerCorner = x >= 4 * xsize && y >= 4 * ysize;
                    if (inCorner || (onBorder && inLargerCorner))
                    {
                        sumOfHighestFreqRowAndColumn += MathF.Abs(val);
                    }
                }
            }
        }

        if (c == 1 && sumOfVals * 8 < xsize * ysize)
        {
            const double limit = 0.46;
            const double mul = 0.9999;
            int origQuant = quant;
            int newQuant = quant;
            for (int i = 1; i < 4; ++i)
            {
                if (hfNonZeros[i] == 0.0 && hfMaxError[i] > limit)
                {
                    newQuant = origQuant + 1;
                    break;
                }
            }

            quant = newQuant;
            if (hfNonZeros[3] == 0.0 && hfMaxError[3] > limit)
            {
                thresholds[3] = (float)(mul * hfMaxError[3] * newQuant / origQuant);
            }
            else if ((hfNonZeros[1] == 0.0 && hfMaxError[1] > limit) ||
                     (hfNonZeros[2] == 0.0 && hfMaxError[2] > limit))
            {
                thresholds[1] = (float)(mul * MathF.Max(hfMaxError[1], hfMaxError[2]) * newQuant / origQuant);
                thresholds[2] = thresholds[1];
            }
            else if (hfNonZeros[0] == 0.0 && hfMaxError[0] > limit)
            {
                thresholds[0] = (float)(mul * hfMaxError[0] * newQuant / origQuant);
            }
        }

        // Heuristic for improving accuracy of high-frequency patterns
        // occurring in an environment with no medium-frequency masking
        // patterns.
        {
            float all = hfNonZeros[0] + hfNonZeros[1] + hfNonZeros[2] + hfNonZeros[3] + 1;
            float mul = c == 0 ? 70 : c == 1 ? 30 : 60;
            if (mul * sumOfHighestFreqRowAndColumn >= all)
            {
                quant = (int)(quant + (mul * sumOfHighestFreqRowAndColumn / all));
                if (quant >= JxlQuantizer.QuantMax)
                {
                    quant = JxlQuantizer.QuantMax - 1;
                }
            }
        }

        if (quantKind == JxlAcStrategyType.Dct)
        {
            // If this 8x8 block is too flat, increase the adaptive quantization level
            // a bit to reduce visible block boundaries and requantize the block.
            if (hfNonZeros[0] + hfNonZeros[1] + hfNonZeros[2] + hfNonZeros[3] < 11)
            {
                quant += 1;
                if (quant >= JxlQuantizer.QuantMax)
                {
                    quant = JxlQuantizer.QuantMax - 1;
                }
            }
        }

        {
            const double quantNormalizer = 2.2942708343284721;
            sumOfError = (float)(sumOfError * quantNormalizer);
            sumOfVals = (float)(sumOfVals * quantNormalizer);
            if (quantKind >= JxlAcStrategyType.Dct16X16)
            {
                int ix = 3;
                if (quantKind is JxlAcStrategyType.Dct32X16 or JxlAcStrategyType.Dct16X32)
                {
                    ix = 1;
                }
                else if (quantKind == JxlAcStrategyType.Dct16X16)
                {
                    ix = 0;
                }
                else if (quantKind == JxlAcStrategyType.Dct32X32)
                {
                    ix = 2;
                }

                double mul1 = AdjustQuantMul1[(ix * 3) + c];
                double mul2 = AdjustQuantMul2[(ix * 3) + c];
                double denominator = (mul1 * xsize * ysize * BlockDim * BlockDim) + (mul2 * sumOfVals);
                int step = (int)(sumOfError / denominator);
                if (step >= 2)
                {
                    step = 2;
                }

                if (step < 0)
                {
                    step = 0;
                }

                if (sumOfError > denominator)
                {
                    quant += step;
                    if (quant >= JxlQuantizer.QuantMax)
                    {
                        quant = JxlQuantizer.QuantMax - 1;
                    }
                }
            }
        }

        {
            // Reduce quant in highly active areas.
            int div = xsize * ysize;
            float minHfNonZeros = MathF.Min(MathF.Min(MathF.Min(hfNonZeros[0], hfNonZeros[1]), hfNonZeros[2]), hfNonZeros[3]);
            int activity = 15;
            if (minHfNonZeros < 15.0f * div)
            {
                activity = ((int)minHfNonZeros + (div / 2)) / div;
            }

            int qp = quant - activity;
            if (c == 1)
            {
                for (int i = 1; i < 4; ++i)
                {
                    thresholds[i] = (float)(thresholds[i] + (0.01 * activity));
                }
            }

            int origQpLimit = Math.Max(4, quant / 2);
            if (qp < origQpLimit)
            {
                qp = origQpLimit;
            }

            quant = qp;
        }
    }

    // Quantises the Y channel, then dequantises it in place, so that X and B can take out the
    // chroma-from-luma prediction from the Y the decoder will see. From Hare down it first runs
    // AdjustQuantBlockAC on Y, X and B (pre-CfL), each from the original quant: the block takes the
    // largest quant, and Y its thresholds from the Y call.
    private static void QuantizeRoundtripYBlockAc(JxlPassesEncoderState encState, int size, JxlQuantizer quantizer, JxlAcStrategyType quantKind, int xsize, int ysize, ref int quant, float[] inout, int[] quantized)
    {
        float[] thresY = { 0.58f, 0.64f, 0.64f, 0.64f };
        if (encState.SpeedTier <= JxlSpeedTier.Hare)
        {
            int maxQuant = 0;
            int quantOrig = quant;
            Span<float> thres = stackalloc float[4];
            for (int i = 0; i < 3; i++)
            {
                // libjxl's order: Y, then X, then B.
                int c = i == 0 ? 1 : i == 1 ? 0 : 2;
                float qmMultiplier = c == 0 ? encState.XQmMultiplier : c == 1 ? 1.0f : encState.BQmMultiplier;
                thres[0] = 0.58f;
                thres[1] = 0.64f;
                thres[2] = 0.64f;
                thres[3] = 0.64f;
                quant = quantOrig;
                AdjustQuantBlockAc(quantizer, c, qmMultiplier, quantKind, xsize, ysize, thres, inout.AsSpan(c * size, size), ref quant);

                // Dead zone adjustment
                if (c == 1)
                {
                    thres.CopyTo(thresY);
                }

                maxQuant = Math.Max(quant, maxQuant);
            }

            quant = maxQuant;
        }
        else
        {
            thresY[0] = 0.56f;
            thresY[1] = 0.62f;
            thresY[2] = 0.62f;
            thresY[3] = 0.62f;
        }

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
