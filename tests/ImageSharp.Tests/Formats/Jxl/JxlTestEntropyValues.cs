// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Writes the histograms and symbols that libjxl (https://github.com/libjxl/libjxl, commit ef67fde2) reads
// with DecodeHistograms and ANSSymbolReader::ReadHybridUint (lib/jxl/dec_ans.cc).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Linq;
using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Entropy codes a short list of values for the dictionaries of a <see cref="JxlTestFrame"/> (patches and
/// splines). The values are coded with prefix codes: one cluster per entry of a simple context map, each
/// a single symbol that costs no bits or a flat code of 2^L symbols, and hybrid integers with split
/// exponent 0, so a value v above 0 is the token FloorLog2(v) + 1 followed by FloorLog2(v) raw bits.
/// </summary>
internal static class JxlTestEntropyValues
{
    // The order of the code length code lengths: kCodeLengthCodeOrder in lib/jxl/dec_huffman.cc.
    private static readonly int[] CodeLengthCodeOrder = { 1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

    /// <summary>Writes the histograms, then the values in order.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="contextMap">The cluster of each context, at most 7, written as a simple context map of 3 bits per entry.</param>
    /// <param name="values">The context and value of each symbol, in the order the decoder reads them.</param>
    /// <param name="ansState">
    /// When set, the values are coded instead with one ANS histogram of the single symbol 0, which never
    /// changes the state, so every value must be 0 and the final-state check passes only for the state
    /// 0x130000.
    /// </param>
    public static void Write(JxlTestBitWriter w, int[] contextMap, List<(int Context, long Value)> values, uint? ansState = null)
    {
        if (ansState is uint state)
        {
            if (values.Any(v => v.Value != 0))
            {
                throw new InvalidOperationException("An ANS dictionary can only hold zeros.");
            }

            w.Bit(false); // no LZ77
            w.Bit(true).Write(2, 0); // simple context map: every context on histogram 0
            w.Bit(false).Write(2, 0); // ANS, log alpha size 5
            w.Write(3, 0); // split exponent 0
            w.Bit(true).Bit(false).Bit(false); // simple histogram of the one symbol 0
            w.Write(32, state);
            return;
        }

        // The tokens of each cluster.
        int numClusters = contextMap.Max() + 1;
        SortedSet<int>[] tokens = new SortedSet<int>[numClusters];
        for (int i = 0; i < numClusters; i++)
        {
            tokens[i] = new SortedSet<int>();
        }

        foreach ((int context, long value) in values)
        {
            tokens[contextMap[context]].Add(Token(value, out _, out _));
        }

        w.Bit(false); // no LZ77
        w.Bit(true).Write(2, 3); // simple context map, 3 bits per entry
        foreach (int cluster in contextMap)
        {
            w.Write(3, (ulong)cluster);
        }

        w.Bit(true); // prefix codes
        for (int i = 0; i < numClusters; i++)
        {
            w.Write(4, 0); // split exponent 0
        }

        int[] codeBits = new int[numClusters];
        for (int i = 0; i < numClusters; i++)
        {
            int alphabetSize;
            if (tokens[i].Count == 0 || (tokens[i].Count == 1 && tokens[i].Min == 0))
            {
                alphabetSize = 1;
            }
            else if (tokens[i].Count == 1)
            {
                alphabetSize = tokens[i].Min + 1;
            }
            else
            {
                codeBits[i] = Math.Max(1, JxlBits.CeilLog2Nonzero((uint)tokens[i].Max + 1));
                alphabetSize = 1 << codeBits[i];
            }

            WriteVarLenUint16(w, alphabetSize - 1);
        }

        for (int i = 0; i < numClusters; i++)
        {
            if (codeBits[i] > 0)
            {
                // A complex code whose only code length code is L, so every symbol has length L.
                w.Write(2, 0);
                foreach (int codeLength in CodeLengthCodeOrder)
                {
                    w.Write(2, codeLength == codeBits[i] ? 1UL : 0UL); // code length code length 4, or 0
                }
            }
            else if (tokens[i].Count == 1 && tokens[i].Min != 0)
            {
                // A simple code of one symbol, which costs no bits.
                int alphabetSize = tokens[i].Min + 1;
                int maxBits = JxlBits.FloorLog2Nonzero((uint)(alphabetSize - 1)) + 1;
                w.Write(2, 1).Write(2, 0).Write(maxBits, (ulong)tokens[i].Min);
            }
        }

        foreach ((int context, long value) in values)
        {
            int cluster = contextMap[context];
            int token = Token(value, out int rawBits, out ulong raw);
            if (codeBits[cluster] > 0)
            {
                w.Write(codeBits[cluster], Reverse((uint)token, codeBits[cluster]));
            }

            w.Write(rawBits, raw);
        }
    }

    private static int Token(long value, out int rawBits, out ulong raw)
    {
        if (value == 0)
        {
            rawBits = 0;
            raw = 0;
            return 0;
        }

        rawBits = JxlBits.FloorLog2Nonzero((ulong)value);
        raw = (ulong)value - (1UL << rawBits);
        return rawBits + 1;
    }

    // A canonical code is read most significant bit first.
    private static ulong Reverse(uint value, int bits)
    {
        ulong result = 0;
        for (int i = 0; i < bits; i++)
        {
            result |= ((value >> i) & 1) << (bits - 1 - i);
        }

        return result;
    }

    private static void WriteVarLenUint16(JxlTestBitWriter w, int value)
    {
        if (value == 0)
        {
            w.Bit(false);
            return;
        }

        int nbits = JxlBits.FloorLog2Nonzero((uint)value);
        w.Bit(true).Write(4, (ulong)nbits);
        w.Write(nbits, (ulong)(value - (1 << nbits)));
    }
}
