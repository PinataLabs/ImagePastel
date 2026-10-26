// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_adaptive_quantization.cc (ComputeMask, RatioOfDerivativesOfCubicRootToSimpleGamma,
// GammaModulation, BlueModulation, HfModulation, PerBlockModulations, MaskingSqrt, StoreMin4, FuzzyErosion,
// AdaptiveQuantizationImpl::ComputeTile, Blur1x1Masking, AdaptiveQuantizationMap, AdjustQuantField and
// InitialQuantField).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.VarDct;

/// <summary>
/// The encoder's adaptive quantisation: the initial quant field of each block from the opsin image, and the
/// masking image the AC strategy search reads, as libjxl computes them from Hare (effort 5) down.
/// </summary>
/// <remarks>
/// <para>
/// No fused multiply-add (ADR 0004): each Highway <c>MulAdd(a, b, c)</c> is <c>a * b + c</c>, rounded twice,
/// in libjxl's operand order.
/// </para>
/// <para>
/// libjxl's modulations sum each 8x8 block over the lanes of an 8-lane vector and then across the lanes
/// (<c>SumOfLanes</c>); its pre-erosion pass takes a vector path for whole vectors inside a row and a
/// scalar one at the ends, with sums in another order. This port follows AVX2, which cjxl runs on x64:
/// pixel <c>dx</c> of a block row goes to lane <c>dx</c>, the lanes add up as Highway's <c>SumOfLanes</c>
/// adds them (see <see cref="JxlAvx2Lanes"/>), and the vector path covers runs of 8 pixels. On another
/// vector width libjxl's values differ in the last bits.
/// </para>
/// </remarks>
internal static class JxlEncAdaptiveQuantization
{
    // libjxl's kEncTileDimInBlocks: the field is computed per tile of 8x8 blocks (64x64 pixels).
    private const int EncTileDimInBlocks = 8;

    private const int EncTileDim = EncTileDimInBlocks * BlockDim;

    private const int BlockDim = 8;

    // The number of float lanes of AVX2, libjxl's Lanes(df) for its vector paths.
    private const int Lanes = JxlAvx2Lanes.Count;

    private const float AcQuant = 0.765f;

    // The XYB gamma is 3.0 to be able to decode faster with two muls. Butteraugli's gamma is matching the
    // gamma of human eye, around 2.6. We approximate the gamma difference by adding one cubic root into the
    // adaptive quantization. This gives us a total gamma of 2.6666 for quantization uses.
    private const float MatchGammaOffset = 0.019f;

    // libjxl's kInvLog2e of lib/jxl/base/common.h, a float.
    private const float InvLog2e = 0.6931471805599453f;

    // mul and mul2 represent a scaling difference between jxl and butteraugli.
    private const float SgMul = 226.77216153508914f;
    private const float SgVOffset = 7.7825991679894591f;
    private const float SgEpsilon = 1e-2f;

    private const float MaskingLogOffset = 27.505837037000106f;
    private const float MaskingMul = 211.66567973503678f;

    private const float PreErosionLimit = 0.2f;

    private static readonly float SgMul2 = 1.0f / 73.377132366608819f;

    // Includes correction factor for std::log -> log2.
    private static readonly float SgRetMul = SgMul2 * 18.6580932135f * InvLog2e;

    private static readonly float SgNumMul = SgRetMul * 3 * SgMul;
    private static readonly float SgVOffsetLog = (SgVOffset * InvLog2e) + SgEpsilon;
    private static readonly float SgDenMul = InvLog2e * SgMul;

    // libjxl's Sqrt(Set(d, kMul * 1e8)): the product is a double, rounded to float by the Set.
    private static readonly float MaskingSqrtMul = MathF.Sqrt((float)(MaskingMul * 1e8));

    private static readonly float[] FilterMask1x1 = { 0.364911248f, 0.05f, 0.1688888021f, 0.221069183f, 0.306563504f };

    private static readonly float[] FuzzyMulBase = { 0.125f, 0.1f, 0.09f, 0.06f };
    private static readonly float[] FuzzyMulAdd = { 0.0f, -0.1f, -0.09f, -0.06f };

