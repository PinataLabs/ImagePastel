// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_ans.h and lib/jxl/enc_ans.cc (EntropyEncodingData, BuildAndStoreEntropyCodes,
// BuildAndStoreANSEncodingData, ChooseUintConfigs).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Formats.Jxl.Entropy;

/// <summary>
/// The entropy codes of one stream, as the writer builds them: the context map, the hybrid uint
/// configurations and the encoding of each symbol. It is the encoder's counterpart of <see cref="JxlAnsCode"/>.
/// </summary>
internal sealed class JxlEntropyEncodingData
{
    /// <summary>Gets the encoding of each symbol, per clustered histogram.</summary>
    public List<JxlAnsEncSymbolInfo[]> EncodingInfo { get; } = new();

    /// <summary>Gets or sets a value indicating whether the stream uses prefix codes instead of ANS.</summary>
    public bool UsePrefixCode { get; set; }

    /// <summary>Gets the hybrid uint configuration of each clustered histogram.</summary>
    public List<JxlHybridUintConfig> UintConfig { get; } = new();

    /// <summary>Gets or sets log2 of the ANS alphabet size.</summary>
    public int LogAlphaSize { get; set; }

    /// <summary>Gets the LZ77 parameters.</summary>
    public JxlLz77Params Lz77 { get; } = new();

    /// <summary>Gets the clustered histogram of each context.</summary>
    public List<byte> ContextMap { get; } = new();

    /// <summary>
    /// Clusters the histograms of <paramref name="builder"/>, then writes the context map, the
    /// ANS-or-prefix choice, the hybrid uint configurations and each clustered histogram. With no
    /// <paramref name="writer"/>, it only builds the codes and estimates their cost.
    /// </summary>
    /// <param name="parameters">The histogram parameters.</param>
    /// <param name="builder">The histogram of each context.</param>
    /// <param name="writer">The writer, or <see langword="null"/> to estimate the cost only.</param>
    /// <returns>The cost in bits: exact for the headers, estimated for the data.</returns>
    public long BuildAndStoreEntropyCodes(JxlHistogramParams parameters, IReadOnlyList<JxlHistogram> builder, JxlBitWriter writer)
    {
        // libjxl can extend the codes of an earlier stream here (prev_histograms); only its streaming
        // mode does, so the port always starts from none.
        DebugGuard.IsTrue(this.EncodingInfo.Count == 0, "Entropy codes are built once");
        List<JxlHistogram> clusteredHistograms = new();
        int contextOffset = this.ContextMap.Count;
        for (int c = 0; c < builder.Count; c++)
        {
            this.ContextMap.Add(0);
        }

        if (builder.Count > 1)
        {
            JxlHistogramClustering.ClusterHistograms(parameters, builder, JxlHistogramClustering.ClustersLimit, clusteredHistograms, out int[] histogramSymbols);
            for (int c = 0; c < builder.Count; ++c)
            {
                this.ContextMap[contextOffset + c] = (byte)histogramSymbols[c];
            }

            if (writer != null)
            {
                JxlContextMapEncoder.EncodeContextMap(this.ContextMap, clusteredHistograms.Count, writer);
            }
        }
        else
        {
            clusteredHistograms.Add(builder[0]);
        }

        this.ChooseUintConfigs(parameters, clusteredHistograms);

        // Used if writer == nullptr to estimate costs.
        JxlSizeWriter sizeWriter = new();
        long cost = this.UsePrefixCode ? 1 : 3;

        writer?.Write(1, this.UsePrefixCode ? 1UL : 0UL);
        if (writer == null)
        {
            JxlEntropyEncoder.EncodeUintConfigs(this.UintConfig, sizeWriter, this.LogAlphaSize);
        }
        else
        {
            if (!this.UsePrefixCode)
            {
                writer.Write(2, (ulong)(this.LogAlphaSize - 5));
            }

            JxlEntropyEncoder.EncodeUintConfigs(this.UintConfig, writer, this.LogAlphaSize);
        }

        if (this.UsePrefixCode)
        {
            foreach (JxlHistogram histo in clusteredHistograms)
            {
                int alphabetSize = Math.Max(1, histo.AlphabetSize());
                JxlAnsEncodingHistogram.StoreVarLenUint16(alphabetSize - 1, writer ?? (IJxlBitSink)sizeWriter);
            }
        }

        cost += sizeWriter.Size;
        foreach (JxlHistogram histo in clusteredHistograms)
        {
            // libjxl sizes this to the alphabet; it is at least one entry here, because an empty
            // histogram still fills the entry of symbol 0.
            int alphabetSize = histo.AlphabetSize();
            this.EncodingInfo.Add(new JxlAnsEncSymbolInfo[Math.Max(1, alphabetSize)]);
            cost += this.BuildAndStoreAnsEncodingData(parameters.AnsStrategy, histo, writer);
        }

        return cost;
    }

    // Returns an estimate or exact cost of encoding this histogram and the corresponding data.
    private long BuildAndStoreAnsEncodingData(JxlHistogramParams.AnsHistogramStrategy strategy, JxlHistogram histogram, JxlBitWriter writer)
    {
        JxlAnsEncSymbolInfo[] info = this.EncodingInfo[^1];
        if (this.UsePrefixCode)
        {
            // libjxl builds and stores a Huffman tree here (BuildAndStoreHuffmanTree); slice E1g ports it.
            JxlThrowHelper.ThrowNotSupportedException("Prefix codes in the encoder");
        }

        JxlAnsEncodingHistogram normalized = JxlAnsEncodingHistogram.ComputeBest(histogram, strategy);

        JxlAliasEntry[] a = new JxlAliasEntry[1 << this.LogAlphaSize];
        JxlAliasTable.InitAliasTable(new List<int>(normalized.Counts), JxlAnsParams.AnsLogTabSize, this.LogAlphaSize, a, 0);
        normalized.AnsBuildInfoTable(a, this.LogAlphaSize, info);
        if (writer != null)
        {
            normalized.Encode(writer);
        }

        return (long)MathF.Ceiling(normalized.Cost);
    }

    private void ChooseUintConfigs(JxlHistogramParams parameters, List<JxlHistogram> clusteredHistograms)
    {
        // Set sane default `log_alpha_size`. libjxl's streaming mode uses 8 too; it is not ported.
        if (this.UsePrefixCode)
        {
            this.LogAlphaSize = JxlAnsParams.PrefixMaxBits;
        }
        else if (this.Lz77.Enabled)
        {
            this.LogAlphaSize = 8;
        }
        else
        {
            this.LogAlphaSize = 7;
        }

        this.UintConfig.Clear();
        for (int i = 0; i < clusteredHistograms.Count; i++)
        {
            this.UintConfig.Add(parameters.UintConfig());
        }

        // If the uint config is fixed, just use it. libjxl searches for a better one per histogram
        // for kBest and kFast, which only the higher efforts use; that search comes with E2.
    }
}
