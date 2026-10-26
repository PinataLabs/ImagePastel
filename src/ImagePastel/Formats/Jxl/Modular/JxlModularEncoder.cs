// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/encoding/enc_encoding.cc (MakeFixedTree, GatherTreeData, LearnTree,
// EncodeModularChannelMAANS, PredefinedTree, ModularCompress) and lib/jxl/modular/encoding/enc_ma.cc
// (TokenizeTree).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Entropy;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms;

namespace PinataLabs.ImagePastel.Formats.Jxl.Modular;

/// <summary>
/// Turns Modular images into tokens with an MA tree, and the tree itself into tokens. It is the
/// mirror of <see cref="JxlModularDecoder"/> and <see cref="JxlMaTree"/>.
/// </summary>
internal static class JxlModularEncoder
{
    // The split values of libjxl's kWPFixedDC tree, on the weighted predictor's error property.
    private static readonly int[] WpFixedDcCutoffs =
    {
        -500, -392, -255, -191, -127, -95, -63, -47, -31, -23, -15,
        -11, -7, -4, -3, -1, 0, 1, 3, 5, 7, 11,
        15, 23, 31, 47, 63, 95, 127, 191, 255, 392, 500,
    };

    /// <summary>
    /// Returns libjxl's <c>PredefinedTree</c> of a kind other than <see cref="JxlModularOptions.TreeKind.Learn"/>.
    /// </summary>
    /// <param name="treeKind">The kind of tree.</param>
    /// <param name="totalPixels">The number of pixels in all channels of the streams the tree codes.</param>
    /// <param name="bitdepth">The bit depth the cutoffs scale with.</param>
    /// <returns>The tree.</returns>
    public static List<JxlPropertyDecisionNode> PredefinedTree(JxlModularOptions.TreeKind treeKind, long totalPixels, int bitdepth) => treeKind switch
    {
        // All the data is 0 except the quant field. TODO(veluca): make that 0 too.
        JxlModularOptions.TreeKind.FalconACMeta => new List<JxlPropertyDecisionNode> { Leaf(JxlPredictor.Left) },
        JxlModularOptions.TreeKind.ACMeta => PredefinedTreeAcMeta(totalPixels),
        JxlModularOptions.TreeKind.WPFixedDC => PredefinedTreeWpFixedDc(totalPixels, bitdepth),
        _ => throw new ArgumentOutOfRangeException(nameof(treeKind), treeKind, "A learned tree is not predefined."),
    };

    /// <summary>
    /// Returns libjxl's predefined <c>kWPFixedDC</c> tree, the one effort 3 uses: the Weighted
    /// predictor, split on its error property, with fewer levels for small images. libjxl's
    /// <c>PredefinedTree</c> also builds the trees of effort 2; they come with the slice that uses them.
    /// </summary>
    /// <param name="totalPixels">The number of pixels in all channels of all streams.</param>
    /// <param name="bitdepth">The largest bit depth of the streams.</param>
    /// <returns>The tree.</returns>
    public static List<JxlPropertyDecisionNode> PredefinedTreeWpFixedDc(long totalPixels, int bitdepth)
        => MakeFixedTree(JxlModularPredict.WPProp, WpFixedDcCutoffs, JxlPredictor.Weighted, totalPixels, bitdepth);

