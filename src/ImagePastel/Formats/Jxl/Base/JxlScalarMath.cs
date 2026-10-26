// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// Clamp1 in lib/jxl/base/common.h, and the std::min, std::max and copysignf that lib/jxl/cms/tone_mapping.h,
// lib/jxl/cms/transfer_functions.h and lib/jxl/cms/transfer_functions-inl.h call.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Runtime.CompilerServices;

namespace PinataLabs.ImagePastel.Formats.Jxl.Base;

/// <summary>
/// The scalar helpers libjxl's colour code calls, with libjxl's exact comparisons, so that NaN and
/// signed zero come out as they do in libjxl.
/// </summary>
internal static class JxlScalarMath
{
    /// <summary>libjxl's <c>Clamp1</c>: <paramref name="low"/> if the value is below it, <paramref name="high"/> if above.</summary>
    /// <param name="value">The value.</param>
    /// <param name="low">The lower bound.</param>
    /// <param name="high">The upper bound.</param>
    /// <returns>The clamped value; NaN stays NaN.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static float Clamp1(float value, float low, float high) => value < low ? low : value > high ? high : value;

    /// <summary><c>std::min</c>: the first argument unless the second is smaller.</summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <returns>The smaller value.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static float StdMin(float a, float b) => b < a ? b : a;

    /// <summary><c>std::max</c>: the first argument unless the second is larger.</summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <returns>The larger value.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static float StdMax(float a, float b) => a < b ? b : a;

    /// <summary>
    /// The magnitude of <paramref name="magnitude"/> with the sign bit of <paramref name="sign"/>: C's
    /// <c>copysignf</c>, and the <c>Or(AndNot(kSign, magnitude), original_sign)</c> of libjxl's vector
    /// transfer functions.
    /// </summary>
    /// <param name="magnitude">The value whose magnitude is kept.</param>
    /// <param name="sign">The value whose sign bit is taken.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// libjxl's double-precision transfer functions return <c>copysignf</c> of doubles, which converts both
    /// to float first; their callers here pass the doubles cast to float.
    /// </remarks>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static float CopySign(float magnitude, float sign)
    {
        int bits = (BitConverter.SingleToInt32Bits(magnitude) & 0x7fffffff) | (BitConverter.SingleToInt32Bits(sign) & unchecked((int)0x80000000));
        return BitConverter.Int32BitsToSingle(bits);
    }
}
