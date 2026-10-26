// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Stands in for libjxl's (https://github.com/libjxl/libjxl, commit ef67fde2) per-thread working memory of
// the AC-strategy search: AcStrategyHeuristics::mem (lib/jxl/enc_ac_strategy.h:84), sized in
// AcStrategyHeuristics::PrepareForThreads (lib/jxl/enc_ac_strategy.cc:1126-1136) as
// 6 * AcStrategy::kMaxCoeffArea (lib/jxl/ac_strategy.h:88) plus the DCT scratch, and split into the
// block (enc_ac_strategy.cc:843) and EstimateEntropy's mem and scratch space (:370-371).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Formats.Jxl.VarDct;

/// <summary>
/// The working memory of the AC-strategy search, allocated once per frame (libjxl's per-thread
/// <c>mem</c> of <c>AcStrategyHeuristics</c>).
/// </summary>
/// <remarks>
/// libjxl sizes it for a 256x256 block (<c>kMaxCoeffArea</c>); the encoder never chooses a block above
/// 64x64 at any speed tier, so this holds 64x64.
/// </remarks>
internal sealed class JxlAcsScratch
{
    /// <summary>The most coefficients of one channel of a block the encoder chooses (DCT64X64).</summary>
    public const int MaxCoeffArea = 64 * 64;

    /// <summary>Gets the coefficients of the three channels, <see cref="MaxCoeffArea"/> apart.</summary>
    public float[] Block { get; } = new float[3 * MaxCoeffArea];

    /// <summary>Gets the dequantised rounding error of one channel, the input of the inverse transform.</summary>
    public float[] Mem { get; } = new float[MaxCoeffArea];

    /// <summary>Gets the pixels of one channel of the block: the copy the forward transform reads and the inverse transform's output.</summary>
    public float[] Pixels { get; } = new float[MaxCoeffArea];

    /// <summary>Gets the scratch space of the transforms.</summary>
    public float[] DctScratch { get; } = new float[JxlDct.ScratchSize];
}