    /// <summary>
    /// Returns libjxl's predefined <c>kACMeta</c> tree, which codes the VarDCT AC metadata from effort 4
    /// to 7: one Left leaf for a small image, else a fixed tree over the channel (0 and 1 the
    /// chroma-from-luma maps, 2 the strategies and quant field, 3 the EPF sharpness), the row, and the
    /// top (property 6) and left (property 7) values.
    /// </summary>
    /// <param name="totalPixels">The number of pixels in all channels of the AC metadata streams.</param>
    /// <returns>The tree.</returns>
    public static List<JxlPropertyDecisionNode> PredefinedTreeAcMeta(long totalPixels)
    {
        // Small image.
        if (totalPixels < 1024)
        {
            return new List<JxlPropertyDecisionNode> { Leaf(JxlPredictor.Left) };
        }

        return new List<JxlPropertyDecisionNode>
        {
            // 0: c > 1
            Split(0, 1, 1),

            // 1: c > 2
            Split(0, 2, 3),

            // 2: c > 0
            Split(0, 0, 5),

            // 3: EPF control field (all 0 or 4), top > 3
            Split(6, 3, 21),

            // 4: ACS+QF, y > 0
            Split(2, 0, 7),

            // 5: CfL x
            Leaf(JxlPredictor.Gradient),

            // 6: CfL b
            Leaf(JxlPredictor.Gradient),

            // 7: QF: split according to the left quant value.
            Split(7, 5, 9),

            // 8: ACS: split in 4 segments (8x8 from 0 to 3, large square 4-5, large rectangular 6-11, 8x8
            // 12+), according to previous ACS value.
            Split(7, 5, 15),

            // QF
            Split(7, 11, 11),
            Split(7, 3, 13),
            Leaf(JxlPredictor.Left),
            Leaf(JxlPredictor.Left),
            Leaf(JxlPredictor.Left),
            Leaf(JxlPredictor.Left),

            // ACS
            Split(7, 11, 17),
            Split(7, 3, 19),
            Leaf(JxlPredictor.Zero),
            Leaf(JxlPredictor.Zero),
            Leaf(JxlPredictor.Zero),
            Leaf(JxlPredictor.Zero),

            // EPF, left > 3
            Split(7, 3, 23),
            Split(7, 3, 25),
            Leaf(JxlPredictor.Zero),
            Leaf(JxlPredictor.Zero),
            Leaf(JxlPredictor.Zero),
            Leaf(JxlPredictor.Zero),
        };
    }

    /// <summary>
    /// Learns one tree for the streams <paramref name="start"/> to <paramref name="stop"/> (exclusive):
    /// samples the pixels of every stream for the property thresholds, gathers the tree samples of
    /// every coded channel, then splits greedily. The options of stream <paramref name="start"/> set
    /// the predictors, properties and limits.
    /// </summary>
    /// <param name="images">The image of each stream.</param>
    /// <param name="options">The options of each stream.</param>
    /// <param name="start">The first stream.</param>
    /// <param name="stop">The stream after the last.</param>
    /// <param name="multiplierInfo">The multiplier boxes of quantised channels; empty for lossless.</param>
    /// <returns>The tree, before <see cref="TokenizeTree"/>.</returns>
    public static List<JxlPropertyDecisionNode> LearnTree(JxlModularImage[] images, JxlModularOptions[] options, int start, int stop, List<JxlModularMultiplierInfo> multiplierInfo)
    {
        JxlTreeSamples treeSamples = new();
        treeSamples.SetPredictor(options[start].Predictor, options[start].WpTreeMode);
        treeSamples.SetProperties(options[start].SplittingHeuristicsProperties, options[start].WpTreeMode);
        int maxC = 0;
        List<int> pixelSamples = new();
        List<int> diffSamples = new();
        List<uint> groupPixelCount = new();
        List<uint> channelPixelCount = new();
        for (int i = start; i < stop; i++)
        {
            maxC = Math.Max(images[i].Channel.Count, maxC);
            JxlTreeLearner.CollectPixelSamples(images[i], options[i], (uint)i, groupPixelCount, channelPixelCount, pixelSamples, diffSamples);
        }

        JxlStaticPropRange range = default;
        range[0, 0] = 0;
        range[0, 1] = (uint)maxC;
        range[1, 0] = (uint)start;
        range[1, 1] = (uint)stop;

        treeSamples.PreQuantizeProperties(range, multiplierInfo, groupPixelCount, channelPixelCount, pixelSamples, diffSamples, options[start].MaxPropertyValues);

        // libjxl starts the pixel count from the coded channels of the first stream, and GatherTreeData
        // then adds every pixel it visits, so the first stream counts twice.
        long totalPixels = 0;
        JxlModularImage first = images[start];
        for (int i = 0; i < first.Channel.Count; i++)
        {
            if (i >= first.NbMetaChannels && (first.Channel[i].W > options[start].MaxChanSize || first.Channel[i].H > options[start].MaxChanSize))
            {
                break;
            }

            totalPixels += (long)first.Channel[i].W * first.Channel[i].H;
        }

        totalPixels = Math.Max(totalPixels, 1);

        for (int i = start; i < stop; i++)
        {
            JxlModularImage image = images[i];
            int nbChannels = image.Channel.Count;

            if (image.W == 0 || image.H == 0 || nbChannels < 1)
            {
                continue; // is there any use for a zero-channel image?
            }

            if (image.Error)
            {
                JxlThrowHelper.ThrowImageFormatException("Invalid image");
            }

            DebugGuard.IsTrue(options[i].Kind == JxlModularOptions.TreeKind.Learn, "The stream learns its tree");

            // encode transforms. libjxl sets the weighted predictor parameters from wp_mode when the
            // predictor can be Weighted; mode 0 is the default header that Init leaves.
            JxlWeightedHeader wpHeader = new();

            // Gather tree data
            for (int c = 0; c < nbChannels; c++)
            {
                JxlModularChannel channel = image.Channel[c];
                if (c >= image.NbMetaChannels && (channel.W > options[i].MaxChanSize || channel.H > options[i].MaxChanSize))
                {
                    break;
                }

                if (channel.W == 0 || channel.H == 0)
                {
                    continue; // skip empty channels
                }

                GatherTreeData(image, c, i, wpHeader, options[i], treeSamples, ref totalPixels);
            }
        }

        // TODO(veluca): parallelize more.
        return LearnTree(treeSamples, totalPixels, options[start], multiplierInfo, range);
    }