    /// <summary>
    /// Computes the initial quant field of each block, as libjxl's <c>InitialQuantField</c>: the adaptive
    /// quantization map of the whole image, at the AC quant of <paramref name="butteraugliTarget"/>.
    /// </summary>
    /// <remarks>
    /// libjxl also makes a block-resolution masking image (<c>ComputeMaskForAcStrategyUse</c>), which only
    /// the AC strategy search reads as <c>config.Masking</c>; at Hare and Squirrel it never does, so this
    /// port does not make it.
    /// </remarks>
    /// <param name="butteraugliTarget">The distance the field is for: the frame's distance, or 0.62 times it without Gaborish.</param>
    /// <param name="opsin">The X, Y and B planes before the inverse Gaborish filter, padded to whole blocks.</param>
    /// <param name="rescale">The factor of the AC quant.</param>
    /// <param name="allocator">The allocator.</param>
    /// <param name="mask1x1">The masking image at full resolution, which the AC strategy search reads.</param>
    /// <returns>The quant field, one value per block.</returns>
    public static Buffer2D<float> InitialQuantField(float butteraugliTarget, Buffer2D<float>[] opsin, float rescale, MemoryAllocator allocator, out Buffer2D<float> mask1x1)
    {
        float quantAc = AcQuant / butteraugliTarget;
        return AdaptiveQuantizationMap(butteraugliTarget, opsin, quantAc * rescale, allocator, out mask1x1);
    }

    /// <summary>
    /// Replaces the quant field of each block larger than 8x8 in <paramref name="rect"/> with the maximum
    /// over its 8x8 blocks, mixed with their mean at larger distances, as libjxl's <c>AdjustQuantField</c>.
    /// </summary>
    /// <param name="acStrategy">The strategy of each block.</param>
    /// <param name="rect">The blocks to adjust.</param>
    /// <param name="butteraugliTarget">The frame's distance.</param>
    /// <param name="quantField">The quant field, one value per block.</param>
    public static void AdjustQuantField(JxlAcStrategyImage acStrategy, JxlRect rect, float butteraugliTarget, Buffer2D<float> quantField)
    {
        // At low distances it is great to use max, but mean works better at high distances. We
        // interpolate between them for a distance range.
        float meanMaxMixer = 1.0f;
        {
            const float limit = 1.54138f;
            const float mul = 0.56391f;
            const float min = 0.0f;
            if (butteraugliTarget > limit)
            {
                meanMaxMixer -= (butteraugliTarget - limit) * mul;
                if (meanMaxMixer < min)
                {
                    meanMaxMixer = min;
                }
            }
        }

        for (int y = 0; y < rect.YSize; y++)
        {
            int by = (int)rect.Y0 + y;
            for (int x = 0; x < rect.XSize; x++)
            {
                int bx = (int)rect.X0 + x;
                JxlAcStrategy acs = acStrategy.Get(bx, by);
                if (!acs.IsFirstBlock)
                {
                    continue;
                }

                DebugGuard.IsTrue(bx + acs.CoveredBlocksX <= quantField.Width, "The block is inside the field");
                DebugGuard.IsTrue(by + acs.CoveredBlocksY <= quantField.Height, "The block is inside the field");
                float max = quantField[bx, by];
                float mean = 0.0f;
                for (int iy = 0; iy < acs.CoveredBlocksY; iy++)
                {
                    Span<float> row = quantField.DangerousGetRowSpan(by + iy);
                    for (int ix = 0; ix < acs.CoveredBlocksX; ix++)
                    {
                        float v = row[bx + ix];
                        mean += v;

                        // std::max(v, max).
                        max = v < max ? max : v;
                    }
                }

                int covered = acs.CoveredBlocksY * acs.CoveredBlocksX;
                mean /= covered;
                if (covered >= 4)
                {
                    max *= meanMaxMixer;
                    max += (1.0f - meanMaxMixer) * mean;
                }

                for (int iy = 0; iy < acs.CoveredBlocksY; iy++)
                {
                    quantField.DangerousGetRowSpan(by + iy).Slice(bx, acs.CoveredBlocksX).Fill(max);
                }
            }
        }
    }

