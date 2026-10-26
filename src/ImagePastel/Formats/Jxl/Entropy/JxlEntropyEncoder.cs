// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_ans.cc (EncodeUintConfig, EncodeUintConfigs, BuildAndEncodeHistograms, WriteTokens).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;

namespace PinataLabs.ImagePastel.Formats.Jxl.Entropy;

/// <summary>
/// Writes entropy-coded streams: the histograms of a set of token streams, then the tokens. It is the
/// mirror of <see cref="JxlEntropyDecoder"/> and <see cref="JxlAnsSymbolReader"/>.
/// </summary>
internal static class JxlEntropyEncoder
{
    // libjxl's BitWriter::kMaxBitsPerCall, the size of the chunks the reversed ANS stream is cut into.
    private const int MaxBitsPerCall = 56;

    /// <summary>Writes one hybrid uint configuration, as <c>DecodeUintConfig</c> reads it.</summary>
    /// <typeparam name="TWriter">The writer type.</typeparam>
    /// <param name="uintConfig">The configuration.</param>
    /// <param name="writer">The writer.</param>
    /// <param name="logAlphaSize">Log2 of the alphabet size.</param>
    public static void EncodeUintConfig<TWriter>(JxlHybridUintConfig uintConfig, TWriter writer, int logAlphaSize)
        where TWriter : IJxlBitSink
    {
        writer.Write(JxlBits.CeilLog2Nonzero((uint)(logAlphaSize + 1)), uintConfig.SplitExponent);
        if (uintConfig.SplitExponent == logAlphaSize)
        {
            // msb/lsb don't matter.
            return;
        }

        int nbits = JxlBits.CeilLog2Nonzero(uintConfig.SplitExponent + 1);
        writer.Write(nbits, uintConfig.MsbInToken);
        nbits = JxlBits.CeilLog2Nonzero(uintConfig.SplitExponent - uintConfig.MsbInToken + 1);
        writer.Write(nbits, uintConfig.LsbInToken);
    }

    /// <summary>Writes each hybrid uint configuration.</summary>
    /// <typeparam name="TWriter">The writer type.</typeparam>
    /// <param name="uintConfig">The configurations.</param>
    /// <param name="writer">The writer.</param>
    /// <param name="logAlphaSize">Log2 of the alphabet size.</param>
    public static void EncodeUintConfigs<TWriter>(IReadOnlyList<JxlHybridUintConfig> uintConfig, TWriter writer, int logAlphaSize)
        where TWriter : IJxlBitSink
    {
        // TODO(veluca): RLE?
        foreach (JxlHybridUintConfig cfg in uintConfig)
        {
            EncodeUintConfig(cfg, writer, logAlphaSize);
        }
    }