    /// <summary>
    /// Turns <paramref name="tree"/> into tokens in the <see cref="JxlMaTree.NumTreeContexts"/>
    /// tree contexts, in breadth-first order, and returns the tree as the decoder will read it back:
    /// renumbered breadth first, with each leaf's <see cref="JxlPropertyDecisionNode.LChild"/> its leaf id.
    /// </summary>
    /// <param name="tree">The tree.</param>
    /// <param name="tokens">The tokens to add to.</param>
    /// <returns>The decoder's tree.</returns>
    public static List<JxlPropertyDecisionNode> TokenizeTree(List<JxlPropertyDecisionNode> tree, List<JxlToken> tokens)
    {
        // TODO(veluca): very simple encoding scheme. This should be improved.
        if (tree.Count > JxlMaTree.MaxTreeSize)
        {
            JxlThrowHelper.ThrowImageFormatException("MA tree too large");
        }

        Queue<int> q = new();
        q.Enqueue(0);
        uint leafId = 0;
        List<JxlPropertyDecisionNode> decoderTree = new(tree.Count);
        while (q.Count > 0)
        {
            int cur = q.Dequeue();
            JxlPropertyDecisionNode node = tree[cur];
            tokens.Add(new JxlToken(JxlMaTree.PropertyContext, (uint)(node.Property + 1)));
            if (node.Property == -1)
            {
                tokens.Add(new JxlToken(JxlMaTree.PredictorContext, (uint)node.Predictor));
                tokens.Add(new JxlToken(JxlMaTree.OffsetContext, JxlBits.PackSigned((int)node.PredictorOffset)));
                uint mulLog = (uint)JxlBits.Num0BitsBelowLs1BitNonzero(node.Multiplier);
                uint mulBits = (node.Multiplier >> (int)mulLog) - 1;
                tokens.Add(new JxlToken(JxlMaTree.MultiplierLogContext, mulLog));
                tokens.Add(new JxlToken(JxlMaTree.MultiplierBitsContext, mulBits));
                decoderTree.Add(new JxlPropertyDecisionNode(-1, 0, leafId, 0, node.Predictor, node.PredictorOffset, node.Multiplier));
                leafId++;
                continue;
            }

            decoderTree.Add(new JxlPropertyDecisionNode(
                node.Property,
                node.SplitVal,
                (uint)(decoderTree.Count + q.Count + 1),
                (uint)(decoderTree.Count + q.Count + 2),
                JxlPredictor.Zero,
                0,
                1));
            q.Enqueue((int)node.LChild);
            q.Enqueue((int)node.RChild);
            tokens.Add(new JxlToken(JxlMaTree.SplitValContext, JxlBits.PackSigned(node.SplitVal)));
        }

        return decoderTree;
    }

