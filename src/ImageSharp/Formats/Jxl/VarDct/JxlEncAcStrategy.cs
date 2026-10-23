// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_ac_strategy.cc (EstimateEntropy, FindBest8x8Transform, the Hare part of ProcessRectACS with
// TryMergeAcs, FindBestFirstLevelDivisionForSquare and the MultiBlockTransformCrosses boundary checks,
// AcStrategyHeuristics::Init and ProcessRect), for 8 lanes.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The AC-strategy search of the encoder: for each 64x64 tile, the strategy of each block. From Cheetah
/// up every block is DCT8; from Hare down each 8x8 block takes the 8x8 strategy with the lowest estimate of
/// its bits plus its information loss, and blocks then merge into DCTs of up to 64x64 where the estimate of
/// the merged block is lower than the sum of its parts.
/// </summary>
/// <remarks>
/// <para>
/// libjxl sums the entropy, the non-zero count and the loss of <c>EstimateEntropy</c> in vector lanes and
/// adds the lanes with Highway's <c>SumOfLanes</c>. This port uses AVX2's 8 lanes, as cjxl does on x64:
/// coefficient <c>i</c> goes to lane <c>i % 8</c>, pixel <c>dx</c> of a block row to lane <c>dx</c>, and
/// the lanes add up as <see cref="JxlAvx2Lanes.SumOfLanes"/> does. On another CPU libjxl's sums may
/// differ by a few units in the last place.
/// </para>
/// <para>
/// No fused multiply-add (ADR 0004). The unqualified <c>pow(float, float)</c> of the loss and of the
/// DCT2X2 and IDENTITY weight is <c>powf</c> with MSVC's <c>&lt;cmath&gt;</c>, as cjxl for Windows builds it;
/// it is <see cref="MathF.Pow"/> here.
/// </para>
/// </remarks>
internal static class JxlEncAcStrategy
{
    private const int BlockDim = 8;
    private const int DctBlockSize = BlockDim * BlockDim;

    // The vector lanes libjxl sums in: HWY_FULL(float) and HWY_CAPPED(float, 8) on AVX2.
    private const int Lanes = JxlAvx2Lanes.Count;

    // libjxl's kColorTileDimInBlocks: the chroma-from-luma map has one entry per 64x64 tile.
    private const int ColorTileDimInBlocks = 8;

    private const float Favor2X2AtHighQuality = 0.4f;
    private const float AvoidEntropyOfTransforms = 0.5f;

    // Favor all 8x8 transforms (against 16x8 and larger transforms)) at low butteraugli_target distances.
    private const float K8x8Mul1 = -0.4f;
    private const float K8x8Mul2 = 1.0f;
    private const float K8x8Base = 1.4f;

    // The masking offset of each channel's loss, and its weight: kChannelMul is pow(8.2, 8.0), pow(1.0,
    // 8.0) and pow(1.03, 8.0) in double, rounded to float when libjxl broadcasts it.
    private static readonly float[] MaskuLut = { 12.0f, 0.0f, 4.0f };
    private static readonly float[] ChannelMul = { (float)Math.Pow(8.2, 8.0), 1.0f, (float)Math.Pow(1.03, 8.0) };

    // libjxl's kTransforms8x8: each 8x8 strategy, the slowest speed tier that skips it, and its entropy
    // multiplier. DCT4X8, DCT8X4 and AFV start at Wombat.
    private static readonly (JxlAcStrategyType Type, int EncodingSpeedTierMaxLimit, double EntropyMul)[] Transforms8x8 =
    {
        (JxlAcStrategyType.Dct, 9, 0.8),
        (JxlAcStrategyType.Dct4X4, 5, 1.08),
        (JxlAcStrategyType.Dct2X2, 5, 0.95),
        (JxlAcStrategyType.Dct4X8, 4, 0.85931637428340035),
        (JxlAcStrategyType.Dct8X4, 4, 0.85931637428340035),
        (JxlAcStrategyType.Identity, 5, 1.0427542510634957),
        (JxlAcStrategyType.Afv0, 4, 0.81779489591359944),
        (JxlAcStrategyType.Afv1, 4, 0.81779489591359944),
        (JxlAcStrategyType.Afv2, 4, 0.81779489591359944),
        (JxlAcStrategyType.Afv3, 4, 0.81779489591359944),
    };

    // These numbers need to be figured out manually and looking at ringing next to sky etc. Optimization
    // will find smaller numbers and produce more ringing than is ideal. Larger numbers will help stop
    // ringing.
    private const float EntropyMul16X8 = 1.21f;
    private const float EntropyMul16X16 = 1.34f;
    private const float EntropyMul16X32 = 1.49f;
    private const float EntropyMul32X32 = 1.48f;
    private const float EntropyMul64X32 = 2.25f;
    private const float EntropyMul64X64 = 2.25f;

