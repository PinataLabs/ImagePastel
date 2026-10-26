// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/encoding/context_predict.h (namespace weighted: Header, State).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;

namespace SixLabors.ImageSharp.Formats.Jxl.Modular;

/// <summary>
/// The state of the self-correcting weighted predictor while it walks a channel: it mixes four
/// sub-predictions, weighted by their recent errors.
/// </summary>
internal sealed class JxlWeightedState
{
    /// <summary>The number of sub-predictors.</summary>
    public const int NumPredictors = 4;

    /// <summary>The number of properties the predictor contributes to the MA tree.</summary>
    public const int NumProperties = 1;

    private const int PredExtraBits = 3;

    private const long PredictionRound = ((1 << PredExtraBits) >> 1) - 1;

    private static readonly uint[] DivLookup =
    {
        16777216, 8388608, 5592405, 4194304, 3355443, 2796202, 2396745, 2097152,
        1864135, 1677721, 1525201, 1398101, 1290555, 1198372, 1118481, 1048576,
        986895, 932067, 883011, 838860, 798915, 762600, 729444, 699050,
        671088, 645277, 621378, 599186, 578524, 559240, 541200, 524288,
        508400, 493447, 479349, 466033, 453438, 441505, 430185, 419430,
        409200, 399457, 390167, 381300, 372827, 364722, 356962, 349525,
        342392, 335544, 328965, 322638, 316551, 310689, 305040, 299593,
        294337, 289262, 284359, 279620, 275036, 270600, 266305, 262144,
    };

    private readonly long[] prediction = new long[NumPredictors];

    private readonly uint[][] predErrors = new uint[NumPredictors][];

    private readonly int[] error;

    private readonly JxlWeightedHeader header;

    // The prediction *before* removing the added bits.
    private long pred;

    public JxlWeightedState(JxlWeightedHeader header, int xsize)
    {
        this.header = header;
        for (int i = 0; i < NumPredictors; i++)
        {
            this.predErrors[i] = new uint[(xsize + 2) * 2];
        }

        this.error = new int[(xsize + 2) * 2];
    }

    /// <summary>
    /// Predicts the sample at (<paramref name="x"/>, <paramref name="y"/>) from its neighbours, and
    /// stores the predictor's property at <paramref name="offset"/> if <paramref name="computeProperties"/>.
    /// </summary>
    /// <param name="computeProperties">Whether to compute the property.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="xsize">The channel width.</param>
    /// <param name="n">The sample above.</param>
    /// <param name="w">The sample to the left.</param>
    /// <param name="ne">The sample above right.</param>
    /// <param name="nw">The sample above left.</param>
    /// <param name="nn">The sample two rows above.</param>
    /// <param name="properties">The properties.</param>
    /// <param name="offset">The index of the property.</param>
    /// <returns>The prediction.</returns>
    public long Predict(bool computeProperties, int x, int y, int xsize, long n, long w, long ne, long nw, long nn, int[] properties, int offset)
    {
        int curRow = (y & 1) != 0 ? 0 : (xsize + 2);
        int prevRow = (y & 1) != 0 ? (xsize + 2) : 0;
        int posN = prevRow + x;
        int posNE = x < xsize - 1 ? posN + 1 : posN;
        int posNW = x > 0 ? posN - 1 : posN;
        Span<uint> weights = stackalloc uint[NumPredictors];
        for (int i = 0; i < NumPredictors; i++)
        {
            uint[] e = this.predErrors[i];
            weights[i] = unchecked(e[posN] + e[posNE] + e[posNW]);
            weights[i] = ErrorWeight(weights[i], this.header.W[i]);
        }

        n = AddBits(n);
        w = AddBits(w);
        ne = AddBits(ne);
        nw = AddBits(nw);
        nn = AddBits(nn);

        long teW = x == 0 ? 0 : this.error[curRow + x - 1];
        long teN = this.error[posN];
        long teNW = this.error[posNW];
        long sumWN = teN + teW;
        long teNE = this.error[posNE];

        if (computeProperties)
        {
            long p = teW;
            if (Math.Abs(teN) > Math.Abs(p))
            {
                p = teN;
            }

            if (Math.Abs(teNW) > Math.Abs(p))
            {
                p = teNW;
            }

            if (Math.Abs(teNE) > Math.Abs(p))
            {
                p = teNE;
            }

            properties[offset] = unchecked((int)p);
        }

        unchecked
        {
            this.prediction[0] = w + ne - n;
            this.prediction[1] = n - (((sumWN + teNE) * this.header.P1C) >> 5);
            this.prediction[2] = w - (((sumWN + teNW) * this.header.P2C) >> 5);
            this.prediction[3] = n - (((teNW * this.header.P3Ca) + (teN * this.header.P3Cb) + (teNE * this.header.P3Cc) + ((nn - n) * this.header.P3Cd) + ((nw - w) * this.header.P3Ce)) >> 5);
        }

        this.pred = this.WeightedAverage(weights);

        // If all three have the same sign, skip clamping.
        if (((teN ^ teW) | (teN ^ teNW)) > 0)
        {
            return (this.pred + PredictionRound) >> PredExtraBits;
        }

        // Otherwise, clamp to min/max of neighbouring pixels (just W, NE, N).
        long mx = Math.Max(w, Math.Max(ne, n));
        long mn = Math.Min(w, Math.Min(ne, n));
        this.pred = Math.Max(mn, Math.Min(mx, this.pred));
        return (this.pred + PredictionRound) >> PredExtraBits;
    }

    /// <summary>Records the decoded sample <paramref name="val"/> so later predictions see its errors.</summary>
    /// <param name="val">The decoded sample.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="xsize">The channel width.</param>
    public void UpdateErrors(long val, int x, int y, int xsize)
    {
        int curRow = (y & 1) != 0 ? 0 : (xsize + 2);
        int prevRow = (y & 1) != 0 ? (xsize + 2) : 0;
        val = AddBits(val);
        this.error[curRow + x] = unchecked((int)(this.pred - val));
        for (int i = 0; i < NumPredictors; i++)
        {
            long err = (Math.Abs(this.prediction[i] - val) + PredictionRound) >> PredExtraBits;
            this.predErrors[i][curRow + x] = unchecked((uint)err);
            this.predErrors[i][prevRow + x + 1] = unchecked(this.predErrors[i][prevRow + x + 1] + (uint)err);
        }
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private static long AddBits(long x) => unchecked((long)((ulong)x << PredExtraBits));

    [MethodImpl(InliningOptions.ShortMethod)]
    private static uint ErrorWeight(ulong x, uint maxWeight)
    {
        int shift = JxlBits.FloorLog2Nonzero(x + 1) - 5;
        if (shift < 0)
        {
            shift = 0;
        }

        return 4 + ((maxWeight * DivLookup[x >> shift]) >> shift);
    }

    private long WeightedAverage(Span<uint> w)
    {
        uint weightSum = 0;
        for (int i = 0; i < NumPredictors; i++)
        {
            weightSum += w[i];
        }

        // At least 4.
        int logWeight = JxlBits.FloorLog2Nonzero(weightSum);
        weightSum = 0;
        for (int i = 0; i < NumPredictors; i++)
        {
            w[i] >>= logWeight - 4;
            weightSum += w[i];
        }

        // For rounding.
        long sum = (weightSum >> 1) - 1;
        for (int i = 0; i < NumPredictors; i++)
        {
            sum = unchecked(sum + (this.prediction[i] * w[i]));
        }

        return unchecked(sum * DivLookup[weightSum - 1]) >> 24;
    }
}
