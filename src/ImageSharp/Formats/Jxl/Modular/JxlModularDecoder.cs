// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/encoding/encoding.h and lib/jxl/modular/encoding/encoding.cc (GroupHeader, FilterTree,
// DecodeModularChannelMAANS, ValidateChannelDimensions, ModularDecode, ModularGenericDecompress).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Modular;

/// <summary>
/// Decodes Modular sub-streams. Only libjxl's general path is ported: its four fast paths
/// (single leaf, fjxl gradient RLE, gradient-only and weighted-only lookup tables) decode the same
/// samples faster and can come later.
/// </summary>
internal static class JxlModularDecoder
{
    /// <summary>
    /// Decodes a Modular sub-stream into <paramref name="image"/>, whose channels are sized beforehand.
    /// Throws <see cref="InvalidImageContentException"/> for invalid or truncated data.
    /// </summary>
    /// <param name="br">The reader.</param>
    /// <param name="image">The image to fill.</param>
    /// <param name="header">The header to fill, or <see langword="null"/> for a local one.</param>
    /// <param name="groupId">The stream id, which is a static property of the MA tree.</param>
    /// <param name="options">The channel limits.</param>
    /// <param name="undoTransforms">Whether to undo the transforms after decoding.</param>
    /// <param name="tree">The global tree, or <see langword="null"/>.</param>
    /// <param name="code">The global histograms, or <see langword="null"/>.</param>
    /// <param name="ctxMap">The global context map, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">
    /// The cancellation token, checked per transform, per squeeze step and per channel. libjxl has no
    /// cancellation (#293).
    /// </param>
    public static void ModularGenericDecompress(
        JxlBitReader br,
        JxlModularImage image,
        JxlGroupHeader header,
        int groupId,
        JxlModularOptions options,
        bool undoTransforms,
        List<JxlPropertyDecisionNode> tree,
        JxlAnsCode code,
        byte[] ctxMap,
        CancellationToken cancellationToken)
    {
        List<(int W, int H)> reqSizes = new(image.Channel.Count);
        foreach (JxlModularChannel c in image.Channel)
        {
            reqSizes.Add((c.W, c.H));
        }

        header ??= new JxlGroupHeader();
        ModularDecode(br, image, header, groupId, options, tree, code, ctxMap, cancellationToken);
        if (undoTransforms)
        {
            image.UndoTransforms(header.WpHeader, cancellationToken);
        }

        if (image.Error)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Corrupt file. Aborting.");
        }