    // libjxl's kTransformsForMerge: each merge, its priority and its entropy multiplier, in the order they
    // are tried. FindBestFirstLevelDivisionForSquare looks for DCT16X16, DCT32X32 and DCT64X64 and their
    // subdivisions. libjxl's decoding and encoding speed tier limits are left out: the decoding tier is
    // always 0, which every entry allows, and the loop never reads the encoding one.
    private static readonly (JxlAcStrategyType Type, byte Priority, float EntropyMul)[] TransformsForMerge =
    {
        (JxlAcStrategyType.Dct16X8, 2, EntropyMul16X8),
        (JxlAcStrategyType.Dct8X16, 2, EntropyMul16X8),
        (JxlAcStrategyType.Dct16X32, 4, EntropyMul16X32),
        (JxlAcStrategyType.Dct32X16, 4, EntropyMul16X32),
        (JxlAcStrategyType.Dct64X32, 6, EntropyMul64X32),
        (JxlAcStrategyType.Dct32X64, 6, EntropyMul64X32),
    };

    /// <summary>
    /// Prepares the search, as libjxl's <c>AcStrategyHeuristics::Init</c>: computes the dequantization
    /// matrices the tier can choose (DCT8 from Cheetah up, every strategy below DCT128X128 otherwise) and
    /// the weights of the entropy estimate for the distance.
    /// </summary>
    /// <param name="speedTier">The speed tier.</param>
    /// <param name="distance">The butteraugli distance.</param>
    /// <param name="src">The opsin planes, after the inverse Gaborish filter.</param>
    /// <param name="quantField">The float initial quant field.</param>
    /// <param name="mask1x1">The per-pixel masking image, or null above Hare.</param>
    /// <param name="matrices">The dequantization matrices.</param>
    /// <returns>The search's configuration.</returns>
    public static JxlAcsConfig Init(JxlSpeedTier speedTier, float distance, Buffer2D<float>[] src, Buffer2D<float> quantField, Buffer2D<float> mask1x1, JxlDequantMatrices matrices)
    {
        if (speedTier >= JxlSpeedTier.Cheetah)
        {
            // DCT8 only
            matrices.EnsureComputed(1);
        }
        else
        {
            // All transforms up to 64x64.
            matrices.EnsureComputed((1u << (int)JxlAcStrategyType.Dct128X128) - 1);
        }

        // Entropy estimate is composed of two factors:
        //  - estimate of the number of bits that will be used by the block
        //  - information loss due to quantization
        // The following constant controls the relative weights of these components.
        const float kBias = 0.13731742964354549f;
        float ratio = (distance + kBias) / (1.0f + kBias);

        const float kPow1 = 0.33677806662454718f;
        const float kPow2 = 0.50990926717963703f;
        const float kPow3 = 0.36702940662370243f;
        return new JxlAcsConfig(
            matrices,
            quantField,
            mask1x1,
            src,
            infoLossMultiplier: 1.2f * MathF.Pow(ratio, kPow1),
            costDelta: 10.833273317067883f * MathF.Pow(ratio, kPow3),
            zerosMul: 9.3089059022677905f * MathF.Pow(ratio, kPow2));
    }

    /// <summary>
    /// Chooses the strategy of each block of a tile of at most 8x8 blocks, as libjxl's
    /// <c>AcStrategyHeuristics::ProcessRect</c>: DCT8 everywhere from Cheetah up, otherwise the search.
    /// </summary>
    /// <remarks>
    /// From Hare down the search then merges the 8x8 choices into 16x8 up to 64x64 blocks, aligned to their
    /// size, where that lowers the estimate. The non-aligned merges below Hare are not ported yet (E5d).
    /// </remarks>
    /// <param name="config">The search's configuration.</param>
    /// <param name="speedTier">The speed tier.</param>
    /// <param name="distance">The butteraugli distance.</param>
    /// <param name="rect">The tile, in blocks.</param>
    /// <param name="cmap">The chroma-from-luma map as it stands before this tile's fit.</param>
    /// <param name="acStrategy">The strategy image.</param>
    /// <param name="scratch">Working memory, or null from Cheetah up.</param>
    public static void ProcessRect(JxlAcsConfig config, JxlSpeedTier speedTier, float distance, JxlRect rect, JxlColorCorrelation cmap, JxlAcStrategyImage acStrategy, JxlAcsScratch scratch)
    {
        // In Cheetah mode, use DCT8 everywhere and uniform quantization.
        if (speedTier >= JxlSpeedTier.Cheetah)
        {
            acStrategy.FillDct8(rect);
            return;
        }

        ProcessRectAcs(config, speedTier, distance, rect, cmap, acStrategy, scratch);
    }

