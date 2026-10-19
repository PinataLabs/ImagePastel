// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_ans.cc (ReadHistogram, DecodeANSCodes, DecodeUintConfig(s), DecodeHistograms).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;

namespace SixLabors.ImageSharp.Formats.Jxl.Entropy;

/// <summary>Reads the histograms and context map of an entropy-coded stream.</summary>
internal static class JxlEntropyDecoder
{
    // Static Huffman code for the log2 of each count: pairs of (bits, code + 1), indexed by 7 peeked bits.
    private static ReadOnlySpan<byte> LogCountHuff => new byte[]
    {
        3, 10, 7, 12, 3, 7, 4, 3, 3, 6, 3, 8, 3, 9, 4, 5,
        3, 10, 4, 4, 3, 7, 4, 1, 3, 6, 3, 8, 3, 9, 4, 2,
        3, 10, 5, 0, 3, 7, 4, 3, 3, 6, 3, 8, 3, 9, 4, 5,
        3, 10, 4, 4, 3, 7, 4, 1, 3, 6, 3, 8, 3, 9, 4, 2,
        3, 10, 6, 11, 3, 7, 4, 3, 3, 6, 3, 8, 3, 9, 4, 5,
        3, 10, 4, 4, 3, 7, 4, 1, 3, 6, 3, 8, 3, 9, 4, 2,
        3, 10, 5, 0, 3, 7, 4, 3, 3, 6, 3, 8, 3, 9, 4, 5,
        3, 10, 4, 4, 3, 7, 4, 1, 3, 6, 3, 8, 3, 9, 4, 2,
        3, 10, 7, 13, 3, 7, 4, 3, 3, 6, 3, 8, 3, 9, 4, 5,
        3, 10, 4, 4, 3, 7, 4, 1, 3, 6, 3, 8, 3, 9, 4, 2,
        3, 10, 5, 0, 3, 7, 4, 3, 3, 6, 3, 8, 3, 9, 4, 5,
        3, 10, 4, 4, 3, 7, 4, 1, 3, 6, 3, 8, 3, 9, 4, 2,
        3, 10, 6, 11, 3, 7, 4, 3, 3, 6, 3, 8, 3, 9, 4, 5,
        3, 10, 4, 4, 3, 7, 4, 1, 3, 6, 3, 8, 3, 9, 4, 2,
        3, 10, 5, 0, 3, 7, 4, 3, 3, 6, 3, 8, 3, 9, 4, 5,
        3, 10, 4, 4, 3, 7, 4, 1, 3, 6, 3, 8, 3, 9, 4, 2,
    };

    /// <summary>
    /// Reads the LZ77 parameters, the context map and the histograms for <paramref name="numContexts"/>
    /// contexts. Throws <see cref="InvalidImageContentException"/> for invalid data.
    /// </summary>
    /// <param name="br">The reader.</param>
    /// <param name="numContexts">The number of contexts.</param>
    /// <param name="code">The histograms to fill.</param>
    /// <param name="disallowLz77">Whether LZ77 is forbidden in this stream.</param>
    /// <returns>
    /// The context map, which maps each context to a histogram. With LZ77 it has one more entry, the
    /// context of copy distances.
    /// </returns>
    public static byte[] DecodeHistograms(JxlBitReader br, int numContexts, JxlAnsCode code, bool disallowLz77 = false)
    {
        JxlBundle.Read(br, code.Lz77);
        if (code.Lz77.Enabled)
        {
            numContexts++;
            code.Lz77.LengthUintConfig = DecodeUintConfig(8, br);
        }

        if (code.Lz77.Enabled && disallowLz77)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Using LZ77 when explicitly disallowed");
        }

        int numHistograms = 1;
        byte[] contextMap = new byte[numContexts];
        if (numContexts > 1)
        {
            numHistograms = JxlContextMap.DecodeContextMap(contextMap, br);
        }

        code.Lz77.NonserializedDistanceContext = contextMap[^1];
        code.UsePrefixCode = br.ReadBits(1) != 0;
        code.LogAlphaSize = code.UsePrefixCode ? JxlAnsParams.PrefixMaxBits : (int)br.ReadBits(2) + 5;

        code.UintConfig = new JxlHybridUintConfig[numHistograms];
        for (int i = 0; i < numHistograms; i++)
        {
            code.UintConfig[i] = DecodeUintConfig(code.LogAlphaSize, br);
        }

