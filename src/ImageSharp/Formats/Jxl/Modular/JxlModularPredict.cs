// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/encoding/context_predict.h (FlatDecisionNode, MATreeLookup, ClampedGradient, Select,
// PrecomputeReferences, InitPropsRow, PredictOne, Predict and its decoder wrappers, PredictLearnAll).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Formats.Jxl.Modular;

/// <summary>Predictors and context modelling of the Modular mode.</summary>
internal static class JxlModularPredict
{
    /// <summary>The number of static properties: channel and group id.</summary>
    public const int NumStaticProperties = 2;

    /// <summary>The number of properties each earlier channel adds.</summary>
    public const int ExtraPropsPerChannel = 4;

    /// <summary>The number of properties that do not reference earlier channels.</summary>
    public const int NumNonrefProperties = NumStaticProperties + 13 + JxlWeightedState.NumProperties;

    /// <summary>The weighted predictor's property.</summary>
    public const int WPProp = NumNonrefProperties - JxlWeightedState.NumProperties;

    /// <summary>The local gradient property.</summary>
    public const int GradientProp = 9;

    /// <summary>The number of predictors a decoder supports.</summary>
    public const int NumModularPredictors = (int)JxlPredictor.Average4 + 1;

    /// <summary>Returns the gradient top + left - topleft, clamped to the range of top and left.</summary>
    /// <param name="n">The sample above.</param>
    /// <param name="w">The sample to the left.</param>
    /// <param name="l">The sample above left.</param>
    /// <returns>The prediction.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static int ClampedGradient(int n, int w, int l)
    {
        int m = Math.Min(n, w);
        int mx = Math.Max(n, w);

        // The end result of this operation doesn't overflow or underflow if the result is between
        // m and M, but the intermediate value may overflow, so do the operation with unsigned ints.
        int grad = unchecked((int)((uint)n + (uint)w - (uint)l));
        int gradClampM = (l < m) ? mx : grad;
        return (l > mx) ? m : gradClampM;
    }

    /// <summary>Looks up the leaf of <paramref name="properties"/> in a flattened tree.</summary>
    /// <param name="nodes">The flattened tree.</param>
    /// <param name="properties">The properties of the sample.</param>
    /// <returns>The leaf.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static ref readonly JxlFlatDecisionNode Lookup(JxlFlatDecisionNode[] nodes, int[] properties)
    {
        uint pos = 0;
        while (true)
        {
            ref readonly JxlFlatDecisionNode node = ref nodes[pos];
            if (node.Property0 < 0)
            {
                return ref node;
            }

            bool p0 = properties[node.Property0] <= node.SplitVal0;
            uint off0 = properties[node.Properties0] <= node.SplitVals0 ? 1u : 0u;
            uint off1 = 2u | (properties[node.Properties1] <= node.SplitVals1 ? 1u : 0u);
            pos = node.ChildId + (p0 ? off1 : off0);
        }
    }

    /// <summary>
    /// Fills, for row <paramref name="y"/> of channel <paramref name="i"/>, the properties that come
    /// from earlier channels of the same size and shift: |v|, v, |v - gradient| and v - gradient.
    /// </summary>
    /// <param name="ch">The channel being decoded.</param>
    /// <param name="y">The row.</param>
    /// <param name="image">The image.</param>
    /// <param name="i">The index of the channel being decoded.</param>
    /// <param name="references">The properties of each column: <paramref name="numExtraProps"/> values per column.</param>
    /// <param name="numExtraProps">The number of reference properties per column.</param>
    public static void PrecomputeReferences(JxlModularChannel ch, int y, JxlModularImage image, int i, int[] references, int numExtraProps)
    {
        Array.Clear(references, 0, references.Length);
        int offset = 0;
        List<JxlModularChannel> channels = image.Channel;
        for (int j = i - 1; j >= 0 && offset < numExtraProps; j--)
        {
            if (channels[j].W != channels[i].W || channels[j].H != channels[i].H)
            {
                continue;
            }

            if (channels[j].HShift != channels[i].HShift)
            {
                continue;
            }

            if (channels[j].VShift != channels[i].VShift)
            {
                continue;
            }

            ReadOnlySpan<int> rpp = channels[j].Row(y);
            ReadOnlySpan<int> rpprev = channels[j].Row(y != 0 ? y - 1 : 0);
            int rp = offset;
            for (int x = 0; x < ch.W; x++, rp += numExtraProps)
            {
                long v = rpp[x];
                references[rp] = unchecked((int)Math.Abs(v));
                references[rp + 1] = (int)v;
                long vleft = x != 0 ? rpp[x - 1] : 0;
                long vtop = y != 0 ? rpprev[x] : vleft;
                long vtopleft = x != 0 && y != 0 ? rpprev[x - 1] : vleft;
                long vpredicted = ClampedGradient((int)vleft, (int)vtop, (int)vtopleft);
                references[rp + 2] = unchecked((int)Math.Abs(v - vpredicted));
                references[rp + 3] = unchecked((int)(v - vpredicted));
            }

            offset += ExtraPropsPerChannel;
        }
    }