    /// <summary>
    /// Returns libjxl's <c>EstimateEntropy</c> of a block: the bits its quantised coefficients would take,
    /// weighted by <paramref name="entropyMul"/>, plus the information loss of the quantisation, measured
    /// on the pixels through the masking image.
    /// </summary>
    /// <param name="strategy">The strategy.</param>
    /// <param name="entropyMul">The weight of the bits.</param>
    /// <param name="x">The block's first pixel column.</param>
    /// <param name="y">The block's first pixel row.</param>
    /// <param name="config">The search's configuration.</param>
    /// <param name="cmapFactors">The chroma-from-luma factors of X, Y (0) and B.</param>
    /// <param name="scratch">Working memory.</param>
    /// <returns>The estimate.</returns>
    public static float EstimateEntropy(JxlAcStrategyType strategy, float entropyMul, int x, int y, JxlAcsConfig config, ReadOnlySpan<float> cmapFactors, JxlAcsScratch scratch)
    {
        float entropy = 0.0f;
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy((int)strategy);
        int coveredX = acs.CoveredBlocksX;
        int coveredY = acs.CoveredBlocksY;
        int numBlocks = coveredX * coveredY;
        int size = numBlocks * DctBlockSize;
        int pixelsStride = coveredX * BlockDim;
        Span<float> block = scratch.Block;
        Span<float> mem = scratch.Mem;
        Span<float> pixels = scratch.Pixels;
        Span<float> dctScratch = scratch.DctScratch;

        // Apply transform. The block's pixels are copied, rows pixelsStride apart.
        for (int c = 0; c < 3; c++)
        {
            Buffer2D<float> plane = config.Src[c];
            for (int iy = 0; iy < coveredY * BlockDim; iy++)
            {
                plane.DangerousGetRowSpan(y + iy).Slice(x, pixelsStride).CopyTo(pixels.Slice(iy * pixelsStride, pixelsStride));
            }

            JxlDct.TransformFromPixels(strategy, pixels, pixelsStride, block.Slice(c * size, size), dctScratch);
        }

        // avoid large blocks when there is a lot going on in red-green.
        float quantNorm16 = 0;
        if (numBlocks == 1)
        {
            // When it is only one 8x8, we don't need aggregation of values.
            quantNorm16 = config.Quant(x / 8, y / 8);
        }
        else if (numBlocks == 2)
        {
            // Taking max instead of 8th norm seems to work better for smallest blocks up to 16x8. Jyrki
            // couldn't get improvements in trying the same for 16x16 blocks.
            quantNorm16 = coveredY == 2
                ? JxlAvx2Lanes.Max(config.Quant(x / 8, y / 8), config.Quant(x / 8, (y / 8) + 1))
                : JxlAvx2Lanes.Max(config.Quant(x / 8, y / 8), config.Quant((x / 8) + 1, y / 8));
        }
        else
        {
            // Load QF value, calculate empirical heuristic on masking field for weighting the information
            // loss. Information loss manifests itself as ringing, and masking could hide it.
            for (int iy = 0; iy < coveredY; iy++)
            {
                for (int ix = 0; ix < coveredX; ix++)
                {
                    float qval = config.Quant((x / 8) + ix, (y / 8) + iy);
                    qval *= qval;
                    qval *= qval;
                    qval *= qval;
                    quantNorm16 += qval * qval;
                }
            }

            quantNorm16 /= numBlocks;
            quantNorm16 = JxlFastMath.FastPowf(quantNorm16, 1.0f / 16.0f);
        }

        float quant = quantNorm16;

        // Compute entropy.
        Span<float> loss = stackalloc float[Lanes];
        Span<float> entropyV = stackalloc float[Lanes];
        Span<float> nzerosV = stackalloc float[Lanes];
        Span<float> lossc = stackalloc float[Lanes];
        loss.Clear();
        Buffer2D<float> mask1x1 = config.Mask1x1;
        int mask1x1XSize = mask1x1.Width;
        for (int c = 0; c < 3; c++)
        {
            ReadOnlySpan<float> invMatrix = config.Dequant.InvMatrix(strategy, c);
            ReadOnlySpan<float> matrix = config.Dequant.Matrix(strategy, c);
            float cmapFactor = cmapFactors[c];

            entropyV.Clear();
            nzerosV.Clear();
            for (int i = 0; i < size; i++)
            {
                int lane = i % Lanes;
                float inC = block[(c * size) + i];
                float inY = block[size + i] * cmapFactor;
                float im = invMatrix[i];
                float val = (inC - inY) * (im * quant);
                float rval = MathF.Round(val, MidpointRounding.ToEven);
                float diff = val - rval;
                float m = matrix[i];
                mem[i] = m * diff;
                float q = MathF.Abs(rval);

                // We used to have q * C here, but that cost model seems to be punishing large values more
                // than necessary. Sqrt tries to avoid large values less aggressively.
                entropyV[lane] = MathF.Sqrt(q) + entropyV[lane];
                nzerosV[lane] = nzerosV[lane] + (q == 0 ? 0.0f : 1.0f);
            }

            float maskuOff = MaskuLut[c];
            lossc.Clear();
            JxlDct.TransformToPixels(acs, mem.Slice(0, size), pixels, pixelsStride, dctScratch);
            for (int iy = 0; iy < coveredY; iy++)
            {
                for (int ix = 0; ix < coveredX; ix++)
                {
                    for (int dy = 0; dy < BlockDim; ++dy)
                    {
                        // One 8-lane vector per row of the 8x8 block, read only when all its lanes are inside
                        // the masking image.
                        for (int dx = 0; dx < BlockDim; dx += Lanes)
                        {
                            if (x + (ix * 8) + dx + Lanes > mask1x1XSize)
                            {
                                continue;
                            }

                            int row = ((iy * BlockDim) + dy) * pixelsStride;
                            ReadOnlySpan<float> maskRow = mask1x1.DangerousGetRowSpan(y + (iy * 8) + dy).Slice(x + (ix * 8) + dx, Lanes);
                            for (int lane = 0; lane < Lanes; lane++)
                            {
                                float inP = pixels[row + (ix * BlockDim) + dx + lane];
                                float masku = maskRow[lane] + maskuOff;
                                inP = masku * inP;
                                inP *= inP;
                                inP *= inP;
                                inP *= inP;
                                lossc[lane] = lossc[lane] + inP;
                            }
                        }
                    }
                }
            }

            for (int lane = 0; lane < Lanes; lane++)
            {
                lossc[lane] = ChannelMul[c] * lossc[lane];
                loss[lane] = loss[lane] + lossc[lane];
            }

            entropy += config.CostDelta * JxlAvx2Lanes.SumOfLanes(entropyV);
            uint numNzeros = (uint)JxlAvx2Lanes.SumOfLanes(nzerosV);

            // Add #bit of num_nonzeros, as an estimate of the cost for encoding the number of non-zeros of
            // the block.
            int nbits = JxlBits.CeilLog2Nonzero(numNzeros + 1) + 1;

            // Also add #bit of #bit of num_nonzeros, to estimate the ANS cost, with a bias.
            entropy += config.ZerosMul * (JxlBits.CeilLog2Nonzero((uint)(nbits + 17)) + nbits);
            if (c == 0 && numBlocks >= 2)
            {
                // It is X channel (red-green) and we often see ringing in the large blocks. Let's punish that
                // more here.
                float w = (float)(1.0 + Math.Min(3.0, numBlocks / 8.0));
                entropy *= w;
                for (int lane = 0; lane < Lanes; lane++)
                {
                    loss[lane] = loss[lane] * w;
                }
            }
        }

        float lossScalar = MathF.Pow(JxlAvx2Lanes.SumOfLanes(loss) / (numBlocks * DctBlockSize), 1.0f / 8.0f) * (numBlocks * DctBlockSize) / quantNorm16;
        entropy *= entropyMul;
        entropy += config.InfoLossMultiplier * lossScalar;
        return entropy;
    }

