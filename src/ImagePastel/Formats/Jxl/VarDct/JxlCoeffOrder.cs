// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/coeff_order.h and lib/jxl/coeff_order.cc (kCoeffOrderOffset, kStrategyOrder, CoeffOrderContext,
// ReadPermutation, DecodePermutation, DecodeCoeffOrder, DecodeCoeffOrders) and lib/jxl/lehmer_code.h
// (DecodeLehmerCode).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Entropy;

namespace PinataLabs.ImagePastel.Formats.Jxl.VarDct;

/// <summary>
/// The coefficient orders of a VarDCT frame: the natural order of each strategy, or a permutation of
/// it coded as a Lehmer code.
/// </summary>
internal static class JxlCoeffOrder
{
    /// <summary>The number of contexts of a permutation.</summary>
    public const int PermutationContexts = 8;

    /// <summary>The number of coefficients of one set of orders for every strategy (libjxl's <c>kCoeffOrderMaxSize</c>).</summary>
    public const int CoeffOrderMaxSize = CoeffOrderLimit * DctBlockSize;

    private const int CoeffOrderLimit = 6156;

    private const int DctBlockSize = 64;

    /// <summary>
    /// Gets, for each order bucket and channel, where its order starts, in units of 64 coefficients.
    /// Entry 3 * bucket + channel; the last entry is the total.
    /// </summary>
    public static ReadOnlySpan<int> CoeffOrderOffset => new int[]
    {
        0, 1, 2, 3, 4, 5, 6, 10, 14, 18,
        34, 50, 66, 68, 70, 72, 76, 80, 84, 92,
        100, 108, 172, 236, 300, 332, 364, 396, 652, 908,
        1164, 1292, 1420, 1548, 2572, 3596, 4620, 5132, 5644, CoeffOrderLimit,
    };

    /// <summary>
    /// Gets the order bucket of each strategy. Strategies with different natural orders have different
    /// buckets; a transform of size XxY shares its bucket with YxX.
    /// </summary>
    public static ReadOnlySpan<byte> StrategyOrder => new byte[]
    {
        0, 1, 1, 1, 2, 3, 4, 4, 5, 5, 6, 6, 1, 1,
        1, 1, 1, 1, 7, 8, 8, 9, 10, 10, 11, 12, 12,
    };

    /// <summary>Returns where the order of <paramref name="ord"/> for channel <paramref name="c"/> starts.</summary>
    /// <param name="ord">The order bucket.</param>
    /// <param name="c">The channel.</param>
    /// <returns>The offset, in coefficients.</returns>
    public static int Offset(int ord, int c) => CoeffOrderOffset[(3 * ord) + c] * DctBlockSize;

    /// <summary>Returns the number of coefficients one set of orders needs for the strategies used.</summary>
    /// <param name="usedAcs">The strategies used, one bit per raw strategy.</param>
    /// <returns>The size.</returns>
    public static int OrderSize(uint usedAcs)
    {
        int size = 0;
        for (int o = 0; o < JxlAcStrategy.NumValidStrategies; o++)
        {
            if (((1u << o) & usedAcs) == 0)
            {
                continue;
            }

            int ord = StrategyOrder[o];
            size = Math.Max(CoeffOrderOffset[3 * (ord + 1)] * DctBlockSize, size);
        }

        return size;
    }