    /// <summary>
    /// Fills <paramref name="header"/> for <paramref name="image"/> and appends the tokens of its
    /// channels, as <see cref="JxlModularDecoder.ModularGenericDecompress"/> reads them back with the
    /// global tree. Channels stop at the first non-meta channel bigger than the options'
    /// <see cref="JxlModularOptions.MaxChanSize"/>, and empty channels are skipped.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="options">The options.</param>
    /// <param name="groupId">The stream id, which is a static property of the tree.</param>
    /// <param name="tree">The global tree, as the decoder reads it.</param>
    /// <param name="header">The group header to fill.</param>
    /// <param name="tokens">The tokens to add to.</param>
    /// <returns>The width of the widest channel coded.</returns>
    public static int ModularCompress(JxlModularImage image, JxlModularOptions options, int groupId, List<JxlPropertyDecisionNode> tree, JxlGroupHeader header, List<JxlToken> tokens)
    {
        int nbChannels = image.Channel.Count;

        // is there any use for a zero-channel image?
        if (image.W == 0 || image.H == 0 || nbChannels < 1)
        {
            return 0;
        }

        if (image.Error)
        {
            JxlThrowHelper.ThrowImageFormatException("Invalid image");
        }

        // encode transforms
        JxlBundle.Init(header);

        // libjxl sets the weighted predictor parameters from options.wp_mode here. Effort 3 keeps
        // mode 0, which is the default header that Init leaves.
        header.Transforms.Clear();
        header.Transforms.AddRange(image.Transform);
        header.UseGlobalTree = true;

        int imageWidth = 0;
        long totalTokens = 0;
        for (int i = 0; i < nbChannels; i++)
        {
            JxlModularChannel channel = image.Channel[i];
            if (i >= image.NbMetaChannels && (channel.W > options.MaxChanSize || channel.H > options.MaxChanSize))
            {
                break;
            }

            if (channel.W > imageWidth)
            {
                imageWidth = channel.W;
            }

            totalTokens += (long)channel.W * channel.H;
        }

        // libjxl can write all-zero tokens instead (options.zero_tokens), which only its fast paths use.
        // Do one big allocation for all the tokens we'll need, to avoid reallocs that might require copying.
        int start = tokens.Count;
        tokens.Capacity = Math.Max(tokens.Capacity, (int)(start + totalTokens));
        for (int i = 0; i < nbChannels; i++)
        {
            JxlModularChannel channel = image.Channel[i];
            if (i >= image.NbMetaChannels && (channel.W > options.MaxChanSize || channel.H > options.MaxChanSize))
            {
                break;
            }

            if (channel.W == 0 || channel.H == 0)
            {
                // skip empty channels
                continue;
            }

            EncodeModularChannelMaAns(image, i, header.WpHeader, tree, tokens, groupId);
        }

        // Make sure we actually wrote all tokens
        DebugGuard.IsTrue(tokens.Count - start == totalTokens, "Every pixel gives one token");
        return imageWidth;
    }