    /// <summary>
    /// Builds the histograms of <paramref name="tokens"/> in <paramref name="numContexts"/> contexts and
    /// writes them as <see cref="JxlEntropyDecoder.DecodeHistograms"/> reads them: the LZ77 parameters,
    /// the context map, and the codes. With no <paramref name="writer"/>, it only estimates the cost.
    /// When LZ77 is used, each stream of <paramref name="tokens"/> is replaced by its LZ77 form, as libjxl
    /// moves the new tokens into its caller's vector; the caller writes those.
    /// </summary>
    /// <param name="parameters">The histogram parameters.</param>
    /// <param name="numContexts">The number of contexts.</param>
    /// <param name="tokens">The token streams that share the codes; replaced when LZ77 is used.</param>
    /// <param name="codes">The codes to fill.</param>
    /// <param name="writer">The writer, or <see langword="null"/> to estimate the cost only.</param>
    /// <returns>The cost in bits.</returns>
    public static long BuildAndEncodeHistograms(JxlHistogramParams parameters, int numContexts, IReadOnlyList<List<JxlToken>> tokens, JxlEntropyEncodingData codes, JxlBitWriter writer)
    {
        codes.Lz77.NonserializedDistanceContext = numContexts;
        codes.Lz77.MinSymbol = parameters.ForceHuffman ? 512u : 224u;

        List<JxlToken>[] tokensLz77 = JxlLz77Encoder.ApplyLz77(parameters, numContexts, tokens, codes.Lz77);
        if (tokensLz77 != null)
        {
            codes.Lz77.Enabled = true;
            for (int stream = 0; stream < tokens.Count; stream++)
            {
                tokens[stream].Clear();
                tokens[stream].AddRange(tokensLz77[stream]);
            }
        }

        long cost = 0;
        if (writer != null)
        {
            JxlBundle.Write(codes.Lz77, writer);
        }
        else
        {
            JxlBundle.CanEncode(codes.Lz77, out long bits);
            cost += bits;
        }

        if (codes.Lz77.Enabled)
        {
            if (writer != null)
            {
                long bitsBeforeUintConfig = writer.BitsWritten;
                EncodeUintConfig(codes.Lz77.LengthUintConfig, writer, logAlphaSize: 8);
                cost += writer.BitsWritten - bitsBeforeUintConfig;
            }
            else
            {
                JxlSizeWriter sizeWriter = new();
                EncodeUintConfig(codes.Lz77.LengthUintConfig, sizeWriter, logAlphaSize: 8);
                cost += sizeWriter.Size;
            }

            numContexts += 1;
        }

        int totalTokens = 0;

        // Build histograms.
        JxlHistogram[] builder = new JxlHistogram[numContexts];
        for (int c = 0; c < numContexts; c++)
        {
            builder[c] = new JxlHistogram();
        }

        JxlHybridUintConfig uintConfig = parameters.UintConfig();
        foreach (List<JxlToken> stream in tokens)
        {
            foreach (JxlToken token in stream)
            {
                totalTokens++;
                (token.IsLz77Length ? codes.Lz77.LengthUintConfig : uintConfig).Encode(token.Value, out uint tok, out _, out _);
                tok += token.IsLz77Length ? codes.Lz77.MinSymbol : 0;
                DebugGuard.IsTrue(token.Context < numContexts, "Token context out of range");
                builder[numContexts == 1 ? 0 : (int)token.Context].Add((int)tok);
            }
        }

        if (parameters.InitializeGlobalState)
        {
            codes.UsePrefixCode = UsePrefixCode(parameters, totalTokens, builder);
        }

        // Encode histograms.
        cost += codes.BuildAndStoreEntropyCodes(parameters, tokens, builder, writer);
        return cost;
    }

    /// <summary>
    /// Writes <paramref name="tokens"/> with <paramref name="codes"/>, as <see cref="JxlAnsSymbolReader"/>
    /// reads them. ANS codes the tokens in reverse, so the stream starts with the final ANS state.
    /// </summary>
    /// <param name="tokens">The tokens.</param>
    /// <param name="codes">The codes.</param>
    /// <param name="contextOffset">The first context map entry of these tokens.</param>
    /// <param name="writer">The writer.</param>
    /// <returns>The number of raw extra bits written.</returns>
    public static long WriteTokens(IReadOnlyList<JxlToken> tokens, JxlEntropyEncodingData codes, int contextOffset, JxlBitWriter writer)
    {
        long numExtraBits = 0;
        if (codes.UsePrefixCode)
        {
            foreach (JxlToken token in tokens)
            {
                int histo = codes.ContextMap[contextOffset + (int)token.Context];
                (token.IsLz77Length ? codes.Lz77.LengthUintConfig : codes.UintConfig[histo]).Encode(token.Value, out uint tok, out uint nbits, out uint bits);
                tok += token.IsLz77Length ? codes.Lz77.MinSymbol : 0;

                // Combine two calls to the BitWriter. Equivalent to:
                // writer->Write(codes.encoding_info[histo][tok].depth, codes.encoding_info[histo][tok].bits);
                // writer->Write(nbits, bits);
                ref JxlAnsEncSymbolInfo info = ref codes.EncodingInfo[histo][tok];
                ulong data = info.Bits;
                data |= (ulong)bits << info.Depth;
                writer.Write(info.Depth + (int)nbits, data);
                numExtraBits += nbits;
            }

            return numExtraBits;
        }

        ReversedBits reversed = new(tokens.Count);

        // libjxl has a second loop for a single context with no LZ77, which skips the context map
        // lookup; with one context the map has a single entry 0, so one loop covers both.
        JxlAnsCoder ans = new();
        for (int i = tokens.Count - 1; i >= 0; --i)
        {
            JxlToken token = tokens[i];
            int histo = codes.ContextMap[contextOffset + (int)token.Context];
            (token.IsLz77Length ? codes.Lz77.LengthUintConfig : codes.UintConfig[histo]).Encode(token.Value, out uint tok, out uint nbits, out uint bits);
            tok += token.IsLz77Length ? codes.Lz77.MinSymbol : 0;
            ref JxlAnsEncSymbolInfo info = ref codes.EncodingInfo[histo][tok];
            DebugGuard.IsTrue(info.Freq > 0, "Token has no ANS frequency");

            // Extra bits first as this is reversed.
            reversed.Add(bits, (int)nbits);
            numExtraBits += nbits;
            uint ansBits = ans.PutSymbol(info, out int ansNbits);
            reversed.Add(ansBits, ansNbits);
        }

        uint state = ans.State;
        writer.Write(32, state);
        reversed.WriteTo(writer);
        return numExtraBits;
    }

