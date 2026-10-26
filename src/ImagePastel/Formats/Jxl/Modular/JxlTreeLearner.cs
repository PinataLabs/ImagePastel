// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/encoding/enc_ma.cc (EstimateBits, MakeSplitNode, BoxIntersects, SplitTreeSamples,
// CollectExtraBitsIncrease, FindBestSplit, ComputeBestTree, CollectPixelSamples).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Entropy;

namespace PinataLabs.ImagePastel.Formats.Jxl.Modular;

/// <summary>
/// Learns an MA tree from <see cref="JxlTreeSamples"/>: each leaf is split greedily on the property and
/// value that save the most estimated bits, while the saving is above a threshold.
/// </summary>
internal static class JxlTreeLearner
{
    private const int NumStaticProperties = JxlModularPredict.NumStaticProperties;

    private enum IntersectionType
    {
        None,
        Partial,
        Inside,
    }

    /// <summary>
    /// Learns a tree from <paramref name="treeSamples"/>, starting from one leaf with the first
    /// predictor tried.
    /// </summary>
    /// <param name="treeSamples">The samples; reordered as the tree splits them.</param>
    /// <param name="threshold">The bits a split must save.</param>
    /// <param name="mulInfo">The multiplier boxes, which force splits.</param>
    /// <param name="staticPropRange">The channels and streams the tree covers.</param>
    /// <param name="fastDecodeMultiplier">How much costlier a split that is faster to decode may be.</param>
    /// <returns>The tree, with children indexed into it; leaf ids are set when it is tokenised.</returns>
    public static List<JxlPropertyDecisionNode> ComputeBestTree(JxlTreeSamples treeSamples, float threshold, List<JxlModularMultiplierInfo> mulInfo, JxlStaticPropRange staticPropRange, float fastDecodeMultiplier)
    {
        // TODO(veluca): take into account that different contexts can have different uint configs.
        // Initialize tree.
        List<JxlPropertyDecisionNode> tree = new() { Leaf(treeSamples.PredictorFromIndex(0)) };
        if (treeSamples.NumProperties >= 64)
        {
            JxlThrowHelper.ThrowImageFormatException("Too many properties to learn a tree");
        }

        FindBestSplit(treeSamples, threshold, mulInfo, staticPropRange, fastDecodeMultiplier, tree);
        return tree;
    }

    /// <summary>
    /// Samples about a tenth of the pixels that tree learning will see, for the property thresholds,
    /// and counts the pixels of each channel and stream. The sampling is a geometric skip over the
    /// pixels of every channel that is coded in this stream, seeded with the stream id.
    /// </summary>
    /// <param name="image">The stream's image.</param>
    /// <param name="options">The stream's options.</param>
    /// <param name="groupId">The stream id.</param>
    /// <param name="groupPixelCount">The pixel count of each stream, to add to.</param>
    /// <param name="channelPixelCount">The pixel count of each channel, to add to.</param>
    /// <param name="pixelSamples">The sampled pixel values, to add to.</param>
    /// <param name="diffSamples">The sampled differences to the left neighbour, to add to.</param>
    public static void CollectPixelSamples(JxlModularImage image, JxlModularOptions options, uint groupId, List<uint> groupPixelCount, List<uint> channelPixelCount, List<int> pixelSamples, List<int> diffSamples)
    {
        if (options.NbRepeats == 0)
        {
            return;
        }

        while (groupPixelCount.Count <= groupId)
        {
            groupPixelCount.Add(0);
        }

        while (channelPixelCount.Count < image.Channel.Count)
        {
            channelPixelCount.Add(0);
        }

        JxlRng rng = new(groupId);

        // Sample 10% of the final number of samples for property quantization.
        float fraction = (float)Math.Min(options.NbRepeats * 0.1, 0.99);
        float dist = JxlRng.MakeGeometric(fraction);
        List<int> channelIds = new();
        for (int c = 0; c < image.Channel.Count; c++)
        {
            JxlModularChannel ch = image.Channel[c];
            if (c >= image.NbMetaChannels && (ch.W > options.MaxChanSize || ch.H > options.MaxChanSize))
            {
                break;
            }

            if (ch.W <= 1 || ch.H == 0)
            {
                continue; // skip empty or width-1 channels.
            }

            channelIds.Add(c);
            uint pixels = unchecked((uint)ch.W * (uint)ch.H);
            groupPixelCount[(int)groupId] += pixels;
            channelPixelCount[c] += pixels;
        }

        if (channelIds.Count == 0)
        {
            return;
        }

        int i = 0;
        int y = 0;
        ulong x = 0;
        for (bool more = Advance(image, channelIds, rng.Geometric(dist), ref i, ref y, ref x);
             more;
             more = Advance(image, channelIds, unchecked(rng.Geometric(dist) + 1u), ref i, ref y, ref x))
        {
            Span<int> row = image.Channel[channelIds[i]].Row(y);
            int xi = (int)x;
            pixelSamples.Add(row[xi]);
            int xp = xi == 0 ? 1 : xi - 1;
            diffSamples.Add(unchecked((int)((long)row[xi] - row[xp])));
        }
    }