    // libjxl's AdaptiveQuantizationMap over the whole image (rect = the image): ComputeTile per 64x64 tile
    // in tile order, then the blur of the masking image.
    private static Buffer2D<float> AdaptiveQuantizationMap(float butteraugliTarget, Buffer2D<float>[] xyb, float scale, MemoryAllocator allocator, out Buffer2D<float> mask1x1)
    {
        int xsize = xyb[0].Width;
        int ysize = xyb[0].Height;
        DebugGuard.IsTrue(xsize % BlockDim == 0 && ysize % BlockDim == 0, "The opsin is padded to whole blocks");
        int xsizeBlocks = xsize / BlockDim;
        int ysizeBlocks = ysize / BlockDim;
        Buffer2D<float> aqMap = allocator.Allocate2D<float>(xsizeBlocks, ysizeBlocks, AllocationOptions.Clean);
        Buffer2D<float> mask = allocator.Allocate2D<float>(xsize, ysize, AllocationOptions.Clean);
        try
        {
            // libjxl keeps one diff buffer row and one pre-erosion image per thread.
            float[] diffBuffer = new float[EncTileDim + 8];
            float[] preErosion = new float[((EncTileDimInBlocks * 2) + 2) * ((EncTileDimInBlocks * 2) + 2)];
            int numTilesX = (int)JxlBits.DivCeil(xsizeBlocks, EncTileDimInBlocks);
            int numTiles = numTilesX * (int)JxlBits.DivCeil(ysizeBlocks, EncTileDimInBlocks);

            // The tiles are independent: this loop is where libjxl runs them in parallel.
            for (int tid = 0; tid < numTiles; tid++)
            {
                int tx = tid % numTilesX;
                int ty = tid / numTilesX;
                int by0 = ty * EncTileDimInBlocks;
                int by1 = Math.Min((ty + 1) * EncTileDimInBlocks, ysizeBlocks);
                int bx0 = tx * EncTileDimInBlocks;
                int bx1 = Math.Min((tx + 1) * EncTileDimInBlocks, xsizeBlocks);
                JxlRect rectOut = new(bx0, by0, bx1 - bx0, by1 - by0);
                ComputeTile(butteraugliTarget, scale, xyb, rectOut, diffBuffer, preErosion, aqMap, mask);
            }

            mask1x1 = Blur1x1Masking(mask, allocator);
            return aqMap;
        }
        catch
        {
            aqMap.Dispose();
            throw;
        }
        finally
        {
            mask.Dispose();
        }
    }

