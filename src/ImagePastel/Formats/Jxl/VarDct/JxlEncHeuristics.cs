// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_heuristics.cc (LossyFrameHeuristics at efforts 3, 5 and 7, and ComputeARHeuristics),
// lib/jxl/enc_adaptive_quantization.cc (InitialQuantDC; FindBestQuantizer, which acts only from kKitten
// down, effort 8 and up, so it is not ported) and lib/jxl/quantizer.cc (SetQuantFieldRect).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.VarDct;

/// <summary>
/// The encoder's choices for a lossy frame before the coefficients: the quantiser, the strategy of each
/// block, the quant field, the chroma-from-luma map, the block context map and the EPF sharpness. libjxl
/// gates each search by speed tier; <see cref="JxlFrameEncoder"/> lists what each effort does.
/// </summary>
internal static class JxlEncHeuristics
{
    // Butteraugli target where non-linearity kicks in.
    private const float DcMul = 0.3f;
    private const float DcQuantPow = 0.83f;
    private const float DcQuant = 1.095924047623553f;

    // libjxl's kEncTileDimInBlocks: the heuristics run per tile of 8x8 blocks (64x64 pixels).
    private const int EncTileDimInBlocks = 8;

    // libjxl's kMinButteraugliForDynamicAR.
    private const float MinButteraugliForDynamicAr = 0.5f;

    // ComputeARHeuristics: kNumEPFVals, and kFavorNoSmoothing and the constants of the multipliers, written
    // as doubles and stored as floats (so each is rounded from the double, as libjxl's are).
    private const int NumEpfVals = 8;
    private const double FavorNoSmoothingDouble = 0.99;
    private const double C3BaseDouble = 0.98017198824148288;
    private const double C3ClampDouble = 0.85970338919928291;
    private const double C5Double = 0.1087690359555803;
    private const float FavorNoSmoothing = (float)FavorNoSmoothingDouble;
    private const float C3Base = (float)C3BaseDouble;
    private const float C3Clamp = (float)C3ClampDouble;
    private const float C5 = (float)C5Double;

    // ComputeBlockL2Distance's channel weights, doubles.
    private static readonly double[] BlockL2Weights = { 12.339445295782363, 1.0, 0.2 };

    // The weight of the inverse Gaborish filter of each plane.
    private static readonly float[] GaborishWeights = { 1.0f, 1.0f, 1.0f };

    /// <summary>Returns the DC quant for a distance, as libjxl's <c>InitialQuantDC</c>.</summary>
    /// <param name="butteraugliTarget">The distance.</param>
    /// <returns>The DC quant.</returns>
    public static float InitialQuantDc(float butteraugliTarget)
    {
        float butteraugliTargetDc = MathF.Max(
            0.5f * butteraugliTarget,
            MathF.Min(butteraugliTarget, DcMul * MathF.Pow((1.0f / DcMul) * butteraugliTarget, DcQuantPow)));

        // We want the maximum DC value to be at most 2**15 * kInvDCQuant / quant_dc. The maximum DC value
        // might not be in the kXybRange because of inverse gaborish, so we add some slack to the maximum
        // theoretical quant obtained this way (64).
        return MathF.Min(DcQuant / butteraugliTargetDc, 50.0f);
    }

