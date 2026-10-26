// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Formats.Webp.Lossy;
using PinataLabs.ImagePastel.Tests.TestUtilities;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.WebP;

[Trait("Format", "Webp")]
public class Vp8ResidualTests
{
    private static void RunSetCoeffsTest()
    {
        // arrange
        var residual = new Vp8Residual();
        short[] coeffs = { 110, 0, -2, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0 };

        // act
        residual.SetCoeffs(coeffs);

        // assert
        Assert.Equal(9, residual.Last);
    }

    private static void RunGetResidualCostTest()
    {
        // The expected costs come from the scalar path, which follows the reference algorithm.
        // Non-zero coefficients in positions 8 to 15 check that the vectorized path
        // does not use the levels of positions 0 to 7 for them.
        AssertResidualCost(new short[] { 1, 2, 3, 4, 5, 6, 7, 8, 0, 0, 0, 0, 0, 0, 0, 0 }, 19151);
        AssertResidualCost(new short[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5 }, 8433);
        AssertResidualCost(new short[] { 0, 0, 0, 0, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 1 }, 7817);
        AssertResidualCost(new short[] { 1, 1, 1, 1, 1, 1, 1, 1, 9, 9, 9, 9, 9, 9, 9, 9 }, 32216);
    }

    private static void AssertResidualCost(short[] coeffs, int expectedCost)
    {
        // arrange
        var proba = new Vp8EncProba();
        proba.CalculateLevelCosts();
        var residual = new Vp8Residual();
        residual.Init(0, 3, proba);
        residual.SetCoeffs(coeffs);

        // act
        int cost = residual.GetResidualCost(0);

        // assert
        Assert.Equal(expectedCost, cost);
    }

    [Fact]
    public void RunSetCoeffsTest_Works() => RunSetCoeffsTest();

    [Fact]
    public void RunSetCoeffsTest_WithHardwareIntrinsics_Works() => FeatureTestRunner.RunWithHwIntrinsicsFeature(RunSetCoeffsTest, HwIntrinsics.AllowAll);

    [Fact]
    public void RunSetCoeffsTest_WithoutHardwareIntrinsics_Works() => FeatureTestRunner.RunWithHwIntrinsicsFeature(RunSetCoeffsTest, HwIntrinsics.DisableHWIntrinsic);

    [Fact]
    public void GetResidualCost_Works() => RunGetResidualCostTest();

    [Fact]
    public void GetResidualCost_WithoutAvx2_Works() => FeatureTestRunner.RunWithHwIntrinsicsFeature(RunGetResidualCostTest, HwIntrinsics.DisableAVX2);

    [Fact]
    public void GetResidualCost_WithoutHardwareIntrinsics_Works() => FeatureTestRunner.RunWithHwIntrinsicsFeature(RunGetResidualCostTest, HwIntrinsics.DisableHWIntrinsic);
}
