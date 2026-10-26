// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// Xorshift128Plus (its four-seed constructor, Fill and SplitMix64) in lib/jxl/xorshift128plus-inl.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace PinataLabs.ImagePastel.Formats.Jxl.Base;

/// <summary>
/// The fast, weak random generator of libjxl's noise synthesis: eight independent xorshift128+
/// generators, seeded with SplitMix64, whose words the noise planes are made from.
/// </summary>
/// <remarks>
/// libjxl fills the eight lanes with Highway vectors. Each lane is independent and uses only integer
/// operations, so its words do not depend on the vector width, and this port fills them one at a time.
/// </remarks>
internal sealed class JxlXorshift128Plus
{
    /// <summary>The number of independent generators, and of words each <see cref="Fill"/> gives.</summary>
    public const int N = 8;

    private readonly ulong[] s0 = new ulong[N];
    private readonly ulong[] s1 = new ulong[N];

    /// <summary>Initializes a new instance of the <see cref="JxlXorshift128Plus"/> class from four seeds.</summary>
    /// <param name="seed1">The first seed; the noise uses the visible frame index.</param>
    /// <param name="seed2">The second seed; the noise uses the non-visible frame index.</param>
    /// <param name="seed3">The third seed; the noise uses the x position of the tile.</param>
    /// <param name="seed4">The fourth seed; the noise uses the y position of the tile.</param>
    public JxlXorshift128Plus(uint seed1, uint seed2, uint seed3, uint seed4)
    {
        // Init state using SplitMix64 generator
        this.s0[0] = SplitMix64(unchecked(((ulong)seed1 << 32) + seed2 + 0x9E3779B97F4A7C15UL));
        this.s1[0] = SplitMix64(unchecked(((ulong)seed3 << 32) + seed4 + 0x9E3779B97F4A7C15UL));
        for (int i = 1; i < N; i++)
        {
            this.s0[i] = SplitMix64(this.s0[i - 1]);
            this.s1[i] = SplitMix64(this.s1[i - 1]);
        }
    }

    /// <summary>Writes the next word of each of the <see cref="N"/> generators.</summary>
    /// <param name="randomBits">The words, at least <see cref="N"/>.</param>
    public void Fill(Span<ulong> randomBits)
    {
        for (int i = 0; i < N; i++)
        {
            ulong s1 = this.s0[i];
            ulong s0 = this.s1[i];
            ulong bits = unchecked(s1 + s0); // b, c
            this.s0[i] = s0;
            s1 ^= s1 << 23;
            randomBits[i] = bits;
            s1 ^= s0 ^ (s1 >> 18) ^ (s0 >> 5);
            this.s1[i] = s1;
        }
    }

    private static ulong SplitMix64(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
