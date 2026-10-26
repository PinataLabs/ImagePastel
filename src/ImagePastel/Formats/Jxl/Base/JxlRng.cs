// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/base/random.h (Rng).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl.Base;

/// <summary>
/// libjxl's random number generator, a xorshift128+, and the distributions the encoder draws from it.
/// libjxl does not use <c>&lt;random&gt;</c>, whose results differ between standard libraries; this
/// port keeps its sequence. Only the members the encoder uses are ported.
/// </summary>
internal struct JxlRng
{
    private ulong s0;
    private ulong s1;

    /// <summary>Initializes a new instance of the <see cref="JxlRng"/> struct.</summary>
    /// <param name="seed">The seed.</param>
    public JxlRng(ulong seed)
    {
        this.s0 = 0x94D049BB133111EBUL;
        this.s1 = unchecked(0xBF58476D1CE4E5B9UL + seed);
    }

    /// <summary>
    /// Returns the inverse of the log of <c>1 - p</c>, the state of a geometric distribution with
    /// success probability <paramref name="p"/>.
    /// </summary>
    /// <param name="p">The success probability.</param>
    /// <returns>The distribution.</returns>
    /// <remarks>
    /// libjxl takes the float overload of <c>std::log</c>; <see cref="MathF.Log(float)"/> can differ from
    /// it in the last bit, which can move a sample. The samples only guide the encoder's choices.
    /// </remarks>
    public static float MakeGeometric(float p) => (float)(1.0 / MathF.Log(1 - p));

    /// <summary>Returns the next 64 random bits. Xorshift128+ adapted from xorshift128+-inl.h.</summary>
    /// <returns>The bits.</returns>
    public ulong Next()
    {
        unchecked
        {
            ulong s1 = this.s0;
            ulong s0 = this.s1;
            ulong bits = s1 + s0; // b, c
            this.s0 = s0;
            s1 ^= s1 << 23;
            s1 ^= s0 ^ (s1 >> 18) ^ (s0 >> 5);
            this.s1 = s1;
            return bits;
        }
    }

    /// <summary>
    /// Returns a uniformly distributed float in [<paramref name="begin"/>, <paramref name="end"/>).
    /// Note: only 23 bits of randomness.
    /// </summary>
    /// <param name="begin">The lower bound.</param>
    /// <param name="end">The upper bound.</param>
    /// <returns>The float.</returns>
    public float UniformF(float begin, float end)
    {
        // Bits of a random [1, 2) float.
        uint u = (uint)(this.Next() >> (64 - 23)) | 0x3F800000;
        float f = BitConverter.Int32BitsToSingle((int)u);

        // Note: (end-begin) * f + (2*begin-end) may fail to return a number >= begin.
        return ((end - begin) * (f - 1.0f)) + begin;
    }

    /// <summary>Draws from the geometric distribution <paramref name="dist"/>.</summary>
    /// <param name="dist">The distribution, from <see cref="MakeGeometric"/>.</param>
    /// <returns>The number of failures before the first success.</returns>
    public uint Geometric(float dist)
    {
        float f = this.UniformF(0, 1);
        float log = MathF.Log(1 - f) * dist;
        return (uint)log;
    }
}
