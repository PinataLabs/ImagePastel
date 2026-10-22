// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_heuristics.cc (the effort 3 path of LossyFrameHeuristics and ComputeARHeuristics),
// lib/jxl/enc_adaptive_quantization.cc (InitialQuantDC; AdjustQuantField and FindBestQuantizer, which do
// nothing at effort 3), lib/jxl/enc_ac_strategy.cc (AcStrategyHeuristics::Init and ProcessRect from Cheetah up)
// and lib/jxl/quantizer.cc (SetQuantFieldRect).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The encoder's choices for a lossy frame before the coefficients: the quantiser, the strategy of each
/// block, the quant field, the chroma-from-luma map and the EPF sharpness. libjxl gates each search by
/// speed tier; from Falcon (effort 3) up, each is a constant.
/// </summary>
internal static class JxlEncHeuristics
{
    // Butteraugli target where non-linearity kicks in.
    private const float DcMul = 0.3f;
    private const float DcQuantPow = 0.83f;
    private const float DcQuant = 1.095924047623553f;

    // libjxl's kMinButteraugliForDynamicAR.
    private const float MinButteraugliForDynamicAr = 0.5f;

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
    /// Makes the choices of libjxl's <c>LossyFrameHeuristics</c> for one frame, not streamed, with
    /// Gaborish off: the global scale and DC quantisation, DCT8 for every block, the quant field, and the
    /// EPF sharpness. The dequantization matrices, the chroma-from-luma map and the block context map keep
    /// their defaults.
    /// </summary>
    /// <param name="encState">The encoder state.</param>
    public static void LossyFrameHeuristics(JxlPassesEncoderState encState)
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
        if (speedTier <= JxlSpeedTier.Hare)
        {
            JxlThrowHelper.ThrowNotSupportedException("The adaptive quantization field (libjxl effort 5 and up; slice E4)");
        }

        // A double quotient rounded to float, as libjxl's `float q = 0.79 / cparams.butteraugli_distance`.
        float q = (float)(0.79 / encState.Distance);
        encState.Quantizer.ComputeGlobalScaleAndQuant(quantDc, q, 0);

        // Inverse Gaborish runs when the loop filter has Gaborish, which is off above Hare. libjxl's
        // FindBestDequantMatrices resets the matrices to the defaults, which the state already holds.
        // The chroma-from-luma map is computed from Hare down, so it keeps its defaults.

        // Choose block sizes: DCT8 everywhere from Cheetah up (AcStrategyHeuristics::ProcessRect).
        if (speedTier < JxlSpeedTier.Cheetah)
        {
            JxlThrowHelper.ThrowNotSupportedException("The AC strategy search (libjxl effort 5 and up; slice E4)");
        }

        // AcStrategyHeuristics::Init: DCT8 only.
        encState.Matrices.EnsureComputed(1);
        encState.AcStrategy.FillDct8();

        // AdjustQuantField only changes the field under blocks larger than 8x8, so it leaves q.
        Array.Fill(encState.RawQuantField, encState.Quantizer.RawQuantField(q));

        // Refine quantization levels. libjxl's FindBestQuantizer only runs from Kitten down.
        Array.Fill(encState.EpfSharpness, (byte)4);

        // The block context map is chosen below Falcon (FindBestBlockEntropyModel), so it keeps its default.
        if (speedTier < JxlSpeedTier.Falcon)
        {
            JxlThrowHelper.ThrowNotSupportedException("Choosing the block context map (libjxl effort 4 and up; slice E4)");
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
