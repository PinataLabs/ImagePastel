// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_heuristics.cc (LossyFrameHeuristics at efforts 3 and 5, without the AC-strategy merges of
// effort 5 for now, and ComputeARHeuristics),
// lib/jxl/enc_adaptive_quantization.cc (InitialQuantDC; FindBestQuantizer, which does nothing above
// effort 7) and lib/jxl/quantizer.cc (SetQuantFieldRect).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The encoder's choices for a lossy frame before the coefficients: the quantiser, the strategy of each
/// block, the quant field, the chroma-from-luma map and the EPF sharpness. libjxl gates each search by
/// speed tier; from Falcon (effort 3) up, each is a constant. At Hare (effort 5) the quant field adapts to
/// the image (<see cref="JxlEncAdaptiveQuantization"/>) and the opsin is inverse-Gaborish filtered.
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
    /// <remarks>
    /// From Hare down the strategy of each block is searched (<see cref="JxlEncAcStrategy"/>), and the
    /// adaptive quant field, the chroma-from-luma map of each tile and the block context map are computed,
    /// as libjxl does. Temporary departure (E4g): the search keeps each block's 8x8 choice, without libjxl's
    /// merges into larger blocks, until E4h.
    /// </remarks>
    /// <param name="encState">The encoder state.</param>
    /// <param name="gaborish">Whether the frame's loop filter has Gaborish, so the opsin is inverse-filtered.</param>
    /// <param name="opsin">The padded XYB planes; replaced by their inverse-Gaborish copies when <paramref name="gaborish"/> is set.</param>
    /// <param name="allocator">The allocator.</param>
    public static void LossyFrameHeuristics(JxlPassesEncoderState encState, bool gaborish, Buffer2D<float>[] opsin, MemoryAllocator allocator)
    {
        JxlSpeedTier speedTier = encState.SpeedTier;

        // Splines and patches are found from Squirrel down.
        if (speedTier <= JxlSpeedTier.Squirrel)
        {
            JxlThrowHelper.ThrowNotSupportedException("Lossy splines and patches (libjxl effort 7 and up; slice E5)");
        }

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

        // Per 64x64 tile, in tile order (libjxl's process_tile): choose block sizes, then always set the
        // initial quant field, so we can compute the CfL map with more accuracy. The search reads the float
        // field of its own tile before AdjustQuantField changes it. The initial quant field might change in
        // slower modes, but adjusting the quant field with butteraugli when all the other encoding
        // parameters are fixed is likely a more reliable choice anyway.
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

            // The chroma-from-luma map is still the default here, as libjxl's is before this tile's fit.
            JxlEncAcStrategy.ProcessRect(acsConfig, speedTier, encState.Distance, r, encState.Cmap, encState.AcStrategy, acsScratch);
            JxlEncAdaptiveQuantization.AdjustQuantField(encState.AcStrategy, r, encState.Distance, encState.InitialQuantField);
            encState.Quantizer.SetQuantFieldRect(encState.InitialQuantField, r, encState.RawQuantField, encState.XSizeBlocks);
        }

        // Compute a non-default CfL map if we are at Hare speed, or slower. libjxl fits each tile right after
        // its strategies and quant field; the fit reads only its own tile, and the search of a later tile
        // reads only that tile's map entry, so it runs here for all of them.
        if (speedTier <= JxlSpeedTier.Hare)
        {
            JxlEncChromaFromLuma.ComputeMap(encState, opsin, fast: speedTier >= JxlSpeedTier.Wombat);
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
    /// Sets the EPF sharpness of each block, as libjxl's <c>ComputeARHeuristics</c>: a constant 4 unless the
    /// encoder is at Wombat or slower, with EPF on, above a small distance.
    /// </summary>
    /// <param name="encState">The encoder state.</param>
    /// <param name="epfIters">The frame's EPF iterations.</param>
    public static void ComputeArHeuristics(JxlPassesEncoderState encState, uint epfIters)
    {
        if (encState.Distance < MinButteraugliForDynamicAr || encState.SpeedTier > JxlSpeedTier.Wombat || epfIters == 0)
        {
            Array.Fill(encState.EpfSharpness, (byte)4);
            return;
        }

        JxlThrowHelper.ThrowNotSupportedException("The EPF sharpness search (libjxl effort 6 and up; slice E5)");
    }
}
