// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_context_map.cc (IndexOf, MoveToFront, MoveToFrontTransform, EncodeContextMap).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using System.Linq;
using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Formats.Jxl.Entropy;

/// <summary>Writes a context map, as <see cref="JxlContextMap.DecodeContextMap"/> reads it.</summary>
internal static class JxlContextMapEncoder
{
    /// <summary>
    /// Writes <paramref name="contextMap"/>, which maps each context to one of
    /// <paramref name="numHistograms"/> histograms: as raw entries, or entropy coded with or without
    /// a move-to-front transform, whichever is cheapest.
    /// </summary>
    /// <param name="contextMap">The context map.</param>
    /// <param name="numHistograms">The number of histograms.</param>
    /// <param name="writer">The writer.</param>
    public static void EncodeContextMap(IReadOnlyList<byte> contextMap, int numHistograms, JxlBitWriter writer)
    {
        if (numHistograms == 1)
        {
            // Simple code
            writer.Write(1, 1);

            // 0 bits per entry.
            writer.Write(2, 0);
            return;
        }

        byte[] transformedSymbols = MoveToFrontTransform(contextMap);
        List<List<JxlToken>> tokens = new() { new List<JxlToken>() };
        List<List<JxlToken>> mtfTokens = new() { new List<JxlToken>() };
        foreach (byte ctx in contextMap)
        {
            tokens[0].Add(new JxlToken(0, ctx));
        }

        foreach (byte sym in transformedSymbols)
        {
            mtfTokens[0].Add(new JxlToken(0, sym));
        }

        // Departure (ADR 0001): libjxl leaves these parameters' LZ77 method at its default, kRLE, so
        // it can RLE-code a long context map. This is kNone, which keeps the Fastest output
        // byte-identical to E1's; the output is still valid, and only larger for long context maps.
        JxlHistogramParams parameters = new()
        {
            UintMethod = JxlHistogramParams.HybridUintMethod.ContextMap,
            Lz77 = JxlHistogramParams.Lz77Method.None,
        };

        long ansCost = JxlEntropyEncoder.BuildAndEncodeHistograms(parameters, 1, tokens, new JxlEntropyEncodingData(), null);
        long mtfCost = JxlEntropyEncoder.BuildAndEncodeHistograms(parameters, 1, mtfTokens, new JxlEntropyEncodingData(), null);
        bool useMtf = mtfCost < ansCost;

        // Rebuild token list.
        tokens[0].Clear();
        for (int i = 0; i < transformedSymbols.Length; i++)
        {
            tokens[0].Add(new JxlToken(0, useMtf ? transformedSymbols[i] : contextMap[i]));
        }

        int entryBits = JxlBits.CeilLog2Nonzero((uint)numHistograms);
        long simpleCost = (long)entryBits * contextMap.Count;
        if (entryBits < 4 && simpleCost < ansCost && simpleCost < mtfCost)
        {
            writer.Write(1, 1);
            writer.Write(2, (ulong)entryBits);
            foreach (byte entry in contextMap)
            {
                writer.Write(entryBits, entry);
            }
        }
        else
        {
            writer.Write(1, 0);

            // Use/don't use MTF.
            writer.Write(1, useMtf ? 1UL : 0UL);
            JxlEntropyEncodingData codes = new();
            JxlEntropyEncoder.BuildAndEncodeHistograms(parameters, 1, tokens, codes, writer);
            JxlEntropyEncoder.WriteTokens(tokens[0], codes, 0, writer);
        }
    }

    private static int IndexOf(byte[] v, byte value)
    {
        int i = 0;
        for (; i < v.Length; ++i)
        {
            if (v[i] == value)
            {
                return i;
            }
        }

        return i;
    }

    private static void MoveToFront(byte[] v, int index)
    {
        byte value = v[index];
        for (int i = index; i != 0; --i)
        {
            v[i] = v[i - 1];
        }

        v[0] = value;
    }

    private static byte[] MoveToFrontTransform(IReadOnlyList<byte> v)
    {
        if (v.Count == 0)
        {
            return System.Array.Empty<byte>();
        }

        byte maxValue = v.Max();
        byte[] mtf = new byte[maxValue + 1];
        for (int i = 0; i <= maxValue; ++i)
        {
            mtf[i] = (byte)i;
        }

        byte[] result = new byte[v.Count];
        for (int i = 0; i < v.Count; ++i)
        {
            int index = IndexOf(mtf, v[i]);
            DebugGuard.IsTrue(index < mtf.Length, "MTF value out of range");
            result[i] = (byte)index;
            MoveToFront(mtf, index);
        }

        return result;
    }
}
