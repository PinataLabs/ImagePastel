// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_chroma_from_luma.cc (FindBestMultiplier with fast = true, ComputeTile and
// CfLHeuristics::ComputeTile).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The encoder's AC chroma-from-luma map: for each 64x64 colour tile, the multipliers of Y that predict X
/// and B best, by least squares over the AC coefficients of the blocks in the tile, as libjxl computes them
/// from Hare (effort 5) down once the strategies and the quant field are known.
/// </summary>
/// <remarks>
/// libjxl sums the accumulators in vector lanes and adds the lanes with Highway's <c>SumOfLanes</c>. This
/// port sums in the 8 float lanes of AVX2, which cjxl runs on x64 (<see cref="JxlAvx2Lanes"/>), and its
/// <c>MulAdd</c> is not fused (ADR 0004).
/// </remarks>
internal static class JxlEncChromaFromLuma
{
    private const int BlockDim = 8;
    private const int Lanes = JxlAvx2Lanes.Count;
    private const int ColorTileDim = JxlColorCorrelation.ColorTileDim;
    private const int ColorTileDimInBlocks = JxlColorCorrelation.ColorTileDimInBlocks;

    // libjxl's kDistanceMultiplierAC.
    private const float DistanceMultiplierAc = 1e-9f;

    // kYToBRatio in lib/jxl/cms/opsin_params.h.
    private const float YToBRatio = 1.0f;

    private const float InvColorFactor = 1.0f / JxlColorCorrelation.DefaultColorFactor;

    /// <summary>
    /// Fits the AC chroma-from-luma map of every 64x64 tile, as libjxl's <c>CfLHeuristics::ComputeTile</c>
    /// for each encoder tile after its strategies and raw quant field are set. The tiles are independent,
    /// so this runs them in one pass after the field is complete. The DC map keeps its default: libjxl fills
    /// its DC values but never fits them.
    /// </summary>
    /// <param name="encState">The encoder state, with the strategies and the raw quant field set.</param>
    /// <param name="opsin">The XYB planes the coefficients are computed from (after the inverse Gaborish filter), padded to whole blocks.</param>
    /// <param name="fast">Whether to fit by least squares (libjxl's fast search, from Wombat up).</param>
    public static void ComputeMap(JxlPassesEncoderState encState, Buffer2D<float>[] opsin, bool fast)
    {
        // Temporary departure (E5a): below Wombat (fast = false) libjxl fits each multiplier with Newton's
        // method on its CFLFunction. E5e ports it; until then every tier takes the fast least squares, as
        // Hare does, and fast is not read.
        Scratch scratch = new();
        int xsizeBlocks = encState.XSizeBlocks;
        int ysizeBlocks = encState.YSizeBlocks;
        for (int by0 = 0; by0 < ysizeBlocks; by0 += ColorTileDimInBlocks)
        {
            for (int bx0 = 0; bx0 < xsizeBlocks; bx0 += ColorTileDimInBlocks)
            {
                int bx1 = Math.Min(bx0 + ColorTileDimInBlocks, xsizeBlocks);
                int by1 = Math.Min(by0 + ColorTileDimInBlocks, ysizeBlocks);
                ComputeTile(encState, opsin, bx0, by0, bx1, by1, scratch);
            }
        }
    }

