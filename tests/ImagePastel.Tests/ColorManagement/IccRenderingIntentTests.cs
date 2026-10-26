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
/// Converts colors with each rendering intent, with and without black-point compensation, through
/// <see cref="IccColorTransform"/>, against Little CMS references (see <see cref="LittleCmsReferences"/>).
/// </summary>
/// <remarks>
/// The test profiles (<c>Icc/LittleCms/RgbIntents*.icc</c>) have different tables for each intent,
/// lighter black points than sRGB and a paper white as their media white point.
/// </remarks>
[Trait("Profile", "Icc")]
public class IccRenderingIntentTests
{
    // In 8-bit code values, or percent of ink for CMYK: the LUT tolerance of IccColorTransformTests.
    // Little CMS rounds the inputs of tabulated curves to 16 bits; the engine works in float.
    private const float Tolerance = 0.2F;

    private const string V2 = TestImages.Icc.LittleCmsRgbIntentsV2;
    private const string V4 = TestImages.Icc.LittleCmsRgbIntentsV4;
    private const string Matrix = TestImages.Icc.LittleCmsRgbIntentsMatrixV2;
    private const string SrgbV2 = TestImages.Icc.LittleCmsSrgbV2;
    private const string Cmyk = TestImages.Icc.LittleCmsFakeCmykV4;

    private const IccRenderingIntent Perceptual = IccRenderingIntent.Perceptual;
    private const IccRenderingIntent Relative = IccRenderingIntent.MediaRelativeColorimetric;
    private const IccRenderingIntent Saturation = IccRenderingIntent.Saturation;
    private const IccRenderingIntent Absolute = IccRenderingIntent.AbsoluteColorimetric;

    public static TheoryData<string, string, IccRenderingIntent> EachIntent { get; } = new()
    {
        // The source's A2B0, A2B1 and A2B2 tables differ; absolute colorimetric uses A2B1 and the paper white.
        { V2, null, Perceptual },
        { V2, null, Relative },
        { V2, null, Saturation },
        { V2, null, Absolute },

        // The target's B2A0, B2A1 and B2A2 tables differ.
        { null, V2, Perceptual },
        { null, V2, Relative },
        { null, V2, Saturation },
        { null, V2, Absolute },

        // A version 2 display profile's media white is D50, whatever its wtpt tag says.
        { V2, Matrix, Absolute },
    };

    public static TheoryData<string, string, IccRenderingIntent> Fallbacks { get; } = new()
    {
        // No A2B0: perceptual and saturation use the matrix/TRC tags, relative and absolute A2B1.
        { Matrix, null, Perceptual },
        { Matrix, null, Relative },
        { Matrix, null, Saturation },
        { Matrix, null, Absolute },

        // No A2B2 or B2A2: saturation uses A2B0 or B2A0 (not the relative colorimetric tables).
        { V4, null, Saturation },
        { V4, null, Relative },
        { null, V4, Saturation },
    };

    public static TheoryData<string, string, IccRenderingIntent> BlackPointCompensation { get; } = new()
    {
        // The source black point from its darkest colorant (lut16 table), to sRGB.
        { V2, null, Relative },

        // The target black point by the Adobe round-trip algorithm on its B2A1 table.
        { null, V2, Relative },

        // Lab PCS on both sides.
        { V2, V4, Relative },

        // Matrix/TRC profiles with a lighter black: no fixed black, so compensation on and off differ.
        { Matrix, SrgbV2, Perceptual },
        { null, Matrix, Perceptual },
        { null, Matrix, Relative },
        { null, V4, Relative },
    };

    [Theory]
    [MemberData(nameof(EachIntent))]
    public void Transform_EachIntent_MatchesLittleCms(string source, string target, IccRenderingIntent intent)
        => AssertMatches(source, target, intent, false);

    [Theory]
    [MemberData(nameof(Fallbacks))]
    public void Transform_IntentTableMissing_FallsBackAsLittleCms(string source, string target, IccRenderingIntent intent)
        => AssertMatches(source, target, intent, false);

    [Theory]
    [MemberData(nameof(BlackPointCompensation))]
    public void Transform_WithBlackPointCompensation_MatchesLittleCms(string source, string target, IccRenderingIntent intent)
        => AssertMatches(source, target, intent, true);