    // libjxl's AdaptiveQuantizationImpl::ComputeTile for a rect_in that is the whole image: the masking
    // image of the tile's pixels, the pre-erosion image of the tile and 4 pixels around it, the fuzzy
    // erosion into the tile's blocks of the field, and the per-block modulations.
    private static void ComputeTile(float butteraugliTarget, float scale, Buffer2D<float>[] xyb, JxlRect rectOut, float[] diffBuffer, float[] preErosion, Buffer2D<float> aqMap, Buffer2D<float> mask1x1)
    {
        int xsize = xyb[1].Width;
        int ysize = xyb[1].Height;

        // libjxl widens the masking rows by 2 pixels at a tile border inside rect_in only when rect_in
        // starts or ends inside the image (streaming), never for the whole image.
        int yStart1x1 = (int)rectOut.Y0 * 8;
        int yEnd1x1 = yStart1x1 + ((int)rectOut.YSize * 8);
        int xStart1x1 = (int)rectOut.X0 * 8;
        int xEnd1x1 = xStart1x1 + ((int)rectOut.XSize * 8);

        // Computes image (padded to multiple of 8x8) of local pixel differences.
        // Subsample both directions by 4.
        // 1x1 Laplacian of intensity.
        for (int y = yStart1x1; y < yEnd1x1; ++y)
        {
            int y2 = y + 1 < ysize ? y + 1 : y;
            int y1 = y > 0 ? y - 1 : y;
            ReadOnlySpan<float> rowIn = xyb[1].DangerousGetRowSpan(y);
            ReadOnlySpan<float> rowIn1 = xyb[1].DangerousGetRowSpan(y1);
            ReadOnlySpan<float> rowIn2 = xyb[1].DangerousGetRowSpan(y2);
            Span<float> mask1x1Out = mask1x1.DangerousGetRowSpan(y);
            for (int x = xStart1x1; x < xEnd1x1; ++x)
            {
                int x2 = x + 1 < xsize ? x + 1 : x;
                int x1 = x > 0 ? x - 1 : x;
                float baseValue = 0.25f * (((rowIn2[x] + rowIn1[x]) + rowIn[x1]) + rowIn[x2]);
                float gammac = RatioOfDerivativesOfCubicRootToSimpleGamma(rowIn[x] + MatchGammaOffset, invert: false);
                float diff = MathF.Abs(gammac * (rowIn[x] - baseValue));

                // libjxl's kScaler is 1.0 (a double): the multiply leaves diff as it is.
                // std::log1p, as a double logarithm rounded to float (ADR 0004).
                diff = (float)Math.Log(1.0 + diff);
                const float mul = 1.0f;
                const float offset = 0.01f;
                mask1x1Out[x] = mul / (diff + offset);
            }
        }

        int yStart = (int)rectOut.Y0 * 8;
        int yEnd = yStart + ((int)rectOut.YSize * 8);
        int xStart = (int)rectOut.X0 * 8;
        int xEnd = xStart + ((int)rectOut.XSize * 8);

        if (xStart != 0)
        {
            xStart -= 4;
        }

        if (xEnd != xsize)
        {
            xEnd += 4;
        }

        if (yStart != 0)
        {
            yStart -= 4;
        }

        if (yEnd != ysize)
        {
            yEnd += 4;
        }

        int preXSize = (xEnd - xStart) / 4;
        int preYSize = (yEnd - yStart) / 4;
        Span<float> rowOut = diffBuffer;
        for (int y = yStart; y < yEnd; ++y)
        {
            int y2 = y + 1 < ysize ? y + 1 : y;
            int y1 = y > 0 ? y - 1 : y;
            ReadOnlySpan<float> rowIn = xyb[1].DangerousGetRowSpan(y);
            ReadOnlySpan<float> rowIn1 = xyb[1].DangerousGetRowSpan(y1);
            ReadOnlySpan<float> rowIn2 = xyb[1].DangerousGetRowSpan(y2);

            int x = xStart;

            // First pixel of the row.
            if (xStart == 0)
            {
                PreErosionScalarPixel(rowIn, rowIn1, rowIn2, rowOut, x, xStart, xsize, y);
                ++x;
            }

            // libjxl's vector path, 8 lanes at a time; its base sums in another order from the scalar
            // pixel's.
            for (; x + 1 + Lanes < xEnd; x += Lanes)
            {
                for (int lane = 0; lane < Lanes; lane++)
                {
                    int xl = x + lane;
                    float inV = rowIn[xl];
                    float inR = rowIn[xl + 1];
                    float inL = rowIn[xl - 1];
                    float inT = rowIn2[xl];
                    float inB = rowIn1[xl];
                    float baseValue = 0.25f * ((inR + inL) + (inT + inB));
                    float gammacv = RatioOfDerivativesOfCubicRootToSimpleGamma(inV + MatchGammaOffset, invert: false);
                    float diff = gammacv * (inV - baseValue);
                    diff *= diff;

                    diff = JxlAvx2Lanes.Min(diff, PreErosionLimit);
                    diff = MaskingSqrt(diff);
                    if ((y & 3) != 0)
                    {
                        diff += rowOut[xl - xStart];
                    }

                    rowOut[xl - xStart] = diff;
                }
            }

            // Scalar
            for (; x < xEnd; ++x)
            {
                PreErosionScalarPixel(rowIn, rowIn1, rowIn2, rowOut, x, xStart, xsize, y);
            }

            if (y % 4 == 3)
            {
                Span<float> rowDOut = preErosion.AsSpan(((y - yStart) / 4) * preXSize, preXSize);
                for (int qx = 0; qx < preXSize; qx++)
                {
                    rowDOut[qx] = (((rowOut[qx * 4] + rowOut[(qx * 4) + 1]) + rowOut[(qx * 4) + 2]) + rowOut[(qx * 4) + 3]) * 0.25f;
                }
            }
        }

        DebugGuard.IsTrue(xStart % (BlockDim / 2) == 0, "The pre-erosion starts at a multiple of 4");
        DebugGuard.IsTrue(yStart % (BlockDim / 2) == 0, "The pre-erosion starts at a multiple of 4");
        JxlRect fromRect = new(xStart % 8 == 0 ? 0 : 1, yStart % 8 == 0 ? 0 : 1, rectOut.XSize * 2, rectOut.YSize * 2);
        FuzzyErosion(butteraugliTarget, fromRect, preErosion, preXSize, preYSize, rectOut, aqMap);

        // libjxl fills the block-resolution masking image here (ComputeMaskForAcStrategyUse of the eroded
        // map), which nothing reads at Hare or Squirrel; see InitialQuantField.
        PerBlockModulations(butteraugliTarget, xyb[0], xyb[1], xyb[2], scale, rectOut, aqMap);
    }

