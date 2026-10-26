// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_ans_params.h (HistogramParams, and its HistogramParams(tier, num_ctx) constructor),
// lib/jxl/enc_ans.cc (HistogramParams::ForModular) and the histogram parameters that EncodeGlobalACInfo in
// lib/jxl/enc_frame.cc sets.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Modular;

namespace PinataLabs.ImagePastel.Formats.Jxl.Entropy;

/// <summary>How the encoder builds the histograms of an entropy-coded stream.</summary>
internal sealed class JxlHistogramParams
{
    /// <summary>How hard to look for histograms that can share a code.</summary>
    public enum ClusteringType
    {
        /// <summary>Only 4 clusters.</summary>
        Fastest,

        /// <summary>A k-means step only.</summary>
        Fast,

        /// <summary>A k-means step, then pair merging by ANS cost.</summary>
        Best,
    }

    /// <summary>How the hybrid uint configuration is chosen.</summary>
    public enum HybridUintMethod
    {
        /// <summary>Just use the default configuration (4, 2, 0).</summary>
        None,

        /// <summary>Force the fastest option, (0, 0, 0).</summary>
        K000,

        /// <summary>Just try a couple of options.</summary>
        Fast,

        /// <summary>The fast choice for the context map, (2, 0, 1).</summary>
        ContextMap,

        /// <summary>Search all options.</summary>
        Best,
    }

    /// <summary>
    /// Whether to try LZ77. libjxl also has several LZ77 match finders, which only e8 and above and
    /// the ICC stream use; only the first two values are ported.
    /// </summary>
    public enum Lz77Method
    {
        /// <summary>Do not try LZ77.</summary>
        None,

        /// <summary>Only try doing RLE.</summary>
        Rle,
    }

    /// <summary>How hard to look for the cheapest ANS histogram precision.</summary>
    public enum AnsHistogramStrategy
    {
        /// <summary>Only try some methods, early exit.</summary>
        Fast,

        /// <summary>Only try some methods.</summary>
        Approximate,

        /// <summary>Try all methods.</summary>
        Precise,
    }

    /// <summary>Gets or sets the clustering.</summary>
    public ClusteringType Clustering { get; set; } = ClusteringType.Best;

    /// <summary>Gets or sets the hybrid uint method.</summary>
    public HybridUintMethod UintMethod { get; set; } = HybridUintMethod.Best;

    /// <summary>Gets or sets the LZ77 method.</summary>
    public Lz77Method Lz77 { get; set; } = Lz77Method.Rle;

    /// <summary>Gets or sets the ANS histogram strategy.</summary>
    public AnsHistogramStrategy AnsStrategy { get; set; } = AnsHistogramStrategy.Precise;

    /// <summary>Gets or sets the largest number of histograms after clustering.</summary>
    public int MaxHistograms { get; set; } = int.MaxValue;

    /// <summary>Gets or sets a value indicating whether to always use prefix codes.</summary>
    public bool ForceHuffman { get; set; }

    /// <summary>Gets or sets a value indicating whether the stream chooses ANS or prefix codes itself.</summary>
    public bool InitializeGlobalState { get; set; } = true;

    /// <summary>
    /// Gets or sets the width of each token stream's image, which LZ77 uses as its distance
    /// multiplier. A stream with no width, or a width of 0, has no special distances.
    /// </summary>
    public IReadOnlyList<int> ImageWidths { get; set; } = Array.Empty<int>();

