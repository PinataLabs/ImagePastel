// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_ac_strategy.h (ACSConfig).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// What the AC-strategy search reads, as libjxl's <c>ACSConfig</c>: the dequantization matrices, the float
/// initial quant field, the per-pixel masking image, the opsin and the weights of the entropy estimate.
/// </summary>
/// <remarks>
/// libjxl's config also points at the per-block masking image (<c>Masking</c>), which only the searches
/// below Hare read; the encoder does not compute it (<see cref="JxlEncAdaptiveQuantization"/>).
/// </remarks>
internal sealed class JxlAcsConfig
{
    /// <summary>Initializes a new instance of the <see cref="JxlAcsConfig"/> class.</summary>
    /// <param name="dequant">The dequantization matrices.</param>
    /// <param name="quantField">The float quant field.</param>
    /// <param name="mask1x1">The per-pixel masking image, or null above Hare.</param>
    /// <param name="src">The opsin planes.</param>
    /// <param name="infoLossMultiplier">The weight of the information loss.</param>
    /// <param name="costDelta">The weight of each coefficient.</param>
    /// <param name="zerosMul">The weight of the non-zero count.</param>
    public JxlAcsConfig(JxlDequantMatrices dequant, Buffer2D<float> quantField, Buffer2D<float> mask1x1, Buffer2D<float>[] src, float infoLossMultiplier, float costDelta, float zerosMul)
    {
        this.Dequant = dequant;
        this.QuantField = quantField;
        this.Mask1x1 = mask1x1;
        this.Src = src;
        this.InfoLossMultiplier = infoLossMultiplier;
        this.CostDelta = costDelta;
        this.ZerosMul = zerosMul;
    }

    /// <summary>Gets the dequantization matrices.</summary>
    public JxlDequantMatrices Dequant { get; }

    /// <summary>Gets the float quant field, one value per block, before <c>AdjustQuantField</c>.</summary>
    public Buffer2D<float> QuantField { get; }

    /// <summary>Gets the blurred per-pixel masking image (libjxl's <c>mask1x1</c>), or null above Hare.</summary>
    public Buffer2D<float> Mask1x1 { get; }

    /// <summary>Gets the opsin planes the search transforms, after the inverse Gaborish filter.</summary>
    public Buffer2D<float>[] Src { get; }

    /// <summary>Gets the weight of the information loss in the entropy estimate.</summary>
    public float InfoLossMultiplier { get; }

    /// <summary>Gets the weight of the square root of each quantised coefficient.</summary>
    public float CostDelta { get; }

    /// <summary>Gets the weight of the bits that code the number of non-zeros.</summary>
    public float ZerosMul { get; }

    /// <summary>Returns the float quant field of block (<paramref name="bx"/>, <paramref name="by"/>).</summary>
    /// <param name="bx">The block column.</param>
    /// <param name="by">The block row.</param>
    /// <returns>The value.</returns>
    public float Quant(int bx, int by) => this.QuantField[bx, by];
}
