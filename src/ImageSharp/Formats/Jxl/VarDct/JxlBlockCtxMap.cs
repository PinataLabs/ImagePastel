// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/ac_context.h (kCoeffFreqContext, kCoeffNumNonzeroContext, ZeroDensityContext, BlockCtxMap),
// lib/jxl/entropy_coder.h (PredictFromTopAndLeft, kDCThresholdDist, kQFThresholdDist) and
// lib/jxl/entropy_coder.cc (DecodeBlockCtxMap).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The block context map: which context the coefficients of a block use, from its channel, its
/// coefficient order, its quant field and the bucket of its quantised DC.
/// </summary>
internal sealed class JxlBlockCtxMap
{
    /// <summary>
    /// The number of buckets for the predicted number of non-zeros. It goes from 0 to 1008; the
    /// context is ceil(log2(predicted + 1)).
    /// </summary>
    public const int NonZeroBuckets = 37;

    /// <summary>Supremum of <see cref="ZeroDensityContext"/> + 1, when x + y &lt; 64.</summary>
    public const int ZeroDensityContextCount = 458;

    /// <summary>Supremum of <see cref="ZeroDensityContext"/> + 1.</summary>
    public const int ZeroDensityContextLimit = 474;

    /// <summary>The number of coefficient orders, one per size class.</summary>
    public const int NumOrders = 13;

    /// <summary>The distribution of the packed DC thresholds (libjxl's <c>kDCThresholdDist</c>).</summary>
    internal static readonly JxlU32Enc DcThresholdDist = new(Bits(4), BitsOffset(8, 16), BitsOffset(16, 272), BitsOffset(32, 65808));

    /// <summary>The distribution of the quant field thresholds less one (libjxl's <c>kQFThresholdDist</c>).</summary>
    internal static readonly JxlU32Enc QfThresholdDist = new(Bits(2), BitsOffset(3, 4), BitsOffset(5, 12), BitsOffset(8, 44));

    /// <summary>Initializes a new instance of the <see cref="JxlBlockCtxMap"/> class with the default map.</summary>
    public JxlBlockCtxMap() => this.SetDefault();

    /// <summary>Gets the quantised DC thresholds of each channel.</summary>
    public int[][] DcThresholds { get; } = { Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>() };

    /// <summary>Gets the quant field thresholds.</summary>
    public uint[] QfThresholds { get; private set; } = Array.Empty<uint>();

    /// <summary>Gets the map from block context to context.</summary>
    public byte[] CtxMap { get; private set; }

    /// <summary>Gets the number of distinct contexts.</summary>
    public int NumCtxs { get; private set; }

    /// <summary>Gets the number of DC buckets.</summary>
    public int NumDcCtxs { get; private set; }

    /// <summary>Gets the number of AC contexts: non-zero counts, then coefficient symbols.</summary>
    public int NumAcContexts => this.NumCtxs * (NonZeroBuckets + ZeroDensityContextCount);

    /// <summary>Gets a value indicating whether this is the default map, which is written as one bit.</summary>
    public bool IsDefault
        => this.DcThresholds[0].Length == 0 && this.DcThresholds[1].Length == 0 && this.DcThresholds[2].Length == 0
        && this.QfThresholds.Length == 0 && this.CtxMap.AsSpan().SequenceEqual(DefaultCtxMap);

    // Default ctx map clusters all the large transforms together.
    private static ReadOnlySpan<byte> DefaultCtxMap => new byte[]
    {
        0, 1, 2, 2, 3, 3, 4, 5, 6, 6, 6, 6, 6,
        7, 8, 9, 9, 10, 11, 12, 13, 14, 14, 14, 14, 14,
        7, 8, 9, 9, 10, 11, 12, 13, 14, 14, 14, 14, 14,
    };