        int maxAlphabetSize = 1 << code.LogAlphaSize;
        DecodeAnsCodes(numHistograms, maxAlphabetSize, br, code);
        return contextMap;
    }

    private static int DecodeVarLenUint8(JxlBitReader input)
    {
        if (input.ReadBits(1) != 0)
        {
            int nbits = (int)input.ReadBits(3);
            if (nbits == 0)
            {
                return 1;
            }

            return (int)input.ReadBits(nbits) + (1 << nbits);
        }

        return 0;
    }

    private static int DecodeVarLenUint16(JxlBitReader input)
    {
        if (input.ReadBits(1) != 0)
        {
            int nbits = (int)input.ReadBits(4);
            if (nbits == 0)
            {
                return 1;
            }

            return (int)input.ReadBits(nbits) + (1 << nbits);
        }

        return 0;
    }

    // Returns false for an invalid histogram.
    private static bool ReadHistogram(int precisionBits, out List<int> counts, JxlBitReader input)
    {
        int range = 1 << precisionBits;
        int simpleCode = (int)input.ReadBits(1);
        if (simpleCode == 1)
        {
            Span<int> symbols = stackalloc int[2];
            symbols.Clear();
            int maxSymbol = 0;
            int numSymbols = (int)input.ReadBits(1) + 1;
            for (int i = 0; i < numSymbols; ++i)
            {
                symbols[i] = DecodeVarLenUint8(input);
                if (symbols[i] > maxSymbol)
                {
                    maxSymbol = symbols[i];
                }
            }

            counts = NewCounts(maxSymbol + 1);
            if (numSymbols == 1)
            {
                counts[symbols[0]] = range;
            }
            else
            {
                if (symbols[0] == symbols[1])
                {
                    // corrupt data
                    return false;
                }

                counts[symbols[0]] = (int)input.ReadBits(precisionBits);
                counts[symbols[1]] = range - counts[symbols[0]];
            }

            return true;
        }

        int isFlat = (int)input.ReadBits(1);
        if (isFlat == 1)
        {
            int alphabetSize = DecodeVarLenUint8(input) + 1;
            if (alphabetSize > range)
            {
                counts = null;
                return false;
            }

            counts = JxlAnsCommon.CreateFlatHistogram(alphabetSize, range);
            return true;
        }

        int shift;
        {
            int upperBoundLog = JxlBits.FloorLog2Nonzero((uint)(JxlAnsParams.AnsLogTabSize + 1));
            int log = 0;
            for (; log < upperBoundLog; log++)
            {
                if (input.ReadBits(1) == 0)
                {
                    break;
                }
            }

            shift = (int)(input.ReadBits(log) | (1u << log)) - 1;
            if (shift > JxlAnsParams.AnsLogTabSize + 1)
            {
                counts = null;
                return false;
            }
        }

        int length = DecodeVarLenUint8(input) + 3;
        counts = NewCounts(length);
        int totalCount = 0;

        int[] logCounts = new int[length];
        int omitLog = -1;
        int omitPos = -1;

        // This array remembers which symbols have an RLE length.
        int[] same = new int[length];
        for (int i = 0; i < length; ++i)
        {
            // for PeekFixedBits + Advance
            input.Refill();
            int idx = (int)input.PeekBits(7);
            input.Consume(LogCountHuff[2 * idx]);
            logCounts[i] = LogCountHuff[(2 * idx) + 1] - 1;

            // The RLE symbol.
            if (logCounts[i] == JxlAnsParams.AnsLogTabSize)
            {
                int rleLength = DecodeVarLenUint8(input);
                same[i] = rleLength + 5;
                i += rleLength + 3;
                continue;
            }

            if (logCounts[i] > omitLog)
            {
                omitLog = logCounts[i];
                omitPos = i;
            }
        }

        // Invalid input, e.g. due to invalid usage of RLE.
        if (omitPos < 0)
        {
            return false;
        }

        if (omitPos + 1 < length && logCounts[omitPos + 1] == JxlAnsParams.AnsLogTabSize)
        {
            return false;
        }

        int prev = 0;
        int numSame = 0;
        for (int i = 0; i < length; ++i)
        {
            if (same[i] != 0)
            {
                // RLE sequence, let this loop output the same count for the next iterations.
                numSame = same[i] - 1;
                prev = i > 0 ? counts[i - 1] : 0;
            }

            if (numSame > 0)
            {
                counts[i] = prev;
                numSame--;
            }
            else
            {
                int code = logCounts[i];

                // omit_pos may not be negative at this point (checked before).
                if (i == omitPos || code < 0)
                {
                    continue;
                }
                else if (shift == 0 || code == 0)
                {
                    // `shift = 0` means `bitcount = 0`
                    counts[i] = 1 << code;
                }
                else
                {
                    int bitCount = JxlAnsCommon.GetPopulationCountPrecision(code, shift);
                    counts[i] = (1 << code) + ((int)input.ReadBits(bitCount) << (code - bitCount));
                }
            }

            totalCount += counts[i];
        }

        counts[omitPos] = range - totalCount;
        if (counts[omitPos] <= 0)
        {
            // The histogram we've read sums to more than total_count (including at least 1 for the
            // omitted value).
            return false;
        }

        return true;
    }

    private static List<int> NewCounts(int length)
    {
        List<int> counts = new(length);
        for (int i = 0; i < length; i++)
        {
            counts.Add(0);
        }

        return counts;
    }

    private static void DecodeAnsCodes(int numHistograms, int maxAlphabetSize, JxlBitReader input, JxlAnsCode result)
    {
        result.DegenerateSymbols = new int[numHistograms];
        Array.Fill(result.DegenerateSymbols, -1);
        if (result.UsePrefixCode)
        {
            if (maxAlphabetSize > 1 << JxlAnsParams.PrefixMaxBits)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Prefix alphabet is too large");
            }

            result.HuffmanData = new JxlHuffmanDecodingData[numHistograms];
            int[] alphabetSizes = new int[numHistograms];
            for (int c = 0; c < numHistograms; c++)
            {
                alphabetSizes[c] = DecodeVarLenUint16(input) + 1;
                if (alphabetSizes[c] > maxAlphabetSize)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException($"Alphabet size is too long: {alphabetSizes[c]}");
                }
            }

            for (int c = 0; c < numHistograms; c++)
            {
                JxlHuffmanDecodingData data = new();
                result.HuffmanData[c] = data;
                if (alphabetSizes[c] > 1)
                {
                    if (!data.ReadFromBitStream(alphabetSizes[c], input))
                    {
                        input.EnsureAllReadsWithinBounds("huffman code");
                        JxlThrowHelper.ThrowInvalidImageContentException($"Invalid huffman tree number {c}, alphabet size {alphabetSizes[c]}");
                    }
                }
                else
                {
                    // 0-bit codes do not require extension tables.
                    data.SetSingleZeroSymbol();
                }
            }

            // libjxl also tracks the largest number of bits a value can take (max_num_bits), which only
            // VarDCT uses to pick 16- or 32-bit coefficients.
        }
        else
        {
            if (maxAlphabetSize > JxlAnsParams.AnsMaxAlphabetSize)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("ANS alphabet is too large");
            }

            JxlAliasEntry[] aliasTables = new JxlAliasEntry[numHistograms * (1 << result.LogAlphaSize)];
            result.AliasTables = aliasTables;
            for (int c = 0; c < numHistograms; ++c)
            {
                if (!ReadHistogram(JxlAnsParams.AnsLogTabSize, out List<int> counts, input))
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid histogram bitstream.");
                }

                if (counts.Count > maxAlphabetSize)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException($"Alphabet size is too long: {counts.Count}");
                }

                while (counts.Count > 0 && counts[^1] == 0)
                {
                    counts.RemoveAt(counts.Count - 1);
                }

                // InitAliasTable "fixes" empty counts to contain degenerate "0" symbol.
                int degenerateSymbol = counts.Count == 0 ? 0 : counts.Count - 1;
                for (int s = 0; s < degenerateSymbol; ++s)
                {
                    if (counts[s] != 0)
                    {
                        degenerateSymbol = -1;
                        break;
                    }
                }

                result.DegenerateSymbols[c] = degenerateSymbol;
                JxlAliasTable.InitAliasTable(counts, JxlAnsParams.AnsLogTabSize, result.LogAlphaSize, aliasTables, c * (1 << result.LogAlphaSize));
            }
        }
    }

    private static JxlHybridUintConfig DecodeUintConfig(int logAlphaSize, JxlBitReader br)
    {
        br.Refill();
        uint splitExponent = (uint)br.ReadBits(JxlBits.CeilLog2Nonzero((uint)(logAlphaSize + 1)));
        uint msbInToken = 0;
        uint lsbInToken = 0;
        if (splitExponent != logAlphaSize)
        {
            // Otherwise, msb/lsb don't matter.
            int nbits = JxlBits.CeilLog2Nonzero(splitExponent + 1);
            msbInToken = (uint)br.ReadBits(nbits);
            if (msbInToken > splitExponent)
            {
                // This could be invalid here already and we need to check this before we use its
                // value to read more bits.
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid HybridUintConfig");
            }

            nbits = JxlBits.CeilLog2Nonzero(splitExponent - msbInToken + 1);
            lsbInToken = (uint)br.ReadBits(nbits);
        }

        if (lsbInToken + msbInToken > splitExponent)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid HybridUintConfig");
        }

        return new JxlHybridUintConfig(splitExponent, msbInToken, lsbInToken);
    }
}
