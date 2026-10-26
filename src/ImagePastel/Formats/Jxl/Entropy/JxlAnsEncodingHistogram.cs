// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_ans.cc (StoreVarLenUint8, StoreVarLenUint16, ANSEncodingHistogram).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;

namespace PinataLabs.ImagePastel.Formats.Jxl.Entropy;

/// <summary>
/// A histogram normalised to the ANS table size, with the way it is stored: the only way to build a
/// valid histogram for ANS encoding. It is the mirror of the decoder's histogram reader.
/// </summary>
internal sealed class JxlAnsEncodingHistogram
{
    private const int MaxNumSymbolsForSmallCode = 2;

    // The bit widths are encoded with a static Huffman code. The last symbol is used as RLE sequence.
    private static readonly byte[] BitWidthLengths = { 5, 4, 4, 4, 4, 4, 3, 3, 3, 3, 3, 6, 7, 7 };
    private static readonly byte[] BitWidthSymbols = { 17, 11, 15, 3, 9, 7, 4, 2, 5, 6, 0, 33, 1, 65 };

    // Fixed-point log2 LUT for values of [0,4096].
    private static readonly uint[] Lg2 = CreateLg2();

    private static readonly Lazy<AllowedCounts> AllowedCountsTable = new(CreateAllowedCounts);

    private readonly int[] symbols = new int[MaxNumSymbolsForSmallCode];
    private float cost;
    private int method;
    private int omitPos;
    private int alphabetSize;
    private int numSymbols;
    private int[] counts = Array.Empty<int>();

    private JxlAnsEncodingHistogram()
    {
    }

    /// <summary>Gets the normalised counts, which add up to the ANS table size.</summary>
    public int[] Counts => this.counts;

    /// <summary>Gets the estimated cost, in bits, of the histogram and the data it codes.</summary>
    public float Cost => this.cost;

    /// <summary>Writes <paramref name="n"/>, at most 255, as libjxl's <c>StoreVarLenUint8</c>.</summary>
    /// <typeparam name="TWriter">The writer type.</typeparam>
    /// <param name="n">The value.</param>
    /// <param name="writer">The writer.</param>
    public static void StoreVarLenUint8<TWriter>(int n, TWriter writer)
        where TWriter : IJxlBitSink
    {
        DebugGuard.IsTrue(n is >= 0 and <= 255, "n must be 0 to 255");
        if (n == 0)
        {
            writer.Write(1, 0);
        }
        else
        {
            writer.Write(1, 1);
            int nbits = JxlBits.FloorLog2Nonzero((uint)n);
            writer.Write(3, (ulong)nbits);
            writer.Write(nbits, (ulong)(n - (1L << nbits)));
        }
    }

    /// <summary>Writes <paramref name="n"/>, at most 65535, as libjxl's <c>StoreVarLenUint16</c>.</summary>
    /// <typeparam name="TWriter">The writer type.</typeparam>
    /// <param name="n">The value.</param>
    /// <param name="writer">The writer.</param>
    public static void StoreVarLenUint16<TWriter>(int n, TWriter writer)
        where TWriter : IJxlBitSink
    {
        DebugGuard.IsTrue(n is >= 0 and <= 65535, "n must be 0 to 65535");
        if (n == 0)
        {
            writer.Write(1, 0);
        }
        else
        {
            writer.Write(1, 1);
            int nbits = JxlBits.FloorLog2Nonzero((uint)n);
            writer.Write(4, (ulong)nbits);
            writer.Write(nbits, (ulong)(n - (1L << nbits)));
        }
    }