    private static ReadOnlySpan<ushort> CoeffFreqContext => new ushort[]
    {
        0xBAD, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14,
        15, 15, 16, 16, 17, 17, 18, 18, 19, 19, 20, 20, 21, 21, 22, 22,
        23, 23, 23, 23, 24, 24, 24, 24, 25, 25, 25, 25, 26, 26, 26, 26,
        27, 27, 27, 27, 28, 28, 28, 28, 29, 29, 29, 29, 30, 30, 30, 30,
    };

    private static ReadOnlySpan<ushort> CoeffNumNonzeroContext => new ushort[]
    {
        0xBAD, 0, 31, 62, 62, 93, 93, 93, 93, 123, 123, 123, 123,
        152, 152, 152, 152, 152, 152, 152, 152, 180, 180, 180, 180, 180,
        180, 180, 180, 180, 180, 180, 180, 206, 206, 206, 206, 206, 206,
        206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206,
        206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206,
    };

    /// <summary>Reads the map from the DC global section.</summary>
    /// <param name="br">The reader.</param>
    public void Decode(JxlBitReader br)
    {
        bool isDefault = br.ReadBits(1) != 0;
        if (isDefault)
        {
            this.SetDefault();
            return;
        }

        this.NumDcCtxs = 1;
        for (int j = 0; j < 3; j++)
        {
            int[] dct = new int[br.ReadBits(4)];
            this.NumDcCtxs *= dct.Length + 1;
            for (int i = 0; i < dct.Length; i++)
            {
                dct[i] = JxlBits.UnpackSigned(JxlFieldCoders.ReadU32(DcThresholdDist, br));
            }

            this.DcThresholds[j] = dct;
        }

        uint[] qft = new uint[br.ReadBits(4)];
        for (int i = 0; i < qft.Length; i++)
        {
            qft[i] = JxlFieldCoders.ReadU32(QfThresholdDist, br) + 1;
        }

        this.QfThresholds = qft;
        if (this.NumDcCtxs * (qft.Length + 1) > 64)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid block context map: too big");
        }

