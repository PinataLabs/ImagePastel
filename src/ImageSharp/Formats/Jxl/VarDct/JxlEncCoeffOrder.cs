// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_coeff_order.cc (ComputeUsedOrders, the natural-order part of ComputeCoeffOrder, and
// EncodeCoeffOrders for no coded order) and lib/jxl/enc_frame.cc (ComputeAllCoeffOrders).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The encoder's coefficient orders: which orders the frame uses, the order of each, and the orders the AC
/// global section codes.
/// </summary>
/// <remarks>
/// Departure (ADR 0001), temporary: at effort 3 libjxl customises the DCT8 order from the zero counts of
/// the coefficients and codes it as a Lehmer code (<c>ComputeUsedOrders</c> returns {1, 1} at Falcon). Slice
/// E3a writes the natural order, which is valid; slice E3b ports the custom order and drops this departure.
/// </remarks>
internal static class JxlEncCoeffOrder
{
    /// <summary>
    /// Returns libjxl's <c>ComputeUsedOrders</c>: the order buckets the strategies use, and the ones whose
    /// order is worth customising.
    /// </summary>
    /// <param name="speedTier">The speed tier.</param>
    /// <param name="acStrategy">The strategy of each block.</param>
    /// <returns>The used orders and the orders to customise, one bit per bucket.</returns>
    public static (uint UsedOrders, uint CustomizedOrders) ComputeUsedOrders(JxlSpeedTier speedTier, JxlAcStrategyImage acStrategy)
    {
        // At Falcon and faster only DCT8 (order bucket 0) is used, so the bitfield is 1.
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
    /// Sets the orders of the strategies in <paramref name="currentUsedAcs"/>, as libjxl's
    /// <c>ComputeCoeffOrder</c>, and adds the orders to code to <paramref name="allUsedOrders"/>.
    /// </summary>
    /// <param name="currentUsedOrders">The orders to customise; only 0 (every order natural) is ported (E3b).</param>
    /// <param name="currentUsedAcs">The strategies used, one bit per order bucket.</param>
    /// <param name="allUsedOrders">The orders the frame codes.</param>
    /// <param name="order">The orders, laid out by <see cref="JxlCoeffOrder.Offset"/>.</param>
    public static void ComputeCoeffOrder(uint currentUsedOrders, uint currentUsedAcs, ref uint allUsedOrders, Span<uint> order)
    {
        if (currentUsedOrders != 0)
        {
            JxlThrowHelper.ThrowNotSupportedException("Custom coefficient orders (slice E3b)");
        }

        uint computed = 0;
        uint[] naturalOrderBuffer = Array.Empty<uint>();
        for (int o = 0; o < JxlAcStrategy.NumValidStrategies; ++o)
        {
            int ord = JxlCoeffOrder.StrategyOrder[o];
            if ((computed & (1u << ord)) != 0)
            {
                continue;
            }

            computed |= 1u << ord;
            JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(o);
            int sz = 64 * acs.CoveredBlocksX * acs.CoveredBlocksY;

            // Do nothing for transforms that don't appear.
            if (((1u << ord) & ~currentUsedAcs) != 0)
            {
                continue;
            }

            // libjxl also skips orders an earlier pass committed to, which one pass never has.
            if (((1u << ord) & allUsedOrders) != 0)
            {
                continue;
            }

            if (naturalOrderBuffer.Length < sz)
            {
                naturalOrderBuffer = new uint[sz];
            }

            acs.ComputeNaturalCoeffOrder(naturalOrderBuffer);

            // Ensure natural coefficient order is not permuted if the order is not transmitted.
            for (int c = 0; c < 3; c++)
            {
                naturalOrderBuffer.AsSpan(0, sz).CopyTo(order.Slice(JxlCoeffOrder.Offset(ord, c), sz));
            }
        }

        allUsedOrders |= currentUsedOrders;
    }

    /// <summary>
    /// Writes the coefficient orders of <paramref name="usedOrders"/>, as libjxl's <c>EncodeCoeffOrders</c>.
    /// Nothing is written when no order is coded.
    /// </summary>
    /// <param name="usedOrders">The orders to code, one bit per bucket.</param>
    public static void EncodeCoeffOrders(uint usedOrders)
    {
        // Do not write anything if no order is used. Coding an order is slice E3b.
        if (usedOrders != 0)
        {
            JxlThrowHelper.ThrowNotSupportedException("Writing custom coefficient orders (slice E3b)");
        }
    }
}
