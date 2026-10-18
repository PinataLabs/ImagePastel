// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Tests.Processing.Processors.ColorManagement;
using Xunit;

namespace SixLabors.ImageSharp.Tests.ColorManagement;

/// <summary>
/// Uses <see cref="IccColorTransform"/> directly, on rows of scaled colors, against Little CMS references.
/// </summary>
[Trait("Profile", "Icc")]
public class IccColorTransformTests
{
    // In 8-bit code values. The transform runs in float and does not round.
    private const float Tolerance = 0.02F;

    // In 8-bit code values, or percent of ink for CMYK. Little CMS rounds the inputs of tabulated
    // curves to 16 bits and interpolates 16-bit tables in fixed point; the engine works in float.
    // Near black, where the test profiles' curves are steep, that differs by up to about 0.15.
    private const float LutTolerance = 0.2F;

    [Fact]
    public void Transform_IsReusableForManyRows()
    {
        LittleCmsReference reference = LittleCmsReferences.Get(TestImages.Icc.QcmsRec2020, null);
        IccColorTransform transform = new(ConvertColorProfileProcessorTests.LoadProfile(TestImages.Icc.QcmsRec2020), null);

        // Rows of the reference that stay in the sRGB gamut: (128, 64, 32), (128, 128, 128) and (1, 2, 3).
        foreach (int row in new[] { 5, 10, 11 })
        {
            Vector4[] colors = [ToScaled(reference.Input(row), 0.25F)];

            transform.Transform(colors);

            AssertClose(reference.Output(row), colors[0]);
            Assert.Equal(0.25F, colors[0].W);
        }
    }

    [Fact]
    public void Transform_SrgbToSrgb_IsIdentity()
    {
        IccColorTransform transform = new(null, null);
        Vector4[] colors = [new(0.2F, 0.4F, 0.6F, 0.8F), new(1, 0, 0.5F, 1)];

        transform.Transform(colors);

        Assert.Equal(new Vector4(0.2F, 0.4F, 0.6F, 0.8F), colors[0], new ApproximateFloatComparer(1e-5F));
        Assert.Equal(new Vector4(1, 0, 0.5F, 1), colors[1], new ApproximateFloatComparer(1e-5F));
    }

    [Fact]
    public void Transform_OutOfGamut_IsNotClamped()
    {
        // ProPhoto green is far outside sRGB: Little CMS without -s gives a negative red.
        IccColorTransform transform = new(ConvertColorProfileProcessorTests.LoadProfile(TestImages.Icc.CompactProPhotoV4), null);
        Vector4[] colors = [new(0, 1, 0, 1)];

        transform.Transform(colors);

        Assert.True(colors[0].X < 0);
    }

    [Theory]
    [InlineData(TestImages.Icc.LittleCmsRgbLut8V2, null)]
    [InlineData(null, TestImages.Icc.LittleCmsRgbLut8V2)]
    [InlineData(TestImages.Icc.LittleCmsRgbLut16V2, null)]
    [InlineData(null, TestImages.Icc.LittleCmsRgbLut16V2)]
    [InlineData(TestImages.Icc.LittleCmsRgbLutAToBV4, null)]
    [InlineData(null, TestImages.Icc.LittleCmsRgbLutAToBV4)]
    [InlineData(TestImages.Icc.LittleCmsGrayLutV4, null)]
    [InlineData(null, TestImages.Icc.LittleCmsGrayLutV4)]
    [InlineData(TestImages.Webp.Lossy.WithIccp, TestImages.Icc.LittleCmsSrgbV2)]
    public void Transform_LutBasedProfile_MatchesLittleCms(string sourceProfile, string targetProfile)
    {
        LittleCmsReference reference = LittleCmsReferences.Get(sourceProfile, targetProfile);
        IccColorTransform transform = new(Load(sourceProfile), Load(targetProfile));

        for (int row = 0; row < reference.Count; row++)
        {
            Vector4[] colors = [reference.ScaledInput(row, 0.25F)];

            transform.Transform(colors);

            AssertClose(row, reference.ScaledOutput(row, 0.25F), Clamp(colors[0]), LutTolerance / 255);
        }
    }