    /// <summary>
    /// Normalises <paramref name="histo"/> and chooses how to store it: flat, one or two symbols, or a
    /// general histogram at the cheapest precision <paramref name="strategy"/> tries.
    /// </summary>
    /// <param name="histo">The histogram.</param>
    /// <param name="strategy">Which precisions to try.</param>
    /// <returns>The normalised histogram.</returns>
    public static JxlAnsEncodingHistogram ComputeBest(JxlHistogram histo, JxlHistogramParams.AnsHistogramStrategy strategy)
    {
        JxlAnsEncodingHistogram result = new()
        {
            alphabetSize = histo.AlphabetSize(),
        };

        if (result.alphabetSize > JxlAnsParams.AnsMaxAlphabetSize)
        {
            JxlThrowHelper.ThrowImageFormatException("Too many entries in an ANS histogram");
        }

        if (result.alphabetSize > 0)
        {
            // Flat code
            result.method = 0;
            result.numSymbols = result.alphabetSize;
            result.counts = new int[histo.Counts.Length];
            JxlAnsCommon.CreateFlatHistogram(result.alphabetSize, JxlAnsParams.AnsTabSize).CopyTo(result.counts);
            JxlSizeWriter writer = new();
            result.Encode(writer);
            result.cost = writer.Size + EstimateDataBitsFlat(histo);
        }
        else
        {
            // Empty histogram
            result.method = 1;
            result.numSymbols = 0;
            result.cost = 3;
            return result;
        }

        int symbolCount = 0;
        for (int n = 0; n < result.alphabetSize; ++n)
        {
            if (histo.Counts[n] > 0)
            {
                if (symbolCount < MaxNumSymbolsForSmallCode)
                {
                    result.symbols[symbolCount] = n;
                }

                ++symbolCount;
            }
        }

        result.numSymbols = symbolCount;
        if (symbolCount == 1)
        {
            // Single-bin histogram
            result.method = 1;
            result.counts = (int[])histo.Counts.Clone();
            result.counts[result.symbols[0]] = JxlAnsParams.AnsTabSize;
            JxlSizeWriter writer = new();
            result.Encode(writer);
            result.cost = writer.Size;
            return result;
        }

        // Here min 2 symbols
        JxlAnsEncodingHistogram normalized = result.Clone();
        void TryShift(int shift)
        {
            // `shift = 12` and `shift = 11` are the same
            normalized.method = Math.Min(shift, JxlAnsParams.AnsLogTabSize - 1) + 1;

            if (!normalized.RebalanceHistogram(histo))
            {
                JxlThrowHelper.ThrowImageFormatException("Logic error: couldn't rebalance a histogram");
            }

            JxlSizeWriter writer = new();
            normalized.Encode(writer);
            normalized.cost = writer.Size + normalized.EstimateDataBits(histo);
            if (normalized.cost < result.cost)
            {
                result = normalized.Clone();
            }
        }

        switch (strategy)
        {
            case JxlHistogramParams.AnsHistogramStrategy.Precise:
                for (int shift = 0; shift < JxlAnsParams.AnsLogTabSize; shift++)
                {
                    TryShift(shift);
                }

                break;
            case JxlHistogramParams.AnsHistogramStrategy.Approximate:
                for (int shift = 0; shift <= JxlAnsParams.AnsLogTabSize; shift += 2)
                {
                    TryShift(shift);
                }

                break;
            case JxlHistogramParams.AnsHistogramStrategy.Fast:
                TryShift(0);
                TryShift(JxlAnsParams.AnsLogTabSize / 2);
                TryShift(JxlAnsParams.AnsLogTabSize);
                break;
        }

        // libjxl checks the result here in debug builds only.
        DebugGuard.IsTrue(histo.Counts.Length == result.counts.Length, "The normalised histogram has the input's length");
        return result;
    }

