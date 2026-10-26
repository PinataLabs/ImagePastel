// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_ans.h and lib/jxl/enc_ans.cc (EntropyEncodingData, BuildAndStoreEntropyCodes,
// BuildAndStoreANSEncodingData, ChooseUintConfigs, Histogram::ANSPopulationCost),
// lib/jxl/enc_ans_params.h (Histogram::MaxSymbol) and lib/jxl/enc_ans_simd.cc (EstimateTokenCost).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;

namespace PinataLabs.ImagePastel.Formats.Jxl.Entropy;

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
    /// <param name="tokens">The token streams that <paramref name="builder"/> counts.</param>
    /// <param name="builder">The histogram of each context.</param>
    /// <param name="writer">The writer, or <see langword="null"/> to estimate the cost only.</param>
    /// <returns>The cost in bits: exact for the headers, estimated for the data.</returns>
    public long BuildAndStoreEntropyCodes(JxlHistogramParams parameters, IReadOnlyList<List<JxlToken>> tokens, IReadOnlyList<JxlHistogram> builder, JxlBitWriter writer)
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

        this.ChooseUintConfigs(parameters, tokens, clusteredHistograms);

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
        int size = histogram.AlphabetSize();
        if (this.UsePrefixCode)
        {
            long cost = 0;
            if (size <= 1)
            {
                return 0;
            }

            uint[] histo = new uint[size];
            for (int i = 0; i < size; i++)
            {
                DebugGuard.IsTrue(histogram.Counts[i] >= 0, "Histogram counts are not negative");
                histo[i] = (uint)histogram.Counts[i];
            }

            byte[] depths = new byte[size];
            ushort[] bits = new ushort[size];
            if (writer == null)
            {
                JxlSizeWriter tmpWriter = new();
                JxlHuffmanEncoder.BuildAndStoreHuffmanTree(histo, size, depths, bits, tmpWriter);
                cost = tmpWriter.Size;
            }
            else
            {
                long start = writer.BitsWritten;
                JxlHuffmanEncoder.BuildAndStoreHuffmanTree(histo, size, depths, bits, writer);
                cost = writer.BitsWritten - start;
            }

            for (int i = 0; i < size; i++)
            {
                info[i].Bits = depths[i] == 0 ? (ushort)0 : bits[i];
                info[i].Depth = depths[i];
            }

            // Estimate data cost.
            for (int i = 0; i < size; i++)
            {
                cost += histo[i] * info[i].Depth;
            }

            return cost;
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

    // Chooses the hybrid uint configuration of each clustered histogram, re-tokenizes the histogram
    // with it, and sets the alphabet size from the largest token.
    private void ChooseUintConfigs(JxlHistogramParams parameters, IReadOnlyList<List<JxlToken>> tokens, List<JxlHistogram> clusteredHistograms)
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

        // If the uint config is fixed, just use it. Departure (ADR 0001): libjxl also searches for
        // kBest, over 28 configurations. kBest is the e8+ search, and the parameters of the ICC stream,
        // which every effort writes, keep libjxl's default kBest; searching there would change the
        // Fastest output. So kBest keeps the default configuration, a valid choice.
        if (parameters.UintMethod != JxlHistogramParams.HybridUintMethod.Fast)
        {
            return;
        }

        // Brute-force method that tries a few options.
        JxlHybridUintConfig[] configs =
        {
            new(4, 2, 0), // default
            new(4, 1, 2), // add parity but less msb
            new(0, 0, 0), // smallest histograms
            new(2, 0, 1), // works well for ctx map
        };

        // The values of each clustered histogram, then the LZ77 lengths of each, in the second half.
        int numHisto = clusteredHistograms.Count;
        List<uint>[] values = new List<uint>[2 * numHisto];
        uint[] maxValuePerHisto = new uint[2 * numHisto];
        for (int h = 0; h < 2 * numHisto; h++)
        {
            values[h] = new List<uint>();
        }

        // TODO(veluca): do not ignore lz77 commands.
        foreach (List<JxlToken> stream in tokens)
        {
            foreach (JxlToken token in stream)
            {
                int histo = this.ContextMap[(int)token.Context] + (token.IsLz77Length ? numHisto : 0);
                values[histo].Add(token.Value);
                maxValuePerHisto[histo] = Math.Max(maxValuePerHisto[histo], token.Value);
            }
        }

        // Wider histograms are assigned max cost in PopulationCost anyway and therefore will not be used
        const uint maxAlpha = JxlAnsParams.AnsMaxAlphabetSize;
        for (int h = 0; h < numHisto; h++)
        {
            float bestCost = float.MaxValue;
            foreach (JxlHybridUintConfig cfg in configs)
            {
                cfg.Encode(maxValuePerHisto[h], out uint tokOfMax, out _, out _);
                tokOfMax |= cfg.LsbMask;
                if (tokOfMax >= maxAlpha || (this.Lz77.Enabled && tokOfMax >= this.Lz77.MinSymbol))
                {
                    // Not valid config for this context
                    continue;
                }

                JxlHistogram histo = new();
                uint extraBits = EstimateTokenCost(values[h], cfg, histo);
                float cost = AnsPopulationCost(histo);
                cost += extraBits;

                // Add signaling cost of the hybriduintconfig itself.
                cost += JxlBits.CeilLog2Nonzero(cfg.SplitExponent + 1);
                cost += JxlBits.CeilLog2Nonzero(cfg.SplitExponent - cfg.MsbInToken + 1);
                if (cost < bestCost)
                {
                    this.UintConfig[h] = cfg;
                    bestCost = cost;
                    clusteredHistograms[h] = histo;
                }
            }
        }

        // libjxl adds the LZ77 length symbols back into each histogram. When no configuration was
        // valid for a histogram, it still holds them from the context's histogram, so they count
        // twice; that is libjxl's behaviour, and the code is still valid.
        int maxTok = 0;
        for (int h = 0; h < numHisto; h++)
        {
            JxlHistogram histo = clusteredHistograms[h];
            maxTok = Math.Max(maxTok, MaxSymbol(histo));
            List<uint> lengths = values[numHisto + h];
            if (lengths.Count == 0)
            {
                // E.g. when lz77 not enabled
                continue;
            }

            foreach (uint length in lengths)
            {
                this.Lz77.LengthUintConfig.Encode(length, out uint tok, out _, out _);
                histo.Add((int)(tok + this.Lz77.MinSymbol));
            }

            maxTok = Math.Max(maxTok, MaxSymbol(histo));
        }

        // `log_alpha_size - 5` is encoded in the header, so min is 5.
        int logSize = 5;
        while (maxTok >= (1 << logSize))
        {
            ++logSize;
        }

        int maxLogAlphaSize = this.UsePrefixCode ? JxlAnsParams.PrefixMaxBits : 8;
        DebugGuard.IsTrue(logSize <= maxLogAlphaSize, "The tokens must fit the alphabet");
        this.LogAlphaSize = this.UsePrefixCode ? JxlAnsParams.PrefixMaxBits : logSize;
    }

    // libjxl's EstimateTokenCost, in its scalar form: adds the token of each value to histo and
    // returns the number of raw bits.
    private static uint EstimateTokenCost(List<uint> values, JxlHybridUintConfig cfg, JxlHistogram histo)
    {
        uint extraBits = 0;
        foreach (uint v in values)
        {
            cfg.Encode(v, out uint tok, out uint nbits, out _);
            extraBits += nbits;
            histo.Add((int)tok);
        }

        return extraBits;
    }

    // libjxl's Histogram::ANSPopulationCost: the cost of the histogram with the fast ANS strategy,
    // whatever the parameters ask for.
    private static float AnsPopulationCost(JxlHistogram histo)
        => histo.AlphabetSize() > JxlAnsParams.AnsMaxAlphabetSize
            ? float.MaxValue
            : JxlAnsEncodingHistogram.ComputeBest(histo, JxlHistogramParams.AnsHistogramStrategy.Fast).Cost;

    // libjxl's Histogram::MaxSymbol: the largest symbol with a count, or 0.
    private static int MaxSymbol(JxlHistogram histo) => Math.Max(0, histo.AlphabetSize() - 1);
}
