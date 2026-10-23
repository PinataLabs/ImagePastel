// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

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