    /// <summary>
    /// Returns libjxl's histogram parameters for the AC tokens of a VarDCT frame: its
    /// <c>HistogramParams(tier, num_ctx)</c> constructor, with LZ77 off above Tortoise as
    /// <c>EncodeGlobalACInfo</c> sets it.
    /// </summary>
    /// <param name="speedTier">The speed tier.</param>
    /// <returns>The parameters.</returns>
    public static JxlHistogramParams ForAc(JxlSpeedTier speedTier)
    {
        JxlHistogramParams result = new();
        if (speedTier > JxlSpeedTier.Falcon)
        {
            result.Clustering = ClusteringType.Fastest;
            result.Lz77 = Lz77Method.None;
        }
        else if (speedTier > JxlSpeedTier.Tortoise)
        {
            result.Clustering = ClusteringType.Fast;
        }
        else
        {
            // libjxl's kBest clustering and LZ77 match finders of the slowest efforts are not ported.
            JxlThrowHelper.ThrowNotSupportedException("The AC histograms of libjxl effort 9 and up");
        }

        if (speedTier > JxlSpeedTier.Tortoise)
        {
            result.UintMethod = HybridUintMethod.None;
        }

        if (speedTier >= JxlSpeedTier.Squirrel)
        {
            result.AnsStrategy = AnsHistogramStrategy.Approximate;
        }

        if (speedTier > JxlSpeedTier.Tortoise)
        {
            result.Lz77 = Lz77Method.None;
        }

        return result;
    }

    /// <summary>
    /// Returns libjxl's histogram parameters for Modular streams.
    /// </summary>
    /// <param name="speedTier">The speed tier.</param>
    /// <param name="modularMode">Whether the frame is a Modular frame.</param>
    /// <param name="predictor">The predictor option.</param>
    /// <param name="extraDcPrecision">
    /// The extra DC precision of each LF group of a VarDCT frame, or empty; only the first group's is read,
    /// as libjxl does.
    /// </param>
    /// <param name="decodingSpeedTier">The decoding speed tier.</param>
    /// <returns>The parameters.</returns>
    public static JxlHistogramParams ForModular(JxlSpeedTier speedTier, bool modularMode, JxlPredictor predictor, ReadOnlySpan<int> extraDcPrecision = default, int decodingSpeedTier = 0)
    {
        JxlHistogramParams result = new();
        if (speedTier > JxlSpeedTier.Kitten)
        {
            result.Clustering = ClusteringType.Fast;
            result.AnsStrategy = speedTier > JxlSpeedTier.Thunder ? AnsHistogramStrategy.Fast : AnsHistogramStrategy.Approximate;
            result.Lz77 = modularMode && speedTier <= JxlSpeedTier.Hare ? Lz77Method.Rle : Lz77Method.None;

            // Near-lossless DC, as well as modular mode, require choosing hybrid uint more carefully.
            bool nearLosslessDc = !extraDcPrecision.IsEmpty && extraDcPrecision[0] != 0;
            result.UintMethod = nearLosslessDc || (modularMode && speedTier < JxlSpeedTier.Cheetah) ? HybridUintMethod.Fast : HybridUintMethod.None;
        }
        else
        {
            // libjxl picks its LZ77 match finders here (kOptc256 and kLZ77b3w3f), which are not ported.
            JxlThrowHelper.ThrowNotSupportedException("The LZ77 match finders of Modular streams (libjxl effort 8 and up)");
        }

        if (decodingSpeedTier >= 2)
        {
            result.MaxHistograms = 12;
        }

        // No predictor requires LZ77 to compress residuals. Effort 3 and lower have forced predictors,
        // so kNone is set. Departure (ADR 0001): libjxl picks the kLZ77b3w3f match finder at Wombat and
        // Squirrel (slower tiers threw above). The match finder is not ported, so RLE stands in for it;
        // no effort sets the Zero predictor below Falcon, so this never runs.
        if (predictor == JxlPredictor.Zero && modularMode)
        {
            result.Lz77 = speedTier >= JxlSpeedTier.Falcon ? Lz77Method.None : Lz77Method.Rle;
        }

        return result;
    }

    /// <summary>Returns the hybrid uint configuration that <see cref="UintMethod"/> fixes.</summary>
    /// <returns>The configuration.</returns>
    public JxlHybridUintConfig UintConfig()
        => this.UintMethod switch
        {
            HybridUintMethod.ContextMap => new JxlHybridUintConfig(2, 0, 1),
            HybridUintMethod.K000 => new JxlHybridUintConfig(0, 0, 0),

            // Default config for clustering.
            _ => new JxlHybridUintConfig(4, 2, 0),
        };
}