    // libjxl's advance lambda in CollectPixelSamples: moves the sample position (channel i, row y,
    // column x) on by amount pixels. Returns false once it runs past the last channel.
    private static bool Advance(JxlModularImage image, List<int> channelIds, ulong amount, ref int i, ref int y, ref ulong x)
    {
        x += amount;

        // Detect row overflow (rare).
        while (x >= (ulong)image.Channel[channelIds[i]].W)
        {
            x -= (ulong)image.Channel[channelIds[i]].W;
            y++;

            // Detect end-of-channel (even rarer).
            if (y == image.Channel[channelIds[i]].H)
            {
                i++;
                y = 0;
                if (i >= channelIds.Count)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Computes the entropy of the histogram, taking into account the minimum probability for symbols
    /// with non-zero counts.
    /// </summary>
    /// <remarks>
    /// libjxl pads the histogram to a multiple of the SIMD width and sums per lane; padding adds
    /// symbols with no count, which add no bits. This port sums in the order of the 8 float lanes of
    /// AVX2, which cjxl runs on x64: symbol i goes to lane i % 8, and the lanes are added as Highway's
    /// <c>SumOfLanes</c> adds them (see <see cref="JxlAvx2Lanes"/>). The integer total does not depend on the order. The estimate can
    /// still differ from cjxl's in the last bits, because <see cref="JxlFastMath.FastLog2f"/> does not
    /// fuse its multiply-adds as libjxl's does on AVX2 (ADR 0004).
    /// </remarks>
    /// <param name="counts">The counts.</param>
    /// <param name="offset">The index of the first symbol's count.</param>
    /// <param name="numSymbols">The number of symbols.</param>
    /// <returns>The estimate, in bits.</returns>
    internal static float EstimateBits(int[] counts, int offset, int numSymbols)
    {
        int total = 0;
        for (int i = 0; i < numSymbols; i++)
        {
            total += counts[offset + i];
        }

        const float minprob = 1.0f / JxlAnsParams.AnsTabSize;
        float invTotal = 1.0f / total;
        Span<float> bitsLanes = stackalloc float[JxlAvx2Lanes.Count];
        bitsLanes.Clear();
        for (int i = 0; i < numSymbols; i++)
        {
            int count = counts[offset + i];
            float countF = count;
            float probs = countF * invTotal;
            float mprobs = Math.Max(probs, minprob);
            float nbps = count == total ? 0 : JxlFastMath.FastLog2f(mprobs);
            bitsLanes[i & (JxlAvx2Lanes.Count - 1)] -= countF * nbps;
        }

        return JxlAvx2Lanes.SumOfLanes(bitsLanes);
    }

    private static JxlPropertyDecisionNode Leaf(JxlPredictor predictor) => new(-1, 0, 0, 0, predictor, 0, 1);

    private static void MakeSplitNode(int pos, int property, int splitval, JxlPredictor lpred, long loff, JxlPredictor rpred, long roff, List<JxlPropertyDecisionNode> tree)
    {
        // Note that the tree splits on *strictly greater*.
        JxlPropertyDecisionNode node = tree[pos];
        node.LChild = (uint)tree.Count;
        node.RChild = (uint)tree.Count + 1;
        node.SplitVal = splitval;
        node.Property = (short)property;
        tree[pos] = node;
        tree.Add(new JxlPropertyDecisionNode(-1, 0, 0, 0, rpred, roff, 1));
        tree.Add(new JxlPropertyDecisionNode(-1, 0, 0, 0, lpred, loff, 1));
    }

    private static IntersectionType BoxIntersects(JxlStaticPropRange needle, JxlStaticPropRange haystack, out int partialAxis, out uint partialVal)
    {
        partialAxis = 0;
        partialVal = 0;
        bool partial = false;
        for (int i = 0; i < NumStaticProperties; i++)
        {
            if (haystack[i, 0] >= needle[i, 1])
            {
                return IntersectionType.None;
            }

            if (haystack[i, 1] <= needle[i, 0])
            {
                return IntersectionType.None;
            }

            if (haystack[i, 0] <= needle[i, 0] && haystack[i, 1] >= needle[i, 1])
            {
                continue;
            }

            partial = true;
            partialAxis = i;
            if (haystack[i, 0] > needle[i, 0] && haystack[i, 0] < needle[i, 1])
            {
                partialVal = haystack[i, 0] - 1;
            }
            else
            {
                DebugGuard.IsTrue(haystack[i, 1] > needle[i, 0] && haystack[i, 1] < needle[i, 1], "The box cuts the range");
                partialVal = haystack[i, 1] - 1;
            }
        }

        return partial ? IntersectionType.Partial : IntersectionType.Inside;
    }

    // Moves the samples in [begin, end) whose property is at most val before pos, and the others after.
    private static void SplitTreeSamples(JxlTreeSamples treeSamples, bool isStatic, int begin, int pos, int end, int prop, uint val)
    {
        int beginPos = begin;
        int endPos = pos;
        do
        {
            while (beginPos < pos && treeSamples.Property(isStatic, prop, beginPos) <= val)
            {
                ++beginPos;
            }

            while (endPos < end && treeSamples.Property(isStatic, prop, endPos) > val)
            {
                ++endPos;
            }

            if (beginPos < pos && endPos < end)
            {
                treeSamples.Swap(beginPos, endPos);
            }

            ++beginPos;
            ++endPos;
        }
        while (beginPos < pos && endPos < end);
    }

    private static void CollectExtraBitsIncrease(JxlTreeSamples treeSamples, bool isStatic, List<JxlTreeSamples.ResidualToken> rtokens, int[] countIncrease, ulong[] extraBitsIncrease, int begin, int end, int propIdx, int maxSymbols)
    {
        for (int i2 = begin; i2 < end; i2++)
        {
            JxlTreeSamples.ResidualToken rt = rtokens[i2];
            int cnt = treeSamples.Count(i2);
            int p = treeSamples.Property(isStatic, propIdx, i2);
            int sym = rt.Tok;
            ulong ebi = (ulong)rt.NBits * (ulong)cnt;
            countIncrease[(p * maxSymbols) + sym] += cnt;
            extraBitsIncrease[p] += ebi;
        }
    }

    private static void FindBestSplit(JxlTreeSamples treeSamples, float threshold, List<JxlModularMultiplierInfo> mulInfo, JxlStaticPropRange initialStaticPropRange, float fastDecodeMultiplier, List<JxlPropertyDecisionNode> tree)
    {
        List<NodeInfo> nodes = new() { new NodeInfo(0, 0, treeSamples.NumDistinctSamples, initialStaticPropRange) };

        int numPredictors = treeSamples.NumPredictors;
        int numProperties = treeSamples.NumProperties;

        // TODO(veluca): consider parallelizing the search (processing multiple nodes at a time).
        while (nodes.Count > 0)
        {
            NodeInfo node = nodes[^1];
            nodes.RemoveAt(nodes.Count - 1);
            int pos = node.Pos;
            int begin = node.Begin;
            int end = node.End;
            JxlStaticPropRange staticPropRange = node.StaticPropRange;
            if (begin == end)
            {
                continue;
            }

            SplitInfo bestSplitStaticConstant = new();
            SplitInfo bestSplitStatic = new();
            SplitInfo bestSplitNonstatic = new();
            SplitInfo bestSplitNowp = new();

            DebugGuard.IsTrue(begin <= end && end <= treeSamples.NumDistinctSamples, "The node's samples are in range");

            // Compute the maximum token in the range.
            int maxSymbols = 0;
            for (int pred = 0; pred < numPredictors; pred++)
            {
                for (int i = begin; i < end; i++)
                {
                    int tok = treeSamples.Token(pred, i);
                    maxSymbols = maxSymbols > tok + 1 ? maxSymbols : tok + 1;
                }
            }

            int[] counts = new int[maxSymbols * numPredictors];
            uint[] totExtraBits = new uint[numPredictors];
            for (int pred = 0; pred < numPredictors; pred++)
            {
                ulong extraBits = 0;
                List<JxlTreeSamples.ResidualToken> rtokens = treeSamples.RTokens(pred);
                for (int i = begin; i < end; i++)
                {
                    JxlTreeSamples.ResidualToken rt = rtokens[i];
                    int count = treeSamples.Count(i);
                    ulong eb = (ulong)rt.NBits * (ulong)count;
                    counts[(pred * maxSymbols) + rt.Tok] += count;
                    extraBits += eb;
                }

                totExtraBits[pred] = unchecked((uint)extraBits);
            }

            float baseBits;
            {
                int pred = treeSamples.PredictorIndex(tree[pos].Predictor);
                baseBits = EstimateBits(counts, pred * maxSymbols, maxSymbols) + totExtraBits[pred];
            }

            SplitInfo best = bestSplitNonstatic;

            SplitInfo forcedSplit = new();

            // The multiplier ranges cut halfway through the current ranges of static properties. We
            // do this even if the current node is not a leaf, to minimize the number of nodes in the
            // resulting tree.
            foreach (JxlModularMultiplierInfo mmi in mulInfo)
            {
                IntersectionType t = BoxIntersects(staticPropRange, mmi.Range, out int axis, out uint val);
                if (t == IntersectionType.None)
                {
                    continue;
                }

                if (t == IntersectionType.Inside)
                {
                    JxlPropertyDecisionNode n = tree[pos];
                    n.Multiplier = mmi.Multiplier;
                    tree[pos] = n;
                    break;
                }

                if (t == IntersectionType.Partial)
                {
                    DebugGuard.IsTrue(axis < NumStaticProperties, "The axis is a static property");
                    forcedSplit.Val = treeSamples.QuantizeStaticProperty(axis, (int)val);
                    forcedSplit.Prop = axis;
                    forcedSplit.LCost = forcedSplit.RCost = (baseBits / 2) - threshold;
                    forcedSplit.LPred = forcedSplit.RPred = tree[pos].Predictor;
                    best = forcedSplit;
                    best.Pos = begin;
                    if (best.Prop < treeSamples.NumStaticProps)
                    {
                        for (int x = begin; x < end; x++)
                        {
                            if (treeSamples.Property(true, best.Prop, x) <= best.Val)
                            {
                                best.Pos++;
                            }
                        }
                    }
                    else
                    {
                        int prop = best.Prop - treeSamples.NumStaticProps;
                        for (int x = begin; x < end; x++)
                        {
                            if (treeSamples.Property(false, prop, x) <= best.Val)
                            {
                                best.Pos++;
                            }
                        }
                    }

                    break;
                }
            }

            if (best != forcedSplit)
            {
                int[] countIncrease = Array.Empty<int>();
                ulong[] extraBitsIncrease = Array.Empty<ulong>();

                // For each property, compute which of its values are used, and what tokens correspond
                // to those usages. Then, iterate through the values, and compute the entropy of each
                // side of the split (of the form `prop > threshold`). Finally, find the split that
                // minimizes the cost.
                int[] countsAbove = new int[maxSymbols];
                int[] countsBelow = new int[maxSymbols];

                // The lower the threshold, the higher the expected noisiness of the estimate. Thus,
                // discourage changing predictors.
                float changePredPenalty = 800.0f / (100.0f + threshold);
                for (int prop = 0; prop < numProperties && baseBits > threshold; prop++)
                {
                    bool isStatic = prop < treeSamples.NumStaticProps;
                    int propIdx = isStatic ? prop : prop - treeSamples.NumStaticProps;
                    int propSize = treeSamples.NumPropertyValues(prop);
                    if (extraBitsIncrease.Length < propSize)
                    {
                        Array.Resize(ref countIncrease, propSize * maxSymbols);
                        Array.Resize(ref extraBitsIncrease, propSize);
                    }

                    // Clear prop_value_used_count (which cannot be cleared "on the go")
                    int[] propValueUsedCount = new int[propSize];

                    int firstUsed = propSize;
                    int lastUsed = 0;

                    // TODO(veluca): consider finding multiple splits along a single property at the
                    // same time, possibly with a bottom-up approach.
                    for (int i = begin; i < end; i++)
                    {
                        int p = treeSamples.Property(isStatic, propIdx, i);
                        propValueUsedCount[p]++;
                        lastUsed = Math.Max(lastUsed, p);
                        firstUsed = Math.Min(firstUsed, p);
                    }

                    CostInfo[] costsL = NewCosts(lastUsed - firstUsed);
                    CostInfo[] costsR = NewCosts(lastUsed - firstUsed);

                    // For all predictors, compute the right and left costs of each split.
                    for (int pred = 0; pred < numPredictors; pred++)
                    {
                        // Compute cost and histogram increments for each property value.
                        List<JxlTreeSamples.ResidualToken> rtokens = treeSamples.RTokens(pred);
                        CollectExtraBitsIncrease(treeSamples, isStatic, rtokens, countIncrease, extraBitsIncrease, begin, end, propIdx, maxSymbols);
                        Array.Copy(counts, pred * maxSymbols, countsAbove, 0, maxSymbols);
                        countsBelow.AsSpan().Clear();
                        ulong extraBitsBelow = 0;

                        // Exclude last used: this ensures neither counts_above nor counts_below is empty.
                        for (int i = firstUsed; i < lastUsed; i++)
                        {
                            if (propValueUsedCount[i] == 0)
                            {
                                continue;
                            }

                            extraBitsBelow += extraBitsIncrease[i];

                            // The increase for this property value has been used, and will not be used
                            // again: clear it. Also below.
                            extraBitsIncrease[i] = 0;
                            for (int sym = 0; sym < maxSymbols; sym++)
                            {
                                countsAbove[sym] -= countIncrease[(i * maxSymbols) + sym];
                                countsBelow[sym] += countIncrease[(i * maxSymbols) + sym];
                                countIncrease[(i * maxSymbols) + sym] = 0;
                            }

                            float rcost = EstimateBits(countsAbove, 0, maxSymbols) + totExtraBits[pred] - extraBitsBelow;
                            float lcost = EstimateBits(countsBelow, 0, maxSymbols) + extraBitsBelow;
                            DebugGuard.IsTrue(extraBitsBelow <= totExtraBits[pred], "The extra bits below are part of the total");
                            float penalty = 0;

                            // Never discourage moving away from the Weighted predictor.
                            JxlPredictor predictor = treeSamples.PredictorFromIndex(pred);
                            if (predictor != tree[pos].Predictor && tree[pos].Predictor != JxlPredictor.Weighted)
                            {
                                penalty = changePredPenalty;
                            }

                            // If everything else is equal, disfavour Weighted (slower) and favour Zero
                            // (faster if it's the only predictor used in a group+channel combination)
                            if (predictor == JxlPredictor.Weighted)
                            {
                                penalty += 1e-8f;
                            }

                            if (predictor == JxlPredictor.Zero)
                            {
                                penalty -= 1e-8f;
                            }

                            ref CostInfo r = ref costsR[i - firstUsed];
                            if (rcost + penalty < r.Cost)
                            {
                                r.CostBits = rcost;
                                r.ExtraCost = penalty;
                                r.Pred = predictor;
                            }

                            ref CostInfo l = ref costsL[i - firstUsed];
                            if (lcost + penalty < l.Cost)
                            {
                                l.CostBits = lcost;
                                l.ExtraCost = penalty;
                                l.Pred = predictor;
                            }
                        }
                    }

                    // Iterate through the possible splits and find the one with minimum sum of costs
                    // of the two sides.
                    int split = begin;
                    for (int i = firstUsed; i < lastUsed; i++)
                    {
                        if (propValueUsedCount[i] == 0)
                        {
                            continue;
                        }

                        split += propValueUsedCount[i];
                        float rcost = costsR[i - firstUsed].CostBits;
                        float lcost = costsL[i - firstUsed].CostBits;

                        bool usesWp = treeSamples.PropertyFromIndex(prop) == JxlModularPredict.WPProp ||
                            costsL[i - firstUsed].Pred == JxlPredictor.Weighted ||
                            costsR[i - firstUsed].Pred == JxlPredictor.Weighted;
                        bool zeroEntropySide = rcost == 0 || lcost == 0;

                        SplitInfo bestRef = treeSamples.PropertyFromIndex(prop) < NumStaticProperties
                            ? (zeroEntropySide ? bestSplitStaticConstant : bestSplitStatic)
                            : (usesWp ? bestSplitNonstatic : bestSplitNowp);
                        if (lcost + rcost < bestRef.Cost)
                        {
                            bestRef.Prop = prop;
                            bestRef.Val = (uint)i;
                            bestRef.Pos = split;
                            bestRef.LCost = lcost;
                            bestRef.LPred = costsL[i - firstUsed].Pred;
                            bestRef.RCost = rcost;
                            bestRef.RPred = costsR[i - firstUsed].Pred;
                        }
                    }

                    // Clear extra_bits_increase and cost_increase for last_used.
                    extraBitsIncrease[lastUsed] = 0;
                    Array.Clear(countIncrease, lastUsed * maxSymbols, maxSymbols);
                }

                // Try to avoid introducing WP.
                if (bestSplitNowp.Cost + threshold < baseBits && bestSplitNowp.Cost <= fastDecodeMultiplier * best.Cost)
                {
                    best = bestSplitNowp;
                }

                // Split along static props if possible and not significantly more expensive.
                if (bestSplitStatic.Cost + threshold < baseBits && bestSplitStatic.Cost <= fastDecodeMultiplier * best.Cost)
                {
                    best = bestSplitStatic;
                }

                // Split along static props to create constant nodes if possible.
                if (bestSplitStaticConstant.Cost + threshold < baseBits)
                {
                    best = bestSplitStaticConstant;
                }
            }

            if (best.Cost + threshold < baseBits)
            {
                uint p = treeSamples.PropertyFromIndex(best.Prop);
                int dequant = treeSamples.UnquantizeProperty(best.Prop, best.Val);

                // Split node and try to split children.
                MakeSplitNode(pos, (int)p, dequant, best.LPred, 0, best.RPred, 0, tree);

                // "Sort" according to winning property
                bool isStatic = best.Prop < treeSamples.NumStaticProps;
                SplitTreeSamples(treeSamples, isStatic, begin, best.Pos, end, isStatic ? best.Prop : best.Prop - treeSamples.NumStaticProps, best.Val);

                JxlStaticPropRange newSpRange = staticPropRange;
                if (p < NumStaticProperties)
                {
                    DebugGuard.IsTrue((uint)(dequant + 1) <= newSpRange[(int)p, 1], "The split is inside the range");
                    newSpRange[(int)p, 1] = (uint)(dequant + 1);
                }

                nodes.Add(new NodeInfo((int)tree[pos].RChild, begin, best.Pos, newSpRange));
                newSpRange = staticPropRange;
                if (p < NumStaticProperties)
                {
                    DebugGuard.IsTrue(newSpRange[(int)p, 0] <= (uint)(dequant + 1), "The split is inside the range");
                    newSpRange[(int)p, 0] = (uint)(dequant + 1);
                }

                nodes.Add(new NodeInfo((int)tree[pos].LChild, best.Pos, end, newSpRange));
            }
        }
    }

    private static CostInfo[] NewCosts(int count)
    {
        CostInfo[] costs = new CostInfo[count];
        Array.Fill(costs, new CostInfo { CostBits = float.MaxValue });
        return costs;
    }

    private readonly struct NodeInfo
    {
        public NodeInfo(int pos, int begin, int end, JxlStaticPropRange staticPropRange)
        {
            this.Pos = pos;
            this.Begin = begin;
            this.End = end;
            this.StaticPropRange = staticPropRange;
        }

        public int Pos { get; }

        public int Begin { get; }

        public int End { get; }

        public JxlStaticPropRange StaticPropRange { get; }
    }

    private struct CostInfo
    {
        public float CostBits;

        public float ExtraCost;

        // will be uninitialized in some cases, but never used.
        public JxlPredictor Pred;

        public readonly float Cost => this.CostBits + this.ExtraCost;
    }

    // A candidate split; libjxl keeps pointers to these, so they are a class here.
    private sealed class SplitInfo
    {
        public int Prop { get; set; }

        public uint Val { get; set; }

        public int Pos { get; set; }

        public float LCost { get; set; } = float.MaxValue;

        public float RCost { get; set; } = float.MaxValue;

        public JxlPredictor LPred { get; set; } = JxlPredictor.Zero;

        public JxlPredictor RPred { get; set; } = JxlPredictor.Zero;

        public float Cost => this.LCost + this.RCost;
    }
}
