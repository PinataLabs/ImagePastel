// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The encoder's forward transforms of the AC strategies that the Default (Hare) search chooses, and the
/// DC each block gives the DC image: each inverts the decoder's transform, and each gives libjxl's values.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlForwardDctTests
{
    // The strategies of libjxl's Hare search (enc_ac_strategy.cc): the four 8x8 kinds and the aligned merges.
    public static TheoryData<int> HareStrategies => new()
    {
        (int)JxlAcStrategyType.Dct,
        (int)JxlAcStrategyType.Identity,
        (int)JxlAcStrategyType.Dct2X2,
        (int)JxlAcStrategyType.Dct4X4,
        (int)JxlAcStrategyType.Dct16X16,
        (int)JxlAcStrategyType.Dct32X32,
        (int)JxlAcStrategyType.Dct16X8,
        (int)JxlAcStrategyType.Dct8X16,
        (int)JxlAcStrategyType.Dct32X16,
        (int)JxlAcStrategyType.Dct16X32,
        (int)JxlAcStrategyType.Dct64X64,
        (int)JxlAcStrategyType.Dct64X32,
        (int)JxlAcStrategyType.Dct32X64,
    };

    // The decoder's inverse of the forward transform gives the block back. Values are in [-0.5, 0.5).
    [Theory]
    [MemberData(nameof(HareStrategies))]
    public void TransformToPixels_OfTransformFromPixels_GivesTheBlockBack(int rawStrategy)
    {
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(rawStrategy);
        int rows = acs.CoveredBlocksY * 8;
        int cols = acs.CoveredBlocksX * 8;
        float[] pixels = LcgBlock(rows * cols, (uint)rawStrategy + 1);
        float[] coefficients = new float[rows * cols];
        float[] scratch = new float[JxlDct.ScratchSize];

        JxlDct.TransformFromPixels(acs.Strategy, pixels, cols, coefficients, scratch);
        float[] back = new float[rows * cols];
        JxlDct.TransformToPixels(acs, coefficients, back, cols, scratch);

        Assert.True(MaxAbsDifference(pixels, back) <= 1e-5, $"max abs error {MaxAbsDifference(pixels, back)}");
    }

    // The block of the libjxl harness: an LCG's values with 16 fraction bits, exact in float.
    private static float[] LcgBlock(int count, uint seed)
    {
        float[] values = new float[count];
        uint state = seed;
        for (int i = 0; i < count; i++)
        {
            state = unchecked((state * 1664525u) + 1013904223u);
            values[i] = (((state >> 8) & 0xFFFF) / 65536.0f) - 0.5f;
        }

        return values;
    }

    private static double MaxAbsDifference(float[] a, float[] b)
    {
        double worst = 0;
        for (int i = 0; i < a.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs((double)a[i] - b[i]));
        }

        return worst;
    }
}
