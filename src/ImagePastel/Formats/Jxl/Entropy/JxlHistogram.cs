// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_ans_params.h (Histogram) and lib/jxl/enc_cluster.cc (HistogramEntropy, Histogram::ShannonEntropy).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;

namespace PinataLabs.ImagePastel.Formats.Jxl.Entropy;

/// <summary>The count of each symbol in one context, for building an entropy code.</summary>
internal sealed class JxlHistogram
{
    /// <summary>The counts are padded to a multiple of this, for libjxl's SIMD loops.</summary>
    public const int Rounding = 8;

    private int[] counts = Array.Empty<int>();

    /// <summary>Gets the counts, padded with zeros to a multiple of <see cref="Rounding"/>.</summary>
    public int[] Counts => this.counts;

    /// <summary>Gets the sum of the counts.</summary>
    public long TotalCount { get; private set; }

    /// <summary>
    /// Gets or sets the entropy, as the last <see cref="ComputeEntropy"/> left it. As in libjxl, it is
    /// not kept up to date.
    /// </summary>
    public float Entropy { get; set; }

    /// <summary>
    /// libjxl's per-lane <c>Entropy</c>: the cost in bits of <paramref name="count"/> symbols that
    /// make up <c>count * invTotal</c> of the data. libjxl adds the lanes of its SIMD vectors in a
    /// different order than this loop, so sums can differ from libjxl's in the last bits. A zero count
    /// costs nothing; libjxl multiplies its (finite) log by zero instead. A count lies in [0, total], so
    /// the range tests below are libjxl's exact tests for the whole numbers it holds.
    /// </summary>
    /// <param name="count">The count.</param>
    /// <param name="invTotal">One over the total.</param>
    /// <param name="total">The total.</param>
    /// <returns>The cost.</returns>
    public static float EntropyOf(float count, float invTotal, float total)
        => count >= total || count <= 0 ? 0 : 0 - (count * JxlFastMath.FastLog2f(invTotal * count));

    /// <summary>Counts one <paramref name="symbol"/>.</summary>
    /// <param name="symbol">The symbol.</param>
    public void Add(int symbol)
    {
        if (this.counts.Length <= symbol)
        {
            Array.Resize(ref this.counts, (int)JxlBits.DivCeil(symbol + 1, Rounding) * Rounding);
        }

        ++this.counts[symbol];
        ++this.TotalCount;
    }

    /// <summary>Adds the counts of <paramref name="other"/>.</summary>
    /// <param name="other">The other histogram.</param>
    public void AddHistogram(JxlHistogram other)
    {
        if (other.counts.Length > this.counts.Length)
        {
            Array.Resize(ref this.counts, other.counts.Length);
        }

        for (int i = 0; i < other.counts.Length; ++i)
        {
            this.counts[i] += other.counts[i];
        }

        this.TotalCount += other.TotalCount;
    }

    /// <summary>Returns one more than the largest symbol with a nonzero count, or 0 if there is none.</summary>
    /// <returns>The alphabet size.</returns>
    public int AlphabetSize()
    {
        for (int i = this.counts.Length - 1; i >= 0; --i)
        {
            if (this.counts[i] > 0)
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>Returns a copy, as libjxl copies a histogram by value.</summary>
    /// <returns>The copy.</returns>
    public JxlHistogram Clone() => new()
    {
        counts = (int[])this.counts.Clone(),
        TotalCount = this.TotalCount,
        Entropy = this.Entropy,
    };

    /// <summary>Computes and stores <see cref="Entropy"/>, the Shannon entropy of the counts in bits.</summary>
    /// <returns>The entropy.</returns>
    public float ShannonEntropy()
    {
        this.ComputeEntropy();
        return this.Entropy;
    }

    /// <summary>
    /// libjxl's <c>HistogramEntropy</c>: sets <see cref="Entropy"/> to the sum over the symbols of
    /// <c>-count * log2(count / total)</c>, with a symbol that has every count costing nothing.
    /// </summary>
    public void ComputeEntropy()
    {
        this.Entropy = 0.0f;
        if (this.TotalCount == 0)
        {
            return;
        }

        float invTotal = 1.0f / this.TotalCount;
        float total = this.TotalCount;
        float entropy = 0;
        foreach (int count in this.counts)
        {
            entropy += EntropyOf(count, invTotal, total);
        }

        this.Entropy = entropy;
    }
}