    /// <summary>
    /// Returns the 8x8 strategy with the lowest estimate for the block at pixel (<paramref name="x"/>,
    /// <paramref name="y"/>), as libjxl's <c>FindBest8x8Transform</c>: those the speed tier allows, each
    /// estimate weighted by the strategy's multiplier and adjusted by the distance.
    /// </summary>
    /// <param name="x">The block's first pixel column.</param>
    /// <param name="y">The block's first pixel row.</param>
    /// <param name="encodingSpeedTier">The speed tier.</param>
    /// <param name="butteraugliTarget">The butteraugli distance.</param>
    /// <param name="config">The search's configuration.</param>
    /// <param name="cmapFactors">The chroma-from-luma factors of X, Y (0) and B.</param>
    /// <param name="scratch">Working memory.</param>
    /// <param name="entropyOut">The best estimate.</param>
    /// <returns>The best strategy.</returns>
    public static JxlAcStrategyType FindBest8x8Transform(int x, int y, JxlSpeedTier encodingSpeedTier, float butteraugliTarget, JxlAcsConfig config, ReadOnlySpan<float> cmapFactors, JxlAcsScratch scratch, out float entropyOut)
    {
        double best = 1e30;
        JxlAcStrategyType bestTx = Transforms8x8[0].Type;
        foreach ((JxlAcStrategyType type, int limit, double txEntropyMul) in Transforms8x8)
        {
            if (limit < (int)encodingSpeedTier)
            {
                continue;
            }

            float entropyMul = (float)(txEntropyMul / Transforms8x8[0].EntropyMul);
            if ((type == JxlAcStrategyType.Dct2X2 || type == JxlAcStrategyType.Identity) && butteraugliTarget < 5.0)
            {
                float weight = MathF.Pow((5.0f - butteraugliTarget) / 5.0f, 2.0f);
                entropyMul -= Favor2X2AtHighQuality * weight;
            }

            if (type != JxlAcStrategyType.Dct && type != JxlAcStrategyType.Dct2X2 && type != JxlAcStrategyType.Identity && butteraugliTarget > 4.0)
            {
                float mul = 1.0f;
                if (butteraugliTarget < 12.0)
                {
                    mul = (float)(mul * ((12.0 - 4.0) / (butteraugliTarget - 4.0)));
                }

                entropyMul += AvoidEntropyOfTransforms * mul;
            }

            float entropy = EstimateEntropy(type, entropyMul, x, y, config, cmapFactors, scratch);
            if (entropy < best)
            {
                bestTx = type;
                best = entropy;
            }
        }

        entropyOut = (float)best;
        return bestTx;
    }

