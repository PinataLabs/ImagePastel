// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_chroma_from_luma.cc (CFLFunction, FindBestMultiplier, ComputeTile and
// CfLHeuristics::ComputeTile).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The encoder's AC chroma-from-luma map: for each 64x64 colour tile, the multipliers of Y that predict X
/// and B best over the AC coefficients of the blocks in the tile, as libjxl computes them from Hare (effort
/// 5) down once the strategies and the quant field are known, and from Squirrel (effort 7) down also before
/// the strategy search, on DCT8 blocks.
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
    /// Fits the AC chroma-from-luma multipliers of one 64x64 colour tile, as libjxl's
    /// <c>CfLHeuristics::ComputeTile</c>. With <paramref name="useDct8"/> (libjxl's call with no strategy
    /// image, raw quant field or quantizer: the pre-pass before the strategy search, from Squirrel down)
    /// every block is taken as DCT8 with a quant of 1; otherwise the tile's chosen strategies and raw quant
    /// field are read (from Hare down, after the search). The DC map keeps its default: libjxl fills its DC
    /// values but never fits them.
    /// </summary>
    /// <param name="encState">The encoder state; without <paramref name="useDct8"/>, with the tile's strategies and raw quant field set.</param>
    /// <param name="opsin">The XYB planes the coefficients are computed from (after the inverse Gaborish filter), padded to whole blocks.</param>
    /// <param name="r">The tile, in blocks: an encoder tile, which is one colour tile.</param>
    /// <param name="useDct8">Whether to take every block as DCT8 with no quant field (the pre-pass).</param>
    /// <param name="fast">Whether to fit by least squares (libjxl's fast search, from Wombat up) rather than by Newton's method.</param>
    /// <param name="scratch">The working set, reused across tiles.</param>
    public static void ComputeTile(JxlPassesEncoderState encState, Buffer2D<float>[] opsin, JxlRect r, bool useDct8, bool fast, Scratch scratch)
    {
        int bx0 = (int)r.X0;
        int by0 = (int)r.Y0;
        int bx1 = bx0 + (int)r.XSize;
        int by1 = by0 + (int)r.YSize;
        DebugGuard.IsTrue(bx0 % ColorTileDimInBlocks == 0 && by0 % ColorTileDimInBlocks == 0 && r.XSize <= ColorTileDimInBlocks && r.YSize <= ColorTileDimInBlocks, "The rect is one colour tile");
        JxlQuantizer quantizer = encState.Quantizer;
        JxlAcStrategyImage acStrategy = encState.AcStrategy;
        int ty = by0 / ColorTileDimInBlocks;
        int tx = bx0 / ColorTileDimInBlocks;
        int numAc = 0;

        for (int y = by0; y < by1; ++y)
        {
            for (int x = bx0; x < bx1; x++)
            {
                JxlAcStrategy acs = useDct8 ? JxlAcStrategy.FromRawStrategy((int)JxlAcStrategyType.Dct) : acStrategy.Get(x, y);
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
                // which was also a hack.) With no raw quant field (the pre-pass) libjxl takes qq = 1.
                float qq = useDct8 ? 1.0f : encState.RawQuantField[encState.BlockIndex(x, y)];

                // Experimentally values 128-130 seem best -- I don't know why we need this multiplier.
                const float strangeMultiplier = 128;
                float q = useDct8 ? 1 : quantizer.Scale * strangeMultiplier * qq;
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
        cmap.YtoXMap[tile] = (sbyte)FindBestMultiplier(scratch.CoeffsYx.AsSpan(0, numAc), scratch.CoeffsX.AsSpan(0, numAc), 0.0f, DistanceMultiplierAc, fast, out _);
        cmap.YtoBMap[tile] = (sbyte)FindBestMultiplier(scratch.CoeffsYb.AsSpan(0, numAc), scratch.CoeffsB.AsSpan(0, numAc), YToBRatio, DistanceMultiplierAc, fast, out _);
    }

    /// <summary>
    /// Returns libjxl's chroma-from-luma multiplier: the <c>x</c> for which the residual
    /// <c>m / 84 * x + (base * m - s)</c> is smallest, with a small penalty on <c>x</c>, shrunk towards 0 by
    /// 2.6, rounded half away from zero and clamped to a signed byte. The fast fit (Wombat and up) is by
    /// least squares; the other (Squirrel and down) minimises libjxl's <c>CFLFunction</c> by Newton's method.
    /// </summary>
    /// <param name="valuesM">The luma coefficients; a multiple of 8 of them.</param>
    /// <param name="valuesS">The chroma coefficients, as many.</param>
    /// <param name="baseCorrelation">The base correlation of the channel.</param>
    /// <param name="distanceMul">The weight of the penalty on <c>x</c>.</param>
    /// <param name="fast">Whether to fit by least squares rather than by Newton's method.</param>
    /// <param name="beforeRound">The multiplier after the shrink, before it is rounded (NaN with no values).</param>
    /// <returns>The multiplier.</returns>
    public static int FindBestMultiplier(ReadOnlySpan<float> valuesM, ReadOnlySpan<float> valuesS, float baseCorrelation, float distanceMul, bool fast, out float beforeRound)
    {
        int num = valuesM.Length;
        beforeRound = float.NaN;
        if (num == 0)
        {
            return 0;
        }

        DebugGuard.IsTrue(num % Lanes == 0, "The values fill whole vectors");
        float x;
        if (fast)
        {
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
            x = -JxlAvx2Lanes.SumOfLanes(cb) / (JxlAvx2Lanes.SumOfLanes(ca) + ((float)num * distanceMul * 0.5f));
        }
        else
        {
            const float eps = 100;
            const float clamp = 20.0f;
            x = 0;

            // Up to 20 Newton iterations, with approximate derivatives. Derivatives are approximate due to
            // the high amount of noise in the exact derivatives.
            for (int i = 0; i < 20; i++)
            {
                float df = CflDerivative(valuesM, valuesS, baseCorrelation, distanceMul, x, eps, out float dfpeps, out float dfmeps);
                float ddf = (dfpeps - dfmeps) / (2 * eps);
                const float experimentalInsignificantStabilizer = 0.85f;
                float step = df / (ddf + experimentalInsignificantStabilizer);
                x -= MathF.Min(clamp, MathF.Max(-clamp, step));

                // libjxl compares the float with the double 3e-3.
                if (MathF.Abs(step) < 3e-3)
                {
                    break;
                }
            }
        }

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

    // libjxl's CFLFunction::Compute: f'(x), f'(x + eps) and f'(x - eps), where f is
    // 1/3 * sum ((|color residual| + 1)^2 - 1) + distance_mul * x^2 * num, and residuals of 100 or more
    // drop out of the sums. The sums run in 8 lanes and MulAdd is not fused (ADR 0004).
    private static float CflDerivative(ReadOnlySpan<float> valuesM, ReadOnlySpan<float> valuesS, float baseCorrelation, float distanceMul, float x, float eps, out float fpeps, out float fmeps)
    {
        const float coeff = 1.0f / 3;
        const float thres = 100.0f;
        const float coeffx2 = coeff * 2.0f;
        int num = valuesM.Length;

        // libjxl multiplies 2 * distance_mul by the size_t count, in float.
        float twiceDistanceMulNum = 2 * distanceMul * (float)num;
        float firstDerivative = twiceDistanceMulNum * x;
        float firstDerivativePeps = twiceDistanceMulNum * (x + eps);
        float firstDerivativeMeps = twiceDistanceMulNum * (x - eps);

        float xpe = x + eps;
        float xme = x - eps;
        Span<float> fd = stackalloc float[Lanes];
        Span<float> fdpe = stackalloc float[Lanes];
        Span<float> fdme = stackalloc float[Lanes];
        fd.Clear();
        fdpe.Clear();
        fdme.Clear();
        for (int i = 0; i < num; i += Lanes)
        {
            for (int l = 0; l < Lanes; l++)
            {
                // color residual = ax + b
                float m = valuesM[i + l];
                float a = InvColorFactor * m;
                float b = (baseCorrelation * m) - valuesS[i + l];
                float v = (a * x) + b;
                float vpe = (a * xpe) + b;
                float vme = (a * xme) + b;
                float av = MathF.Abs(v);
                float avpe = MathF.Abs(vpe);
                float avme = MathF.Abs(vme);
                float acoeffx2 = coeffx2 * a;
                float d = acoeffx2 * (av + 1.0f);
                float dpe = acoeffx2 * (avpe + 1.0f);
                float dme = acoeffx2 * (avme + 1.0f);
                d = v < 0.0f ? 0.0f - d : d;
                dpe = vpe < 0.0f ? 0.0f - dpe : dpe;
                dme = vme < 0.0f ? 0.0f - dme : dme;
                bool above = av >= thres;
                fd[l] += above ? 0.0f : d;
                fdpe[l] += above ? 0.0f : dpe;
                fdme[l] += above ? 0.0f : dme;
            }
        }

        fpeps = firstDerivativePeps + JxlAvx2Lanes.SumOfLanes(fdpe);
        fmeps = firstDerivativeMeps + JxlAvx2Lanes.SumOfLanes(fdme);
        return firstDerivative + JxlAvx2Lanes.SumOfLanes(fd);
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

    /// <summary>
    /// The working set of one tile. A block the encoder chooses lies in one tile, so a tile's area bounds
    /// both one block and the coefficients of all the blocks in the tile.
    /// </summary>
    internal sealed class Scratch
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