    [Fact]
    public void Transform_WithBlackPointCompensation_FittedTargetBlackPoint_MatchesLittleCms()
    {
        // The perceptual target black point comes from a quadratic fit of the round trip's shadows
        // (L* from 11 to 41 here). The fit is ill-conditioned for this profile: the first point sits
        // at 0.030003 of the range, just inside the 0.03 limit, so a round trip that differs from
        // Little CMS's by 0.001 L* (its 16-bit table inputs) moves the black point from L* 2.32 to
        // 2.54. That moves the darkest colors by up to half a code value, within one code.
        AssertMatches(null, V2, Perceptual, true, 1F);
    }

    [Fact]
    public void Transform_WithoutBlackPointCompensation_MatchesLittleCms()
    {
        // The same pairs as above, compensation off: the references differ in the shadows.
        AssertMatches(V2, null, Relative, false);
        AssertMatches(null, V2, Relative, false);
        AssertMatches(Matrix, SrgbV2, Perceptual, false);
    }

    [Fact]
    public void Transform_PerceptualToVersion4Target_CompensatesBlackPointAlways()
    {
        // Little CMS turns black-point compensation on for a version 4 target with the perceptual
        // and saturation intents; the reference without -b already has it.
        AssertMatches(Matrix, null, Perceptual, false);
        AssertMatches(null, V4, Saturation, false);
    }

    [Theory]
    [InlineData(Cmyk, null, Relative)]
    [InlineData(Cmyk, null, Absolute)]
    public void Transform_CmykSourceWithBlackPointCompensation_MatchesLittleCms(string source, string target, IccRenderingIntent intent)
    {
        // Absolute colorimetric ignores black-point compensation.
        LittleCmsReference reference = LittleCmsReferences.Get(source, target, intent, true);
        IccColorTransform transform = new(Load(source), Load(target), new IccColorTransformOptions { RenderingIntent = intent, BlackPointCompensation = true });

        for (int row = 0; row < reference.Count; row++)
        {
            Vector4[] colors = [reference.ScaledInput(row, 0)];

            transform.Transform(colors);

            AssertClose(row, reference.ScaledOutput(row, 1), Clamp(colors[0]), Tolerance / 255);
        }
    }

    [Fact]
    public void Transform_CmykTargetWithBlackPointCompensation_MatchesLittleCms()
    {
        LittleCmsReference reference = LittleCmsReferences.Get(null, Cmyk, Relative, true);
        IccColorTransform transform = new(null, Load(Cmyk), new IccColorTransformOptions { RenderingIntent = Relative, BlackPointCompensation = true });

        for (int row = 0; row < reference.Count; row++)
        {
            Vector4[] colors = [reference.ScaledInput(row, 0.25F)];

            transform.Transform(colors);

            AssertClose(row, reference.ScaledOutput(row, 0), colors[0], Tolerance / 100);
        }
    }

    [Fact]
    public void Constructor_UndefinedIntent_ThrowsArgumentOutOfRangeException()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new IccColorTransform(null, null, new IccColorTransformOptions { RenderingIntent = (IccRenderingIntent)4 }));

    private static void AssertMatches(string source, string target, IccRenderingIntent intent, bool blackPointCompensation, float tolerance = Tolerance)
    {
        LittleCmsReference reference = LittleCmsReferences.Get(source, target, intent, blackPointCompensation);
        IccColorTransformOptions options = new() { RenderingIntent = intent, BlackPointCompensation = blackPointCompensation };
        IccColorTransform transform = new(Load(source), Load(target), options);

        for (int row = 0; row < reference.Count; row++)
        {
            Vector4[] colors = [reference.ScaledInput(row, 0.25F)];

            transform.Transform(colors);

            AssertClose(row, reference.ScaledOutput(row, 0.25F), Clamp(colors[0]), tolerance / 255);
        }
    }

    private static IccProfile Load(string file) => file is null ? null : ConvertColorProfileProcessorTests.LoadProfile(file);

    // transicc -s clips the color channels to the gamut.
    private static Vector4 Clamp(Vector4 v) => new(Math.Clamp(v.X, 0, 1), Math.Clamp(v.Y, 0, 1), Math.Clamp(v.Z, 0, 1), v.W);

    private static void AssertClose(int row, Vector4 expected, Vector4 actual, float tolerance)
    {
        Vector4 difference = Vector4.Abs(expected - actual);
        Assert.True(
            difference.X <= tolerance && difference.Y <= tolerance && difference.Z <= tolerance && difference.W <= tolerance,
            $"Row {row}: expected {expected * 255}, got {actual * 255} (x255).");
    }
}