    /// <summary>
    /// Returns libjxl's fast chroma-from-luma multiplier: the least-squares <c>x</c> for which the residual
    /// <c>m / 84 * x + (base * m - s)</c> is smallest, with a small penalty on <c>x</c>, shrunk towards 0 by
    /// 2.6, rounded half away from zero and clamped to a signed byte.
    /// </summary>
    /// <param name="valuesM">The luma coefficients; a multiple of 8 of them.</param>
    /// <param name="valuesS">The chroma coefficients, as many.</param>
    /// <param name="baseCorrelation">The base correlation of the channel.</param>
    /// <param name="distanceMul">The weight of the penalty on <c>x</c>.</param>
    /// <param name="beforeRound">The multiplier after the shrink, before it is rounded (NaN with no values).</param>
    /// <returns>The multiplier.</returns>
    public static int FindBestMultiplier(ReadOnlySpan<float> valuesM, ReadOnlySpan<float> valuesS, float baseCorrelation, float distanceMul, out float beforeRound)
    {
        int num = valuesM.Length;
        beforeRound = float.NaN;
        if (num == 0)
        {
            return 0;
        }

        DebugGuard.IsTrue(num % Lanes == 0, "The values fill whole vectors");
        Span<float> ca = stackalloc float[Lanes];
        Span<float> cb = stackalloc float[Lanes];
        ca.Clear();
        cb.Clear();
        for (int i = 0; i < num; i += Lanes)
        {
            for (int l = 0; l < Lanes; l++)
            {
                // color residual = ax + b
                float m = valuesM[i + l];
                float a = InvColorFactor * m;
                float b = (baseCorrelation * m) - valuesS[i + l];
                ca[l] = (a * a) + ca[l];
                cb[l] = (a * b) + cb[l];
            }
        }

        // + distance_mul * x^2 * num. libjxl multiplies the size_t count by the float, in float.
        float x = -JxlAvx2Lanes.SumOfLanes(cb) / (JxlAvx2Lanes.SumOfLanes(ca) + ((float)num * distanceMul * 0.5f));

        // CFL seems to be tricky for larger transforms for HF components close to zero. This heuristic
        // brings the solutions closer to zero and reduces red-green oscillations.
        const float towardsZero = 2.6f;
        if (x >= towardsZero)
        {
            x -= towardsZero;
        }
        else if (x <= -towardsZero)
        {
            x += towardsZero;
        }
        else
        {
            x = 0;
        }

        beforeRound = x;
        return (int)Math.Clamp(MathF.Round(x, MidpointRounding.AwayFromZero), -128.0f, 127.0f);
    }