    /// <summary>
    /// Makes the choices of libjxl's <c>LossyFrameHeuristics</c> for one frame, not streamed, in libjxl's
    /// order: the global scale and DC quantisation, the inverse Gaborish filter when the frame has
    /// Gaborish, the strategy of each block, the quant field, the chroma-from-luma map and the block
    /// context map. The dequantization matrices keep their defaults.
    /// </summary>
    /// <param name="encState">The encoder state.</param>
    /// <param name="gaborish">Whether the frame's loop filter has Gaborish, so the opsin is inverse-filtered.</param>
    /// <param name="opsin">The padded XYB planes; replaced by their inverse-Gaborish copies when <paramref name="gaborish"/> is set.</param>
    /// <param name="allocator">The allocator.</param>
    public static void LossyFrameHeuristics(JxlPassesEncoderState encState, bool gaborish, Buffer2D<float>[] opsin, MemoryAllocator allocator)
    {
        JxlSpeedTier speedTier = encState.SpeedTier;

        // Find and subtract splines. libjxl does this from Squirrel down (speed_tier <= kSquirrel), but its
        // FindSplines returns an empty set at ef67fde2 ("TODO: implement spline detection",
        // enc_splines.cc:103-106), and the encoder has no custom splines. Drawing and subtracting no splines
        // leaves the opsin as it is, and the frame keeps its splines flag off, so the step is a no-op at
        // every tier and nothing runs here.

        // Departure (ADR 0001): from Squirrel down libjxl also finds patches and dots
        // (FindBestPatchDictionary) and subtracts them from the opsin; docs/jxl/spec.md ("Out of scope")
        // leaves them out. Measured with cjxl v0.12.0 -e 7, --patches=0 against --patches=1 on the
        // calibration images: only text changes, 1.18x the size and 0.34 (d1), 1.49 (d2) and 1.18 (d4)
        // lower in SSIMULACRA2, within the comparer's slack; line_art and the photos are byte-identical.
        float quantDc = InitialQuantDc(encState.Distance);

        // Compute an initial estimate of the quantization field. Call InitialQuantField only in Hare mode or
        // slower; Falcon sets a constant.
        if (speedTier > JxlSpeedTier.Hare)
        {
            encState.InitialQuantField = allocator.Allocate2D<float>(encState.XSizeBlocks, encState.YSizeBlocks);

            // A double quotient rounded to float, as libjxl's `float q = 0.79 / cparams.butteraugli_distance`.
            float q = (float)(0.79 / encState.Distance);
            for (int y = 0; y < encState.YSizeBlocks; y++)
            {
                encState.InitialQuantField.DangerousGetRowSpan(y).Fill(q);
            }

            // libjxl also fills its masking image with 1 / (q + 0.001); only the AC strategy search reads it,
            // which does not run here.
            encState.Quantizer.ComputeGlobalScaleAndQuant(quantDc, q, 0);
        }
        else
        {
            // Call this here, as it relies on pre-gaborish values.
            float butteraugliDistanceForIqf = encState.Distance;
            if (!gaborish)
            {
                butteraugliDistanceForIqf *= 0.62f;
            }

            encState.InitialQuantField = JxlEncAdaptiveQuantization.InitialQuantField(butteraugliDistanceForIqf, opsin, 1.0f, allocator, out Buffer2D<float> mask1x1);
            encState.InitialQuantMasking1x1 = mask1x1;
            float q = (float)(0.39 / encState.Distance);
            encState.Quantizer.ComputeGlobalScaleAndQuant(quantDc, q, 0);
        }

        // Apply inverse-gaborish.
        if (gaborish)
        {
            // Changing the weight here to 0.99f would help to reduce ringing in generation loss.
            JxlEncGaborish.GaborishInverse(opsin, GaborishWeights, allocator);
        }

        // libjxl's FindBestDequantMatrices resets the matrices to the defaults, which the state already holds.
        JxlAcsConfig acsConfig = JxlEncAcStrategy.Init(speedTier, encState.Distance, opsin, encState.InitialQuantField, encState.InitialQuantMasking1x1, encState.Matrices);
        JxlAcsScratch acsScratch = speedTier < JxlSpeedTier.Cheetah ? new JxlAcsScratch() : null;

        // Per 64x64 tile, in tile order (libjxl's process_tile): a first CfL map from Squirrel down, then
        // choose block sizes, then always set the initial quant field, so we can compute the CfL map with
        // more accuracy, then that map from Hare down. The search reads the float field of its own tile
        // before AdjustQuantField changes it. The initial quant field might change in slower modes, but
        // adjusting the quant field with butteraugli when all the other encoding parameters are fixed is
        // likely a more reliable choice anyway. Each CfL fit writes the map entry of its tile, which is the
        // entry that tile's search reads, so the order within a tile is libjxl's.
        DebugGuard.IsTrue(EncTileDimInBlocks == JxlColorCorrelation.ColorTileDimInBlocks, "An encoder tile is a colour tile, so each CfL fit covers one map entry");
        JxlEncChromaFromLuma.Scratch cflScratch = speedTier <= JxlSpeedTier.Hare ? new JxlEncChromaFromLuma.Scratch() : null;
        int numTilesX = (int)JxlBits.DivCeil(encState.XSizeBlocks, EncTileDimInBlocks);
        int numTiles = numTilesX * (int)JxlBits.DivCeil(encState.YSizeBlocks, EncTileDimInBlocks);
        for (int tid = 0; tid < numTiles; tid++)
        {
            int tx = tid % numTilesX;
            int ty = tid / numTilesX;
            int by0 = ty * EncTileDimInBlocks;
            int by1 = Math.Min((ty + 1) * EncTileDimInBlocks, encState.YSizeBlocks);
            int bx0 = tx * EncTileDimInBlocks;
            int bx1 = Math.Min((tx + 1) * EncTileDimInBlocks, encState.XSizeBlocks);
            JxlRect r = new(bx0, by0, bx1 - bx0, by1 - by0);

            // For speeds up to Wombat, we only compute the color correlation map once we know the transform
            // type and the quantization map. Slower than that, a first map on DCT8 blocks with no quant
            // field steers the search.
            if (speedTier <= JxlSpeedTier.Squirrel)
            {
                JxlEncChromaFromLuma.ComputeTile(encState, opsin, r, useDct8: true, fast: false, cflScratch);
            }

            // Choose block sizes.
            JxlEncAcStrategy.ProcessRect(acsConfig, speedTier, encState.Distance, r, encState.Cmap, encState.AcStrategy, acsScratch);
            JxlEncAdaptiveQuantization.AdjustQuantField(encState.AcStrategy, r, encState.Distance, encState.InitialQuantField);
            encState.Quantizer.SetQuantFieldRect(encState.InitialQuantField, r, encState.RawQuantField, encState.XSizeBlocks);

            // Compute a non-default CfL map if we are at Hare speed, or slower.
            if (speedTier <= JxlSpeedTier.Hare)
            {
                JxlEncChromaFromLuma.ComputeTile(encState, opsin, r, useDct8: false, fast: speedTier >= JxlSpeedTier.Wombat, cflScratch);
            }
        }

        // Refine quantization levels: libjxl fills the EPF sharpness with 4 for FindBestQuantizer, which only
        // searches from Kitten down. ComputeArHeuristics sets the sharpness before anything reads it, so
        // this skips the fill.

        // Choose a context model that depends on the amount of quantization for AC, on the raw quant field
        // before AdjustQuantBlockAC changes it. The frame is never streamed, so it initializes the global
        // state.
        if (speedTier < JxlSpeedTier.Falcon)
        {
            JxlEncBlockCtxMap.FindBestBlockEntropyModel(speedTier, encState.Distance, encState.RawQuantField, encState.AcStrategy, encState.BlockCtxMap);
        }
    }

