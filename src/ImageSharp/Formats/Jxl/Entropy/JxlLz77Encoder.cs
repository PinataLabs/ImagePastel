// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_lz77.cc (SymbolCostEstimator, ApplyLZ77_RLE, ApplyLZ77).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Formats.Jxl.Entropy;

/// <summary>
/// Rewrites token streams with LZ77 copies where that saves bits. Only libjxl's run-length method is
/// ported; its match finders are e8+ features.
/// </summary>
internal static class JxlLz77Encoder
{
    /// <summary>
    /// Returns <paramref name="tokens"/> with LZ77 copies, as <paramref name="parameters"/> asks, or
    /// <see langword="null"/> when LZ77 is not worth it. libjxl returns an empty list instead.
    /// </summary>
    /// <param name="parameters">The histogram parameters.</param>
    /// <param name="numContexts">The number of contexts.</param>
    /// <param name="tokens">The token streams.</param>
    /// <param name="lz77">The LZ77 parameters.</param>
    /// <returns>The new token streams, or <see langword="null"/>.</returns>
    public static List<JxlToken>[] ApplyLz77(JxlHistogramParams parameters, int numContexts, IReadOnlyList<List<JxlToken>> tokens, JxlLz77Params lz77)
        => parameters.Lz77 switch
        {
            JxlHistogramParams.Lz77Method.Rle => ApplyLz77Rle(parameters, numContexts, tokens, lz77),
            _ => null,
        };

    private static List<JxlToken>[] ApplyLz77Rle(JxlHistogramParams parameters, int numContexts, IReadOnlyList<List<JxlToken>> tokens, JxlLz77Params lz77)
    {
        List<JxlToken>[] tokensLz77 = new List<JxlToken>[tokens.Count];

        // TODO(veluca): tune heuristics here.
        SymbolCostEstimator sce = new(numContexts, parameters.ForceHuffman, tokens, lz77);
        float bitDecrease = 0;
        long totalSymbols = 0;
        JxlHybridUintConfig uintConfig = new(4, 2, 0);
        for (int stream = 0; stream < tokens.Count; stream++)
        {
            int distanceMultiplier = parameters.ImageWidths.Count > stream ? parameters.ImageWidths[stream] : 0;
            List<JxlToken> input = tokens[stream];
            List<JxlToken> output = new(input.Count);
            tokensLz77[stream] = output;
            totalSymbols += input.Count;

            // Cumulative sum of bit costs.
            float[] symCost = new float[input.Count + 1];
            for (int i = 0; i < input.Count; i++)
            {
                uintConfig.Encode(input[i].Value, out uint tok, out uint nbits, out _);
                symCost[i + 1] = sce.Bits((int)input[i].Context, (int)tok) + nbits + symCost[i];
            }

            for (int i = 0; i < input.Count; i++)
            {
                int numToCopy = 0;

                // 1 for RLE. With a multiplier, special distance 1 is (1, 0): the previous symbol. With
                // none, symbol 0 is distance 0 + 1.
                uint distanceSymbol = distanceMultiplier != 0 ? 1u : 0u;
                if (i > 0)
                {
                    for (; i + numToCopy < input.Count; numToCopy++)
                    {
                        if (input[i + numToCopy].Value != input[i - 1].Value)
                        {
                            break;
                        }
                    }
                }

                if (numToCopy == 0)
                {
                    output.Add(input[i]);
                    continue;
                }

                float cost = symCost[i + numToCopy] - symCost[i];

                // libjxl lets this subtraction wrap; the length is only used when it does not.
                uint lz77Len = (uint)numToCopy - lz77.MinLength;
                float lz77Cost = numToCopy >= lz77.MinLength ? JxlBits.CeilLog2Nonzero(lz77Len + 1) + 1 : 0;
                if (numToCopy < lz77.MinLength || cost <= lz77Cost)
                {
                    for (int j = 0; j < numToCopy; j++)
                    {
                        output.Add(input[i + j]);
                    }

                    i += numToCopy - 1;
                    continue;
                }

                // Output the LZ77 length
                output.Add(new JxlToken(input[i].Context, lz77Len, isLz77Length: true));
                i += numToCopy - 1;
                bitDecrease += cost - lz77Cost;

                // Output the LZ77 copy distance.
                output.Add(new JxlToken((uint)lz77.NonserializedDistanceContext, distanceSymbol));
            }
        }

        if (bitDecrease > (totalSymbols * 0.2) + 16)
        {
            return tokensLz77;
        }

        return null;
    }

    /// <summary>
    /// The cost in bits of each symbol in each context, from histograms of all tokens with the default
    /// hybrid uint configuration. libjxl also keeps the costs of LZ77 lengths, distances and of adding a
    /// symbol to a context, which only its match finders use.
    /// </summary>
    private sealed class SymbolCostEstimator
    {
        private readonly int maxAlphabetSize;
        private readonly float[] bits;

        public SymbolCostEstimator(int numContexts, bool forceHuffman, IReadOnlyList<List<JxlToken>> tokens, JxlLz77Params lz77)
        {
            JxlHistogram[] builder = new JxlHistogram[numContexts];
            for (int c = 0; c < numContexts; c++)
            {
                builder[c] = new JxlHistogram();
            }

            // Build histograms for estimating lz77 savings.
            JxlHybridUintConfig uintConfig = new(4, 2, 0);
            foreach (List<JxlToken> stream in tokens)
            {
                foreach (JxlToken token in stream)
                {
                    (token.IsLz77Length ? lz77.LengthUintConfig : uintConfig).Encode(token.Value, out uint tok, out _, out _);
                    tok += token.IsLz77Length ? lz77.MinSymbol : 0;
                    DebugGuard.IsTrue(token.Context < numContexts, "Token context out of range");
                    builder[token.Context].Add((int)tok);
                }
            }

            // libjxl sizes this from the padded counts; the padding only adds symbols no token has.
            this.maxAlphabetSize = 0;
            for (int i = 0; i < numContexts; i++)
            {
                this.maxAlphabetSize = Math.Max(this.maxAlphabetSize, builder[i].Counts.Length);
            }

            this.bits = new float[numContexts * this.maxAlphabetSize];

            // TODO(veluca): SIMD?
            for (int i = 0; i < numContexts; i++)
            {
                float invTotal = 1.0f / (builder[i].TotalCount + 1e-8f);
                int[] counts = builder[i].Counts;
                for (int j = 0; j < counts.Length; j++)
                {
                    long cnt = counts[j];
                    float cost = 0;
                    if (cnt != 0 && cnt != builder[i].TotalCount)
                    {
                        cost = -JxlFastMath.FastLog2f(cnt * invTotal);
                        if (forceHuffman)
                        {
                            cost = MathF.Ceiling(cost);
                        }
                    }
                    else if (cnt == 0)
                    {
                        // Highest possible cost.
                        cost = JxlAnsParams.AnsLogTabSize;
                    }

                    this.bits[(i * this.maxAlphabetSize) + j] = cost;
                }
            }
        }

        public float Bits(int ctx, int sym) => this.bits[(ctx * this.maxAlphabetSize) + sym];
    }
}