    [Theory]
    [InlineData(TestImages.Icc.LittleCmsFakeCmykV4)]
    [InlineData(TestImages.Icc.TiffCmykTestProfile)]
    public void Transform_CmykSourceToSrgb_MatchesLittleCms(string sourceProfile)
    {
        LittleCmsReference reference = LittleCmsReferences.Get(sourceProfile, null);
        IccColorTransform transform = new(Load(sourceProfile), null);

        for (int row = 0; row < reference.Count; row++)
        {
            // C, M, Y and K in X, Y, Z and W.
            Vector4[] colors = [reference.ScaledInput(row, 0)];

            transform.Transform(colors);

            // CMYK has no alpha, so the result is opaque.
            AssertClose(row, reference.ScaledOutput(row, 1), Clamp(colors[0]), LutTolerance / 255);
        }
    }

    [Fact]
    public void Transform_SrgbToCmykTarget_MatchesLittleCms()
    {
        LittleCmsReference reference = LittleCmsReferences.Get(null, TestImages.Icc.LittleCmsFakeCmykV4);
        IccColorTransform transform = new(null, Load(TestImages.Icc.LittleCmsFakeCmykV4));

        for (int row = 0; row < reference.Count; row++)
        {
            Vector4[] colors = [reference.ScaledInput(row, 0.25F)];

            transform.Transform(colors);

            // C, M, Y and K in X, Y, Z and W, in percent in the reference.
            AssertClose(row, reference.ScaledOutput(row, 0), colors[0], LutTolerance / 100);
        }
    }

    [Fact]
    public void Constructor_LutWithWrongChannelCount_ThrowsInvalidImageContentException()
    {
        // An RGB table (three inputs) in a profile that says it is CMYK.
        IccProfile rgb = Load(TestImages.Icc.LittleCmsRgbLut16V2);
        IccProfileHeader header = rgb.Header;
        header.DataColorSpace = IccColorSpaceType.Cmyk;
        IccProfile profile = new(new IccProfile(header, rgb.Entries).ToByteArray());

        Assert.Throws<InvalidImageContentException>(() => new IccColorTransform(profile, null));
    }

    [Fact]
    public void Constructor_CmykProfileWithoutTable_ThrowsInvalidImageContentException()
    {
        IccProfile cmyk = Load(TestImages.Icc.TiffCmykTestProfile);
        IccProfile profile = new(cmyk.Header, Array.FindAll(cmyk.Entries, e => e.TagSignature != IccProfileTag.AToB0));

        Assert.Throws<InvalidImageContentException>(() => new IccColorTransform(profile, null));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
        => Assert.Throws<ArgumentNullException>(() => new IccColorTransform(null, null, null));

    [Fact]
    public void Constructor_CorruptTarget_ThrowsInvalidImageContentException()
        => Assert.Throws<InvalidImageContentException>(() => new IccColorTransform(null, new IccProfile(new byte[16])));

    private static IccProfile Load(string file) => file is null ? null : ConvertColorProfileProcessorTests.LoadProfile(file);

    // transicc -s clips the color channels to the gamut.
    private static Vector4 Clamp(Vector4 v) => new(Math.Clamp(v.X, 0, 1), Math.Clamp(v.Y, 0, 1), Math.Clamp(v.Z, 0, 1), v.W);

    private static void AssertClose(int row, Vector4 expected, Vector4 actual, float tolerance)
    {
        Vector4 difference = Vector4.Abs(expected - actual);
        Assert.True(
            difference.X <= tolerance && difference.Y <= tolerance && difference.Z <= tolerance && difference.W <= tolerance,
            $"Row {row}: expected {expected}, got {actual}.");
    }

    private static Vector4 ToScaled((float R, float G, float B) code, float alpha) => new(code.R / 255, code.G / 255, code.B / 255, alpha);

    private static void AssertClose((float R, float G, float B) expected, Vector4 actual)
    {
        Vector3 difference = Vector3.Abs(new Vector3(expected.R, expected.G, expected.B) - (new Vector3(actual.X, actual.Y, actual.Z) * 255));
        Assert.True(
            difference.X <= Tolerance && difference.Y <= Tolerance && difference.Z <= Tolerance,
            $"Expected {expected}, got {new Vector3(actual.X, actual.Y, actual.Z) * 255}.");
    }
}