    // libjxl's ComputeTile with an AC strategy image, a raw quant field and a quantizer (use_dct8 false), for
    // the tile of blocks [bx0, bx1) x [by0, by1).
    private static void ComputeTile(JxlPassesEncoderState encState, Buffer2D<float>[] opsin, int bx0, int by0, int bx1, int by1, Scratch scratch)
    {
        JxlQuantizer quantizer = encState.Quantizer;
        JxlAcStrategyImage acStrategy = encState.AcStrategy;
        int ty = by0 / ColorTileDimInBlocks;
        int tx = bx0 / ColorTileDimInBlocks;
        int numAc = 0;

        for (int y = by0; y < by1; ++y)
        {
            for (int x = bx0; x < bx1; x++)
            {
                JxlAcStrategy acs = acStrategy.Get(x, y);
                if (!acs.IsFirstBlock)
                {
                    continue;
                }

                int xs = acs.CoveredBlocksX;
                int ys = acs.CoveredBlocksY;
                TransformFromPixels(acs.Strategy, opsin[1], x, y, xs, ys, scratch.Pixels, scratch.BlockY, scratch.Dct);
                TransformFromPixels(acs.Strategy, opsin[0], x, y, xs, ys, scratch.Pixels, scratch.BlockX, scratch.Dct);
                TransformFromPixels(acs.Strategy, opsin[2], x, y, xs, ys, scratch.Pixels, scratch.BlockB, scratch.Dct);

                // libjxl also takes each block's DCs into its dc_values here, for a DC fit that it never
                // runs; nothing reads them, so they are not computed.

                // Do not use this block for computing AC CfL. libjxl compares with the tile's first block,
                // not this one's.
                if (acs.CoveredBlocksX + bx0 > bx1 || acs.CoveredBlocksY + by0 > by1)
                {
                    continue;
                }

                // Copy AC coefficients in the local block. The order in which coefficients get stored does
                // not matter.
                int cx = acs.CoveredBlocksX;
                int cy = acs.CoveredBlocksY;
                JxlAcStrategy.CoefficientLayout(ref cy, ref cx);

                // Zero out LFs. This introduces terms in the optimization loop that don't affect the result,
                // as they are all 0, but allow for simpler SIMDfication.
                for (int iy = 0; iy < cy; iy++)
                {
                    for (int ix = 0; ix < cx; ix++)
                    {
                        scratch.BlockY[(cx * BlockDim * iy) + ix] = 0;
                        scratch.BlockX[(cx * BlockDim * iy) + ix] = 0;
                        scratch.BlockB[(cx * BlockDim * iy) + ix] = 0;
                    }
                }

                // Unclear why this is like it is. (This works slightly better than the previous approach
                // which was also a hack.)
                float qq = encState.RawQuantField[encState.BlockIndex(x, y)];

                // Experimentally values 128-130 seem best -- I don't know why we need this multiplier.
                const float strangeMultiplier = 128;
                float q = quantizer.Scale * strangeMultiplier * qq;
                ReadOnlySpan<float> qmX = quantizer.InvDequantMatrix(acs.Strategy, 0);
                ReadOnlySpan<float> qmB = quantizer.InvDequantMatrix(acs.Strategy, 2);
                int size = cx * cy * BlockDim * BlockDim;
                for (int i = 0; i < size; i++)
                {
                    float by = scratch.BlockY[i];
                    float qqmX = q * qmX[i];
                    float qqmB = q * qmB[i];
                    scratch.CoeffsYx[numAc] = by * qqmX;
                    scratch.CoeffsX[numAc] = scratch.BlockX[i] * qqmX;
                    scratch.CoeffsYb[numAc] = by * qqmB;
                    scratch.CoeffsB[numAc] = scratch.BlockB[i] * qqmB;
                    numAc++;
                }
            }
        }

        JxlColorCorrelation cmap = encState.Cmap;
        int tile = (ty * cmap.XSizeTiles) + tx;
        cmap.YtoXMap[tile] = (sbyte)FindBestMultiplier(scratch.CoeffsYx.AsSpan(0, numAc), scratch.CoeffsX.AsSpan(0, numAc), 0.0f, DistanceMultiplierAc, out _);
        cmap.YtoBMap[tile] = (sbyte)FindBestMultiplier(scratch.CoeffsYb.AsSpan(0, numAc), scratch.CoeffsB.AsSpan(0, numAc), YToBRatio, DistanceMultiplierAc, out _);
    }

    // The forward transform of the block of one plane whose first 8x8 block is (bx, by), covering xs x ys
    // blocks: its pixels are copied out with rows xs * 8 apart.
    private static void TransformFromPixels(JxlAcStrategyType strategy, Buffer2D<float> plane, int bx, int by, int xs, int ys, float[] pixels, float[] coefficients, float[] dctScratch)
    {
        int cols = xs * BlockDim;
        for (int iy = 0; iy < ys * BlockDim; iy++)
        {
            plane.DangerousGetRowSpan((by * BlockDim) + iy).Slice(bx * BlockDim, cols).CopyTo(pixels.AsSpan(iy * cols, cols));
        }

        JxlDct.TransformFromPixels(strategy, pixels, cols, coefficients, dctScratch);
    }

    // The working set of one tile. A block the encoder chooses lies in one tile, so a tile's area bounds
    // both one block and the coefficients of all the blocks in the tile.
    private sealed class Scratch
    {
        private const int TileArea = ColorTileDim * ColorTileDim;

        public float[] Pixels { get; } = new float[TileArea];

        public float[] BlockY { get; } = new float[TileArea];

        public float[] BlockX { get; } = new float[TileArea];

        public float[] BlockB { get; } = new float[TileArea];

        public float[] CoeffsYx { get; } = new float[TileArea];

        public float[] CoeffsX { get; } = new float[TileArea];

        public float[] CoeffsYb { get; } = new float[TileArea];

        public float[] CoeffsB { get; } = new float[TileArea];

        public float[] Dct { get; } = new float[JxlDct.ScratchSize];
    }
}