    /// <summary>
    /// Gets whether <see cref="ComputeArHeuristics"/> searches the EPF sharpness, so it reads the
    /// pre-Gaborish opsin: from Wombat down, from distance 0.5, on a frame with EPF. Otherwise the sharpness
    /// is a constant 4.
    /// </summary>
    /// <param name="encState">The encoder state.</param>
    /// <param name="epfIters">The frame's EPF iterations.</param>
    /// <returns>Whether the sharpness is searched.</returns>
    public static bool UsesDynamicAr(JxlPassesEncoderState encState, uint epfIters)
        => !(encState.Distance < MinButteraugliForDynamicAr || encState.SpeedTier > JxlSpeedTier.Wombat || epfIters == 0);

    /// <summary>
    /// Sets the EPF sharpness of each block, as libjxl's <c>ComputeARHeuristics</c>: a constant 4 unless the
    /// encoder is at Wombat or slower, with EPF on, above a small distance. Then it reconstructs the frame
    /// once per EPF step with every block at that step, measures each block's error against the
    /// pre-Gaborish opsin, and chooses each block's step from the errors in two passes.
    /// </summary>
    /// <param name="frameHeader">The frame header the encoder writes.</param>
    /// <param name="encState">The encoder state, once the coefficients and the DC are computed.</param>
    /// <param name="origOpsin">
    /// The opsin before <see cref="LossyFrameHeuristics"/> (pre-Gaborish), exactly the frame size, or
    /// <see langword="null"/> when <see cref="UsesDynamicAr"/> is false.
    /// </param>
    /// <param name="allocator">The allocator.</param>
    public static void ComputeArHeuristics(JxlFrameHeader frameHeader, JxlPassesEncoderState encState, Buffer2D<float>[] origOpsin, MemoryAllocator allocator)
    {
        if (!UsesDynamicAr(encState, frameHeader.LoopFilter.EpfIters))
        {
            Array.Fill(encState.EpfSharpness, (byte)4);
            return;
        }

        int xsizeBlocks = encState.XSizeBlocks;
        int ysizeBlocks = encState.YSizeBlocks;
        EpfStepSet epfSteps = EpfSteps(encState.Distance);
        float[][] errorImages = new float[NumEpfVals][];
        foreach (byte val in epfSteps.Values)
        {
            Array.Fill(encState.EpfSharpness, val);
            Buffer2D<float>[] decoded = JxlEncReconstruct.ReconstructImage(frameHeader, encState, allocator);
            try
            {
                float[] errors = new float[xsizeBlocks * ysizeBlocks];
                for (int by = 0; by < ysizeBlocks; by++)
                {
                    for (int bx = 0; bx < xsizeBlocks; bx++)
                    {
                        errors[(by * xsizeBlocks) + bx] = ComputeBlockL2Distance(origOpsin, decoded, encState.InitialQuantMasking1x1, by, bx);
                    }
                }

                errorImages[val] = errors;
            }
            finally
            {
                JxlPlanes.DisposeAll(decoded);
            }
        }

        ChooseEpfSharpness(encState.Distance, epfSteps, errorImages, encState.EpfSharpness, xsizeBlocks, ysizeBlocks);
    }