        if (undoTransforms)
        {
            bool sameSizes = image.Channel.Count == reqSizes.Count;
            for (int c = 0; sameSizes && c < reqSizes.Count; c++)
            {
                sameSizes = reqSizes[c].W == image.Channel[c].W && reqSizes[c].H == image.Channel[c].H;
            }

            if (!sameSizes)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Undoing transforms changed the channel sizes");
            }
        }
    }

    /// <summary>
    /// Specialises the global tree for one channel and group: splits on the static properties
    /// (channel, group id) are resolved now, and the remaining tree is flattened so a lookup step
    /// descends two levels. The encoder uses it too, as libjxl's does.
    /// </summary>
    /// <param name="globalTree">The tree.</param>
    /// <param name="staticProps">The channel and the group id.</param>
    /// <param name="numProps">The number of properties the tree uses, rounded up to whole reference channels.</param>
    /// <param name="useWp">Whether the tree uses the weighted predictor or its property.</param>
    /// <returns>The flattened tree. Each leaf's <see cref="JxlFlatDecisionNode.ChildId"/> is its leaf id, not yet clustered.</returns>
    public static JxlFlatDecisionNode[] FilterTree(
        List<JxlPropertyDecisionNode> globalTree,
        ReadOnlySpan<int> staticProps,
        out int numProps,
        out bool useWp)
    {
        numProps = 0;
        bool hasWp = false;
        List<JxlFlatDecisionNode> output = new();
        Queue<int> nodes = new();
        nodes.Enqueue(0);

        // Produces a trimmed and flattened tree by doing a BFS visit of the original tree, ignoring
        // branches that are known to be false and proceeding two levels at a time to collapse nodes
        // in a flatter tree; if an inner parent node has a leaf as a child, the leaf is duplicated
        // and an implicit fake node is added. This allows to reduce the number of branches when
        // traversing the resulting flat tree.
        while (nodes.Count > 0)
        {
            int cur = nodes.Dequeue();

            // Skip nodes that we can decide now, by jumping directly to their children.
            cur = SkipStaticSplits(globalTree, staticProps, cur);
            JxlFlatDecisionNode flat = default;
            JxlPropertyDecisionNode node = globalTree[cur];
            if (node.Property == -1)
            {
                flat.Property0 = -1;
                flat.ChildId = node.LChild;
                flat.Predictor = node.Predictor;

                // libjxl's union stores the offset as 32 bits and the multiplier as signed 32 bits.
                flat.PredictorOffset = unchecked((int)node.PredictorOffset);
                flat.Multiplier = unchecked((int)node.Multiplier);
                hasWp |= node.Predictor == JxlPredictor.Weighted;
                output.Add(flat);
                continue;
            }

            flat.ChildId = (uint)(output.Count + nodes.Count + 1);

            flat.Property0 = node.Property;
            numProps = Math.Max(flat.Property0 + 1, numProps);
            flat.SplitVal0 = node.SplitVal;

            for (int i = 0; i < 2; i++)
            {
                int curChild = SkipStaticSplits(globalTree, staticProps, (int)(i == 0 ? node.LChild : node.RChild));
                JxlPropertyDecisionNode child = globalTree[curChild];
                short property;
                int splitVal;

                // We ended up in a leaf, add a placeholder decision and two copies of the leaf.
                if (child.Property == -1)
                {
                    property = 0;
                    splitVal = 0;
                    nodes.Enqueue(curChild);
                    nodes.Enqueue(curChild);
                }
                else
                {
                    property = child.Property;
                    splitVal = child.SplitVal;
                    nodes.Enqueue((int)child.LChild);
                    nodes.Enqueue((int)child.RChild);
                    numProps = Math.Max(property + 1, numProps);
                }

                if (i == 0)
                {
                    flat.Properties0 = property;
                    flat.SplitVals0 = splitVal;
                }
                else
                {
                    flat.Properties1 = property;
                    flat.SplitVals1 = splitVal;
                }
            }

            hasWp |= flat.Properties0 == JxlModularPredict.WPProp || flat.Properties1 == JxlModularPredict.WPProp || flat.Property0 == JxlModularPredict.WPProp;
            output.Add(flat);
        }

        if (numProps > JxlModularPredict.NumNonrefProperties)
        {
            numProps = ((int)JxlBits.DivCeil(numProps - JxlModularPredict.NumNonrefProperties, JxlModularPredict.ExtraPropsPerChannel) * JxlModularPredict.ExtraPropsPerChannel) + JxlModularPredict.NumNonrefProperties;
        }
        else
        {
            numProps = JxlModularPredict.NumNonrefProperties;
        }

        useWp = hasWp;
        return output.ToArray();
    }

    private static void ValidateChannelDimensions(JxlModularImage image, JxlModularOptions options)
    {
        int nbChannels = image.Channel.Count;
        foreach (bool isDc in stackalloc bool[] { true, false })
        {
            long groupDim = options.GroupDim * (isDc ? 8 : 1);
            int c = image.NbMetaChannels;
            for (; c < nbChannels; c++)
            {
                JxlModularChannel ch = image.Channel[c];
                if (ch.W > options.GroupDim || ch.H > options.GroupDim)
                {
                    break;
                }
            }

            for (; c < nbChannels; c++)
            {
                JxlModularChannel ch = image.Channel[c];
                if (ch.W == 0 || ch.H == 0)
                {
                    continue; // skip empty
                }

                bool isDcChannel = Math.Min(ch.HShift, ch.VShift) >= 3;
                if (isDcChannel != isDc)
                {
                    continue;
                }

                int shift = Math.Max(ch.HShift, ch.VShift);
                long tileDim = shift >= 63 ? 0 : groupDim >> shift;
                if (tileDim == 0)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Inconsistent transforms");
                }
            }
        }
    }

    private static void ModularDecode(
        JxlBitReader br,
        JxlModularImage image,
        JxlGroupHeader header,
        int groupId,
        JxlModularOptions options,
        List<JxlPropertyDecisionNode> globalTree,
        JxlAnsCode globalCode,
        byte[] globalCtxMap,
        CancellationToken cancellationToken)
    {
        if (image.Channel.Count == 0)
        {
            return;
        }

        // Decode transforms.
        JxlBundle.Read(br, header);
        br.EnsureAllReadsWithinBounds("ModularDecode");

        image.Transform = new List<JxlTransform>(header.Transforms);
        foreach (JxlTransform transform in image.Transform)
        {
            transform.MetaApply(image, cancellationToken);
        }

        if (image.Error)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Corrupt file. Aborting.");
        }

        ValidateChannelDimensions(image, options);

        int nbChannels = image.Channel.Count;

        int numChans = 0;
        int distanceMultiplier = 0;
        for (int i = 0; i < nbChannels; i++)
        {
            JxlModularChannel channel = image.Channel[i];
            if (i >= image.NbMetaChannels && (channel.W > options.MaxChanSize || channel.H > options.MaxChanSize))
            {
                break;
            }

            if (channel.W == 0 || channel.H == 0)
            {
                continue; // skip empty channels
            }

            if (channel.W > distanceMultiplier)
            {
                distanceMultiplier = channel.W;
            }

            numChans++;
        }

        if (numChans == 0)
        {
            return;
        }

        // Read tree.
        List<JxlPropertyDecisionNode> tree;
        JxlAnsCode code;
        byte[] contextMap;
        if (!header.UseGlobalTree)
        {
            long maxTreeSize = 1024;
            for (int i = 0; i < nbChannels; i++)
            {
                JxlModularChannel channel = image.Channel[i];
                if (i >= image.NbMetaChannels && (channel.W > options.MaxChanSize || channel.H > options.MaxChanSize))
                {
                    break;
                }

                long pixels = (long)channel.W * channel.H;
                maxTreeSize += pixels;
            }

            maxTreeSize = Math.Min(1 << 20, maxTreeSize);
            tree = JxlMaTree.DecodeTree(br, maxTreeSize);
            code = new JxlAnsCode();
            contextMap = JxlEntropyDecoder.DecodeHistograms(br, (tree.Count + 1) / 2, code);
        }
        else
        {
            if (globalTree == null || globalCode == null || globalCtxMap == null || globalTree.Count == 0)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("No global tree available but one was requested");
            }

            tree = globalTree;
            code = globalCode;
            contextMap = globalCtxMap;
        }

        // Read channels.
        JxlAnsSymbolReader reader = new(code, br, distanceMultiplier);
        for (int nextChannel = 0; nextChannel < nbChannels; nextChannel++)
        {
            JxlModularChannel channel = image.Channel[nextChannel];
            if (nextChannel >= image.NbMetaChannels && (channel.W > options.MaxChanSize || channel.H > options.MaxChanSize))
            {
                break;
            }

            if (channel.W == 0 || channel.H == 0)
            {
                continue; // skip empty channels
            }

            cancellationToken.ThrowIfCancellationRequested();
            DecodeModularChannelMaAns(br, reader, contextMap, tree, header.WpHeader, nextChannel, groupId, image);

            // Truncated group.
            br.EnsureAllReadsWithinBounds("ModularDecode");
        }

        if (!reader.CheckAnsFinalState)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("ANS decode final state failed");
        }
    }

    private static int SkipStaticSplits(List<JxlPropertyDecisionNode> tree, ReadOnlySpan<int> staticProps, int cur)
    {
        while (tree[cur].Property < JxlModularPredict.NumStaticProperties && tree[cur].Property != -1)
        {
            cur = (int)(staticProps[tree[cur].Property] > tree[cur].SplitVal ? tree[cur].LChild : tree[cur].RChild);
        }

        return cur;
    }

    // libjxl's "slow track" (no weighted predictor) and "slowest track" (with it): the general path.
    private static void DecodeModularChannelMaAns(
        JxlBitReader br,
        JxlAnsSymbolReader reader,
        byte[] contextMap,
        List<JxlPropertyDecisionNode> globalTree,
        JxlWeightedHeader wpHeader,
        int chan,
        int groupId,
        JxlModularImage image)
    {
        JxlModularChannel channel = image.Channel[chan];
        if (channel.W == 0 || channel.H == 0)
        {
            return;
        }

        Span<int> staticProps = stackalloc int[] { chan, groupId };
        JxlFlatDecisionNode[] tree = FilterTree(globalTree, staticProps, out int numProps, out bool treeHasWpPropOrPred);

        // From here on, tree lookup returns a *clustered* context ID. This avoids an extra memory
        // lookup after tree traversal.
        for (int i = 0; i < tree.Length; i++)
        {
            if (tree[i].Property0 == -1)
            {
                tree[i].ChildId = contextMap[tree[i].ChildId];
            }
        }

        int[] properties = new int[numProps];
        int numExtraProps = numProps - JxlModularPredict.NumNonrefProperties;
        int[] references = numExtraProps == 0 ? Array.Empty<int>() : new int[numExtraProps * channel.W];
        JxlWeightedState wpState = treeHasWpPropOrPred ? new JxlWeightedState(wpHeader, channel.W) : null;
        int w = channel.W;
        for (int y = 0; y < channel.H; y++)
        {
            Span<int> p = channel.Row(y);
            ReadOnlySpan<int> top = y > 0 ? channel.Row(y - 1) : Span<int>.Empty;
            ReadOnlySpan<int> topTop = y > 1 ? channel.Row(y - 2) : Span<int>.Empty;
            JxlModularPredict.InitPropsRow(properties, chan, groupId, y);
            if (numExtraProps > 0)
            {
                JxlModularPredict.PrecomputeReferences(channel, y, image, chan, references, numExtraProps);
            }

            for (int x = 0; x < w; x++)
            {
                JxlPredictionResult res = JxlModularPredict.PredictTree(properties, w, p, top, topTop, x, y, tree, references, numExtraProps, wpState);
                uint v = reader.ReadHybridUintClustered(res.Context, br);
                p[x] = MakePixel(v, res.Multiplier, res.Guess);
                wpState?.UpdateErrors(p[x], x, y, w);
            }
        }
    }

    private static int MakePixel(uint v, int multiplier, long offset)
    {
        long val = JxlBits.UnpackSigned(v);

        // if it overflows, it overflows, and we have a problem anyway
        return unchecked((int)((val * multiplier) + offset));
    }
}
