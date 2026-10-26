// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_heuristics.cc (FindBestBlockEntropyModel) and lib/jxl/enc_context_map.cc (EncodeBlockCtxMap).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Entropy;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;

namespace PinataLabs.ImagePastel.Formats.Jxl.VarDct;

/// <summary>
/// The encoder's block context map: libjxl's choice of a map from the quant field and the strategies of
/// a frame, below Falcon, and the writer of the map in the DC global section.
/// </summary>
internal static class JxlEncBlockCtxMap
{
    private const int NumOrders = JxlBlockCtxMap.NumOrders;

    /// <summary>
    /// Chooses a block context map that depends on the amount of quantization for AC, as libjxl's
    /// <c>FindBestBlockEntropyModel</c>: up to two quant field segments, and the (order, segment) clusters
    /// merged smallest first. It leaves the map as it is from Falcon up, and for frames with fewer than
    /// 1024·d blocks.
    /// </summary>
    /// <remarks>
    /// libjxl first sets a simple map from decoding speed tier 1 (into a copy, so it has no effect); the
    /// encoder has no decoding speed tier option.
    /// </remarks>
    /// <param name="speedTier">The speed tier.</param>
    /// <param name="distance">The distance.</param>
    /// <param name="rawQuantField">The raw quant field, one value per block in row-major order.</param>
    /// <param name="acStrategy">The strategy of each block.</param>
    /// <param name="blockCtxMap">The map to set.</param>
    public static void FindBestBlockEntropyModel(JxlSpeedTier speedTier, float distance, ReadOnlySpan<int> rawQuantField, JxlAcStrategyImage acStrategy, JxlBlockCtxMap blockCtxMap)
    {
        if (speedTier >= JxlSpeedTier.Falcon)
        {
            return;
        }

        // No need to change context modeling for small images. libjxl's size_t of a float product, truncated.
        int xsize = acStrategy.XSize;
        int ysize = acStrategy.YSize;
        long tot = (long)xsize * ysize;
        long sizeForCtxModel = (long)((1 << 10) * distance);
        if (tot < sizeForCtxModel)
        {
            return;
        }

        // Count the occurrences of each qf value and each strategy type.
        long[] qfCounts = new long[256];
        long[] qfOrdCounts = new long[NumOrders * 256];
        for (int y = 0; y < ysize; y++)
        {
            ReadOnlySpan<int> qfRow = rawQuantField.Slice(y * xsize, xsize);
            for (int x = 0; x < xsize; x++)
            {
                int ord = JxlCoeffOrder.StrategyOrder[acStrategy.Get(x, y).RawStrategy];
                int qf = qfRow[x] - 1;
                qfCounts[qf]++;
                qfOrdCounts[(ord * 256) + qf]++;
            }
        }

        // Splitting the context model according to the quantization field seems to mostly benefit only
        // large images.
        long sizeForQfSplit = (long)((1 << 13) * distance);
        long numQfSegments = tot < sizeForQfSplit ? 1 : 2;
        List<uint> qft = new();

        // Divide the quant field in up to num_qf_segments segments.
        long cumsum = 0;
        long next = 1;
        long lastCut = 256;
        long cut = tot * next / numQfSegments;
        for (uint j = 0; j < 256; j++)
        {
            cumsum += qfCounts[j];
            if (cumsum > cut)
            {
                if (j != 0)
                {
                    qft.Add(j);
                }

                lastCut = j;
                while (cumsum > cut)
                {
                    next++;
                    cut = tot * next / numQfSegments;
                }
            }
            else if (next > qft.Count + 1)
            {
                if (j != 0 && j - 1 == lastCut)
                {
                    qft.Add(j);
                }
            }
        }

        // Count the occurrences of each segment.
        int segments = qft.Count + 1;
        long[] counts = new long[NumOrders * segments];
        int qftPos = 0;
        for (int j = 0; j < 256; j++)
        {
            if (qftPos < qft.Count && j == qft[qftPos])
            {
                qftPos++;
            }

            for (int i = 0; i < NumOrders; i++)
            {
                counts[qftPos + (i * segments)] += qfOrdCounts[(i * 256) + j];
            }
        }

        // Repeatedly merge the lowest-count pair.
        byte[] remap = new byte[segments * NumOrders];
        for (int i = 0; i < remap.Length; i++)
        {
            remap[i] = (byte)i;
        }

        List<byte> clusters = new(remap);
        int nbClusters = Math.Clamp((int)(tot / sizeForCtxModel / 2), 2, 9);
        int nbClustersChroma = Math.Clamp((int)(tot / sizeForCtxModel / 3), 1, 5);

        // This is O(n^2 log n), but n is small.
        while (clusters.Count > nbClusters)
        {
            SortByCountDescending(clusters, counts);
            counts[clusters[^2]] += counts[clusters[^1]];
            counts[clusters[^1]] = 0;
            remap[clusters[^1]] = clusters[^2];
            clusters.RemoveAt(clusters.Count - 1);
        }

        for (int i = 0; i < remap.Length; i++)
        {
            while (remap[remap[i]] != remap[i])
            {
                remap[i] = remap[remap[i]];
            }
        }

        // Relabel starting from 0.
        byte[] remapRemap = new byte[remap.Length];
        Array.Fill(remapRemap, (byte)remap.Length);
        int num = 0;
        for (int i = 0; i < remap.Length; i++)
        {
            if (remapRemap[remap[i]] == remap.Length)
            {
                remapRemap[remap[i]] = (byte)num++;
            }

            remap[i] = remapRemap[remap[i]];
        }

        // Write the block context map. For chroma, only use up to nb_clusters_chroma separate block
        // contexts (those for the biggest clusters).
        byte[] ctxMap = new byte[remap.Length * 3];
        remap.CopyTo(ctxMap, 0);
        for (int i = remap.Length; i < ctxMap.Length; i++)
        {
            ctxMap[i] = (byte)(num + Math.Clamp(remap[i % remap.Length], 0, nbClustersChroma - 1));
        }

        byte max = 0;
        foreach (byte b in ctxMap)
        {
            max = Math.Max(max, b);
        }

        blockCtxMap.SetQfContexts(qft.ToArray(), ctxMap, max + 1);
    }

