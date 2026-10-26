// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_coeff_order.cc (ComputeUsedOrders, ComputeCoeffOrder, TokenizePermutation,
// EncodeCoeffOrder and EncodeCoeffOrders), lib/jxl/lehmer_code.h (ComputeLehmerCode) and
// lib/jxl/enc_frame.cc (ComputeAllCoeffOrders).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The encoder's coefficient orders: which orders the frame uses, the order of each, and the orders the AC
/// global section codes. A customised order sorts the coefficients of each channel by how often they are
/// zero, and is coded as a Lehmer code of the permutation of the natural order.
/// </summary>
internal static class JxlEncCoeffOrder
{
    private const int DctBlockSize = 64;

    // The group size in blocks (libjxl's kGroupDimInBlocks).
    private const int GroupDimInBlocks = JxlFrameDimensions.GroupDim / JxlFrameDimensions.BlockDim;

    /// <summary>
    /// Returns libjxl's <c>ComputeUsedOrders</c>: the order buckets the strategies use, and the ones whose
    /// order is worth customising.
    /// </summary>
    /// <param name="speedTier">The speed tier.</param>
    /// <param name="acStrategy">The strategy of each block.</param>
    /// <returns>The used orders and the orders to customise, one bit per bucket.</returns>
    public static (uint UsedOrders, uint CustomizedOrders) ComputeUsedOrders(JxlSpeedTier speedTier, JxlAcStrategyImage acStrategy)
    {
        // At Falcon and faster only DCT8 (order bucket 0) is used, so the bitfield is 1. libjxl's comment
        // here says Falcon does no reordering, but the second value customises the DCT8 order.
        if (speedTier >= JxlSpeedTier.Falcon)
        {
            return (1, 1);
        }

        uint ret = 0;
        uint retCustomize = 0;
        for (int by = 0; by < acStrategy.YSize; ++by)
        {
            for (int bx = 0; bx < acStrategy.XSize; ++bx)
            {
                int ord = JxlCoeffOrder.StrategyOrder[acStrategy.Get(bx, by).RawStrategy];

                // Do not customize coefficient orders for blocks bigger than 32x32.
                ret |= 1u << ord;
                if (ord > 6)
                {
                    continue;
                }

                retCustomize |= 1u << ord;
            }
        }

        // Use default orders for small images.
        if (acStrategy.XSize < 5 && acStrategy.YSize < 5)
        {
            return (ret, 0);
        }

        return (ret, retCustomize);
    }

