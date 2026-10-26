// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_modular_simd.h and lib/jxl/enc_modular_simd.cc (EstimateCost, estimate_cost_detail::ContextMap).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Entropy;

namespace PinataLabs.ImagePastel.Formats.Jxl.Modular;

/// <summary>
/// libjxl's <c>EstimateCost</c>: a quick estimate, in bits, of a Modular image coded with the clamped
/// gradient predictor and 17 contexts on the local gradient, which the encoder uses to compare transforms.
/// </summary>
internal static class JxlModularCostEstimate
{
    // libjxl's estimate_cost_detail::kLastThreshold - 1: a larger max_diff has the last context.
    private const uint MaxDiffCap = 500;

    // libjxl's estimate_cost_detail::kLastCtx.
    private const int LastCtx = 16;

    // The thresholds behind libjxl's ContextMap: max_diff has the context of the last one it reaches.
    private static readonly uint[] Cutoffs = { 0, 1, 3, 5, 7, 11, 15, 23, 31, 47, 63, 95, 127, 191, 255, 392, 500 };

    // libjxl's ContextMap table, built from the cutoffs as its comment describes.
    private static readonly byte[] ContextMap = CreateContextMap();

    /// <summary>Estimates the cost of <paramref name="image"/> in bits.</summary>
    /// <param name="image">The image; every channel counts, meta channels included.</param>
    /// <returns>The estimate.</returns>
    /// <remarks>
    /// libjxl runs a scalar loop on scalar targets and a Highway loop otherwise. They number the contexts
    /// in opposite orders but give the same histograms. This port follows the Highway loop, which cjxl
    /// runs, so the fractional parts are summed in its order. Departure (ADR 0001): the Highway loop
    /// adds the extra bits in 32-bit lanes; this adds them in a 64-bit sum, which differs only when a
    /// lane would overflow, past about 2^27 samples.
    /// </remarks>
    public static float EstimateCost(JxlModularImage image)
    {
        ulong histoCost = 0;
        float histoCostFrac = 0.0f;
        ulong extraBits = 0;

        // libjxl's default HybridUintConfig.
        JxlHybridUintConfig config = new(4, 2, 0);
        JxlHistogram[] histo = new JxlHistogram[LastCtx + 1];
        foreach (JxlModularChannel ch in image.Channel)
        {
            if (ch.H == 0 || ch.W == 0)
            {
                continue;
            }

            for (int i = 0; i < histo.Length; i++)
            {
                histo[i] = new JxlHistogram();
            }

            // The first row: the residual from the left sample (0 at x = 0), all in context 0.
            Span<int> r = ch.Row(0);
            int left = 0;
            for (int x = 0; x < ch.W; x++)
            {
                extraBits += Add(histo[0], config, unchecked(r[x] - left));
                left = r[x];
            }

            for (int y = 1; y < ch.H; y++)
            {
                r = ch.Row(y);
                Span<int> t = ch.Row(y - 1);

                // At x = 0 the left and top-left samples are the top one.
                left = t[0];
                int topleft = t[0];
                for (int x = 0; x < ch.W; x++)
                {
                    int top = t[x];
                    int m = Math.Min(left, top);
                    int mx = Math.Max(left, top);
                    uint maxDiff = unchecked((uint)(Math.Max(topleft, mx) - Math.Min(topleft, m)));
                    int ctx = ContextMap[Math.Min(maxDiff, MaxDiffCap)];
                    int prediction = JxlModularPredict.ClampedGradient(top, left, topleft);
                    extraBits += Add(histo[ctx], config, unchecked(r[x] - prediction));
                    left = r[x];
                    topleft = top;
                }
            }

            foreach (JxlHistogram h in histo)
            {
                float fCost = h.ShannonEntropy();
                ulong iCost = (ulong)fCost;
                histoCost += iCost;
                histoCostFrac += fCost - iCost;
            }
        }

        // libjxl returns the integer total as a StatusOr<float>.
        ulong totalCost = extraBits + histoCost + (ulong)histoCostFrac;
        return totalCost;
    }

    // Counts the token of one residual and returns its extra bits.
    private static uint Add(JxlHistogram histo, JxlHybridUintConfig config, int residual)
    {
        config.Encode(JxlBits.PackSigned(residual), out uint token, out uint nbits, out _);
        histo.Add((int)token);
        return nbits;
    }

    private static byte[] CreateContextMap()
    {
        byte[] map = new byte[MaxDiffCap + 1];
        for (int i = 0; i < map.Length; i++)
        {
            int ctx = -1;
            foreach (uint cutoff in Cutoffs)
            {
                ctx += cutoff <= i ? 1 : 0;
            }

            map[i] = (byte)ctx;
        }

        return map;
    }
}
