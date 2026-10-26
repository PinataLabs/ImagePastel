// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.CompilerServices;

namespace PinataLabs.ImagePastel.Formats.Jxl.Base;

/// <summary>
/// The 8 float lanes of AVX2, as Highway gives them to libjxl's encoder on x64, which is where cjxl runs.
/// The encoder's searches sum in these lanes and add them in this order, so that their sums, and so their
/// choices, equal cjxl's bit for bit.
/// </summary>
/// <remarks>
/// On another vector width libjxl's sums differ in the last bits; this port always follows AVX2.
/// </remarks>
internal static class JxlAvx2Lanes
{
    /// <summary>The number of float lanes: libjxl's <c>Lanes(df)</c> for <c>HWY_FULL(float)</c> and <c>HWY_CAPPED(float, 8)</c>.</summary>
    public const int Count = 8;

    /// <summary>
    /// Highway's <c>SumOfLanes</c> over the 8 lanes: each lane of the lower block plus its partner in the
    /// upper block, then lanes 0 and 3 and lanes 1 and 2, then those two sums.
    /// </summary>
    /// <param name="lanes">The 8 lanes.</param>
    /// <returns>The sum.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static float SumOfLanes(ReadOnlySpan<float> lanes)
    {
        float l0 = lanes[0] + lanes[4];
        float l1 = lanes[1] + lanes[5];
        float l2 = lanes[2] + lanes[6];
        float l3 = lanes[3] + lanes[7];
        return (l0 + l3) + (l1 + l2);
    }

    /// <summary>
    /// Highway's <c>Min(a, b)</c> on x86 (<c>minps</c>): <paramref name="a"/> if it is smaller, otherwise
    /// <paramref name="b"/>, which is also the result when either is NaN or both are zero.
    /// </summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <returns>The smaller value.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static float Min(float a, float b) => a < b ? a : b;

    /// <summary>
    /// Highway's <c>Max(a, b)</c> on x86 (<c>maxps</c>): <paramref name="a"/> if it is larger, otherwise
    /// <paramref name="b"/>, which is also the result when either is NaN or both are zero.
    /// </summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <returns>The larger value.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static float Max(float a, float b) => a > b ? a : b;
}