    // libjxl's ProcessRectACS from Hare down: the best 8x8 strategy of each block of the tile, then the
    // aligned merges into larger DCTs.
    private static void ProcessRectAcs(JxlAcsConfig config, JxlSpeedTier speedTier, float butteraugliTarget, JxlRect rect, JxlColorCorrelation cmap, JxlAcStrategyImage acStrategy, JxlAcsScratch scratch)
    {
        // Main philosophy here:
        // 1. First find best 8x8 transform for each area.
        // 2. Merging them into larger transforms where possibly, but starting from the smallest transforms
        // (16x8 and 8x16).
        // Additional complication: 16x8 and 8x16 are considered simultaneously and fairly against each other.
        // We are looking at 64x64 squares since the Y-to-X and Y-to-B maps happen to be at that resolution,
        // and having integral transforms cross these boundaries leads to additional complications.
        int bx = (int)rect.X0;
        int by = (int)rect.Y0;
        DebugGuard.IsTrue(rect.XSize <= 8 && rect.YSize <= 8, "A tile has at most 8x8 blocks");
        int tx = bx / ColorTileDimInBlocks;
        int ty = by / ColorTileDimInBlocks;
        int tile = (ty * cmap.XSizeTiles) + tx;
        ReadOnlySpan<float> cmapFactors = stackalloc float[3]
        {
            cmap.YtoXRatio(cmap.YtoXMap[tile]),
            0.0f,
            cmap.YtoBRatio(cmap.YtoBMap[tile]),
        };
        if (speedTier > JxlSpeedTier.Hare)
        {
            return;
        }

        // First compute the best 8x8 transform for each square. Later, we do not experiment with different
        // combinations, but only use the best of the 8x8s when DCT8X8 is specified in the tree search.
        // 8x8 transforms have 10 variants, but every larger transform is just a DCT.
        Span<float> entropyEstimate = stackalloc float[64];
        entropyEstimate.Clear();

        // Favor all 8x8 transforms (against 16x8 and larger transforms)) at low butteraugli_target distances.
        float mul8x8 = K8x8Mul2 + (K8x8Mul1 / (butteraugliTarget + K8x8Base));
        for (int iy = 0; iy < rect.YSize; iy++)
        {
            for (int ix = 0; ix < rect.XSize; ix++)
            {
                JxlAcStrategyType bestOf8x8s = FindBest8x8Transform(8 * (bx + ix), 8 * (by + iy), speedTier, butteraugliTarget, config, cmapFactors, scratch, out float entropy);
                acStrategy.Set(bx + ix, by + iy, bestOf8x8s);
                entropyEstimate[(iy * 8) + ix] = entropy * mul8x8;
            }
        }

        // Merge when a larger transform is better than the previously searched best combination of 8x8
        // transforms. The encoder has no decoding speed tier option, so libjxl's decoding_speed_tier is 0:
        // every entry of kTransformsForMerge is tried, and 32x32 and the 64x64 level are enabled.
        // Priority is a tricky kludge to avoid collisions so that transforms don't overlap.
        Span<byte> priority = stackalloc byte[64];
        priority.Clear();
        const bool enable32x32 = true;
        int ysize = (int)rect.YSize;
        int xsize = (int)rect.XSize;
        foreach ((JxlAcStrategyType type, byte candidatePriority, float entropyMul) in TransformsForMerge)
        {
            JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy((int)type);
            for (int cy = 0; cy + acs.CoveredBlocksY - 1 < ysize; cy += acs.CoveredBlocksY)
            {
                for (int cx = 0; cx + acs.CoveredBlocksX - 1 < xsize; cx += acs.CoveredBlocksX)
                {
                    if (cy + 7 < ysize && cx + 7 < xsize)
                    {
                        if (type == JxlAcStrategyType.Dct32X64)
                        {
                            // We handle both DCT8X16 and DCT16X8 at the same time.
                            if ((cy | cx) % 8 == 0)
                            {
                                FindBestFirstLevelDivisionForSquare(8, true, bx, by, cx, cy, config, cmapFactors, acStrategy, entropyMul, EntropyMul64X64, entropyEstimate, scratch);
                            }

                            continue;
                        }
                        else if (type == JxlAcStrategyType.Dct32X16)
                        {
                            // We handled both DCT8X16 and DCT16X8 at the same time, and that is above. The
                            // last column and last row, when the last column or last row is odd numbered,
                            // are still handled by TryMergeAcs.
                            continue;
                        }
                    }

                    if ((type == JxlAcStrategyType.Dct16X32 && cy % 4 != 0) || (type == JxlAcStrategyType.Dct32X16 && cx % 4 != 0))
                    {
                        // already covered by FindBest32X32
                        continue;
                    }

                    if (cy + 3 < ysize && cx + 3 < xsize)
                    {
                        if (type == JxlAcStrategyType.Dct16X32)
                        {
                            // We handle both DCT8X16 and DCT16X8 at the same time.
                            if ((cy | cx) % 4 == 0)
                            {
                                FindBestFirstLevelDivisionForSquare(4, enable32x32, bx, by, cx, cy, config, cmapFactors, acStrategy, entropyMul, EntropyMul32X32, entropyEstimate, scratch);
                            }

                            continue;
                        }
                        else if (type == JxlAcStrategyType.Dct32X16)
                        {
                            // We handled both DCT8X16 and DCT16X8 at the same time, and that is above. The
                            // last column and last row, when the last column or last row is odd numbered,
                            // are still handled by TryMergeAcs.
                            continue;
                        }
                    }

                    if ((type == JxlAcStrategyType.Dct16X32 && cy % 4 != 0) || (type == JxlAcStrategyType.Dct32X16 && cx % 4 != 0))
                    {
                        // already covered by FindBest32X32
                        continue;
                    }

                    if (cy + 1 < ysize && cx + 1 < xsize)
                    {
                        if (type == JxlAcStrategyType.Dct8X16)
                        {
                            // We handle both DCT8X16 and DCT16X8 at the same time.
                            if ((cy | cx) % 2 == 0)
                            {
                                FindBestFirstLevelDivisionForSquare(2, true, bx, by, cx, cy, config, cmapFactors, acStrategy, entropyMul, EntropyMul16X16, entropyEstimate, scratch);
                            }

                            continue;
                        }
                        else if (type == JxlAcStrategyType.Dct16X8)
                        {
                            // We handled both DCT8X16 and DCT16X8 at the same time, and that is above. The
                            // last column and last row, when the last column or last row is odd numbered,
                            // are still handled by TryMergeAcs.
                            continue;
                        }
                    }

                    if ((type == JxlAcStrategyType.Dct8X16 && cy % 2 == 1) || (type == JxlAcStrategyType.Dct16X8 && cx % 2 == 1))
                    {
                        // already covered by FindBestFirstLevelDivisionForSquare
                        continue;
                    }

                    // All other merge sizes are handled here. Some of the DCT16X8s and DCT8X16s will still leak
                    // through here when there is an odd number of 8x8 blocks, then the last row and column will
                    // get their DCT16X8s and DCT8X16s through the normal integral transform merging process.
                    TryMergeAcs(type, bx, by, cx, cy, config, cmapFactors, acStrategy, entropyMul, candidatePriority, priority, entropyEstimate, scratch);
                }
            }
        }

        if (speedTier >= JxlSpeedTier.Hare)
        {
            return;
        }

        // Temporary departure (E5a): below Hare libjxl then tries non-aligned 16x8, 8x16 and 16x16 merges,
        // and non-aligned 32x16, 16x32 and 32x32 ones. E5d adds them; until then every tier keeps the
        // aligned merges only, as Hare does.
    }

