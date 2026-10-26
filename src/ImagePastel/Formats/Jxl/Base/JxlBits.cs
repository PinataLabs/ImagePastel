// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/base/bits.h, lib/jxl/pack_signed.h and DivCeil from lib/jxl/base/common.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

#if NETCOREAPP3_0_OR_GREATER
using System.Numerics;
#endif
using System.Runtime.CompilerServices;

namespace PinataLabs.ImagePastel.Formats.Jxl.Base;

/// <summary>
/// Bit helpers used throughout the codestream.
/// </summary>
internal static class JxlBits
{
    /// <summary>Returns floor(log2(x)) for a nonzero <paramref name="x"/>.</summary>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static int FloorLog2Nonzero(uint x) => Numerics.Log2(x);

    /// <summary>Returns floor(log2(x)) for a nonzero <paramref name="x"/>.</summary>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static int FloorLog2Nonzero(ulong x)
    {
        uint high = (uint)(x >> 32);
        return high != 0 ? 32 + Numerics.Log2(high) : Numerics.Log2((uint)x);
    }

    /// <summary>Returns ceil(log2(x)) for a nonzero <paramref name="x"/>.</summary>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static int CeilLog2Nonzero(uint x)
    {
        int floorLog2 = FloorLog2Nonzero(x);
        return (x & (x - 1)) == 0 ? floorLog2 : floorLog2 + 1;
    }

    /// <summary>Returns the number of zero bits below the lowest set bit of a nonzero <paramref name="x"/>.</summary>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static int Num0BitsBelowLs1BitNonzero(uint x)
    {
#if NETCOREAPP3_0_OR_GREATER
        return BitOperations.TrailingZeroCount(x);
#else
        // netstandard2.1 has no BitOperations: the lowest set bit alone is a power of two.
        return Numerics.Log2(x & (~x + 1));
#endif
    }

    /// <summary>Returns ceil(log2(x)) for a nonzero <paramref name="x"/>.</summary>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static int CeilLog2Nonzero(ulong x)
    {
        int floorLog2 = FloorLog2Nonzero(x);
        return (x & (x - 1)) == 0 ? floorLog2 : floorLog2 + 1;
    }

    /// <summary>Maps a signed value to an unsigned one: 0, -1, 1, -2, ... become 0, 1, 2, 3, ...</summary>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static uint PackSigned(int value)
        => unchecked(((uint)value << 1) ^ ((~(uint)value >> 31) - 1));

    /// <summary>The inverse of <see cref="PackSigned"/>.</summary>
    /// <remarks>
    /// libjxl takes a <c>size_t</c>. Every caller passes a value that fits in 32 bits,
    /// and the result is truncated to 32 bits, so 32-bit arithmetic gives the same result.
    /// </remarks>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static int UnpackSigned(uint value)
        => unchecked((int)((value >> 1) ^ ((~value & 1) - 1)));

    /// <summary>Returns ceil(a / b).</summary>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static long DivCeil(long a, long b) => (a + b - 1) / b;
}