    /// <summary>Writes the histogram.</summary>
    /// <typeparam name="TWriter">The writer type.</typeparam>
    /// <param name="writer">The writer.</param>
    public void Encode<TWriter>(TWriter writer)
        where TWriter : IJxlBitSink
    {
        // The check ensures also that all RLE sequences can be encoded by `StoreVarLenUint8`
        DebugGuard.IsTrue(this.alphabetSize <= JxlAnsParams.AnsMaxAlphabetSize, "Alphabet too large");

        // Flat histogram.
        if (this.method == 0)
        {
            // Mark non-small tree.
            writer.Write(1, 0);

            // Mark uniform histogram.
            writer.Write(1, 1);

            // Encode alphabet size.
            StoreVarLenUint8(this.alphabetSize - 1, writer);
            return;
        }

        // Small tree.
        if (this.numSymbols <= MaxNumSymbolsForSmallCode)
        {
            // Small tree marker to encode 1-2 symbols.
            writer.Write(1, 1);
            if (this.numSymbols == 0)
            {
                writer.Write(1, 0);
                StoreVarLenUint8(0, writer);
            }
            else
            {
                writer.Write(1, (ulong)(this.numSymbols - 1));
                for (int i = 0; i < this.numSymbols; ++i)
                {
                    StoreVarLenUint8(this.symbols[i], writer);
                }
            }

            if (this.numSymbols == 2)
            {
                writer.Write(JxlAnsParams.AnsLogTabSize, (ulong)this.counts[this.symbols[0]]);
            }

            return;
        }

        // General tree.
        // Mark non-small tree.
        writer.Write(1, 0);

        // Mark non-flat histogram.
        writer.Write(1, 0);

        // Elias gamma-like code for `shift = method - 1`. Only difference is that if the number of
        // bits to be encoded is equal to `upper_bound_log`, we skip the terminating 0 in unary coding.
        int upperBoundLog = JxlBits.FloorLog2Nonzero((uint)(JxlAnsParams.AnsLogTabSize + 1));
        int log = JxlBits.FloorLog2Nonzero((uint)this.method);
        writer.Write(log, (1UL << log) - 1);
        if (log != upperBoundLog)
        {
            writer.Write(1, 0);
        }

        writer.Write(log, (ulong)(((1 << log) - 1) & this.method));

        // Since `num_symbols_ >= 3`, we know that `alphabet_size_ >= 3`, therefore we encode
        // `alphabet_size_ - 3`.
        StoreVarLenUint8(this.alphabetSize - 3, writer);

        // Precompute sequences for RLE encoding. Contains the number of identical values starting at a
        // given index. Only contains that value at the first element of the series.
        Span<int> same = stackalloc int[JxlAnsParams.AnsMaxAlphabetSize];
        same.Clear();
        int last = 0;
        for (int i = 1; i <= this.alphabetSize; i++)
        {
            // Store the sequence length once different symbol reached, or we are near the omit_pos_,
            // or we're at the end. We don't support including the omit_pos_ in an RLE sequence because
            // this value may use a different amount of log2 bits than standard, it is too complex to
            // handle in the decoder.
            if (i == this.alphabetSize || i == this.omitPos || i == this.omitPos + 1 || this.counts[i] != this.counts[last])
            {
                same[last] = i - last;
                last = i;
            }
        }

        // Use shortest possible Huffman code to encode `omit_pos` (see `kBitWidthLengths`).
        // `bit_width` value at `omit_pos` should be the first of maximal values in the whole
        // `bit_width` array, so it can be increased without changing that property
        Span<int> bitWidth = stackalloc int[JxlAnsParams.AnsMaxAlphabetSize];
        bitWidth.Clear();
        int omitWidth = 10;
        for (int i = 0; i < this.alphabetSize; ++i)
        {
            if (i != this.omitPos && this.counts[i] > 0)
            {
                bitWidth[i] = JxlBits.FloorLog2Nonzero((uint)this.counts[i]) + 1;
                omitWidth = Math.Max(omitWidth, bitWidth[i] + (i < this.omitPos ? 1 : 0));
            }
        }

        bitWidth[this.omitPos] = omitWidth;

        const int minReps = 5;
        const int rep = JxlAnsParams.AnsLogTabSize + 1;

        // Encode count bit widths
        for (int i = 0; i < this.alphabetSize; ++i)
        {
            writer.Write(BitWidthLengths[bitWidth[i]], BitWidthSymbols[bitWidth[i]]);
            if (same[i] >= minReps)
            {
                // Encode the RLE symbol and skip the repeated ones.
                writer.Write(BitWidthLengths[rep], BitWidthSymbols[rep]);
                StoreVarLenUint8(same[i] - minReps, writer);
                i += same[i] - 1;
            }
        }

        // Encode additional bits of accuracy
        int shift = this.method - 1;
        if (shift != 0)
        {
            // otherwise `bitcount = 0`
            for (int i = 0; i < this.alphabetSize; ++i)
            {
                if (bitWidth[i] > 1 && i != this.omitPos)
                {
                    int bitcount = JxlAnsCommon.GetPopulationCountPrecision(bitWidth[i] - 1, shift);
                    int dropBits = bitWidth[i] - 1 - bitcount;
                    DebugGuard.IsTrue((this.counts[i] & ((1 << dropBits) - 1)) == 0, "Dropped bits must be zero");
                    writer.Write(bitcount, (ulong)((this.counts[i] >> dropBits) - (1 << bitcount)));
                }

                if (same[i] >= minReps)
                {
                    // Skip symbols encoded by RLE.
                    i += same[i] - 1;
                }
            }
        }
    }

