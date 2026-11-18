// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Linq;
using PinataLabs.ImagePastel.Formats.Webp;
using PinataLabs.ImagePastel.Formats.Webp.Lossy;
using PinataLabs.ImagePastel.Tests.TestUtilities;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Webp;

[Trait("Format", "Webp")]
public class QuantEncTests
{
    private static unsafe void RunQuantizeBlockTest()
    {
        // arrange
        short[] input = { 378, 777, -851, 888, 259, 148, 0, -111, -185, -185, -74, -37, 148, 74, 111, 74 };
        short[] output = new short[16];
        ushort[] q = { 42, 37, 37, 37, 37, 37, 37, 37, 37, 37, 37, 37, 37, 37, 37, 37 };
        ushort[] iq = { 3120, 3542, 3542, 3542, 3542, 3542, 3542, 3542, 3542, 3542, 3542, 3542, 3542, 3542, 3542, 3542 };
        uint[] bias = { 49152, 55296, 55296, 55296, 55296, 55296, 55296, 55296, 55296, 55296, 55296, 55296, 55296, 55296, 55296, 55296 };
        uint[] zthresh = { 26, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21 };
        short[] expectedOutput = { 9, 21, 7, -5, 4, -23, 24, 0, -5, 4, 2, -2, -3, -1, 3, 2 };
        int expectedResult = 1;
        Vp8Matrix vp8Matrix = default;
        for (int i = 0; i < 16; i++)
        {
            vp8Matrix.Q[i] = q[i];
            vp8Matrix.IQ[i] = iq[i];
            vp8Matrix.Bias[i] = bias[i];
            vp8Matrix.ZThresh[i] = zthresh[i];
        }

        // act
        int actualResult = QuantEnc.QuantizeBlock(input, output, ref vp8Matrix);

        // assert
        Assert.True(output.SequenceEqual(expectedOutput));
        Assert.Equal(expectedResult, actualResult);
    }

    [Fact]
    public void QuantizeBlock_Works() => RunQuantizeBlockTest();

    [Fact]
    public void QuantizeBlock_WithHardwareIntrinsics_Works() => FeatureTestRunner.RunWithHwIntrinsicsFeature(RunQuantizeBlockTest, HwIntrinsics.AllowAll);

    [Fact]
    public void QuantizeBlock_WithoutSSE2_Works() => FeatureTestRunner.RunWithHwIntrinsicsFeature(RunQuantizeBlockTest, HwIntrinsics.DisableSSE2);

    [Fact]
    public void QuantizeBlock_WithoutAVX2_Works() => FeatureTestRunner.RunWithHwIntrinsicsFeature(RunQuantizeBlockTest, HwIntrinsics.DisableAVX2);

    private static unsafe void RunQuantizeBlockMatchesScalarTest()
    {
        var rng = new Random(354);
        Vp8Matrix mtx = default;
        short[] input = new short[16];
        short[] output = new short[16];
        short[] scalarInput = new short[16];
        short[] scalarOutput = new short[16];
        bool sharpened = false;

        for (int i = 0; i < 6000; i++)
        {
            mtx.Q[0] = (ushort)rng.Next(4, 158);
            mtx.Q[1] = (ushort)rng.Next(4, 158);

            // Only matrix type 0 (luma AC) sharpens.
            mtx.Expand(i % 3);

            // Half the blocks stay near the zero threshold, where sharpening decides the level.
            int range = (i & 1) == 0 ? 2048 : 2 * mtx.Q[1];
            for (int j = 0; j < 16; j++)
            {
                input[j] = (short)rng.Next(-range, range + 1);
                sharpened |= mtx.Sharpen[j] != 0;
            }

            input.CopyTo(scalarInput, 0);

            int nz = QuantEnc.QuantizeBlock(input, output, ref mtx);
            int scalarNz = QuantEnc.QuantizeBlockScalar(scalarInput, scalarOutput, ref mtx);

            Assert.Equal(scalarNz, nz);
            Assert.Equal(scalarOutput, output);
            Assert.Equal(scalarInput, input);
        }

        Assert.True(sharpened);
    }

    [Theory]
    [InlineData(HwIntrinsics.AllowAll)]
    [InlineData(HwIntrinsics.DisableAVX2)]
    [InlineData(HwIntrinsics.DisableHWIntrinsic)]
    public void QuantizeBlock_MatchesScalar(HwIntrinsics intrinsics) => FeatureTestRunner.RunWithHwIntrinsicsFeature(RunQuantizeBlockMatchesScalarTest, intrinsics);