    /// <summary>
    /// Sets the orders of the buckets in <paramref name="currentUsedAcs"/>, as libjxl's
    /// <c>ComputeCoeffOrder</c>: the natural order, or for the buckets of
    /// <paramref name="currentUsedOrders"/> the coefficients sorted by their number of zeros. Adds the
    /// orders that differ from the natural one to <paramref name="allUsedOrders"/>.
    /// </summary>
    /// <param name="speedTier">The speed tier.</param>
    /// <param name="acImage">The quantised coefficients of each group and channel (libjxl's ACImage).</param>
    /// <param name="acStrategy">The strategy of each block.</param>
    /// <param name="frameDim">The frame dimensions.</param>
    /// <param name="allUsedOrders">The orders the frame codes.</param>
    /// <param name="currentUsedAcs">The order buckets used.</param>
    /// <param name="currentUsedOrders">The order buckets to customise.</param>
    /// <param name="order">The orders, laid out by <see cref="JxlCoeffOrder.Offset"/>.</param>
    public static void ComputeCoeffOrder(
        JxlSpeedTier speedTier,
        int[][][] acImage,
        JxlAcStrategyImage acStrategy,
        JxlFrameDimensions frameDim,
        ref uint allUsedOrders,
        uint currentUsedAcs,
        uint currentUsedOrders,
        Span<uint> order)
    {
        // Departure (ADR 0001): libjxl sizes this for every bucket (kCoeffOrderMaxSize). The counts are
        // written for every bucket used (64x64 and 64x32 too, whose orders stay natural) and read for the
        // buckets to customise, so this allocates up to the end of the last of them.
        long[] numZeros = new long[NumZerosSize(currentUsedAcs | currentUsedOrders)];

        // If compressing at high speed and only using 8x8 DCTs, only consider a subset of blocks.
        double blockFraction = 1.0f;
        if (speedTier >= JxlSpeedTier.Squirrel && currentUsedOrders == 1)
        {
            blockFraction = 0.5f;
        }

        // No need to compute number of zero coefficients if all orders are the default.
        if (currentUsedOrders != 0)
        {
            CountZeros(acImage, acStrategy, frameDim, (ulong)((ulong.MaxValue >> 32) * blockFraction), numZeros);
        }

        ulong[] countAndIdx = Array.Empty<ulong>();
        uint[] naturalOrderBuffer = Array.Empty<uint>();
        uint computed = 0;
        for (int o = 0; o < JxlAcStrategy.NumValidStrategies; ++o)
        {
            int ord = JxlCoeffOrder.StrategyOrder[o];
            if ((computed & (1u << ord)) != 0)
            {
                continue;
            }

            computed |= 1u << ord;
            JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(o);
            int sz = DctBlockSize * acs.CoveredBlocksX * acs.CoveredBlocksY;

            // Do nothing for transforms that don't appear.
            if (((1u << ord) & ~currentUsedAcs) != 0)
            {
                continue;
            }

            // Do nothing if we already committed to this custom order previously. libjxl also skips the
            // strategies of an earlier pass (prev_used_acs), which one pass never has.
            if (((1u << ord) & allUsedOrders) != 0)
            {
                continue;
            }

            if (naturalOrderBuffer.Length < sz)
            {
                naturalOrderBuffer = new uint[sz];
                countAndIdx = new ulong[sz];
            }

            acs.ComputeNaturalCoeffOrder(naturalOrderBuffer);

            // Ensure natural coefficient order is not permuted if the order is not transmitted.
            if (((1u << ord) & ~currentUsedOrders) != 0)
            {
                for (int c = 0; c < 3; c++)
                {
                    naturalOrderBuffer.AsSpan(0, sz).CopyTo(order.Slice(JxlCoeffOrder.Offset(ord, c), sz));
                }

                continue;
            }

            bool isNondefault = false;
            for (int c = 0; c < 3; c++)
            {
                // Apply zig-zag order.
                int offset = JxlCoeffOrder.Offset(ord, c);

                // libjxl's std::sqrt of an integer is the double overload; the reciprocal is stored as float.
                float invSqrtSz = (float)(1.0 / Math.Sqrt(sz));
                for (int i = 0; i < sz; ++i)
                {
                    int pos = (int)naturalOrderBuffer[i];

                    // We don't care for the exact number -> quantize number of zeros, to get less permuted
                    // order. The LLF count of -1 gives a negative value, which truncates to 0 (through long,
                    // so the conversion does not depend on the runtime).
                    ulong count = (ulong)(long)((numZeros[offset + pos] * invSqrtSz) + 0.1f);

                    // Saving the index breaks the ties: the sort keeps equal counts in their natural order.
                    countAndIdx[i] = (count << 16) | (uint)i;
                }

                Array.Sort(countAndIdx, 0, sz);

                // Grab indices.
                for (int i = 0; i < sz; ++i)
                {
                    uint pos = naturalOrderBuffer[(int)(countAndIdx[i] & 0xFFFF)];
                    order[offset + i] = pos;
                    isNondefault |= naturalOrderBuffer[i] != pos;
                }
            }

            if (!isNondefault)
            {
                currentUsedOrders &= ~(1u << ord);
            }
        }

        allUsedOrders |= currentUsedOrders;
    }