    // libjxl's scalar_pixel lambda of the pre-erosion pass.
    private static void PreErosionScalarPixel(ReadOnlySpan<float> rowIn, ReadOnlySpan<float> rowIn1, ReadOnlySpan<float> rowIn2, Span<float> rowOut, int x, int xStart, int xsize, int y)
    {
        int x2 = x + 1 < xsize ? x + 1 : x;
        int x1 = x > 0 ? x - 1 : x;
        float baseValue = 0.25f * (((rowIn2[x] + rowIn1[x]) + rowIn[x1]) + rowIn[x2]);
        float gammac = RatioOfDerivativesOfCubicRootToSimpleGamma(rowIn[x] + MatchGammaOffset, invert: false);
        float diff = gammac * (rowIn[x] - baseValue);
        diff *= diff;
        if (diff >= PreErosionLimit)
        {
            diff = PreErosionLimit;
        }

        diff = MaskingSqrt(diff);
        if ((y % 4) != 0)
        {
            rowOut[x - xStart] += diff;
        }
        else
        {
            rowOut[x - xStart] = diff;
        }
    }

    // Look for smooth areas near the area of degradation. If the areas are generally smooth, don't do
    // masking. Output is downsampled 2x.
    private static void FuzzyErosion(float butteraugliTarget, JxlRect fromRect, float[] from, int xsize, int ysize, JxlRect toRect, Buffer2D<float> to)
    {
        const int step = 1;
        DebugGuard.IsTrue(toRect.XSize * 2 == fromRect.XSize && toRect.YSize * 2 == fromRect.YSize, "The output is half the input");
        float mul = 0.0f;
        if (butteraugliTarget < 2.0f)
        {
            mul = (2.0f - butteraugliTarget) * (1.0f / 2.0f);
        }

        Span<float> kMul = stackalloc float[4];
        float normSum = 0.0f;
        for (int ii = 0; ii < 4; ++ii)
        {
            kMul[ii] = FuzzyMulBase[ii] + (mul * FuzzyMulAdd[ii]);
            normSum += kMul[ii];
        }

        const float total = 0.29959705784054957f;
        for (int ii = 0; ii < 4; ++ii)
        {
            kMul[ii] *= total / normSum;
        }

        for (int fy = 0; fy < fromRect.YSize; ++fy)
        {
            int y = fy + (int)fromRect.Y0;
            int ym1 = y >= step ? y - step : y;
            int yp1 = y + step < ysize ? y + step : y;
            ReadOnlySpan<float> rowt = from.AsSpan(ym1 * xsize, xsize);
            ReadOnlySpan<float> row = from.AsSpan(y * xsize, xsize);
            ReadOnlySpan<float> rowb = from.AsSpan(yp1 * xsize, xsize);
            Span<float> rowOut = to.DangerousGetRowSpan((int)toRect.Y0 + (fy / 2)).Slice((int)toRect.X0);
            for (int fx = 0; fx < fromRect.XSize; ++fx)
            {
                int x = fx + (int)fromRect.X0;
                int xm1 = x >= step ? x - step : x;
                int xp1 = x + step < xsize ? x + step : x;
                float min0 = row[x];
                float min1 = row[xm1];
                float min2 = row[xp1];
                float min3 = rowt[xm1];

                // Sort the first four values.
                SortPair(ref min0, ref min1);
                SortPair(ref min0, ref min2);
                SortPair(ref min0, ref min3);
                SortPair(ref min1, ref min2);
                SortPair(ref min1, ref min3);
                SortPair(ref min2, ref min3);

                // The remaining five values of a 3x3 neighbourhood.
                StoreMin4(rowt[x], ref min0, ref min1, ref min2, ref min3);
                StoreMin4(rowt[xp1], ref min0, ref min1, ref min2, ref min3);
                StoreMin4(rowb[xm1], ref min0, ref min1, ref min2, ref min3);
                StoreMin4(rowb[x], ref min0, ref min1, ref min2, ref min3);
                StoreMin4(rowb[xp1], ref min0, ref min1, ref min2, ref min3);

                float v = (((kMul[0] * min0) + (kMul[1] * min1)) + (kMul[2] * min2)) + (kMul[3] * min3);
                if (fx % 2 == 0 && fy % 2 == 0)
                {
                    rowOut[fx / 2] = v;
                }
                else
                {
                    rowOut[fx / 2] += v;
                }
            }
        }
    }

