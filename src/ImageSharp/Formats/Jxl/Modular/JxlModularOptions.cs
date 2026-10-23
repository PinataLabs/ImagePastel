// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/modular_image.h, lib/jxl/modular/modular_image.cc and lib/jxl/modular/options.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;

namespace SixLabors.ImageSharp.Formats.Jxl.Modular;

/// <summary>
/// The options of a Modular stream: the limits on the channels it codes, used to encode and decode,
/// and the encoder's tree-learning settings. libjxl's options also hold the histogram parameters,
/// which the frame encoder keeps itself here.
/// </summary>
internal sealed class JxlModularOptions
{
    // Properties default to channel, group, weighted, gradient residual, W-NW, NW-N, N-NE, N-NN.
    private static readonly uint[] DefaultSplittingHeuristicsProperties = { 0, 1, 15, 9, 10, 11, 12, 13 };

    /// <summary>
    /// Forces the encoder to produce a tree that is compatible with the WP-only decode path (or with
    /// the no-WP path, or the gradient-only path).
    /// </summary>
    public enum TreeMode
    {
        /// <summary>Only the Gradient predictor and its property.</summary>
        GradientOnly,

        /// <summary>Only the Weighted predictor and its property.</summary>
        WPOnly,

        /// <summary>No Weighted predictor or property.</summary>
        NoWP,

        /// <summary>No restriction.</summary>
        Default,
    }

    /// <summary>
    /// The kind of tree to use. libjxl also has predefined trees for the VarDCT metadata of slower efforts
    /// and of JPEG transcodes, and the Gradient fixed tree of effort 2; they come with the slices that use them.
    /// </summary>
    public enum TreeKind
    {
        /// <summary>Learn a tree from the image.</summary>
        Learn,

        /// <summary>The predefined tree of effort 3: the Weighted predictor, split on its error property.</summary>
        WPFixedDC,

        /// <summary>The VarDCT AC metadata tree of effort 3 and faster: a single leaf with the Left predictor.</summary>
        FalconACMeta,

        /// <summary>
        /// The VarDCT AC metadata tree of efforts 4 to 7: split by channel, then by the left or top value of
        /// the quant field, the strategies and the EPF sharpness.
        /// </summary>
        ACMeta,
    }

    /// <summary>Gets or sets the largest channel decoded in this stream; larger channels are decoded per group.</summary>
    public long MaxChanSize { get; set; } = 0xFFFFFF;

    /// <summary>Gets or sets the group size.</summary>
    public long GroupDim { get; set; } = 0x1FFFFFFF;

    /// <summary>
    /// Gets or sets the fraction of pixels to look at to learn an MA tree; 0 means no MA context model.
    /// </summary>
    public float NbRepeats { get; set; } = .5f;

    /// <summary>Gets or sets the maximum number of (previous channel) properties to use in the MA trees.</summary>
    public int MaxProperties { get; set; }

    /// <summary>Gets or sets the properties tree learning may split on, the static ones first.</summary>
    public List<uint> SplittingHeuristicsProperties { get; set; } = new(DefaultSplittingHeuristicsProperties);

    /// <summary>Gets or sets the estimated gain, in bits, that a split must exceed.</summary>
    public float SplittingHeuristicsNodeThreshold { get; set; } = 96;

    /// <summary>Gets or sets the most values each property is quantised to before learning.</summary>
    public int MaxPropertyValues { get; set; } = 32;

    /// <summary>Gets or sets the predictor to use for each channel.</summary>
    public JxlPredictor Predictor { get; set; } = (JxlPredictor)uint.MaxValue;

    /// <summary>Gets or sets the weighted predictor mode.</summary>
    public int WpMode { get; set; }

    /// <summary>Gets or sets how much costlier a split that is faster to decode may be and still be taken.</summary>
    public float FastDecodeMultiplier { get; set; } = 1.01f;

    /// <summary>Gets or sets the decode path the tree must stay compatible with.</summary>
    public TreeMode WpTreeMode { get; set; } = TreeMode.Default;

    /// <summary>Gets or sets the kind of tree to use.</summary>
    public TreeKind Kind { get; set; } = TreeKind.Learn;

    /// <summary>Returns a copy, as libjxl copies its options by value.</summary>
    /// <returns>The copy.</returns>
    public JxlModularOptions Clone()
    {
        JxlModularOptions copy = (JxlModularOptions)this.MemberwiseClone();
        copy.SplittingHeuristicsProperties = new List<uint>(this.SplittingHeuristicsProperties);
        return copy;
    }
}