    /// <summary>
    /// Returns the masked squared error of one block between two opsin images, as libjxl's
    /// <c>ComputeBlockL2Distance</c>: the block's 8x8 rect, clipped to <paramref name="a"/>'s size, with
    /// each sample weighted by the square of its mask, and the three channels weighted by
    /// 12.339445295782363, 1 and 0.2.
    /// </summary>
    /// <param name="a">The first image (the original opsin), whose size clips the block.</param>
    /// <param name="b">The second image (the reconstruction), at least <paramref name="a"/>'s size.</param>
    /// <param name="mask1x1">The per-pixel masking (<see cref="JxlPassesEncoderState.InitialQuantMasking1x1"/>).</param>
    /// <param name="by">The block row.</param>
    /// <param name="bx">The block column.</param>
    /// <returns>The block's error.</returns>
    public static float ComputeBlockL2Distance(Buffer2D<float>[] a, Buffer2D<float>[] b, Buffer2D<float> mask1x1, int by, int bx)
    {
        // Rect's constructor clamps the rect to the image.
        int x0 = bx * JxlFrameDimensions.BlockDim;
        int y0 = by * JxlFrameDimensions.BlockDim;
        int xsize = Math.Max(0, Math.Min(JxlFrameDimensions.BlockDim, a[0].Width - x0));
        int ysize = Math.Max(0, Math.Min(JxlFrameDimensions.BlockDim, a[0].Height - y0));
        float err0 = 0.0f;
        float err1 = 0.0f;
        float err2 = 0.0f;
        for (int y = 0; y < ysize; ++y)
        {
            Span<float> rowA0 = a[0].DangerousGetRowSpan(y0 + y).Slice(x0, xsize);
            Span<float> rowA1 = a[1].DangerousGetRowSpan(y0 + y).Slice(x0, xsize);
            Span<float> rowA2 = a[2].DangerousGetRowSpan(y0 + y).Slice(x0, xsize);
            Span<float> rowB0 = b[0].DangerousGetRowSpan(y0 + y).Slice(x0, xsize);
            Span<float> rowB1 = b[1].DangerousGetRowSpan(y0 + y).Slice(x0, xsize);
            Span<float> rowB2 = b[2].DangerousGetRowSpan(y0 + y).Slice(x0, xsize);
            Span<float> rowMask = mask1x1.DangerousGetRowSpan(y0 + y).Slice(x0, xsize);
            for (int x = 0; x < xsize; ++x)
            {
                float mask = rowMask[x];
                float mask2 = mask * mask;
                float diff = rowA0[x] - rowB0[x];
                err0 += mask2 * diff * diff;
                diff = rowA1[x] - rowB1[x];
                err1 += mask2 * diff * diff;
                diff = rowA2[x] - rowB2[x];
                err2 += mask2 * diff * diff;
            }
        }

        // The weights are doubles, so the sum is a double rounded to float once.
        return (float)((BlockL2Weights[0] * err0) + (BlockL2Weights[1] * err1) + (BlockL2Weights[2] * err2));
    }

    /// <summary>Returns the EPF sharpness values the search tries at a distance: {0, 4} above 4.5, else {0, 2, 7}.</summary>
    /// <param name="distance">The distance.</param>
    /// <returns>The steps, in the order they are tried, with their lookup table.</returns>
    public static EpfStepSet EpfSteps(float distance)
        => new(distance > 4.5f ? new byte[] { 0, 4 } : new byte[] { 0, 2, 7 });