    /// <summary>
    /// Writes the coefficient orders of <paramref name="usedOrders"/>, as libjxl's <c>EncodeCoeffOrders</c>:
    /// the Lehmer code of each channel's permutation of the natural order, entropy coded. Nothing is
    /// written when no order is coded.
    /// </summary>
    /// <param name="usedOrders">The orders to code, one bit per bucket.</param>
    /// <param name="order">The orders, laid out by <see cref="JxlCoeffOrder.Offset"/>.</param>
    /// <param name="writer">The writer.</param>
    public static void EncodeCoeffOrders(uint usedOrders, ReadOnlySpan<uint> order, JxlBitWriter writer)
    {
        uint computed = 0;
        List<JxlToken>[] tokens = { new() };
        uint[] naturalOrderLut = Array.Empty<uint>();
        uint[] orderZigzag = Array.Empty<uint>();
        for (int o = 0; o < JxlAcStrategy.NumValidStrategies; ++o)
        {
            int ord = JxlCoeffOrder.StrategyOrder[o];
            if ((computed & (1u << ord)) != 0)
            {
                continue;
            }

            computed |= 1u << ord;
            if ((usedOrders & (1u << ord)) == 0)
            {
                continue;
            }

            JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(o);
            int llf = acs.CoveredBlocksX * acs.CoveredBlocksY;
            int size = DctBlockSize * llf;
            if (naturalOrderLut.Length < size)
            {
                naturalOrderLut = new uint[size];
                orderZigzag = new uint[size];
            }

            acs.ComputeNaturalCoeffOrderLut(naturalOrderLut);
            for (int c = 0; c < 3; c++)
            {
                EncodeCoeffOrder(order.Slice(JxlCoeffOrder.Offset(ord, c), size), llf, tokens[0], orderZigzag, naturalOrderLut);
            }
        }

        // Do not write anything if no order is used.
        if (usedOrders != 0)
        {
            // libjxl's default HistogramParams(): kBest clustering and kBest hybrid uint search, RLE and
            // kPrecise. Departure (ADR 0001), shared with every stream that keeps those defaults: kBest
            // clusters as kFast does and keeps the default uint configuration (see JxlHistogramClustering
            // and JxlEntropyEncodingData), so the codes are valid but may cost a few more bits.
            JxlEntropyEncodingData codes = new();
            JxlEntropyEncoder.BuildAndEncodeHistograms(new JxlHistogramParams(), JxlCoeffOrder.PermutationContexts, tokens, codes, writer);
            JxlEntropyEncoder.WriteTokens(tokens[0], codes, 0, writer);
        }
    }

    /// <summary>
    /// Computes the Lehmer (factorial base) code of <paramref name="permutation"/>, an array of
    /// <paramref name="n"/> unique indices in [0, n), with a Fenwick tree: N log N time.
    /// </summary>
    /// <param name="permutation">The permutation.</param>
    /// <param name="n">The size.</param>
    /// <param name="code">The code, <paramref name="n"/> entries.</param>
    public static void ComputeLehmerCode(ReadOnlySpan<uint> permutation, int n, Span<uint> code)
    {
        uint[] temp = new uint[n + 1];
        for (int idx = 0; idx < n; ++idx)
        {
            uint s = permutation[idx];

            // Compute sum in Fenwick tree
            uint penalty = 0;
            uint i = s + 1;
            while (i != 0)
            {
                penalty += temp[i];

                // clear lowest bit
                i &= i - 1;
            }

            DebugGuard.IsTrue(s >= penalty, "The input is a permutation");
            code[idx] = s - penalty;
            i = s + 1;

            // Add operation in Fenwick tree
            while (i < n + 1)
            {
                temp[i] += 1;
                i += i & (~i + 1);
            }
        }
    }

    // The number of coefficients of the order buckets in usedOrders: up to the end of the last one.
    private static int NumZerosSize(uint usedOrders)
    {
        int size = 0;
        for (int ord = 0; usedOrders >> ord != 0; ord++)
        {
            if ((usedOrders & (1u << ord)) != 0)
            {
                size = JxlCoeffOrder.Offset(ord + 1, 0);
            }
        }

        return size;
    }

