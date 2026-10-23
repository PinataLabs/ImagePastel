// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The encoder's fast chroma-from-luma fit (libjxl's <c>FindBestMultiplier</c> with <c>fast = true</c>), the
/// least-squares multiplier of one 64x64 tile at Default (Hare).
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncChromaFromLumaTests
{
    // libjxl sums its accumulators in 8 lanes on AVX2 and this port does too (ADR 0004); the harness runs on
    // Highway's scalar target, which sums in plain order. The float before rounding may differ by this much,
    // relative to its size (the largest seen is recorded in the E4d handoff).
    private const float LaneSumTolerance = 2e-6f;

    // From a harness of lib/jxl/enc_chroma_from_luma.cc at ef67fde2 on Highway's scalar target with
    // -ffp-contract=off (e4d-scratch/cfl_fast.cc). The luma is m = scale * u and the chroma
    // s = k * m + sigma * u', with u from the LCG below; distance_mul is libjxl's 1e-9. The float is the
    // multiplier after the shrink towards zero, before std::round, as bits.
    [Theory]
    [InlineData(64, 0.0f, 0.2f, 100.0f, 1.0f, 1u, 14, 0x41619c86u)]
    [InlineData(256, 1.0f, 1.1f, 100.0f, 2.0f, 2u, 6, 0x40ba861bu)]
    [InlineData(4096, 0.0f, -0.9f, 50.0f, 4.0f, 3u, -73, 0xc2920410u)]
    [InlineData(64, 0.0f, 0.02f, 100.0f, 0.5f, 4u, 0, 0x00000000u)]
    [InlineData(256, 0.0f, 3.0f, 100.0f, 1.0f, 5u, 127, 0x43797acau)]
    [InlineData(256, 1.0f, -2.0f, 100.0f, 1.0f, 6u, -128, 0xc3796460u)]
    [InlineData(1024, 1.0f, 1.0f, 30.0f, 30.0f, 7u, 0, 0x00000000u)]
    [InlineData(512, 0.0f, 0.2f, 0.006f, 0.0f, 8u, 5, 0x40a51995u)]
    [InlineData(192, 0.0f, 0.05f, 80.0f, 3.0f, 9u, 2, 0x3fc7f014u)]
    public void FindBestMultiplier_Fast_MatchesLibjxl(int num, float baseCorrelation, float k, float scale, float sigma, uint seed, int expected, uint expectedBeforeRoundBits)
    {
        (float[] valuesM, float[] valuesS) = Vectors(num, k, scale, sigma, seed);

        int actual = JxlEncChromaFromLuma.FindBestMultiplier(valuesM, valuesS, baseCorrelation, 1e-9f, out float beforeRound);

        Assert.Equal(expected, actual);
        float expectedBeforeRound = BitConverter.UInt32BitsToSingle(expectedBeforeRoundBits);
        float error = MathF.Abs(beforeRound - expectedBeforeRound);
        TestContext.Current.TestOutputHelper?.WriteLine($"before round {beforeRound:R}, libjxl {expectedBeforeRound:R}, relative error {error / MathF.Max(1, MathF.Abs(expectedBeforeRound)):E2}");
        Assert.True(error <= LaneSumTolerance * MathF.Max(1, MathF.Abs(expectedBeforeRound)), $"{beforeRound:R} vs libjxl {expectedBeforeRound:R}");
    }

    // No coefficients give the default multiplier.
    [Fact]
    public void FindBestMultiplier_NoValues_IsZero()
        => Assert.Equal(0, JxlEncChromaFromLuma.FindBestMultiplier(Array.Empty<float>(), Array.Empty<float>(), 0.0f, 1e-9f, out _));

    private static (float[] M, float[] S) Vectors(int num, float k, float scale, float sigma, uint seed)
    {
        uint s = seed;
        float Next()
        {
            s = (s * 1664525u) + 1013904223u;
            return (((s >> 8) & 0xFFFF) / 65536.0f) - 0.5f;
        }

        float[] m = new float[num];
        float[] c = new float[num];
        for (int i = 0; i < num; i++)
        {
            m[i] = scale * Next();
            float u2 = Next();
            c[i] = (k * m[i]) + (sigma * u2);
        }

        return (m, c);
    }
}