    /// <summary>Sets the properties that are constant along a row.</summary>
    /// <param name="p">The properties.</param>
    /// <param name="channel">The channel index.</param>
    /// <param name="groupId">The group id.</param>
    /// <param name="y">The row.</param>
    public static void InitPropsRow(int[] p, int channel, int groupId, int y)
    {
        p[0] = channel;
        p[1] = groupId;
        p[2] = y;
        p[9] = 0; // local gradient.
    }

    /// <summary>
    /// Predicts a sample without a tree, as delta palettes do. With <paramref name="wpState"/> the
    /// weighted predictor runs too.
    /// </summary>
    /// <param name="w">The channel width.</param>
    /// <param name="row">The current row.</param>
    /// <param name="top">The row above, or empty for row 0.</param>
    /// <param name="topTop">The row two above, or empty for rows 0 and 1.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row index.</param>
    /// <param name="predictor">The predictor.</param>
    /// <param name="wpState">The weighted predictor state, or <see langword="null"/>.</param>
    /// <returns>The prediction.</returns>
    public static JxlPredictionResult PredictNoTree(int w, ReadOnlySpan<int> row, ReadOnlySpan<int> top, ReadOnlySpan<int> topTop, int x, int y, JxlPredictor predictor, JxlWeightedState wpState)
        => Predict(useTree: false, computeProperties: false, null, w, row, top, topTop, x, y, predictor, null, null, 0, wpState, default);

    /// <summary>
    /// Computes the properties of a sample, looks up its leaf in the tree and predicts it.
    /// With <paramref name="wpState"/> the weighted predictor runs too.
    /// </summary>
    /// <param name="p">The properties, partly set by <see cref="InitPropsRow"/>.</param>
    /// <param name="w">The channel width.</param>
    /// <param name="row">The current row.</param>
    /// <param name="top">The row above, or empty for row 0.</param>
    /// <param name="topTop">The row two above, or empty for rows 0 and 1.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row index.</param>
    /// <param name="tree">The flattened tree.</param>
    /// <param name="references">The reference properties of the row.</param>
    /// <param name="numExtraProps">The number of reference properties per column.</param>
    /// <param name="wpState">The weighted predictor state, or <see langword="null"/>.</param>
    /// <returns>The prediction.</returns>
    public static JxlPredictionResult PredictTree(int[] p, int w, ReadOnlySpan<int> row, ReadOnlySpan<int> top, ReadOnlySpan<int> topTop, int x, int y, JxlFlatDecisionNode[] tree, int[] references, int numExtraProps, JxlWeightedState wpState)
        => Predict(useTree: true, computeProperties: true, p, w, row, top, topTop, x, y, JxlPredictor.Zero, tree, references, numExtraProps, wpState, default);

    /// <summary>
    /// Computes the properties of a sample and the prediction of every predictor, as tree learning
    /// needs them. The weighted predictor always runs.
    /// </summary>
    /// <param name="p">The properties, partly set by <see cref="InitPropsRow"/>.</param>
    /// <param name="w">The channel width.</param>
    /// <param name="row">The current row.</param>
    /// <param name="top">The row above, or empty for row 0.</param>
    /// <param name="topTop">The row two above, or empty for rows 0 and 1.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row index.</param>
    /// <param name="references">The reference properties of the row.</param>
    /// <param name="numExtraProps">The number of reference properties per column.</param>
    /// <param name="wpState">The weighted predictor state.</param>
    /// <param name="predictions">The prediction of each predictor, indexed by predictor.</param>
    public static void PredictLearnAll(int[] p, int w, ReadOnlySpan<int> row, ReadOnlySpan<int> top, ReadOnlySpan<int> topTop, int x, int y, int[] references, int numExtraProps, JxlWeightedState wpState, Span<long> predictions)
        => Predict(useTree: false, computeProperties: true, p, w, row, top, topTop, x, y, JxlPredictor.Zero, null, references, numExtraProps, wpState, predictions);