    // if (a > b) std::swap(a, b).
    private static void SortPair(ref float a, ref float b)
    {
        if (a > b)
        {
            (a, b) = (b, a);
        }
    }

    private static void StoreMin4(float v, ref float min0, ref float min1, ref float min2, ref float min3)
    {
        if (v < min3)
        {
            if (v < min0)
            {
                min3 = min2;
                min2 = min1;
                min1 = min0;
                min0 = v;
            }
            else if (v < min1)
            {
                min3 = min2;
                min2 = min1;
                min1 = v;
            }
            else if (v < min2)
            {
                min3 = min2;
                min2 = v;
            }
            else
            {
                min3 = v;
            }
        }
    }

    private static void PerBlockModulations(float butteraugliTarget, Buffer2D<float> xybX, Buffer2D<float> xybY, Buffer2D<float> xybB, float scale, JxlRect rectOut, Buffer2D<float> output)
    {
        float baseLevel = 0.48f * scale;
        const float dampenRampStart = 2.0f;
        const float dampenRampEnd = 14.0f;
        float dampen = 1.0f;
        if (butteraugliTarget >= dampenRampStart)
        {
            dampen = 1.0f - ((butteraugliTarget - dampenRampStart) / (dampenRampEnd - dampenRampStart));
            if (dampen < 0)
            {
                dampen = 0;
            }
        }

        float mul = scale * dampen;
        float add = (1.0f - dampen) * baseLevel;
        for (int iy = (int)rectOut.Y0; iy < rectOut.Y0 + rectOut.YSize; iy++)
        {
            int y = iy * 8;
            Span<float> rowOut = output.DangerousGetRowSpan(iy);
            for (int ix = (int)rectOut.X0; ix < rectOut.X0 + rectOut.XSize; ix++)
            {
                int x = ix * 8;
                float maskVal = ComputeMask(rowOut[ix]);
                maskVal = GammaModulation(x, y, xybX, xybY, maskVal);
                float outVal = HfModulation(x, y, xybY, maskVal);
                float blue = BlueModulation(x, y, xybX, xybY, xybB, maskVal);

                outVal = JxlAvx2Lanes.Min(outVal, blue);

                // We want multiplicative quantization field, so everything until this point has been
                // modulating the exponent.
                rowOut[ix] = (JxlFastMath.FastPow2f(outVal * 1.442695041f) * mul) + add;
            }
        }
    }