    // Counts the zero coefficients of each position, separately for each bucket and channel, over a sample
    // of the blocks: libjxl's xorshift128+ keeps a block when the high half of its output is at most
    // threshold. The LLF positions are set to -1 so that they come first in the order.
    private static void CountZeros(int[][][] acImage, JxlAcStrategyImage acStrategy, JxlFrameDimensions frameDim, ulong threshold, long[] numZeros)
    {
        // Xorshift128+ adapted from xorshift128+-inl.h. The state runs on across the groups.
        ulong s0State = 0x94D049BB133111EBUL;
        ulong s1State = 0xBF58476D1CE4E5B9UL;
        bool UseSample()
        {
            ulong s1 = s0State;
            ulong s0 = s1State;
            ulong bits = s1 + s0;
            s0State = s0;
            s1 ^= s1 << 23;
            s1 ^= s0 ^ (s1 >> 18) ^ (s0 >> 5);
            s1State = s1;
            return bits >> 32 <= threshold;
        }

        for (long groupIndex = 0; groupIndex < frameDim.NumGroups; groupIndex++)
        {
            JxlRect rect = frameDim.BlockGroupRect(groupIndex);
            DebugGuard.IsTrue(rect.XSize <= GroupDimInBlocks && rect.YSize <= GroupDimInBlocks, "A group has at most 32x32 blocks");
            int[][] rows = acImage[groupIndex];
            int acOffset = 0;
            for (long by = 0; by < rect.YSize; ++by)
            {
                for (long bx = 0; bx < rect.XSize; ++bx)
                {
                    JxlAcStrategy acs = acStrategy.Get((int)(rect.X0 + bx), (int)(rect.Y0 + by));
                    if (!acs.IsFirstBlock)
                    {
                        continue;
                    }

                    // As in libjxl, ac_offset only moves on for a sampled block, so the counts read the
                    // first blocks of the group, as many as were sampled, rather than the sampled ones.
                    // Ported as it is: the order, and so the bytes, follow libjxl's.
                    if (!UseSample())
                    {
                        continue;
                    }

                    int size = DctBlockSize << acs.Log2CoveredBlocks;
                    for (int c = 0; c < 3; ++c)
                    {
                        int orderOffset = JxlCoeffOrder.Offset(JxlCoeffOrder.StrategyOrder[acs.RawStrategy], c);
                        ReadOnlySpan<int> block = rows[c].AsSpan(acOffset, size);
                        for (int k = 0; k < size; k++)
                        {
                            numZeros[orderOffset + k] += block[k] == 0 ? 1 : 0;
                        }

                        // Ensure LLFs are first in the order.
                        int cx = acs.CoveredBlocksX;
                        int cy = acs.CoveredBlocksY;
                        JxlAcStrategy.CoefficientLayout(ref cy, ref cx);
                        for (int iy = 0; iy < cy; iy++)
                        {
                            for (int ix = 0; ix < cx; ix++)
                            {
                                numZeros[orderOffset + (iy * JxlFrameDimensions.BlockDim * cx) + ix] = -1;
                            }
                        }
                    }

                    acOffset += size;
                }
            }
        }
    }

    // libjxl's EncodeCoeffOrder: the order as indices into the natural order, then its permutation tokens,
    // which skip the LLF coefficients (always first).
    private static void EncodeCoeffOrder(ReadOnlySpan<uint> order, int llf, List<JxlToken> tokens, uint[] orderZigzag, uint[] naturalOrderLut)
    {
        int size = DctBlockSize * llf;
        for (int i = 0; i < size; ++i)
        {
            orderZigzag[i] = naturalOrderLut[order[i]];
        }

        TokenizePermutation(orderZigzag.AsSpan(0, size), llf, size, tokens);
    }

    // The tokens of a permutation: the number of Lehmer code entries after skip up to the last non-zero,
    // then each of them, in the context of the one before.
    private static void TokenizePermutation(ReadOnlySpan<uint> order, int skip, int size, List<JxlToken> tokens)
    {
        uint[] lehmer = new uint[size];
        ComputeLehmerCode(order, size, lehmer);
        int end = size;
        while (end > skip && lehmer[end - 1] == 0)
        {
            --end;
        }

        tokens.Add(new JxlToken((uint)JxlCoeffOrder.CoeffOrderContext((uint)size), (uint)(end - skip)));
        uint last = 0;
        for (int i = skip; i < end; ++i)
        {
            tokens.Add(new JxlToken((uint)JxlCoeffOrder.CoeffOrderContext(last), lehmer[i]));
            last = lehmer[i];
        }
    }
}