    // libjxl specialises Predict on a mode bit set through a template; NoEdgeCases variants skip
    // the border checks and are only an optimisation, so the port keeps the checked form.
    private static JxlPredictionResult Predict(
        bool useTree,
        bool computeProperties,
        int[] p,
        int w,
        ReadOnlySpan<int> row,
        ReadOnlySpan<int> topRow,
        ReadOnlySpan<int> topTopRow,
        int x,
        int y,
        JxlPredictor predictor,
        JxlFlatDecisionNode[] tree,
        int[] references,
        int numExtraProps,
        JxlWeightedState wpState,
        Span<long> predictions)
    {
        int offset = 3;
        long left = x != 0 ? row[x - 1] : (y != 0 ? topRow[x] : 0);
        long top = y != 0 ? topRow[x] : left;
        long topleft = x != 0 && y != 0 ? topRow[x - 1] : left;
        long topright = x + 1 < w && y != 0 ? topRow[x + 1] : top;
        long leftleft = x > 1 ? row[x - 2] : left;
        long toptop = y > 1 ? topTopRow[x] : top;
        long toprightright = x + 2 < w && y != 0 ? topRow[x + 2] : topright;

        if (computeProperties)
        {
            unchecked
            {
                // location
                p[offset++] = x;

                // neighbors
                p[offset++] = (int)(top > 0 ? top : -top);
                p[offset++] = (int)(left > 0 ? left : -left);
                p[offset++] = (int)top;
                p[offset++] = (int)left;

                // local gradient
                p[offset] = (int)(left - p[offset + 1]);
                offset++;

                // local gradient
                p[offset++] = (int)(left + top - topleft);

                // FFV1 context properties
                p[offset++] = (int)(left - topleft);
                p[offset++] = (int)(topleft - top);
                p[offset++] = (int)(top - topright);
                p[offset++] = (int)(top - toptop);
                p[offset++] = (int)(left - leftleft);
            }
        }

        long wpPred = 0;
        if (wpState != null)
        {
            wpPred = wpState.Predict(computeProperties, x, y, w, top, left, topright, topleft, toptop, p, offset);
        }

        if (computeProperties)
        {
            offset += JxlWeightedState.NumProperties;
            Array.Copy(references, x * numExtraProps, p, offset, numExtraProps);
        }

        JxlPredictionResult result = default;
        if (useTree)
        {
            ref readonly JxlFlatDecisionNode lr = ref Lookup(tree, p);
            result.Context = (int)lr.ChildId;
            result.Guess = lr.PredictorOffset;
            result.Multiplier = lr.Multiplier;
            predictor = lr.Predictor;
        }

        for (int i = 0; i < predictions.Length; i++)
        {
            predictions[i] = PredictOne((JxlPredictor)i, left, top, toptop, topleft, topright, leftleft, toprightright, wpPred);
        }

        result.Guess += PredictOne(predictor, left, top, toptop, topleft, topright, leftleft, toprightright, wpPred);
        result.Predictor = predictor;
        return result;
    }

    private static long PredictOne(JxlPredictor p, long left, long top, long toptop, long topleft, long topright, long leftleft, long toprightright, long wpPred)
    {
        switch (p)
        {
            case JxlPredictor.Zero:
                return 0;
            case JxlPredictor.Left:
                return left;
            case JxlPredictor.Top:
                return top;
            case JxlPredictor.Select:
                return Select(left, top, topleft);
            case JxlPredictor.Weighted:
                return wpPred;
            case JxlPredictor.Gradient:
                return ClampedGradient(unchecked((int)left), unchecked((int)top), unchecked((int)topleft));
            case JxlPredictor.TopLeft:
                return topleft;
            case JxlPredictor.TopRight:
                return topright;
            case JxlPredictor.LeftLeft:
                return leftleft;
            case JxlPredictor.Average0:
                return (left + top) / 2;
            case JxlPredictor.Average1:
                return (left + topleft) / 2;
            case JxlPredictor.Average2:
                return (topleft + top) / 2;
            case JxlPredictor.Average3:
                return (top + topright) / 2;
            case JxlPredictor.Average4:
                return ((6 * top) - (2 * toptop) + (7 * left) + (1 * leftleft) + (1 * toprightright) + (3 * topright) + 8) / 16;
            default:
                return 0;
        }
    }

    private static long Select(long a, long b, long c)
    {
        long p = a + b - c;
        long pa = Math.Abs(p - a);
        long pb = Math.Abs(p - b);
        return pa < pb ? a : b;
    }
}
