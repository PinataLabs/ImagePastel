// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_cluster.cc (HistogramDistance, NumSymbols, ClusterDistance, FastClusterHistograms,
// HistogramReindex, ClusterHistograms).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;

namespace SixLabors.ImageSharp.Formats.Jxl.Entropy;

/// <summary>Clusters similar histograms so that their contexts can share one entropy code.</summary>
internal static class JxlHistogramClustering
{
    /// <summary>The largest number of clusters, libjxl's <c>kClustersLimit</c>.</summary>
    public const int ClustersLimit = 128;

    private const float MinDistanceForDistinct = 48.0f;

    /// <summary>
    /// Clusters similar histograms in <paramref name="input"/> together. The selected histograms are
    /// placed in <paramref name="output"/>, and for each index in <paramref name="input"/>,
    /// <paramref name="histogramSymbols"/> gives which of the <paramref name="output"/> histograms is
    /// the best approximation.
    /// </summary>
    /// <param name="parameters">The histogram parameters.</param>
    /// <param name="input">The histograms of the contexts.</param>
    /// <param name="maxHistograms">The largest number of clusters.</param>
    /// <param name="output">The clustered histograms; empty on entry.</param>
    /// <param name="histogramSymbols">The cluster of each context.</param>
    public static void ClusterHistograms(JxlHistogramParams parameters, IReadOnlyList<JxlHistogram> input, int maxHistograms, List<JxlHistogram> output, out int[] histogramSymbols)
    {
        // libjxl can also extend histograms from an earlier stream (prev_histograms), which only its
        // streaming mode does; the port always starts from none.
        DebugGuard.IsTrue(output.Count == 0, "Clustering starts with no histograms");
        maxHistograms = Math.Min(maxHistograms, parameters.MaxHistograms);
        maxHistograms = Math.Min(maxHistograms, input.Count);
        if (parameters.Clustering == JxlHistogramParams.ClusteringType.Fastest)
        {
            maxHistograms = Math.Min(maxHistograms, 4);
        }

        FastClusterHistograms(input, maxHistograms, parameters.ForceHuffman, output, out histogramSymbols);

        // libjxl's kBest clustering then merges pairs of clusters while that lowers their ANS cost. Only
        // the higher efforts use it (E2), so until then kBest clusters as kFast does.

        // Convert the context map to a canonical form.
        HistogramReindex(output, histogramSymbols);
    }

    /// <summary>
    /// Returns how many bits merging <paramref name="a"/> and <paramref name="b"/> would cost: the
    /// entropy of the sum minus the entropies of both, which must be up to date.
    /// </summary>
    /// <param name="a">The first histogram.</param>
    /// <param name="b">The second histogram.</param>
    /// <returns>The distance.</returns>
    public static float HistogramDistance(JxlHistogram a, JxlHistogram b)
    {
        if (a.TotalCount == 0 || b.TotalCount == 0)
        {
            return 0;
        }

        float invTotal = 1.0f / (a.TotalCount + b.TotalCount);
        float total = a.TotalCount + b.TotalCount;
        float distance = 0;
        int size = Math.Max(a.Counts.Length, b.Counts.Length);
        for (int i = 0; i < size; i++)
        {
            int aCount = i < a.Counts.Length ? a.Counts[i] : 0;
            int bCount = i < b.Counts.Length ? b.Counts[i] : 0;
            distance += JxlHistogram.EntropyOf(aCount + bCount, invTotal, total);
        }

        return distance - a.Entropy - b.Entropy;
    }

    // With prefix codes, a histogram with a single symbol costs nothing per symbol, and one with more
    // costs at least one bit per symbol. The Shannon entropy misses that: a mostly-zero 2-symbol
    // histogram looks nearly free to merge into large all-zero ones, which then cost a bit per zero.
    // So such a merge costs at least a bit per symbol of the single-symbol side(s).
    private static int NumSymbols(JxlHistogram a, JxlHistogram b)
    {
        int n = 0;
        int size = Math.Max(a.Counts.Length, b?.Counts.Length ?? 0);
        for (int i = 0; i < size; i++)
        {
            int count = (i < a.Counts.Length ? a.Counts[i] : 0) + (b != null && i < b.Counts.Length ? b.Counts[i] : 0);
            n += count > 0 ? 1 : 0;
        }

        return n;
    }