    /// <summary>
    /// Fills <paramref name="info"/> with each symbol's frequency and the map from its slots in the
    /// ANS state back to positions in the alias table.
    /// </summary>
    /// <param name="table">The alias table built from <see cref="Counts"/>.</param>
    /// <param name="logAlphaSize">Log2 of the alias table size.</param>
    /// <param name="info">The symbol information to fill, one entry per symbol and at least one.</param>
    public void AnsBuildInfoTable(JxlAliasEntry[] table, int logAlphaSize, JxlAnsEncSymbolInfo[] info)
    {
        // Create valid alias table for empty streams
        for (int s = 0; s < Math.Max(1, this.alphabetSize); ++s)
        {
            int freq = s == this.alphabetSize ? JxlAnsParams.AnsTabSize : this.counts[s];
            info[s].Freq = (ushort)freq;
            info[s].ReverseMap = new ushort[freq];
        }

        int logEntrySize = JxlAnsParams.AnsLogTabSize - logAlphaSize;
        uint entrySizeMinus1 = (1u << logEntrySize) - 1;
        for (int i = 0; i < JxlAnsParams.AnsTabSize; i++)
        {
            JxlAliasSymbol s = JxlAliasTable.Lookup(table, 0, (uint)i, logEntrySize, entrySizeMinus1);
            info[s.Value].ReverseMap[s.Offset] = (ushort)i;
        }
    }

    private static uint[] CreateLg2()
    {
        uint[] lg2 = new uint[JxlAnsParams.AnsTabSize + 1];

        // for entropy calculations it is OK
        lg2[0] = 0;
        for (int i = 1; i < lg2.Length; ++i)
        {
            lg2[i] = (uint)RoundAwayFromZero(Log2(i) / JxlAnsParams.AnsLogTabSize * (1L << 31));
        }

        return lg2;
    }

    private static AllowedCounts CreateAllowedCounts()
    {
        AllowedCounts result = new();
        for (int shift = 0; shift < JxlAnsParams.AnsLogTabSize; ++shift)
        {
            CountsEntropy[] ac = result.Array[shift];
            ushort[] ai = result.Index[shift];
            int last = ~0;
            int slot = 0;

            // TODO(eustas): are those "default" values relevant?
            ac[0].DeltaLg2 = 0;
            ac[0].StepLog = 0;
            for (int i = ac.Length - 1; i >= 0; --i)
            {
                int curr = i & ~((1 << SmallestIncrementLog((uint)i, shift)) - 1);
                if (curr == last)
                {
                    continue;
                }

                last = curr;
                ac[slot].Count = curr;
                ai[curr] = (ushort)slot;
                if (curr == 0)
                {
                    // Guards against non-possible steps: at max value [0] - 0 (by init), at min value - max
                    ac[slot].DeltaLg2 = int.MaxValue;
                    ac[slot].StepLog = 0;
                }
                else if (slot > 0)
                {
                    int prev = ac[slot - 1].Count;
                    ac[slot].DeltaLg2 = (int)RoundAwayFromZero(Log2((double)prev / curr) / JxlAnsParams.AnsLogTabSize * (1L << 31));
                    ac[slot].StepLog = JxlBits.FloorLog2Nonzero((uint)(prev - curr));
                }

                slot++;
            }
        }

        return result;
    }

    private static double Log2(double x)
#if NETCOREAPP3_0_OR_GREATER
        => Math.Log2(x);
#else
        => Math.Log(x) / Math.Log(2);
#endif

    // C's round(): halves go away from zero.
    private static double RoundAwayFromZero(double x) => Math.Round(x, MidpointRounding.AwayFromZero);

    private static float EstimateDataBitsFlat(JxlHistogram histo)
    {
        int len = histo.AlphabetSize();
        long flatBits = (long)Lg2[len] * JxlAnsParams.AnsLogTabSize;
        return (float)(ulong)(histo.TotalCount * flatBits) * (1.0f / (1L << 31));
    }

    // Returns the difference between largest count that can be represented and is smaller than "count"
    // and smallest representable count larger than "count".
    private static int SmallestIncrementLog(uint count, int shift)
    {
        if (count == 0)
        {
            return 0;
        }

        int bits = JxlBits.FloorLog2Nonzero(count);
        return bits - JxlAnsCommon.GetPopulationCountPrecision(bits, shift);
    }

    private JxlAnsEncodingHistogram Clone()
    {
        JxlAnsEncodingHistogram copy = new()
        {
            cost = this.cost,
            method = this.method,
            omitPos = this.omitPos,
            alphabetSize = this.alphabetSize,
            numSymbols = this.numSymbols,
            counts = (int[])this.counts.Clone(),
        };
        this.symbols.CopyTo(copy.symbols, 0);
        return copy;
    }

    private float EstimateDataBits(JxlHistogram histo)
    {
        long sum = 0;
        for (int i = 0; i < this.alphabetSize; ++i)
        {
            // += histogram[i] * -log(counts[i]/total_counts)
            sum += histo.Counts[i] * (long)Lg2[this.counts[i]];
        }

        return (histo.TotalCount - (sum * (1.0f / (1L << 31)))) * JxlAnsParams.AnsLogTabSize;
    }