    private static unsafe void RunReconstructIntra4PairTest()
    {
        var rng = new Random(354);
        var it = new Vp8EncIterator(new byte[32], new byte[32], new uint[2], new[] { new Vp8MacroBlockInfo() }, new byte[16], new sbyte[8], 1, 1);
        var dqm = new Vp8SegmentInfo();
        byte[] src = new byte[WebpConstants.Bps * 4];
        byte[] pair = new byte[WebpConstants.Bps * 4];
        byte[] single = new byte[WebpConstants.Bps * 4];
        short[] pairLevels = new short[32];
        short[] singleLevels = new short[16];

        for (int i = 0; i < 4000; i++)
        {
            rng.NextBytes(src);
            rng.NextBytes(it.YuvP);
            if ((i & 3) == 0)
            {
                for (int j = 0; j < it.YuvP.Length; j++)
                {
                    it.YuvP[j] = (byte)Math.Clamp(src[j % src.Length] + rng.Next(-3, 4), 0, 255);
                }
            }

            dqm.Y1.Q[0] = (ushort)rng.Next(4, 158);
            dqm.Y1.Q[1] = (ushort)rng.Next(4, 158);
            dqm.Y1.Expand(0);

            for (int mode = 0; mode < WebpConstants.NumBModes; mode += 2)
            {
                int nz = QuantEnc.ReconstructIntra4Pair(it, dqm, pairLevels, src, pair, mode);

                for (int k = 0; k < 2; k++)
                {
                    int expectedNz = QuantEnc.ReconstructIntra4(it, dqm, singleLevels, src, single, mode + k);

                    Assert.Equal(expectedNz, (nz >> k) & 1);
                    Assert.True(singleLevels.AsSpan().SequenceEqual(pairLevels.AsSpan(16 * k, 16)));
                    for (int y = 0; y < 4; y++)
                    {
                        Assert.True(single.AsSpan(y * WebpConstants.Bps, 4).SequenceEqual(pair.AsSpan((y * WebpConstants.Bps) + (4 * k), 4)));
                    }
                }
            }
        }
    }

    [Fact]
    public void ReconstructIntra4Pair_MatchesReconstructIntra4() => RunReconstructIntra4PairTest();

    [Fact]
    public void ReconstructIntra4Pair_WithHardwareIntrinsics_MatchesReconstructIntra4() => FeatureTestRunner.RunWithHwIntrinsicsFeature(RunReconstructIntra4PairTest, HwIntrinsics.AllowAll);

    [Fact]
    public void ReconstructIntra4Pair_WithoutAVX2_MatchesReconstructIntra4() => FeatureTestRunner.RunWithHwIntrinsicsFeature(RunReconstructIntra4PairTest, HwIntrinsics.DisableAVX2);

    [Fact]
    public void ReconstructIntra4Pair_WithoutHardwareIntrinsics_MatchesReconstructIntra4() => FeatureTestRunner.RunWithHwIntrinsicsFeature(RunReconstructIntra4PairTest, HwIntrinsics.DisableHWIntrinsic);

    [Fact]
    public void IsFlat_CountsNonZeroAcLevels()
    {
        short[] levels = new short[16];
        levels[1] = 5;
        levels[2] = -3;
        levels[5] = 1;
        levels[15] = 2;

        Assert.False(QuantEnc.IsFlat(levels, 1, WebpConstants.FlatnessLimitI4));
    }

    [Fact]
    public void IsFlat_IgnoresDcLevel()
    {
        short[] levels = new short[16];
        levels[0] = 40;

        Assert.True(QuantEnc.IsFlat(levels, 1, WebpConstants.FlatnessLimitI16));
    }

    [Fact]
    public void IsFlat_SumsAcLevelsAcrossBlocks()
    {
        short[] levels = new short[32];
        levels[3] = 1;
        levels[4] = 1;
        levels[16 + 7] = 1;

        Assert.True(QuantEnc.IsFlat(levels, 2, WebpConstants.FlatnessLimitI4));
        Assert.False(QuantEnc.IsFlat(levels, 2, WebpConstants.FlatnessLimitIUv));
    }
}
