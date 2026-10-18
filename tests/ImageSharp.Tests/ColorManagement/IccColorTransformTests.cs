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

    [Fact]
    public void Constructor_NullOptions_Throws()
        => Assert.Throws<ArgumentNullException>(() => new IccColorTransform(null, null, null));

    [Fact]
    public void Constructor_CorruptTarget_ThrowsInvalidImageContentException()
        => Assert.Throws<InvalidImageContentException>(() => new IccColorTransform(null, new IccProfile(new byte[16])));

    private static Vector4 ToScaled((float R, float G, float B) code, float alpha) => new(code.R / 255, code.G / 255, code.B / 255, alpha);

    private static void AssertClose((float R, float G, float B) expected, Vector4 actual)
    {
        Vector3 difference = Vector3.Abs(new Vector3(expected.R, expected.G, expected.B) - (new Vector3(actual.X, actual.Y, actual.Z) * 255));
        Assert.True(
            difference.X <= Tolerance && difference.Y <= Tolerance && difference.Z <= Tolerance,
            $"Expected {expected}, got {new Vector3(actual.X, actual.Y, actual.Z) * 255}.");
    }
}