    /// <summary>
    /// Writes the block context map, as libjxl's <c>EncodeBlockCtxMap</c>: one bit for the default map, or
    /// the DC and quant field thresholds and the context map.
    /// </summary>
    /// <param name="blockCtxMap">The map.</param>
    /// <param name="writer">The writer.</param>
    public static void EncodeBlockCtxMap(JxlBlockCtxMap blockCtxMap, JxlBitWriter writer)
    {
        if (blockCtxMap.IsDefault)
        {
            // default
            writer.Write(1, 1);
            return;
        }

        writer.Write(1, 0);
        for (int j = 0; j < 3; j++)
        {
            int[] dct = blockCtxMap.DcThresholds[j];
            writer.Write(4, (ulong)dct.Length);
            foreach (int i in dct)
            {
                JxlFieldCoders.WriteU32(JxlBlockCtxMap.DcThresholdDist, JxlBits.PackSigned(i), writer);
            }
        }

        writer.Write(4, (ulong)blockCtxMap.QfThresholds.Length);
        foreach (uint i in blockCtxMap.QfThresholds)
        {
            JxlFieldCoders.WriteU32(JxlBlockCtxMap.QfThresholdDist, i - 1, writer);
        }

        JxlContextMapEncoder.EncodeContextMap(blockCtxMap.CtxMap, blockCtxMap.NumCtxs, writer);
    }

    // libjxl sorts with std::sort and `counts[a] > counts[b]`, which is not stable, and many counts tie
    // (every order the frame does not use counts 0), so which clusters merge depends on the STL. This
    // sorts stably, by insertion over the current order, which is what MSVC's STL does up to 32 elements
    // and libstdc++'s up to 16 (the map has 13 or 26 clusters). The map is valid with any tie order;
    // cjxl's may differ where its STL partitions.
    private static void SortByCountDescending(List<byte> clusters, long[] counts)
    {
        for (int i = 1; i < clusters.Count; i++)
        {
            byte value = clusters[i];
            int k = i;
            while (k > 0 && counts[value] > counts[clusters[k - 1]])
            {
                clusters[k] = clusters[k - 1];
                k--;
            }

            clusters[k] = value;
        }
    }
}