    private static float ClusterDistance(JxlHistogram a, JxlHistogram b, bool prefixCodes)
    {
        float distance = HistogramDistance(a, b);
        if (!prefixCodes || a.TotalCount == 0 || b.TotalCount == 0 || NumSymbols(a, b) <= 1)
        {
            return distance;
        }

        float single = 0;
        if (NumSymbols(a, null) <= 1)
        {
            single += a.TotalCount;
        }

        if (NumSymbols(b, null) <= 1)
        {
            single += b.TotalCount;
        }

        return Math.Max(distance, single);
    }

    // First step of a k-means clustering with a fancy distance metric.
    private static void FastClusterHistograms(IReadOnlyList<JxlHistogram> input, int maxHistograms, bool prefixCodes, List<JxlHistogram> output, out int[] histogramSymbols)
    {
        histogramSymbols = new int[input.Count];
        Array.Fill(histogramSymbols, maxHistograms);

        float[] dists = new float[input.Count];
        Array.Fill(dists, float.MaxValue);
        int largestIdx = 0;
        for (int i = 0; i < input.Count; i++)
        {
            if (input[i].TotalCount == 0)
            {
                histogramSymbols[i] = 0;
                dists[i] = 0.0f;
                continue;
            }

            input[i].ComputeEntropy();
            if (input[i].TotalCount > input[largestIdx].TotalCount)
            {
                largestIdx = i;
            }
        }

        while (output.Count < maxHistograms)
        {
            histogramSymbols[largestIdx] = output.Count;
            output.Add(input[largestIdx].Clone());
            dists[largestIdx] = 0.0f;
            largestIdx = 0;
            for (int i = 0; i < input.Count; i++)
            {
                if (dists[i] == 0.0f)
                {
                    continue;
                }

                dists[i] = Math.Min(ClusterDistance(input[i], output[^1], prefixCodes), dists[i]);
                if (dists[i] > dists[largestIdx])
                {
                    largestIdx = i;
                }
            }

            if (dists[largestIdx] < MinDistanceForDistinct)
            {
                break;
            }
        }

        for (int i = 0; i < input.Count; i++)
        {
            if (histogramSymbols[i] != maxHistograms)
            {
                continue;
            }

            int best = 0;
            float bestDist = float.MaxValue;
            for (int j = 0; j < output.Count; j++)
            {
                float dist = ClusterDistance(input[i], output[j], prefixCodes);
                if (dist < bestDist)
                {
                    best = j;
                    bestDist = dist;
                }
            }

            if (bestDist >= float.MaxValue)
            {
                JxlThrowHelper.ThrowImageFormatException("Histogram clustering found no cluster");
            }

            output[best].AddHistogram(input[i]);
            output[best].ComputeEntropy();
            histogramSymbols[i] = best;
        }
    }

    // Reorder histograms in output so that the new symbols in symbols come in increasing order.
    private static void HistogramReindex(List<JxlHistogram> output, int[] symbols)
    {
        JxlHistogram[] tmp = output.ToArray();
        Dictionary<int, int> newIndex = new();
        int nextIndex = 0;
        foreach (int symbol in symbols)
        {
            if (!newIndex.ContainsKey(symbol))
            {
                newIndex[symbol] = nextIndex;
                output[nextIndex] = tmp[symbol];
                ++nextIndex;
            }
        }

        output.RemoveRange(nextIndex, output.Count - nextIndex);
        for (int i = 0; i < symbols.Length; i++)
        {
            symbols[i] = newIndex[symbols[i]];
        }
    }
}