    // Adds the tree samples of one channel: about NbRepeats of its pixels, each with its properties
    // and the prediction of every predictor tried.
    private static void GatherTreeData(JxlModularImage image, int chan, int groupId, JxlWeightedHeader wpHeader, JxlModularOptions options, JxlTreeSamples treeSamples, ref long totalPixels)
    {
        JxlModularChannel channel = image.Channel[chan];
        int numExtraProps = JxlModularPredict.ExtraPropsPerChannel * options.MaxProperties;
        int[] properties = new int[JxlModularPredict.NumNonrefProperties + numExtraProps];
        double pixelFraction = Math.Min(1.0f, options.NbRepeats);

        // a fraction of 0 is used to disable learning entirely.
        if (pixelFraction > 0)
        {
            pixelFraction = Math.Max(pixelFraction, Math.Min(1.0, 1024.0 / ((ulong)channel.W * (ulong)channel.H)));
        }

        ulong threshold = (ulong)((ulong.MaxValue >> 32) * pixelFraction);
        ulong s0 = 0x94D049BB133111EBUL;
        ulong s1 = 0xBF58476D1CE4E5B9UL;

        // Xorshift128+ adapted from xorshift128+-inl.h
        bool UseSample()
        {
            unchecked
            {
                ulong a = s0;
                ulong b = s1;
                ulong bits = a + b; // b, c
                s0 = b;
                a ^= a << 23;
                a ^= b ^ (a >> 18) ^ (b >> 5);
                s1 = a;
                return (bits >> 32) <= threshold;
            }
        }

        int w = channel.W;
        int[] references = new int[numExtraProps * w];
        JxlWeightedState wpState = new(wpHeader, w);
        treeSamples.PrepareForSamples((ulong)((pixelFraction * channel.H * channel.W) + 64));

        // libjxl predicts with only the one predictor when one is tried, and takes a path without the
        // edge checks for inner pixels; both give the same predictions and properties as this one.
        Span<long> pred = stackalloc long[JxlModularPredict.NumModularPredictors];
        for (int y = 0; y < channel.H; y++)
        {
            ReadOnlySpan<int> p = channel.Row(y);
            ReadOnlySpan<int> top = y > 0 ? channel.Row(y - 1) : Span<int>.Empty;
            ReadOnlySpan<int> topTop = y > 1 ? channel.Row(y - 2) : Span<int>.Empty;
            JxlModularPredict.PrecomputeReferences(channel, y, image, chan, references, numExtraProps);
            JxlModularPredict.InitPropsRow(properties, chan, groupId, y);

            // TODO(veluca): avoid computing WP if we don't use its property or predictions.
            for (int x = 0; x < w; x++)
            {
                JxlModularPredict.PredictLearnAll(properties, w, p, top, topTop, x, y, references, numExtraProps, wpState, pred);
                totalPixels++;
                if (UseSample())
                {
                    treeSamples.AddSample(p[x], properties, pred);
                }

                wpState.UpdateErrors(p[x], x, y, w);
            }
        }
    }

    // Learns the tree from the gathered samples. A split must save more bits than the node threshold,
    // scaled by the fraction of the pixels that were sampled.
    private static List<JxlPropertyDecisionNode> LearnTree(JxlTreeSamples treeSamples, long totalPixels, JxlModularOptions options, List<JxlModularMultiplierInfo> multiplierInfo, JxlStaticPropRange staticPropRange)
    {
        for (int i = 0; i < JxlModularPredict.NumStaticProperties; i++)
        {
            if (staticPropRange[i, 1] == 0)
            {
                staticPropRange[i, 1] = uint.MaxValue;
            }
        }

        if (!treeSamples.HasSamples)
        {
            return new List<JxlPropertyDecisionNode> { Leaf(treeSamples.PredictorFromIndex(0)) };
        }

        float pixelFraction = treeSamples.NumSamples * 1.0f / totalPixels;
        float requiredCost = (float)((pixelFraction * 0.9) + 0.1);
        treeSamples.AllSamplesDone();
        return JxlTreeLearner.ComputeBestTree(treeSamples, options.SplittingHeuristicsNodeThreshold * requiredCost, multiplierInfo, staticPropRange, options.FastDecodeMultiplier);
    }

