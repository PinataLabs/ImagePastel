// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_heuristics.cc (LossyFrameHeuristics at efforts 3 and 5, with the effort 5 searches in their
// effort 3 form for now, and ComputeARHeuristics),
// lib/jxl/enc_adaptive_quantization.cc (InitialQuantDC; AdjustQuantField and FindBestQuantizer, which do
// nothing at effort 3), lib/jxl/enc_ac_strategy.cc (AcStrategyHeuristics::Init and ProcessRect from Cheetah up)
// and lib/jxl/quantizer.cc (SetQuantFieldRect).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The encoder's choices for a lossy frame before the coefficients: the quantiser, the strategy of each
/// block, the quant field, the chroma-from-luma map and the EPF sharpness. libjxl gates each search by
/// speed tier; from Falcon (effort 3) up, each is a constant. At Hare (effort 5) the opsin is also
/// inverse-Gaborish filtered.
/// </summary>
internal static class JxlEncHeuristics
{
    // Butteraugli target where non-linearity kicks in.
    private const float DcMul = 0.3f;
    private const float DcQuantPow = 0.83f;
    private const float DcQuant = 1.095924047623553f;

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
    /// Temporary departures (E4a, the Default tracer): from Hare down libjxl computes an adaptive quant
    /// field and searches the AC strategies. Until the tickets that port them land, each runs its Falcon
    /// form here, at every tier: the constant field of <c>q = 0.79 / d</c> (E4b) and DCT8 everywhere (E4g).
    /// Each is marked where it runs. The chroma-from-luma map of each tile and the block context map are
    /// chosen from Hare down, as libjxl does.
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

        // Compute an initial estimate of the quantization field. InitialQuantField runs only in Hare mode
        // or slower; Falcon sets a constant. libjxl's masking field only feeds the AC strategy search.
        // Temporary departure (E4a): Hare takes Falcon's constant field and its 0.79 / d until E4b ports
        // InitialQuantField (on the opsin before Gaborish) and the global scale from 0.39 / d.
        // A double quotient rounded to float, as libjxl's `float q = 0.79 / cparams.butteraugli_distance`.
        float q = (float)(0.79 / encState.Distance);
        encState.Quantizer.ComputeGlobalScaleAndQuant(quantDc, q, 0);

        // Apply inverse-gaborish.
        if (gaborish)
        {
            // Changing the weight here to 0.99f would help to reduce ringing in generation loss.
            JxlEncGaborish.GaborishInverse(opsin, GaborishWeights, allocator);
        }

        // libjxl's FindBestDequantMatrices resets the matrices to the defaults, which the state already holds.

        // Choose block sizes: DCT8 everywhere from Cheetah up (AcStrategyHeuristics::ProcessRect).
        // Temporary departure (E4a): Hare fills DCT8 too until E4g ports the search, and Init computes
        // only the DCT8 matrices (libjxl's Hare computes every type below 128x128).
        encState.Matrices.EnsureComputed(1);
        encState.AcStrategy.FillDct8();

        // AdjustQuantField only changes the field under blocks larger than 8x8, so it leaves q.
        Array.Fill(encState.RawQuantField, encState.Quantizer.RawQuantField(q));

        // Compute a non-default CfL map if we are at Hare speed, or slower. libjxl fits each tile right after
        // its strategies and quant field; the fit reads only its own tile, so it runs here for all of them.
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