    /// <summary>
    /// Chooses each block's EPF sharpness from the error of each step, as the end of libjxl's
    /// <c>ComputeARHeuristics</c>: the first pass, the multipliers from its context histogram, then the
    /// second pass.
    /// </summary>
    /// <param name="distance">The distance.</param>
    /// <param name="epfSteps">The steps of the distance (<see cref="EpfSteps"/>).</param>
    /// <param name="errorImages">The error of each block, row-major, indexed by step value; only the steps' entries are read.</param>
    /// <param name="sharpness">The sharpness of each block, row-major, which receives the choice.</param>
    /// <param name="xsizeBlocks">The width in blocks.</param>
    /// <param name="ysizeBlocks">The height in blocks.</param>
    public static void ChooseEpfSharpness(float distance, EpfStepSet epfSteps, float[][] errorImages, byte[] sharpness, int xsizeBlocks, int ysizeBlocks)
    {
        float clampedButteraugli = MathF.Min(5.0f, distance);
        EpfContextHistogram histogram = ChooseEpfSharpnessFirstPass(epfSteps, errorImages, sharpness, xsizeBlocks, ysizeBlocks);
        float[] mul = EpfMultipliers(epfSteps, clampedButteraugli, histogram);
        ChooseEpfSharpnessSecondPass(epfSteps, errorImages, mul, sharpness, xsizeBlocks, ysizeBlocks);
    }

    /// <summary>
    /// The first pass of the choice: each block takes the step of least error, with step 0's error scaled
    /// by 0.99, unless its top or left neighbour's step gives no more error in this block. It counts each
    /// block's choice in the context of its top and left neighbours.
    /// </summary>
    /// <param name="epfSteps">The steps.</param>
    /// <param name="errorImages">The error of each block, indexed by step value.</param>
    /// <param name="sharpness">The sharpness of each block, which receives the choice.</param>
    /// <param name="xsizeBlocks">The width in blocks.</param>
    /// <param name="ysizeBlocks">The height in blocks.</param>
    /// <returns>The context histogram.</returns>
    public static EpfContextHistogram ChooseEpfSharpnessFirstPass(EpfStepSet epfSteps, float[][] errorImages, byte[] sharpness, int xsizeBlocks, int ysizeBlocks)
    {
        int[] epfStepsLut = epfSteps.Lut;
        EpfContextHistogram histogram = new();
        for (int by = 0; by < ysizeBlocks; by++)
        {
            int row = by * xsizeBlocks;
            int prevRow = (by > 0 ? by - 1 : 0) * xsizeBlocks;
            for (int bx = 0; bx < xsizeBlocks; bx++)
            {
                int i = row + bx;
                byte bestVal = 0;
                float bestError = float.MaxValue;
                byte topVal = by > 0 ? sharpness[prevRow + bx] : (byte)0;
                byte leftVal = bx > 0 ? sharpness[i - 1] : (byte)0;
                float topError = errorImages[topVal][i];
                float leftError = errorImages[leftVal][i];
                foreach (byte val in epfSteps.Values)
                {
                    float error = errorImages[val][i];
                    if (val == 0)
                    {
                        error *= FavorNoSmoothing;
                    }

                    if (error < bestError)
                    {
                        bestVal = val;
                        bestError = error;
                    }
                }

                if (bestError < MathF.Min(topError, leftError))
                {
                    sharpness[i] = bestVal;
                }
                else if (topError < leftError)
                {
                    sharpness[i] = topVal;
                }
                else
                {
                    sharpness[i] = leftVal;
                }

                int context = (epfStepsLut[topVal] * 3) + epfStepsLut[leftVal];
                ++histogram.Counts[(context * NumEpfVals) + sharpness[i]];
                ++histogram.Totals[context];
            }
        }

        return histogram;
    }