    // The functions below modulate an exponent (outVal) and return the updated value; libjxl computes them
    // on a vector of 8 lanes, all of which hold the same value.
    private static float ComputeMask(float outVal)
    {
        const float kBase = -0.7647f;
        const float kMul4 = 9.4708735624378946f;
        const float kMul2 = 17.35036561631863f;
        const float kOffset2 = 302.59587815579727f;
        const float kMul3 = 6.7943250517376494f;
        const float kOffset3 = 3.7179635626140772f;
        const float kMul0 = 0.80061762862741759f;
        float kOffset4 = 0.25f * kOffset3;

        // Avoid division by zero.
        float v1 = outVal * kMul0;
        v1 = v1 < 1e-3f ? 1e-3f : v1;
        float v2 = 1.0f / (v1 + kOffset2);
        float v3 = 1.0f / ((v1 * v1) + kOffset3);
        float v4 = 1.0f / ((v1 * v1) + kOffset4);

        // TODO(jyrki):
        // A log or two here could make sense. In butteraugli we have effectively log(log(x + C)) for this
        // kind of use, as a single log is used in saturating visual masking and here the modulation
        // values are exponential, another log would counter that.
        return kBase + ((kMul4 * v4) + ((kMul2 * v2) + (kMul3 * v3)));
    }

    // The opsin space in jxl is the cubic root of photons, i.e., v * v * v is related to the number of
    // photons. SimpleGamma(v * v * v) is the psychovisual space in butteraugli. This ratio allows
    // quantization to move from jxl's opsin space to butteraugli's log-gamma space.
    private static float RatioOfDerivativesOfCubicRootToSimpleGamma(float v, bool invert)
    {
        // Highway's ZeroIfNegative.
        v = v < 0 ? 0 : v;
        float v2 = v * v;
        float num = (SgNumMul * v2) + SgEpsilon;
        float den = ((SgDenMul * v) * v2) + SgVOffsetLog;
        return invert ? num / den : den / num;
    }

    private static float GammaModulation(int x, int y, Buffer2D<float> xybX, Buffer2D<float> xybY, float outVal)
    {
        const float bias = 0.16f;
        Span<float> overallRatio = stackalloc float[Lanes];
        overallRatio.Clear();
        for (int dy = 0; dy < 8; ++dy)
        {
            ReadOnlySpan<float> rowInX = xybX.DangerousGetRowSpan(y + dy).Slice(x, 8);
            ReadOnlySpan<float> rowInY = xybY.DangerousGetRowSpan(y + dy).Slice(x, 8);
            for (int dx = 0; dx < 8; dx++)
            {
                float iny = rowInY[dx] + bias;
                float inx = rowInX[dx];

                float r = iny - inx;
                overallRatio[dx] += RatioOfDerivativesOfCubicRootToSimpleGamma(r, invert: true);

                float g = iny + inx;
                overallRatio[dx] += RatioOfDerivativesOfCubicRootToSimpleGamma(g, invert: true);
            }
        }

        float overall = JxlAvx2Lanes.SumOfLanes(overallRatio) * (0.5f / 64);

        // ideally -1.0, but likely optimal correction adds some entropy, so slightly less than that.
        const float gamma = 0.1005613337192697f;
        return (gamma * JxlFastMath.FastLog2f(overall)) + outVal;
    }