    // Whether a multi-block strategy crosses the horizontal line of blocks from start_x to end_x on row y:
    // some block there is not the first of its strategy, so a transform above reaches below the line.
    private static bool MultiBlockTransformCrossesHorizontalBoundary(JxlAcStrategyImage acStrategy, int startX, int y, int endX)
    {
        if (startX >= acStrategy.XSize || y >= acStrategy.YSize)
        {
            return false;
        }

        if (y % 8 == 0)
        {
            // Nothing crosses 64x64 boundaries, and the memory on the other side of the 64x64 block may still
            // uninitialized.
            return false;
        }

        endX = Math.Min(endX, acStrategy.XSize);

        // The first multiblock might be before the start_x, let's adjust it to point to the first
        // IsFirstBlock() == true block we find by backward tracing.
        int startXLimit = startX & ~7;
        while (startX != startXLimit && !acStrategy.Get(startX, y).IsFirstBlock)
        {
            --startX;
        }

        for (int x = startX; x < endX;)
        {
            JxlAcStrategy acs = acStrategy.Get(x, y);
            if (acs.IsFirstBlock)
            {
                x += acs.CoveredBlocksX;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    // The same for the vertical line of blocks from start_y to end_y on column x.
    private static bool MultiBlockTransformCrossesVerticalBoundary(JxlAcStrategyImage acStrategy, int x, int startY, int endY)
    {
        if (x >= acStrategy.XSize || startY >= acStrategy.YSize)
        {
            return false;
        }

        if (x % 8 == 0)
        {
            // Nothing crosses 64x64 boundaries, and the memory on the other side of the 64x64 block may still
            // uninitialized.
            return false;
        }

        endY = Math.Min(endY, acStrategy.YSize);

        // The first multiblock might be before the start_y, let's adjust it to point to the first
        // IsFirstBlock() == true block we find by backward tracing.
        int startYLimit = startY & ~7;
        while (startY != startYLimit && !acStrategy.Get(x, startY).IsFirstBlock)
        {
            --startY;
        }

        for (int y = startY; y < endY;)
        {
            JxlAcStrategy acs = acStrategy.Get(x, y);
            if (acs.IsFirstBlock)
            {
                y += acs.CoveredBlocksY;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    // cx, cy addresses the left, upper 8x8 block position of the candidate transform. Takes the candidate
    // when its estimate is below the sum of the blocks it covers and none of them was taken by a merge of
    // the same or a higher priority.
    private static void TryMergeAcs(JxlAcStrategyType acsRaw, int bx, int by, int cx, int cy, JxlAcsConfig config, ReadOnlySpan<float> cmapFactors, JxlAcStrategyImage acStrategy, float entropyMul, byte candidatePriority, Span<byte> priority, Span<float> entropyEstimate, JxlAcsScratch scratch)
    {
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy((int)acsRaw);
        float entropyCurrent = 0;
        for (int iy = 0; iy < acs.CoveredBlocksY; ++iy)
        {
            for (int ix = 0; ix < acs.CoveredBlocksX; ++ix)
            {
                if (priority[((cy + iy) * 8) + cx + ix] >= candidatePriority)
                {
                    // Transform would reuse already allocated blocks and lead to invalid overlaps, for example
                    // DCT64X32 vs. DCT32X64.
                    return;
                }

                entropyCurrent += entropyEstimate[((cy + iy) * 8) + cx + ix];
            }
        }

        float entropyCandidate = EstimateEntropy(acsRaw, entropyMul, (bx + cx) * 8, (by + cy) * 8, config, cmapFactors, scratch);
        if (entropyCandidate >= entropyCurrent)
        {
            return;
        }

        // Accept the candidate.
        for (int iy = 0; iy < acs.CoveredBlocksY; iy++)
        {
            for (int ix = 0; ix < acs.CoveredBlocksX; ix++)
            {
                entropyEstimate[((cy + iy) * 8) + cx + ix] = 0;
                priority[((cy + iy) * 8) + cx + ix] = candidatePriority;
            }
        }

        acStrategy.Set(bx + cx, by + cy, acsRaw);
        entropyEstimate[(cy * 8) + cx] = entropyCandidate;
    }

    // The estimate of a strategy goes to its first block, and its other blocks count 0.
    private static void SetEntropyForTransform(int cx, int cy, JxlAcStrategyType acsRaw, float entropy, Span<float> entropyEstimate)
    {
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy((int)acsRaw);
        for (int dy = 0; dy < acs.CoveredBlocksY; ++dy)
        {
            for (int dx = 0; dx < acs.CoveredBlocksX; ++dx)
            {
                entropyEstimate[((cy + dy) * 8) + cx + dx] = 0.0f;
            }
        }

        entropyEstimate[(cy * 8) + cx] = entropy;
    }

    private static JxlAcStrategyType AcsSquare(int blocks)
        => blocks == 2 ? JxlAcStrategyType.Dct16X16 : blocks == 4 ? JxlAcStrategyType.Dct32X32 : JxlAcStrategyType.Dct64X64;

    private static JxlAcStrategyType AcsVerticalSplit(int blocks)
        => blocks == 2 ? JxlAcStrategyType.Dct16X8 : blocks == 4 ? JxlAcStrategyType.Dct32X16 : JxlAcStrategyType.Dct64X32;

    private static JxlAcStrategyType AcsHorizontalSplit(int blocks)
        => blocks == 2 ? JxlAcStrategyType.Dct8X16 : blocks == 4 ? JxlAcStrategyType.Dct16X32 : JxlAcStrategyType.Dct32X64;

    // The following function tries to merge smaller transforms into squares and the rectangles originating
    // from a single middle division (horizontal or vertical) fairly.
    // This is now generalized to concern about squares of blocks X blocks size, where a block is 8x8 pixels.
    private static void FindBestFirstLevelDivisionForSquare(int blocks, bool allowSquareTransform, int bx, int by, int cx, int cy, JxlAcsConfig config, ReadOnlySpan<float> cmapFactors, JxlAcStrategyImage acStrategy, float entropyMulJxK, float entropyMulJxJ, Span<float> entropyEstimate, JxlAcsScratch scratch)
    {
        // We denote J for the larger dimension here, and K for the smaller. For example, for 32x32 block
        // splitting, J would be 32, K 16.
        int blocksHalf = blocks / 2;
        JxlAcStrategyType acsRawJxK = AcsVerticalSplit(blocks);
        JxlAcStrategyType acsRawKxJ = AcsHorizontalSplit(blocks);
        JxlAcStrategyType acsRawJxJ = AcsSquare(blocks);

        // Let's check if we can consider a JXJ block here at all. This is not necessary in the basic use of
        // hierarchically merging blocks in the simplest possible way, but is needed when we try other
        // 'floating' options of merging, possibly after a simple hierarchical merge has been explored.
        if (MultiBlockTransformCrossesHorizontalBoundary(acStrategy, bx + cx, by + cy, bx + cx + blocks) ||
            MultiBlockTransformCrossesHorizontalBoundary(acStrategy, bx + cx, by + cy + blocks, bx + cx + blocks) ||
            MultiBlockTransformCrossesVerticalBoundary(acStrategy, bx + cx, by + cy, by + cy + blocks) ||
            MultiBlockTransformCrossesVerticalBoundary(acStrategy, bx + cx + blocks, by + cy, by + cy + blocks))
        {
            // not suitable for JxJ analysis, some transforms leak out.
            return;
        }

        // For floating transforms there may be already blocks selected that make either or both JXK and KXJ
        // not feasible for this location.
        bool allowJxK = !MultiBlockTransformCrossesVerticalBoundary(acStrategy, bx + cx + blocksHalf, by + cy, by + cy + blocks);
        bool allowKxJ = !MultiBlockTransformCrossesHorizontalBoundary(acStrategy, bx + cx, by + cy + blocksHalf, bx + cx + blocks);

        // Current entropies aggregated on NxN resolution.
        float entropy00 = 0;
        float entropy01 = 0;
        float entropy10 = 0;
        float entropy11 = 0;
        for (int dy = 0; dy < blocks; ++dy)
        {
            for (int dx = 0; dx < blocks; ++dx)
            {
                float e = entropyEstimate[((cy + dy) * 8) + cx + dx];
                if (dy < blocksHalf)
                {
                    if (dx < blocksHalf)
                    {
                        entropy00 += e;
                    }
                    else
                    {
                        entropy01 += e;
                    }
                }
                else if (dx < blocksHalf)
                {
                    entropy10 += e;
                }
                else
                {
                    entropy11 += e;
                }
            }
        }

        float entropyJxKLeft = float.MaxValue;
        float entropyJxKRight = float.MaxValue;
        float entropyKxJTop = float.MaxValue;
        float entropyKxJBottom = float.MaxValue;
        float entropyJxJ = float.MaxValue;
        if (allowJxK)
        {
            if (acStrategy.Get(bx + cx, by + cy).Strategy != acsRawJxK)
            {
                entropyJxKLeft = EstimateEntropy(acsRawJxK, entropyMulJxK, (bx + cx) * 8, (by + cy) * 8, config, cmapFactors, scratch);
            }

            if (acStrategy.Get(bx + cx + blocksHalf, by + cy).Strategy != acsRawJxK)
            {
                entropyJxKRight = EstimateEntropy(acsRawJxK, entropyMulJxK, (bx + cx + blocksHalf) * 8, (by + cy) * 8, config, cmapFactors, scratch);
            }
        }

        if (allowKxJ)
        {
            if (acStrategy.Get(bx + cx, by + cy).Strategy != acsRawKxJ)
            {
                entropyKxJTop = EstimateEntropy(acsRawKxJ, entropyMulJxK, (bx + cx) * 8, (by + cy) * 8, config, cmapFactors, scratch);
            }

            if (acStrategy.Get(bx + cx, by + cy + blocksHalf).Strategy != acsRawKxJ)
            {
                entropyKxJBottom = EstimateEntropy(acsRawKxJ, entropyMulJxK, (bx + cx) * 8, (by + cy + blocksHalf) * 8, config, cmapFactors, scratch);
            }
        }

        if (allowSquareTransform)
        {
            // We control the exploration of the square transform separately so that we can turn it off at
            // high decoding speeds for 32x32, but still allow exploring 16x32 and 32x16.
            entropyJxJ = EstimateEntropy(acsRawJxJ, entropyMulJxJ, (bx + cx) * 8, (by + cy) * 8, config, cmapFactors, scratch);
        }

        // Test if this block should have JXK or KXJ transforms, because it can have only one or the other.
        float costJxN = MathF.Min(entropyJxKLeft, entropy00 + entropy10) + MathF.Min(entropyJxKRight, entropy01 + entropy11);
        float costNxJ = MathF.Min(entropyKxJTop, entropy00 + entropy01) + MathF.Min(entropyKxJBottom, entropy10 + entropy11);
        if (entropyJxJ < costJxN && entropyJxJ < costNxJ)
        {
            acStrategy.Set(bx + cx, by + cy, acsRawJxJ);
            SetEntropyForTransform(cx, cy, acsRawJxJ, entropyJxJ, entropyEstimate);
        }
        else if (costJxN < costNxJ)
        {
            if (entropyJxKLeft < entropy00 + entropy10)
            {
                acStrategy.Set(bx + cx, by + cy, acsRawJxK);
                SetEntropyForTransform(cx, cy, acsRawJxK, entropyJxKLeft, entropyEstimate);
            }

            if (entropyJxKRight < entropy01 + entropy11)
            {
                acStrategy.Set(bx + cx + blocksHalf, by + cy, acsRawJxK);
                SetEntropyForTransform(cx + blocksHalf, cy, acsRawJxK, entropyJxKRight, entropyEstimate);
            }
        }
        else
        {
            if (entropyKxJTop < entropy00 + entropy01)
            {
                acStrategy.Set(bx + cx, by + cy, acsRawKxJ);
                SetEntropyForTransform(cx, cy, acsRawKxJ, entropyKxJTop, entropyEstimate);
            }

            if (entropyKxJBottom < entropy10 + entropy11)
            {
                acStrategy.Set(bx + cx, by + cy + blocksHalf, acsRawKxJ);
                SetEntropyForTransform(cx, cy + blocksHalf, acsRawKxJ, entropyKxJBottom, entropyEstimate);
            }
        }
    }
}