    // We are growing/reducing histogram step by step trying to maximize total entropy i.e. sum of
    // `freq[n] * log[counts[n]]` with a given sum of `counts[n]` chosen from `allowed_counts[shift]`.
    // This sum is balanced by the `counts[omit_pos_]` in the highest bin of histogram. We start from
    // close to correct solution and each time a step with maximum entropy increase per unit of bin
    // change is chosen. This greedy scheme is not guaranteed to achieve the global maximum, but cannot
    // produce invalid histogram. We use a fixed-point approximation for logarithms and all arithmetic is
    // integer besides initial approximation. Sum of `freq` and each of `lg2[counts]` are supposed to be
    // limited to `int32_t` range, so that the sum of their products should not exceed `int64_t`.
    private bool RebalanceHistogram(JxlHistogram histo)
    {
        const int tableSize = JxlAnsParams.AnsTabSize;
        int shift = this.method - 1;

        // Penalties corresponding to different step sizes - entropy decrease in balancing bin, step of
        // size (1 << ANS_LOG_TAB_SIZE - 1) is not possible
        long[] balanceInc = new long[JxlAnsParams.AnsLogTabSize - 1];
        long[] balanceDec = new long[JxlAnsParams.AnsLogTabSize - 1];
        AllowedCounts allowed = AllowedCountsTable.Value;
        CountsEntropy[] ac = allowed.Array[shift];
        ushort[] ai = allowed.Index[shift];

        // Total entropy change by a step: increase/decrease in current bin together with corresponding
        // decrease/increase in the balancing bin. Inc steps increase current bin, dec steps decrease
        long DeltaEntropyInc(in EntropyDelta a)
            => (a.Freq * (long)ac[a.CountInd].DeltaLg2) - balanceInc[ac[a.CountInd].StepLog];
        long DeltaEntropyDec(in EntropyDelta a)
            => (a.Freq * (long)ac[a.CountInd + 1].DeltaLg2) - balanceDec[ac[a.CountInd + 1].StepLog];

        // Compare steps by entropy increase per unit of histogram bin change. Truncation is OK here,
        // accuracy is anyway better than float
        bool IncLess(in EntropyDelta a, in EntropyDelta b)
            => DeltaEntropyInc(a) >> ac[a.CountInd].StepLog < DeltaEntropyInc(b) >> ac[b.CountInd].StepLog;
        bool DecLess(in EntropyDelta a, in EntropyDelta b)
            => DeltaEntropyDec(a) >> ac[a.CountInd + 1].StepLog < DeltaEntropyDec(b) >> ac[b.CountInd + 1].StepLog;

        // Vector of adjustable bins from `allowed_counts`
        List<EntropyDelta> bins = new(256);

        double norm = (double)tableSize / histo.TotalCount;

        // highest balancing bin in the histogram
        int remainderPos = 0;
        long maxFreq = 0;

        // reserve of histogram counts to distribute
        int rest = tableSize;
        for (int n = 0; n < this.alphabetSize; ++n)
        {
            int freq = histo.Counts[n];
            if (freq > maxFreq)
            {
                remainderPos = n;
                maxFreq = freq;
            }

            // rounding
            double target = freq * norm;

            // Keep zeros and clamp nonzero freq counts to [1, table_size)
            int count = Math.Max((int)RoundAwayFromZero(target), freq > 0 ? 1 : 0);
            count = Math.Min(count, tableSize - 1);
            int stepLog = SmallestIncrementLog((uint)count, shift);
            int inc = 1 << stepLog;
            count &= ~(inc - 1);

            this.counts[n] = count;
            rest -= count;
            if (target > 1.0)
            {
                bins.Add(new EntropyDelta(freq, ai[count], n));
            }
        }

        // Delete the highest balancing bin from adjustable by `allowed_counts`
        bins.RemoveAt(bins.FindIndex(a => a.BinInd == remainderPos));

        // From now on `rest` is the height of balancing bin, here it can be negative, but will be
        // tracted into positive domain later
        rest += this.counts[remainderPos];

        if (bins.Count > 0)
        {
            EntropyDelta[] binArray = bins.ToArray();
            int maxLog = ac[1].StepLog;
            while (true)
            {
                // Update balancing bin penalties setting guards and tractors
                for (int log = 0; log <= maxLog; ++log)
                {
                    int delta = 1 << log;
                    if (rest >= tableSize)
                    {
                        // Tract large `rest` into allowed domain:
                        // permit all inc steps
                        balanceInc[log] = 0;

                        // forbid all dec steps
                        balanceDec[log] = 0;
                    }
                    else if (rest > 1)
                    {
                        // `rest` is OK, put guards against non-possible steps
                        balanceInc[log] = rest > delta
                            ? maxFreq * (long)(Lg2[rest] - Lg2[rest - delta])
                            : long.MaxValue; // forbidden
                        balanceDec[log] = rest + delta < tableSize
                            ? maxFreq * (long)(Lg2[rest + delta] - Lg2[rest])
                            : 0; // forbidden
                    }
                    else
                    {
                        // Tract negative or zero `rest` into positive:
                        // forbid all inc steps
                        balanceInc[log] = long.MaxValue;

                        // permit all dec steps
                        balanceDec[log] = long.MaxValue;
                    }
                }

                // Try to increase entropy: std::max_element keeps the first of equal elements.
                int bestBinInc = 0;
                for (int i = 1; i < binArray.Length; i++)
                {
                    if (IncLess(binArray[bestBinInc], binArray[i]))
                    {
                        bestBinInc = i;
                    }
                }

                if (DeltaEntropyInc(binArray[bestBinInc]) > 0)
                {
                    // Grow the bin with the best histogram entropy increase
                    rest -= 1 << ac[binArray[bestBinInc].CountInd--].StepLog;
                }
                else
                {
                    // This still implies that entropy is strictly increasing each step (or `rest` is
                    // tracted into positive domain), so we cannot loop infinitely. std::min_element
                    // keeps the first of equal elements.
                    int bestBinDec = 0;
                    for (int i = 1; i < binArray.Length; i++)
                    {
                        if (DecLess(binArray[i], binArray[bestBinDec]))
                        {
                            bestBinDec = i;
                        }
                    }

                    // Break if no reverse steps can grow entropy (or valid)
                    if (DeltaEntropyDec(binArray[bestBinDec]) >= 0)
                    {
                        break;
                    }

                    // Decrease the bin with the best histogram entropy increase
                    rest += 1 << ac[++binArray[bestBinDec].CountInd].StepLog;
                }
            }

            // Set counts besides the balancing bin
            foreach (EntropyDelta a in binArray)
            {
                this.counts[a.BinInd] = ac[a.CountInd].Count;
            }

            // The scheme works fine if we have room to grow `bit_width` of balancing bin, otherwise we
            // need to put balancing bin to the first bin of 12 bit width. In this case both that bin
            // and balancing one should be close to 2048 in targets, so exchange of them will not
            // produce much worse histogram
            for (int n = 0; n < remainderPos; ++n)
            {
                if (this.counts[n] >= 2048)
                {
                    this.counts[remainderPos] = this.counts[n];
                    remainderPos = n;
                    break;
                }
            }
        }

        // Set balancing bin
        this.counts[remainderPos] = rest;
        this.omitPos = remainderPos;

        return this.counts[remainderPos] > 0;
    }