    // Change precision in 8x8 blocks that have significant amounts of blue content (but are not close to
    // solid blue). This is based on the idea that M and L cone activations saturate the S (blue)
    // receptors, and the S reception becomes more important when both M and L levels are low. In that case
    // M and L receptors may be observing S-spectra instead and viewing them with higher spatial accuracy,
    // justifying spending more bits here.
    private static float BlueModulation(int x, int y, Buffer2D<float> planeX, Buffer2D<float> planeY, Buffer2D<float> planeB, float outVal)
    {
        const float limit = 0.010474084867598155f;
        const float offset = 0.0031994768654636393f;
        Span<float> sum = stackalloc float[Lanes];
        sum.Clear();
        for (int dy = 0; dy < 8; ++dy)
        {
            ReadOnlySpan<float> rowInX = planeX.DangerousGetRowSpan(y + dy).Slice(x, 8);
            ReadOnlySpan<float> rowInY = planeY.DangerousGetRowSpan(y + dy).Slice(x, 8);
            ReadOnlySpan<float> rowInB = planeB.DangerousGetRowSpan(y + dy).Slice(x, 8);
            for (int dx = 0; dx < 8; dx++)
            {
                float px = rowInX[dx];
                float pb = rowInB[dx];
                float pyRaw = rowInY[dx] + offset;
                float pyEffective = pyRaw + MathF.Abs(px);
                if (pb > pyEffective)
                {
                    sum[dx] += JxlAvx2Lanes.Min(pb - pyEffective, limit);
                }
            }
        }

        const float mul = 0.90590804735610064f;
        float scalarSum = JxlAvx2Lanes.SumOfLanes(sum);

        // If it is all blue, don't boost the quantization. All blue likely means low frequency blue.
        // Let's not make the most perfect sky ever.
        if (scalarSum >= 32 * limit)
        {
            scalarSum = (64 * limit) - scalarSum;
        }

        const float maxLimit = 15.463398341612438f;
        if (scalarSum >= maxLimit * limit)
        {
            scalarSum = maxLimit * limit;
        }

        scalarSum *= mul;
        return scalarSum + outVal;
    }

    // Change precision in 8x8 blocks that have high frequency content.
    private static float HfModulation(int x, int y, Buffer2D<float> xybY, float outVal)
    {
        // Sums of deltas of y and x components between (approximate) 4-connected pixels.
        const float valminY = 0.0206f;
        Span<float> sumY = stackalloc float[Lanes];
        sumY.Clear();
        for (int dy = 0; dy < 8; ++dy)
        {
            ReadOnlySpan<float> rowInY = xybY.DangerousGetRowSpan(y + dy).Slice(x, 8);
            ReadOnlySpan<float> rowInYNext = dy == 7 ? rowInY : xybY.DangerousGetRowSpan(y + dy + 1).Slice(x, 8);
            for (int dx = 0; dx < 8; dx++)
            {
                float py = rowInY[dx];

                // libjxl masks out the rightmost difference of each row (kMaskRight), which adds 0.
                if (dx < 7)
                {
                    sumY[dx] += JxlAvx2Lanes.Min(valminY, MathF.Abs(py - rowInY[dx + 1]));
                }

                sumY[dx] += JxlAvx2Lanes.Min(valminY, MathF.Abs(py - rowInYNext[dx]));
            }
        }

        const float mulY = -0.38f;
        float scalarSumY = JxlAvx2Lanes.SumOfLanes(sumY);
        scalarSumY *= mulY;

        // higher value -> more bpp
        const float offset = 0.42f;
        scalarSumY += offset;

        return scalarSumY + outVal;
    }

    private static float MaskingSqrt(float v) => 0.25f * MathF.Sqrt((v * MaskingSqrtMul) + MaskingLogOffset);

    // Blur the mask1x1 to obtain the masking image. Before blurring it contains an image of absolute
    // value of the Laplacian of the intensity channel.
    private static Buffer2D<float> Blur1x1Masking(Buffer2D<float> mask1x1, MemoryAllocator allocator)
    {
        float[] k = FilterMask1x1;

        // The inner sum is in float; the 1.0 + makes it a double.
        double sum = 1.0 + (4 * ((((k[0] + k[1]) + k[2]) + k[4]) + (2 * k[3])));
        if (sum < 1e-5)
        {
            sum = 1e-5;
        }

        float normalize = (float)(1.0 / sum);
        JxlWeightsSymmetric5 weights = new(normalize, normalize * k[0], normalize * k[2], normalize * k[1], normalize * k[4], normalize * k[3]);
        Buffer2D<float> temp = allocator.Allocate2D<float>(mask1x1.Width, mask1x1.Height);
        JxlRect rect = new(0, 0, mask1x1.Width, mask1x1.Height);
        JxlConvolve.Symmetric5(mask1x1, rect, weights, temp, rect);
        return temp;
    }
}