    /// <summary>
    /// The multiplier of each step in each context, laid out as libjxl's <c>mul</c>:
    /// <c>lut[val] + 3 * (lut[top] * 3 + lut[left])</c>.
    /// </summary>
    /// <param name="epfSteps">The steps.</param>
    /// <param name="clampedButteraugli">The distance, at most 5.</param>
    /// <param name="histogram">The first pass's context histogram.</param>
    /// <returns>The 27 multipliers; those of contexts the steps do not reach are 0.</returns>
    public static float[] EpfMultipliers(EpfStepSet epfSteps, float clampedButteraugli, EpfContextHistogram histogram)
    {
        int[] epfStepsLut = epfSteps.Lut;

        // std::pow of two floats is the float pow.
        float c3 = MathF.Max(C3Clamp, MathF.Pow(C3Base, clampedButteraugli));
        float[] mul = new float[3 * 3 * 3];
        foreach (byte topVal in epfSteps.Values)
        {
            foreach (byte leftVal in epfSteps.Values)
            {
                foreach (byte val in epfSteps.Values)
                {
                    int context = (epfStepsLut[topVal] * 3) + epfStepsLut[leftVal];
                    int mulix = epfStepsLut[val] + (3 * context);

                    // libjxl divides two size_t counts, so the ratio is an integer, and 0 because every
                    // count is below its total, which starts at 1. std::log1p of an integer is the double
                    // overload, so the whole expression is a double, rounded to float.
                    ulong ratio = histogram.Counts[(context * NumEpfVals) + val] / histogram.Totals[context];
                    mul[mulix] = (float)(1.0 / (1.0 + ((C5 * Math.Log(1.0 + ratio)) / clampedButteraugli)));
                    if (val == 0)
                    {
                        mul[mulix] *= c3;
                    }
                }
            }
        }

        return mul;
    }

    /// <summary>
    /// The second pass of the choice: each block takes the step of least error times the step's
    /// multiplier in the context of its top and left neighbours, as this pass chose them.
    /// </summary>
    /// <param name="epfSteps">The steps.</param>
    /// <param name="errorImages">The error of each block, indexed by step value.</param>
    /// <param name="mul">The multipliers (<see cref="EpfMultipliers"/>).</param>
    /// <param name="sharpness">The first pass's sharpness of each block, which receives the choice.</param>
    /// <param name="xsizeBlocks">The width in blocks.</param>
    /// <param name="ysizeBlocks">The height in blocks.</param>
    public static void ChooseEpfSharpnessSecondPass(EpfStepSet epfSteps, float[][] errorImages, float[] mul, byte[] sharpness, int xsizeBlocks, int ysizeBlocks)
    {
        int[] epfStepsLut = epfSteps.Lut;
        for (int by = 0; by < ysizeBlocks; by++)
        {
            int row = by * xsizeBlocks;
            int prevRow = (by > 0 ? by - 1 : 0) * xsizeBlocks;
            for (int bx = 0; bx < xsizeBlocks; bx++)
            {
                int i = row + bx;
                byte bestVal = 0;
                float bestError = float.MaxValue;
                byte topVal = by > 0 ? sharpness[prevRow + bx] : (byte)0;
                byte leftVal = bx > 0 ? sharpness[i - 1] : (byte)0;
                int context = (epfStepsLut[topVal] * 3) + epfStepsLut[leftVal];
                foreach (byte val in epfSteps.Values)
                {
                    int mulix = epfStepsLut[val] + (3 * context);
                    float error = errorImages[val][i] * mul[mulix];
                    if (error < bestError)
                    {
                        bestVal = val;
                        bestError = error;
                    }
                }

                sharpness[i] = bestVal;
            }
        }
    }

    /// <summary>
    /// The EPF sharpness values the search tries (libjxl's <c>epf_steps</c>), in the order they are tried,
    /// and libjxl's <c>epf_steps_lut</c> for them, built once per frame.
    /// </summary>
    internal sealed class EpfStepSet
    {
        /// <summary>Initializes a new instance of the <see cref="EpfStepSet"/> class.</summary>
        /// <param name="values">The steps, in the order they are tried.</param>
        public EpfStepSet(byte[] values)
        {
            this.Values = values;
            this.Lut = new int[NumEpfVals];
            for (int i = 0; i < values.Length; i++)
            {
                this.Lut[values[i]] = i;
            }
        }

        /// <summary>Gets the steps, in the order they are tried.</summary>
        public byte[] Values { get; }

        /// <summary>Gets the index of each step value in <see cref="Values"/>, 0 for the other values.</summary>
        public int[] Lut { get; }
    }

    /// <summary>
    /// The first pass's count of each chosen value in each of the 9 contexts of the top and left
    /// neighbours' steps (libjxl's <c>histo</c> and <c>totals</c>, both <c>size_t</c>).
    /// </summary>
    internal sealed class EpfContextHistogram
    {
        /// <summary>Gets the count of each value in each context, at <c>context * 8 + value</c>.</summary>
        public ulong[] Counts { get; } = new ulong[9 * NumEpfVals];

        /// <summary>Gets the number of blocks in each context, plus 1.</summary>
        public ulong[] Totals { get; } = { 1, 1, 1, 1, 1, 1, 1, 1, 1 };
    }
}