    // libjxl's choice between prefix codes and ANS: prefix codes when forced, for fewer than 100
    // tokens, with kFastest clustering, or when every context has a single symbol. libjxl also
    // forces them for its ANS fuzzer (ans_fuzzer_friendly_), which is always false outside it.
    private static bool UsePrefixCode(JxlHistogramParams parameters, int totalTokens, JxlHistogram[] builder)
    {
        bool usePrefixCode = parameters.ForceHuffman || totalTokens < 100 || parameters.Clustering == JxlHistogramParams.ClusteringType.Fastest;
        if (!usePrefixCode)
        {
            bool allSingleton = true;
            foreach (JxlHistogram histogram in builder)
            {
                if (histogram.ShannonEntropy() >= 1e-5)
                {
                    allSingleton = false;
                }
            }

            if (allSingleton)
            {
                usePrefixCode = true;
            }
        }

        return usePrefixCode;
    }

    /// <summary>
    /// The bits of an ANS stream, collected in *reversed* order as libjxl's <c>WriteTokens</c> lambda
    /// collects them, in chunks of at most <see cref="MaxBitsPerCall"/> bits.
    /// </summary>
    private sealed class ReversedBits
    {
        private readonly List<ulong> output;
        private readonly List<int> outputNbits;
        private ulong allbits;
        private int numallbits;

        public ReversedBits(int capacity)
        {
            this.output = new List<ulong>(capacity);
            this.outputNbits = new List<int>(capacity);
        }

        /// <summary>Adds the low <paramref name="nbits"/> bits of <paramref name="bits"/>.</summary>
        /// <param name="bits">The bits.</param>
        /// <param name="nbits">The number of bits.</param>
        public void Add(uint bits, int nbits)
        {
            if (nbits != 0)
            {
                DebugGuard.IsTrue(bits >> nbits == 0, "Only the low nbits bits may be set");
                if (this.numallbits + nbits > MaxBitsPerCall)
                {
                    this.output.Add(this.allbits);
                    this.outputNbits.Add(this.numallbits);
                    this.numallbits = 0;
                    this.allbits = 0;
                }

                this.allbits <<= nbits;
                this.allbits |= bits;
                this.numallbits += nbits;
            }
        }

        /// <summary>Writes the bits in stream order: the last chunk first.</summary>
        /// <param name="writer">The writer.</param>
        public void WriteTo(JxlBitWriter writer)
        {
            writer.Write(this.numallbits, this.allbits);
            for (int i = this.output.Count - 1; i >= 0; --i)
            {
                writer.Write(this.outputNbits[i], this.output[i]);
            }
        }
    }
}