    /// <summary>
    /// Reads the coefficient orders of one pass into <paramref name="order"/>, laid out by
    /// <see cref="Offset"/>. Orders the frame does not use are read and dropped.
    /// </summary>
    /// <param name="usedOrders">The orders that are coded, one bit per bucket.</param>
    /// <param name="usedAcs">The strategies used, one bit per raw strategy.</param>
    /// <param name="order">The orders.</param>
    /// <param name="br">The reader.</param>
    public static void DecodeCoeffOrders(uint usedOrders, uint usedAcs, Span<uint> order, JxlBitReader br)
    {
        uint computed = 0;
        byte[] contextMap = null;
        JxlAnsSymbolReader reader = null;
        uint[] naturalOrder = Array.Empty<uint>();

        // Bitstream does not have histograms if no coefficient order is used.
        if (usedOrders != 0)
        {
            JxlAnsCode code = new();
            contextMap = JxlEntropyDecoder.DecodeHistograms(br, PermutationContexts, code);
            reader = new JxlAnsSymbolReader(code, br);
        }

        uint acsMask = 0;
        for (int o = 0; o < JxlAcStrategy.NumValidStrategies; o++)
        {
            if ((usedAcs & (1u << o)) == 0)
            {
                continue;
            }

            acsMask |= 1u << StrategyOrder[o];
        }

        for (int o = 0; o < JxlAcStrategy.NumValidStrategies; o++)
        {
            int ord = StrategyOrder[o];
            if ((computed & (1u << ord)) != 0)
            {
                continue;
            }

            computed |= 1u << ord;
            JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(o);
            bool used = (acsMask & (1u << ord)) != 0;

            int llf = acs.CoveredBlocksX * acs.CoveredBlocksY;
            int size = DctBlockSize * llf;

            if (used || (usedOrders & (1u << ord)) != 0)
            {
                if (naturalOrder.Length < size)
                {
                    naturalOrder = new uint[size];
                }

                acs.ComputeNaturalCoeffOrder(naturalOrder);
            }

            if ((usedOrders & (1u << ord)) == 0)
            {
                // No need to set the default order if no ACS uses this order.
                if (used)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        naturalOrder.AsSpan(0, size).CopyTo(order.Slice(Offset(ord, c)));
                    }
                }
            }
            else
            {
                for (int c = 0; c < 3; c++)
                {
                    Span<uint> dest = used ? order.Slice(Offset(ord, c), size) : Span<uint>.Empty;
                    DecodeCoeffOrder(llf, size, dest, br, reader, naturalOrder, contextMap);
                }
            }
        }

        if (usedOrders != 0 && !reader.CheckAnsFinalState)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid ANS stream");
        }
    }

    /// <summary>
    /// Reads a permutation of <paramref name="size"/> entries that has its own histograms, over the
    /// <see cref="PermutationContexts"/> permutation contexts, and checks the final ANS state. A permuted TOC
    /// is coded this way, with a skip of 0. Throws <see cref="InvalidImageContentException"/> for an
    /// invalid size or Lehmer code, or a bad final state.
    /// </summary>
    /// <param name="skip">The number of leading entries that keep their place.</param>
    /// <param name="size">The number of entries.</param>
    /// <param name="order">The permutation, <paramref name="size"/> entries.</param>
    /// <param name="br">The reader.</param>
    public static void DecodePermutation(int skip, int size, Span<uint> order, JxlBitReader br)
    {
        JxlAnsCode code = new();
        byte[] contextMap = JxlEntropyDecoder.DecodeHistograms(br, PermutationContexts, code);
        JxlAnsSymbolReader reader = new(code, br);
        ReadPermutation(skip, size, order, br, reader, contextMap);
        if (!reader.CheckAnsFinalState)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid ANS stream");
        }
    }

    /// <summary>
    /// Decodes the Lehmer code in <paramref name="code"/> into <paramref name="permutation"/>, with an
    /// implicit order-statistics tree.
    /// </summary>
    /// <param name="code">The Lehmer code, <paramref name="n"/> entries.</param>
    /// <param name="n">The size.</param>
    /// <param name="permutation">The permutation, <paramref name="n"/> entries.</param>
    public static void DecodeLehmerCode(ReadOnlySpan<uint> code, int n, Span<uint> permutation)
    {
        if (n == 0)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Empty Lehmer code");
        }

        int log2n = JxlBits.CeilLog2Nonzero((uint)n);
        int paddedN = 1 << log2n;

        // temp must have 1 << CeilLog2(n) elements.
        uint[] temp = new uint[paddedN];
        for (int i = 0; i < paddedN; i++)
        {
            int i1 = i + 1;
            temp[i] = (uint)(i1 & -i1);
        }

        for (int i = 0; i < n; i++)
        {
            if (code[i] + (ulong)i >= (ulong)n)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid Lehmer code");
            }

            uint rank = code[i] + 1;

            // Extract i-th unused element via implicit order-statistics tree.
            int bit = paddedN;
            int next = 0;
            for (int b = 0; b <= log2n; b++)
            {
                int cand = next + bit;
                bit >>= 1;
                if (temp[cand - 1] < rank)
                {
                    next = cand;
                    rank -= temp[cand - 1];
                }
            }

            permutation[i] = (uint)next;

            // Mark as used
            next += 1;
            while (next <= paddedN)
            {
                temp[next - 1] -= 1;
                next += next & -next;
            }
        }
    }

    /// <summary>
    /// Returns the context of a permutation token that follows <paramref name="val"/>: the token of
    /// <c>HybridUintConfig(0, 0, 0)</c>, which is 0 for 0 and 1 + FloorLog2(val) otherwise, at most 7.
    /// </summary>
    /// <param name="val">The previous value, or the size for the first token.</param>
    /// <returns>The context.</returns>
    public static int CoeffOrderContext(uint val)
    {
        uint token = val == 0 ? 0 : 1 + (uint)JxlBits.FloorLog2Nonzero(val);
        return (int)Math.Min(token, PermutationContexts - 1);
    }

    private static void ReadPermutation(int skip, int size, Span<uint> order, JxlBitReader br, JxlAnsSymbolReader reader, byte[] contextMap)
    {
        uint[] lehmer = new uint[size];
        uint end = reader.ReadHybridUint(CoeffOrderContext((uint)size), br, contextMap) + (uint)skip;
        if (end > size)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid permutation size");
        }

        uint last = 0;
        for (int i = skip; i < end; ++i)
        {
            lehmer[i] = reader.ReadHybridUint(CoeffOrderContext(last), br, contextMap);
            last = lehmer[i];
            if (lehmer[i] >= size - i)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid lehmer code");
            }
        }

        if (order.IsEmpty)
        {
            return;
        }

        DecodeLehmerCode(lehmer, size, order);
    }

    private static void DecodeCoeffOrder(int llf, int size, Span<uint> order, JxlBitReader br, JxlAnsSymbolReader reader, uint[] naturalOrder, byte[] contextMap)
    {
        ReadPermutation(llf, size, order, br, reader, contextMap);
        if (order.IsEmpty)
        {
            return;
        }

        for (int k = 0; k < size; ++k)
        {
            order[k] = naturalOrder[order[k]];
        }
    }
}