    private struct CountsEntropy
    {
        // allowed value of counts in a histogram bin
        public int Count;

        // log2 of increase step size (can use 5 bits)
        public int StepLog;

        // change of log between that value and the next allowed
        public int DeltaLg2;
    }

    private struct EntropyDelta
    {
        // initial count
        public int Freq;

        // index of current bin value in `allowed_counts`
        public int CountInd;

        // index of current bin in `counts`
        public int BinInd;

        public EntropyDelta(int freq, int countInd, int binInd)
        {
            this.Freq = freq;
            this.CountInd = countInd;
            this.BinInd = binInd;
        }
    }

    // Array is sorted by decreasing allowed counts for each possible shift. Exclusion of single-bin
    // histograms before `RebalanceHistogram` allows to put count upper limit of 4095, and shifts of 11
    // and 12 produce the same table
    private sealed class AllowedCounts
    {
        public AllowedCounts()
        {
            for (int shift = 0; shift < JxlAnsParams.AnsLogTabSize; shift++)
            {
                this.Array[shift] = new CountsEntropy[JxlAnsParams.AnsTabSize];
                this.Index[shift] = new ushort[JxlAnsParams.AnsTabSize];
            }
        }

        public CountsEntropy[][] Array { get; } = new CountsEntropy[JxlAnsParams.AnsLogTabSize][];

        public ushort[][] Index { get; } = new ushort[JxlAnsParams.AnsLogTabSize][];
    }
}
