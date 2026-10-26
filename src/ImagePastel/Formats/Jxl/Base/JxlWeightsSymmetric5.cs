// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/convolve.h (WeightsSymmetric5).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Formats.Jxl.Base;

/// <summary>
/// The weights of a symmetric 5x5 kernel, as libjxl's <c>WeightsSymmetric5</c>. The lower-right quadrant
/// is <c>c r R</c> / <c>r d L</c> / <c>R L D</c>. libjxl repeats each weight over 4 lanes; one is enough
/// here. The constructor takes them in libjxl's field order.
/// </summary>
internal readonly struct JxlWeightsSymmetric5
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JxlWeightsSymmetric5"/> struct.
    /// </summary>
    /// <param name="c">The centre weight.</param>
    /// <param name="r">The weight of the pixels at distance 1 on an axis.</param>
    /// <param name="bigR">The weight of the pixels at distance 2 on an axis.</param>
    /// <param name="d">The weight of the diagonal neighbours.</param>
    /// <param name="bigD">The weight of the pixels at (2, 2).</param>
    /// <param name="l">The weight of the pixels at (1, 2) and (2, 1).</param>
    public JxlWeightsSymmetric5(float c, float r, float bigR, float d, float bigD, float l)
    {
        this.C = c;
        this.R = r;
        this.BigR = bigR;
        this.D = d;
        this.BigD = bigD;
        this.L = l;
    }

    /// <summary>Gets the centre weight (libjxl's <c>c</c>).</summary>
    public float C { get; }

    /// <summary>Gets the weight of the pixels at distance 1 on an axis (libjxl's <c>r</c>).</summary>
    public float R { get; }

    /// <summary>Gets the weight of the pixels at distance 2 on an axis (libjxl's <c>R</c>).</summary>
    public float BigR { get; }

    /// <summary>Gets the weight of the diagonal neighbours (libjxl's <c>d</c>).</summary>
    public float D { get; }

    /// <summary>Gets the weight of the pixels at (2, 2) (libjxl's <c>D</c>).</summary>
    public float BigD { get; }

    /// <summary>Gets the weight of the pixels at (1, 2) and (2, 1) (libjxl's <c>L</c>).</summary>
    public float L { get; }
}
