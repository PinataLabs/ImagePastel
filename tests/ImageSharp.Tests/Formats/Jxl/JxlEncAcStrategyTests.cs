// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using SixLabors.ImageSharp.Memory;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The AC-strategy search of lossy Default at the 8x8 level (libjxl's <c>AcStrategyHeuristics::Init</c>,
/// <c>EstimateEntropy</c> and <c>FindBest8x8Transform</c>, slice E4g), against libjxl's own values.
/// </summary>
/// <remarks>
/// <para>
/// The reference values come from a harness (<c>e4-notes/e4g-scratch/acs.cc</c>) that includes libjxl
/// <c>ef67fde2</c>'s <c>enc_ac_strategy.cc</c> and builds it twice, both with <c>-ffp-contract=off</c>
/// (ADR 0004): on Highway's scalar target, and on its AVX2 target with <c>HWY_DISABLE_BMI2_FMA</c>, which
/// keeps cjxl's 8 lanes but makes each <c>MulAdd</c> a multiply then an add. Its input is the image of
/// <see cref="CreateFixedConfig"/>: six kinds of 8x8 block (a ramp, low noise, high noise, an edge, a
/// checkerboard and thin lines) over 8x4 blocks, with fixed quant and masking values.
/// </para>
/// <para>
/// <c>EstimateEntropy</c> sums its entropy, its non-zero count and its loss over 8 lanes
/// (<c>SumOfLanes</c>), and the port sums in AVX2's lane order, as cjxl does on x64. So it matches the
/// AVX2 build bit for bit, and the scalar build, which sums in one lane, within
/// <see cref="ScalarLaneSumTolerance"/>. The unqualified <c>pow(float, float)</c> of its loss is
/// <c>powf</c> with MSVC's <c>&lt;cmath&gt;</c> (cjxl for Windows made the calibration table), and the
/// harness includes <c>&lt;math.h&gt;</c> first so that g++ picks the same overload.
/// </para>
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlEncAcStrategyTests
{
    // The largest relative difference of an entropy from libjxl's scalar build, which sums in another
    // order: measured 1.8e-6, on the DCT32X32 case. The 8x8 choices are the same in both builds.
    private const float ScalarLaneSumTolerance = 4e-6f;

    private const int FixedWidth = 64;
    private const int FixedHeight = 32;

    // AcStrategyHeuristics::Init: the weights of the information loss, of the zeros and of each coefficient,
    // as float bits. The harness's scalar and AVX2 builds agree.
    [Theory]
    [InlineData(0.5f, 0x3F7CC33Au, 0x40DDB6CCu, 0x410C23DBu)]
    [InlineData(1f, 0x3F99999Au, 0x4114F147u, 0x412D5516u)]
    [InlineData(2f, 0x3FBDF5FCu, 0x414D75C3u, 0x415A7EAAu)]
    [InlineData(4f, 0x3FED48C4u, 0x418FDE40u, 0x418B37A5u)]
    [InlineData(6f, 0x40077E18u, 0x41AFE912u, 0x41A0E5E6u)]
    public void Init_GivesLibjxlsConstants(float distance, uint infoLossMultiplier, uint zerosMul, uint costDelta)
    {
        using FixedConfig fixedConfig = CreateFixedConfig(distance);

        JxlAcsConfig config = fixedConfig.Config;
        Assert.Equal(infoLossMultiplier, Bits(config.InfoLossMultiplier));
        Assert.Equal(zerosMul, Bits(config.ZerosMul));
        Assert.Equal(costDelta, Bits(config.CostDelta));
    }

    // One block of each kind with each 8x8 strategy (row 0, blocks 0 to 5), another entropy multiplier and a
    // non-default chroma-from-luma pair (X 0.1, B 0.9), and the multi-block paths that E4h's merges reach:
    // the maximum of two quant values (DCT16X8, DCT8X16), the 16th-power mean through FastPowf (DCT16X16,
    // DCT32X32) and the X channel's extra weight. At d1.
    [Theory]
    [InlineData(0, 0, 0, 1f, false, 0x439DF154u, 0x439DF154u)]
    [InlineData(3, 0, 0, 1f, false, 0x43DF5B1Bu, 0x43DF5B1Bu)]
    [InlineData(2, 0, 0, 1f, false, 0x43DF14A9u, 0x43DF14A9u)]
    [InlineData(1, 0, 0, 1f, false, 0x43F9E254u, 0x43F9E254u)]
    [InlineData(0, 8, 0, 1f, false, 0x43310C8Au, 0x43310C8Au)]
    [InlineData(3, 8, 0, 1f, false, 0x43A6B8D9u, 0x43A6B8D9u)]
    [InlineData(2, 8, 0, 1f, false, 0x438F99D3u, 0x438F99D3u)]
    [InlineData(1, 8, 0, 1f, false, 0x43969432u, 0x43969432u)]
    [InlineData(0, 16, 0, 1f, false, 0x452B3AFBu, 0x452B3AFAu)]
    [InlineData(3, 16, 0, 1f, false, 0x457898F0u, 0x457898EFu)]
    [InlineData(2, 16, 0, 1f, false, 0x45531FB6u, 0x45531FB5u)]
    [InlineData(1, 16, 0, 1f, false, 0x4576C307u, 0x4576C304u)]
    [InlineData(0, 24, 0, 1f, false, 0x440CDB06u, 0x440CDB06u)]
    [InlineData(3, 24, 0, 1f, false, 0x43A2A55Au, 0x43A2A55Au)]
    [InlineData(2, 24, 0, 1f, false, 0x43C58283u, 0x43C58283u)]
    [InlineData(1, 24, 0, 1f, false, 0x43B861E2u, 0x43B861E2u)]
    [InlineData(0, 32, 0, 1f, false, 0x44459FC1u, 0x44459FC1u)]
    [InlineData(3, 32, 0, 1f, false, 0x44C073DCu, 0x44C073DCu)]
    [InlineData(2, 32, 0, 1f, false, 0x4495BFE6u, 0x4495BFE5u)]
    [InlineData(1, 32, 0, 1f, false, 0x45213CC3u, 0x45213CC1u)]
    [InlineData(0, 40, 0, 1f, false, 0x44034E3Au, 0x44034E3Au)]
    [InlineData(3, 40, 0, 1f, false, 0x446B4A42u, 0x446B4A41u)]
    [InlineData(2, 40, 0, 1f, false, 0x445437A7u, 0x445437A7u)]
    [InlineData(1, 40, 0, 1f, false, 0x4463CCADu, 0x4463CCADu)]
    [InlineData(0, 16, 8, 1.35f, true, 0x442DE2EFu, 0x442DE2EFu)]
    [InlineData(4, 32, 16, 1f, true, 0x45E3FCB4u, 0x45E3FCB0u)]
    [InlineData(6, 8, 0, 1f, false, 0x45090825u, 0x45090824u)]
    [InlineData(7, 24, 8, 1f, false, 0x449B43EBu, 0x449B43ECu)]
    [InlineData(4, 40, 16, 1f, false, 0x45A09F77u, 0x45A09F74u)]
    [InlineData(5, 0, 0, 1f, false, 0x46BD8AB7u, 0x46BD8ACDu)]
    public void EstimateEntropy_OnFixedBlocks_MatchesLibjxl(int rawStrategy, int x, int y, float entropyMul, bool otherCmap, uint scalarBits, uint avx2Bits)
    {
        using FixedConfig fixedConfig = CreateFixedConfig(1.0f);
        float[] cmapFactors = otherCmap ? new[] { 0.1f, 0.0f, 0.9f } : new[] { 0.0f, 0.0f, 1.0f };

        float entropy = JxlEncAcStrategy.EstimateEntropy((JxlAcStrategyType)rawStrategy, entropyMul, x, y, fixedConfig.Config, cmapFactors, new JxlAcsScratch());

        float scalar = BitConverter.UInt32BitsToSingle(scalarBits);
        Assert.True(MathF.Abs(entropy - scalar) <= ScalarLaneSumTolerance * MathF.Abs(scalar), $"{entropy:R} against libjxl's scalar {scalar:R}");
        Assert.Equal(avx2Bits, Bits(entropy));
    }

    // The best 8x8 strategy of each block of the fixed image (raw values, row by row: 0 DCT8, 1 IDENTITY,
    // 2 DCT2X2, 3 DCT4X4), the same in both builds. DCT2X2 and IDENTITY are favoured below d5; DCT4X4 wins
    // the edge blocks at d4 and loses them at d6, where it pays a penalty above d4.
    [Theory]
    [InlineData(0.5f, "00020002020002020002020000220000")]
    [InlineData(1f, "00020000020002020002020000020000")]
    [InlineData(2f, "00020000020000020000020000020000")]
    [InlineData(4f, "00030000020000030000030000030000")]
    [InlineData(6f, "00020000020000020000020000020000")]
    public void FindBest8x8Transform_OnTheFixedImage_ChoosesLibjxlsStrategies(float distance, string expected)
    {
        using FixedConfig fixedConfig = CreateFixedConfig(distance);
        float[] cmapFactors = { 0.0f, 0.0f, 1.0f };
        JxlAcsScratch scratch = new();
        char[] actual = new char[(FixedWidth / 8) * (FixedHeight / 8)];

        for (int i = 0; i < actual.Length; i++)
        {
            JxlAcStrategyType best = JxlEncAcStrategy.FindBest8x8Transform(8 * (i % 8), 8 * (i / 8), JxlSpeedTier.Hare, distance, fixedConfig.Config, cmapFactors, scratch, out _);
            actual[i] = (char)('0' + (int)best);
        }

        Assert.Equal(expected, new string(actual));
    }

    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);

    // The harness's fixed image: 64x32 pixels, block kind (bx + 8 by) % 6, each sample drawn from an LCG
    // (s = s * 1664525 + 1013904223, ((s >> 8) & 0xFFFF) / 65536 - 0.5) in channel, row, column order; then
    // the quant field (seed 7) and the masking image (seed 11).
    private static FixedConfig CreateFixedConfig(float distance)
    {
        float[] amp = { 0.0f, 0.01f, 0.3f, 0.01f, 0.01f, 0.02f };
        float[] channelBase = { 0.0f, 0.4f, 0.35f };
        float[] channelScale = { 0.05f, 1.0f, 0.8f };
        MemoryAllocator allocator = Configuration.Default.MemoryAllocator;
        Buffer2D<float>[] src = new Buffer2D<float>[3];
        uint s = 1;
        for (int c = 0; c < 3; c++)
        {
            src[c] = allocator.Allocate2D<float>(FixedWidth, FixedHeight);
            for (int y = 0; y < FixedHeight; y++)
            {
                for (int x = 0; x < FixedWidth; x++)
                {
                    int k = ((x / 8) + (8 * (y / 8))) % 6;
                    float u = Next(ref s);
                    float v = Pattern(k, x, y) + (amp[k] * u);
                    src[c][x, y] = channelBase[c] + (channelScale[c] * v);
                }
            }
        }

        Buffer2D<float> quantField = allocator.Allocate2D<float>(FixedWidth / 8, FixedHeight / 8);
        s = 7;
        for (int y = 0; y < quantField.Height; y++)
        {
            for (int x = 0; x < quantField.Width; x++)
            {
                quantField[x, y] = 0.3f + (1.2f * (Next(ref s) + 0.5f));
            }
        }

        Buffer2D<float> mask1x1 = allocator.Allocate2D<float>(FixedWidth, FixedHeight);
        s = 11;
        for (int y = 0; y < FixedHeight; y++)
        {
            for (int x = 0; x < FixedWidth; x++)
            {
                mask1x1[x, y] = 1.0f + (20.0f * (Next(ref s) + 0.5f));
            }
        }

        JxlAcsConfig config = JxlEncAcStrategy.Init(JxlSpeedTier.Hare, distance, src, quantField, mask1x1, new JxlDequantMatrices());
        return new FixedConfig(config, src, quantField, mask1x1);
    }

    private static float Next(ref uint s)
    {
        s = unchecked((s * 1664525u) + 1013904223u);
        return (((s >> 8) & 0xFFFF) / 65536.0f) - 0.5f;
    }

    private static float Pattern(int k, int x, int y) => k switch
    {
        0 => (0.01f * (x & 7)) + (0.005f * (y & 7)),
        3 => (x & 7) >= 4 ? 0.3f : 0.0f,
        4 => ((x ^ y) & 1) != 0 ? 0.2f : 0.0f,
        5 => (x & 3) == 0 ? 0.25f : 0.0f,
        _ => 0.0f,
    };

    private sealed class FixedConfig : IDisposable
    {
        private readonly Buffer2D<float>[] src;
        private readonly Buffer2D<float> quantField;
        private readonly Buffer2D<float> mask1x1;

        public FixedConfig(JxlAcsConfig config, Buffer2D<float>[] src, Buffer2D<float> quantField, Buffer2D<float> mask1x1)
        {
            this.Config = config;
            this.src = src;
            this.quantField = quantField;
            this.mask1x1 = mask1x1;
        }

        public JxlAcsConfig Config { get; }

        public void Dispose()
        {
            foreach (Buffer2D<float> plane in this.src)
            {
                plane.Dispose();
            }

            this.quantField.Dispose();
            this.mask1x1.Dispose();
        }
    }
}