        this.CtxMap = new byte[3 * NumOrders * this.NumDcCtxs * (qft.Length + 1)];
        this.NumCtxs = JxlContextMap.DecodeContextMap(this.CtxMap, br);
        if (this.NumCtxs > 16)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid block context map: too many distinct contexts");
        }
    }

    /// <summary>
    /// Sets a map without DC thresholds, as the encoder's <c>FindBestBlockEntropyModel</c> chooses it.
    /// </summary>
    /// <param name="qfThresholds">The quant field thresholds.</param>
    /// <param name="ctxMap">The map: 3 channels x 13 orders x (thresholds + 1) quant field buckets.</param>
    /// <param name="numCtxs">The number of distinct contexts.</param>
    public void SetQfContexts(uint[] qfThresholds, byte[] ctxMap, int numCtxs)
    {
        DebugGuard.IsTrue(ctxMap.Length == 3 * NumOrders * (qfThresholds.Length + 1), "One entry per channel, order and quant field bucket");
        this.DcThresholds[0] = Array.Empty<int>();
        this.DcThresholds[1] = Array.Empty<int>();
        this.DcThresholds[2] = Array.Empty<int>();
        this.NumDcCtxs = 1;
        this.QfThresholds = qfThresholds;
        this.CtxMap = ctxMap;
        this.NumCtxs = numCtxs;
    }

    /// <summary>Returns the block context.</summary>
    /// <param name="dcIdx">The bucket of the quantised DC.</param>
    /// <param name="qf">The quant field of the block.</param>
    /// <param name="ord">The coefficient order of the block's strategy.</param>
    /// <param name="c">The channel.</param>
    /// <returns>The block context.</returns>
    public int Context(int dcIdx, int qf, int ord, int c)
    {
        int qfIdx = 0;
        foreach (uint t in this.QfThresholds)
        {
            if ((uint)qf > t)
            {
                qfIdx++;
            }
        }

        int idx = c < 2 ? c ^ 1 : 2;
        idx = (idx * NumOrders) + ord;
        idx = (idx * (this.QfThresholds.Length + 1)) + qfIdx;
        idx = (idx * this.NumDcCtxs) + dcIdx;
        return this.CtxMap[idx];
    }

    /// <summary>Returns the first context of the coefficient symbols of a block context.</summary>
    /// <param name="blockCtx">The block context.</param>
    /// <returns>The offset.</returns>
    public int ZeroDensityContextsOffset(int blockCtx) => (this.NumCtxs * NonZeroBuckets) + (ZeroDensityContextCount * blockCtx);

    /// <summary>
    /// Returns the context of the number of non-zeros in a block. Contexts with the same number of
    /// non-zeros are grouped, for better clustering.
    /// </summary>
    /// <param name="nonZeros">The predicted number of non-zeros.</param>
    /// <param name="blockCtx">The block context.</param>
    /// <returns>The context.</returns>
    public int NonZeroContext(int nonZeros, int blockCtx)
    {
        int ctx;
        if (nonZeros >= 64)
        {
            nonZeros = 64;
        }

        if (nonZeros < 8)
        {
            ctx = nonZeros;
        }
        else
        {
            ctx = 4 + (nonZeros / 2);
        }

        return (ctx * this.NumCtxs) + blockCtx;
    }

    /// <summary>
    /// Returns the context of a coefficient symbol from the non-zeros left and the position in the
    /// scan order, both clustered so that the number of contexts stays small.
    /// </summary>
    /// <remarks>
    /// libjxl only asserts that <paramref name="nonzerosLeft"/> + <paramref name="k"/> is below 65 in
    /// debug builds, as the hot loop does not check it. With invalid input the context can reach
    /// <see cref="ZeroDensityContextLimit"/> - 1, which the padded context map covers, and the block
    /// then fails its final non-zero check.
    /// </remarks>
    /// <param name="nonzerosLeft">The non-zeros left in the block.</param>
    /// <param name="k">The position in the scan order.</param>
    /// <param name="coveredBlocks">The number of 8x8 blocks the strategy covers.</param>
    /// <param name="log2CoveredBlocks">Log2 of <paramref name="coveredBlocks"/>.</param>
    /// <param name="prev">1 if the previous coefficient was zero, else 0.</param>
    /// <returns>The context.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static int ZeroDensityContext(int nonzerosLeft, int k, int coveredBlocks, int log2CoveredBlocks, int prev)
    {
        nonzerosLeft = (nonzerosLeft + coveredBlocks - 1) >> log2CoveredBlocks;
        k >>= log2CoveredBlocks;
        return ((CoeffNumNonzeroContext[nonzerosLeft] + CoeffFreqContext[k]) * 2) + prev;
    }

    /// <summary>
    /// Predicts a value from the one above and the one to the left, or <paramref name="defaultVal"/>
    /// in the top-left corner.
    /// </summary>
    /// <param name="rowTop">The row above, or empty for the first row.</param>
    /// <param name="row">The current row.</param>
    /// <param name="x">The column.</param>
    /// <param name="defaultVal">The value in the top-left corner.</param>
    /// <returns>The prediction.</returns>
    public static int PredictFromTopAndLeft(ReadOnlySpan<int> rowTop, ReadOnlySpan<int> row, int x, int defaultVal)
    {
        if (x == 0)
        {
            return rowTop.IsEmpty ? defaultVal : rowTop[x];
        }

        if (rowTop.IsEmpty)
        {
            return row[x - 1];
        }

        return (rowTop[x] + row[x - 1] + 1) / 2;
    }

    private void SetDefault()
    {
        this.DcThresholds[0] = Array.Empty<int>();
        this.DcThresholds[1] = Array.Empty<int>();
        this.DcThresholds[2] = Array.Empty<int>();
        this.QfThresholds = Array.Empty<uint>();
        this.CtxMap = DefaultCtxMap.ToArray();
        byte max = 0;
        foreach (byte b in this.CtxMap)
        {
            max = Math.Max(max, b);
        }

        this.NumCtxs = max + 1;
        this.NumDcCtxs = 1;
    }
}
