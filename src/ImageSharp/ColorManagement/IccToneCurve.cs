// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.ColorManagement.Icc;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.ColorManagement;

/// <summary>
/// A tone reproduction curve (TRC) for a profile built by <see cref="IccProfileFactory"/>: the function
/// from encoded device values to linear light, written as an ICC <c>parametricCurveType</c>.
/// </summary>
/// <remarks>
/// Instances are immutable. Parameters are rounded to the s15Fixed16Number precision the profile stores.
/// </remarks>
public sealed class IccToneCurve
{
    private IccToneCurve(IccParametricCurve curve) => this.Curve = curve;

    /// <summary>
    /// Gets the sRGB (IEC 61966-2-1) curve: <c>Y = ((X + 0.055) / 1.055)^2.4</c>, linear below 0.04045.
    /// </summary>
    public static IccToneCurve Srgb { get; } = Parametric(2.4F, (float)(1 / 1.055), (float)(0.055 / 1.055), (float)(1 / 12.92), 0.04045F);

    /// <summary>
    /// Gets the ITU-R BT.709 curve, which BT.2020 also uses: the inverse of the BT.709 OETF,
    /// <c>Y = ((X + 0.099) / 1.099)^(1 / 0.45)</c>, linear below 0.081.
    /// </summary>
    public static IccToneCurve Rec709 { get; } = Parametric((float)(1 / 0.45), (float)(1 / 1.099), (float)(0.099 / 1.099), (float)(1 / 4.5), 0.081F);

    /// <summary>
    /// Gets the linear curve, <c>Y = X</c>.
    /// </summary>
    public static IccToneCurve Linear { get; } = Parametric(1, 1, 0, 1, 0);

    /// <summary>
    /// Gets the curve as the profile stores it.
    /// </summary>
    internal IccParametricCurve Curve { get; }

    /// <summary>
    /// Creates a pure power curve, <c>Y = X^gamma</c>. Gamma 2.2 is a typical display; PNG's <c>gAMA</c>
    /// chunk stores the reciprocal of this value.
    /// </summary>
    /// <param name="gamma">The exponent, greater than zero.</param>
    /// <returns>The curve.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="gamma"/> is not greater than zero, or too large for an ICC profile.
    /// </exception>
    public static IccToneCurve Gamma(float gamma)
    {
        if (!(gamma > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(gamma), gamma, "The gamma must be greater than zero.");
        }

        return new IccToneCurve(new IccParametricCurve(Round(gamma, nameof(gamma))));
    }

    /// <summary>
    /// Creates a curve with a linear segment near black, the ICC parametric function type 3:
    /// <c>Y = (a * X + b)^g</c> for <c>X &gt;= d</c>, and <c>Y = c * X</c> below.
    /// </summary>
    /// <param name="g">The exponent.</param>
    /// <param name="a">The scale of the power segment.</param>
    /// <param name="b">The offset of the power segment.</param>
    /// <param name="c">The slope of the linear segment.</param>
    /// <param name="d">The value of <c>X</c> where the power segment starts.</param>
    /// <returns>The curve.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is not finite, or too large for an ICC profile.</exception>
    public static IccToneCurve Parametric(float g, float a, float b, float c, float d)
        => new(new IccParametricCurve(Round(g, nameof(g)), Round(a, nameof(a)), Round(b, nameof(b)), Round(c, nameof(c)), Round(d, nameof(d))));

    private static float Round(float value, string name)
    {
        try
        {
            return IccProfileBuilder.S15Fixed16(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(name, value, "The value is not finite, or too large for an ICC profile.");
        }
    }
}
