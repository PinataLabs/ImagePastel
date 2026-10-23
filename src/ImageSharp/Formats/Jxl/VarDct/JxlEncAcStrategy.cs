// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_ac_strategy.cc (EstimateEntropy, FindBest8x8Transform, the 8x8 level of ProcessRectACS,
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
/// its bits plus its information loss.
/// </summary>
/// <remarks>
/// <para>
/// libjxl sums the entropy, the non-zero count and the loss of <c>EstimateEntropy</c> in vector lanes and
/// adds the lanes with Highway's <c>SumOfLanes</c>. This port uses AVX2's 8 lanes, as cjxl does on x64:
/// coefficient <c>i</c> goes to lane <c>i % 8</c>, pixel <c>dx</c> of a block row to lane <c>dx</c>, and
/// the lanes add up as <see cref="SumOfLanes"/> does. On another CPU libjxl's sums may differ by a few
/// units in the last place.
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
    private const int Lanes = 8;

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
    // multiplier. DCT4X8, DCT8X4 and AFV start at Wombat (lossy Thorough, slice E5).
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
    /// Temporary departure (E4g): from Hare down libjxl then merges the 8x8 choices into 16x8 up to 64x64
    /// blocks where that lowers the estimate; until E4h ports the merges, each block keeps its 8x8 choice.
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
                ? Max(config.Quant(x / 8, y / 8), config.Quant(x / 8, (y / 8) + 1))
                : Max(config.Quant(x / 8, y / 8), config.Quant((x / 8) + 1, y / 8));
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

            entropy += config.CostDelta * SumOfLanes(entropyV);
            uint numNzeros = (uint)SumOfLanes(nzerosV);

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

        float lossScalar = MathF.Pow(SumOfLanes(loss) / (numBlocks * DctBlockSize), 1.0f / 8.0f) * (numBlocks * DctBlockSize) / quantNorm16;
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

    // libjxl's ProcessRectACS from Hare down, its 8x8 level: the best 8x8 strategy of each block of the tile.
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

        // Temporary departure (E4g): libjxl now merges the 8x8 choices into larger transforms where the
        // entropy estimate says so, comparing against entropyEstimate; E4h ports the merges.
    }

    // Highway's Max on x86.
    private static float Max(float a, float b) => a < b ? b : a;

    // Highway's SumOfLanes over AVX2's 8 float lanes: each lane of the lower block plus its partner in the
    // upper block, then lanes 0 and 3 and lanes 1 and 2, then those two sums.
    private static float SumOfLanes(ReadOnlySpan<float> lanes)
    {
        float l0 = lanes[0] + lanes[4];
        float l1 = lanes[1] + lanes[5];
        float l2 = lanes[2] + lanes[6];
        float l3 = lanes[3] + lanes[7];
        return (l0 + l3) + (l1 + l2);
    }
}
