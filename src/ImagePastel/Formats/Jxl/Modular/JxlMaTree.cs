// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/encoding/dec_ma.h, lib/jxl/modular/encoding/dec_ma.cc and
// lib/jxl/modular/encoding/ma_common.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;

namespace SixLabors.ImageSharp.Formats.Jxl.Modular;

/// <summary>Decodes MA trees.</summary>
internal static class JxlMaTree
{
    /// <summary>The largest MA tree.</summary>
    public const int MaxTreeSize = 1 << 22;

    /// <summary>The context of split values in the tree's own entropy-coded stream.</summary>
    public const int SplitValContext = 0;

    /// <summary>The context of properties.</summary>
    public const int PropertyContext = 1;

    /// <summary>The context of leaf predictors.</summary>
    public const int PredictorContext = 2;

    /// <summary>The context of leaf offsets.</summary>
    public const int OffsetContext = 3;

    /// <summary>The context of the log2 of leaf multipliers.</summary>
    public const int MultiplierLogContext = 4;

    /// <summary>The context of the remaining bits of leaf multipliers.</summary>
    public const int MultiplierBitsContext = 5;

    /// <summary>The number of contexts of the tree's stream.</summary>
    public const int NumTreeContexts = 6;

    private const int HeightLimit = 2048;

    private const int NumModularPredictors = (int)JxlPredictor.Average4 + 1;

    private enum NextAction
    {
        CheckAndGoLeft,
        GoRight,
        Pop,
    }

    /// <summary>
    /// Reads a tree of at most min(<paramref name="treeSizeLimit"/>, <see cref="MaxTreeSize"/>) nodes.
    /// Throws <see cref="InvalidImageContentException"/> for an invalid or too large tree.
    /// </summary>
    /// <param name="br">The reader.</param>
    /// <param name="treeSizeLimit">The node limit for this stream.</param>
    /// <returns>The tree.</returns>
    public static List<JxlPropertyDecisionNode> DecodeTree(JxlBitReader br, long treeSizeLimit)
    {
        JxlAnsCode treeCode = new();
        byte[] treeContextMap = JxlEntropyDecoder.DecodeHistograms(br, NumTreeContexts, treeCode);

        // TODO(eustas): investigate more infinite tree cases.
        if (treeCode.DegenerateSymbols[treeContextMap[PropertyContext]] > 0)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Infinite tree");
        }

        JxlAnsSymbolReader reader = new(treeCode, br);
        List<JxlPropertyDecisionNode> tree = DecodeTree(br, reader, treeContextMap, System.Math.Min(treeSizeLimit, MaxTreeSize));
        if (!reader.CheckAnsFinalState)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("ANS decode final state failed");
        }

        return tree;
    }

    private static List<JxlPropertyDecisionNode> DecodeTree(JxlBitReader br, JxlAnsSymbolReader reader, byte[] contextMap, long treeSizeLimit)
    {
        uint leafId = 0;
        long toDecode = 1;
        List<JxlPropertyDecisionNode> tree = new();
        while (toDecode > 0)
        {
            br.EnsureAllReadsWithinBounds("MA tree");
            if (tree.Count > treeSizeLimit)
            {
                JxlThrowHelper.ThrowInvalidImageContentException($"Tree is too large: {tree.Count} nodes vs {treeSizeLimit} max nodes");
            }

            toDecode--;
            uint prop1 = reader.ReadHybridUint(PropertyContext, br, contextMap);
            if (prop1 > 256)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid tree property value");
            }

            int property = (int)prop1 - 1;
            if (property == -1)
            {
                uint predictor = reader.ReadHybridUint(PredictorContext, br, contextMap);
                if (predictor >= NumModularPredictors)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid predictor");
                }

                long predictorOffset = JxlBits.UnpackSigned(reader.ReadHybridUint(OffsetContext, br, contextMap));
                uint mulLog = reader.ReadHybridUint(MultiplierLogContext, br, contextMap);
                if (mulLog >= 31)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid multiplier logarithm");
                }

                uint mulBits = reader.ReadHybridUint(MultiplierBitsContext, br, contextMap);
                if (mulBits >= (1u << (31 - (int)mulLog)) - 1u)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid multiplier");
                }

                uint multiplier = (mulBits + 1u) << (int)mulLog;
                tree.Add(new JxlPropertyDecisionNode(-1, 0, leafId, 0, (JxlPredictor)predictor, predictorOffset, multiplier));
                leafId++;
                continue;
            }

            int splitVal = JxlBits.UnpackSigned(reader.ReadHybridUint(SplitValContext, br, contextMap));
            tree.Add(new JxlPropertyDecisionNode(property, splitVal, (uint)(tree.Count + toDecode + 1), (uint)(tree.Count + toDecode + 2), JxlPredictor.Zero, 0, 1));
            toDecode += 2;
        }

        ValidateTree(tree);
        return tree;
    }

    // Checks that every split narrows the range of its property, walking the tree with an explicit
    // stack so a deep tree cannot overflow the call stack.
    private static void ValidateTree(List<JxlPropertyDecisionNode> tree)
    {
        if (tree.Count == 0)
        {
            return;
        }

        int numProperties = 0;
        foreach (JxlPropertyDecisionNode node in tree)
        {
            if (node.Property >= numProperties)
            {
                numProperties = node.Property + 1;
            }
        }

        int[] rangeFirst = new int[numProperties];
        int[] rangeSecond = new int[numProperties];
        for (int i = 0; i < numProperties; i++)
        {
            rangeFirst[i] = int.MinValue;
            rangeSecond[i] = int.MaxValue;
        }

        List<(int NodeIndex, int OrigL, int OrigU, NextAction Action)> stack = new()
        {
            (0, 0, 0, NextAction.CheckAndGoLeft),
        };

        while (stack.Count > 0)
        {
            if (stack.Count >= HeightLimit)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Tree too tall");
            }

            int top = stack.Count - 1;
            (int nodeIndex, int origL, int origU, NextAction action) = stack[top];
            JxlPropertyDecisionNode node = tree[nodeIndex];
            switch (action)
            {
                case NextAction.CheckAndGoLeft:
                {
                    int p = node.Property;
                    if (p == -1)
                    {
                        stack.RemoveAt(top);
                        continue;
                    }

                    int v = node.SplitVal;
                    int l = rangeFirst[p];
                    int u = rangeSecond[p];
                    if (l > v || u <= v)
                    {
                        JxlThrowHelper.ThrowInvalidImageContentException("Invalid tree");
                    }

                    stack[top] = (nodeIndex, l, u, NextAction.GoRight);
                    rangeFirst[p] = node.SplitVal + 1;
                    stack.Add(((int)node.LChild, 0, 0, NextAction.CheckAndGoLeft));
                    continue;
                }

                case NextAction.GoRight:
                    stack[top] = (nodeIndex, origL, origU, NextAction.Pop);
                    rangeFirst[node.Property] = origL;
                    rangeSecond[node.Property] = node.SplitVal;
                    stack.Add(((int)node.RChild, 0, 0, NextAction.CheckAndGoLeft));
                    continue;

                default:
                    rangeSecond[node.Property] = origU;
                    stack.RemoveAt(top);
                    continue;
            }
        }
    }
}