    // `cutoffs` must be sorted.
    private static List<JxlPropertyDecisionNode> MakeFixedTree(int property, int[] cutoffs, JxlPredictor pred, long numPixels, int bitdepth)
    {
        int logPx = JxlBits.CeilLog2Nonzero((ulong)numPixels);
        int minGap = 0;

        // Reduce fixed tree height when encoding small images.
        if (logPx < 14)
        {
            minGap = 8 * (14 - logPx);
        }

        int shift = bitdepth > 11 ? Math.Min(4, bitdepth - 11) : 0;
        int mul = 1 << shift;
        List<JxlPropertyDecisionNode> tree = new();
        Queue<(int Begin, int End, int Pos)> q = new();

        // Leaf IDs will be set by roundtrip decoding the tree.
        tree.Add(Leaf(pred));
        q.Enqueue((0, cutoffs.Length, 0));
        while (q.Count > 0)
        {
            (int begin, int end, int pos) = q.Dequeue();
            if (begin + minGap >= end)
            {
                continue;
            }

            int split = (begin + end) / 2;
            int cutoff = cutoffs[split] * mul;
            tree[pos] = new JxlPropertyDecisionNode(property, cutoff, (uint)tree.Count, (uint)tree.Count + 1, JxlPredictor.Zero, 0, 1);
            q.Enqueue((split + 1, end, tree.Count));
            tree.Add(Leaf(pred));
            q.Enqueue((begin, split, tree.Count));
            tree.Add(Leaf(pred));
        }

        return tree;
    }

    private static JxlPropertyDecisionNode Leaf(JxlPredictor predictor) => new(-1, 0, 0, 0, predictor, 0, 1);

    // libjxl's PropertyDecisionNode::Split with the right child after the left one.
    private static JxlPropertyDecisionNode Split(int property, int splitVal, uint lchild) => new(property, splitVal, lchild, lchild + 1, JxlPredictor.Zero, 0, 1);

    // libjxl takes a fast path here when the tree only uses the weighted predictor and its property
    // within a small range, which kWPFixedDC does. The port always takes the general path: it gives
    // the same tokens and shares the decoder's prediction code.
    private static void EncodeModularChannelMaAns(JxlModularImage image, int chan, JxlWeightedHeader wpHeader, List<JxlPropertyDecisionNode> globalTree, List<JxlToken> tokens, int groupId)
    {
        JxlModularChannel channel = image.Channel[chan];
        DebugGuard.IsTrue(channel.W != 0 && channel.H != 0, "Channel is not empty");

        Span<int> staticProps = stackalloc int[] { chan, groupId };

        // The leaves keep their unclustered ids, the contexts of the tokens.
        JxlFlatDecisionNode[] tree = JxlModularDecoder.FilterTree(globalTree, staticProps, out int numProps, out bool useWp);

        int[] properties = new int[numProps];
        int numExtraProps = numProps - JxlModularPredict.NumNonrefProperties;
        int[] references = numExtraProps == 0 ? Array.Empty<int>() : new int[numExtraProps * channel.W];
        JxlWeightedState wpState = useWp ? new JxlWeightedState(wpHeader, channel.W) : null;
        int w = channel.W;
        for (int y = 0; y < channel.H; y++)
        {
            ReadOnlySpan<int> p = channel.Row(y);
            ReadOnlySpan<int> top = y > 0 ? channel.Row(y - 1) : Span<int>.Empty;
            ReadOnlySpan<int> topTop = y > 1 ? channel.Row(y - 2) : Span<int>.Empty;
            if (numExtraProps > 0)
            {
                JxlModularPredict.PrecomputeReferences(channel, y, image, chan, references, numExtraProps);
            }

            JxlModularPredict.InitPropsRow(properties, chan, groupId, y);
            for (int x = 0; x < w; x++)
            {
                JxlPredictionResult res = JxlModularPredict.PredictTree(properties, w, p, top, topTop, x, y, tree, references, numExtraProps, wpState);
                long residual = p[x] - res.Guess;
                DebugGuard.IsTrue(residual % res.Multiplier == 0, "Residual must be a multiple of the multiplier");
                tokens.Add(new JxlToken((uint)res.Context, JxlBits.PackSigned((int)(residual / res.Multiplier))));
                wpState?.UpdateErrors(p[x], x, y, w);
            }
        }
    }
}
