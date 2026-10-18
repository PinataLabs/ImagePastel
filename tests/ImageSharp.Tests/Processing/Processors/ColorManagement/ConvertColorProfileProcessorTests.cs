// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Numerics;
using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Processing.Processors.ColorManagement;

/// <summary>
/// Converts loaded images between ICC profiles with the <c>ConvertColorProfile</c> processor and
/// compares the pixels with Little CMS references (see <see cref="LittleCmsReferences"/>).
/// </summary>
[Trait("Profile", "Icc")]
public class ConvertColorProfileProcessorTests
{
    // About one code value at 8 bits: the conversion runs in float and rounds once at the end.
    private const float EightBitTolerance = 1F;

    // 16-bit results, in 8-bit code values: well below the 0.5 that rounding to 8 bits would cost.
    private const float SixteenBitTolerance = 0.05F;

    // Float results, as a CIE76 colour difference in Lab: far below what is visible (about 1).
    private const float FloatDeltaE = 0.05F;

    [Theory]
    [InlineData(TestImages.Icc.CompactAdobeRgbV2)]
    [InlineData(TestImages.Icc.CompactProPhotoV4)]
    [InlineData(TestImages.Icc.CompactDisplayP3V4)]
    [InlineData(TestImages.Icc.QcmsRec2020)]
    [InlineData(TestImages.Icc.CompactLinearSrgbV2)]
    public void ConvertColorProfileToSrgb_RgbProfile_MatchesLittleCms(string sourceProfile)
    {
        LittleCmsReference reference = LittleCmsReferences.Get(sourceProfile, null);
        using Image<Rgb24> image = CreateRgb24(reference);
        image.Metadata.IccProfile = LoadProfile(sourceProfile);

        image.Mutate(x => x.ConvertColorProfileToSrgb());

        AssertPixels(image, reference, EightBitTolerance);
    }

    [Theory]
    [InlineData(null, TestImages.Icc.CompactDisplayP3V4)]
    [InlineData(TestImages.Icc.LittleCmsSrgbV4, TestImages.Icc.CompactDisplayP3V4)]
    [InlineData(TestImages.Icc.CompactDisplayP3V4, TestImages.Icc.LittleCmsSrgbV2)]
    [InlineData(null, TestImages.Icc.LittleCmsRgbLut8V2)]
    [InlineData(null, TestImages.Icc.LittleCmsRgbLut16V2)]
    [InlineData(null, TestImages.Icc.LittleCmsRgbLutAToBV4)]
    [InlineData(TestImages.Webp.Lossy.WithIccp, TestImages.Icc.LittleCmsSrgbV2)]
    public void ConvertColorProfile_ProfileToProfile_MatchesLittleCms(string sourceProfile, string targetProfile)
    {
        LittleCmsReference reference = LittleCmsReferences.Get(sourceProfile, targetProfile);
        using Image<Rgb24> image = CreateRgb24(reference);
        image.Metadata.IccProfile = sourceProfile is null ? null : LoadProfile(sourceProfile);

        image.Mutate(x => x.ConvertColorProfile(LoadProfile(targetProfile)));

        AssertPixels(image, reference, EightBitTolerance);
    }

    [Theory]
    [InlineData(TestImages.Icc.CompactGrayV4)]
    [InlineData(TestImages.Icc.LittleCmsGray22V4)]
    [InlineData(TestImages.Icc.LittleCmsGrayLab18V4)]
    [InlineData(TestImages.Icc.LittleCmsGrayLutV4)]
    public void ConvertColorProfileToSrgb_GrayProfile_MatchesLittleCms(string sourceProfile)
    {
        LittleCmsReference reference = LittleCmsReferences.Get(sourceProfile, null);
        using Image<L8> image = new(reference.Count, 1);
        for (int i = 0; i < reference.Count; i++)
        {
            image[i, 0] = new L8((byte)reference.Input(i).R);
        }

        image.Metadata.IccProfile = LoadProfile(sourceProfile);

        image.Mutate(x => x.ConvertColorProfileToSrgb());

        for (int i = 0; i < reference.Count; i++)
        {
            float expected = reference.Output(i).R;
            Assert.True(MathF.Abs(image[i, 0].PackedValue - expected) <= EightBitTolerance, $"Gray {reference.Input(i).R}: expected {expected}, got {image[i, 0].PackedValue}.");
        }
    }

    [Theory]
    [InlineData(TestImages.Icc.LittleCmsGray22V4)]
    [InlineData(TestImages.Icc.LittleCmsGrayLab18V4)]
    [InlineData(TestImages.Icc.LittleCmsGrayLutV4)]
    public void ConvertColorProfile_SrgbToGrayProfile_MatchesLittleCms(string targetProfile)
    {
        LittleCmsReference reference = LittleCmsReferences.Get(null, targetProfile);
        using Image<Rgb24> image = CreateRgb24(reference);

        image.Mutate(x => x.ConvertColorProfile(LoadProfile(targetProfile)));

        AssertPixels(image, reference, EightBitTolerance);
    }

    [Fact]
    public void ConvertColorProfileToSrgb_SixteenBitPixels_KeepPrecision()
    {
        LittleCmsReference reference = LittleCmsReferences.Get(TestImages.Icc.CompactProPhotoV4, null);
        using Image<Rgba64> image = new(reference.Count, 1);
        for (int i = 0; i < reference.Count; i++)
        {
            (float r, float g, float b) = reference.Input(i);
            image[i, 0] = new Rgba64((ushort)(r * 257), (ushort)(g * 257), (ushort)(b * 257), (ushort)(i * 5000));
        }

        image.Metadata.IccProfile = LoadProfile(TestImages.Icc.CompactProPhotoV4);

        image.Mutate(x => x.ConvertColorProfileToSrgb());

        for (int i = 0; i < reference.Count; i++)
        {
            (float r, float g, float b) = reference.Output(i);
            Rgba64 actual = image[i, 0];
            Assert.Equal((ushort)(i * 5000), actual.A);
            AssertClose(i, (r, g, b), (actual.R / 257F, actual.G / 257F, actual.B / 257F), SixteenBitTolerance);
        }
    }

    [Fact]
    public void ConvertColorProfile_FloatPixels_MatchWithinDeltaE()
    {
        LittleCmsReference reference = LittleCmsReferences.Get(null, TestImages.Icc.CompactDisplayP3V4);
        using Image<RgbaVector> image = new(reference.Count, 1);
        for (int i = 0; i < reference.Count; i++)
        {
            (float r, float g, float b) = reference.Input(i);
            image[i, 0] = new RgbaVector(r / 255, g / 255, b / 255, i / 16F);
        }

        image.Mutate(x => x.ConvertColorProfile(LoadProfile(TestImages.Icc.CompactDisplayP3V4)));

        ColorSpaceConverter converter = new();
        for (int i = 0; i < reference.Count; i++)
        {
            (float r, float g, float b) = reference.Output(i);
            RgbaVector actual = image[i, 0];
            Assert.Equal(i / 16F, actual.A);

            // Both colours are read as sRGB for the colour difference: any fixed RGB space serves.
            Vector3 expectedLab = ToVector(converter.ToCieLab(new Rgb(r / 255, g / 255, b / 255)));
            Vector3 actualLab = ToVector(converter.ToCieLab(new Rgb(actual.R, actual.G, actual.B)));
            float deltaE = Vector3.Distance(expectedLab, actualLab);
            Assert.True(deltaE <= FloatDeltaE, $"Pixel {i} {reference.Input(i)}: delta E {deltaE}.");
        }
    }

    [Fact]
    public void ConvertColorProfile_Rgba32_LeavesAlphaUnchanged()
    {
        using Image<Rgba32> image = new(3, 1);
        image[0, 0] = new Rgba32(128, 64, 32, 0);
        image[1, 0] = new Rgba32(128, 64, 32, 77);
        image[2, 0] = new Rgba32(128, 64, 32, 255);
        image.Metadata.IccProfile = LoadProfile(TestImages.Icc.CompactAdobeRgbV2);

        image.Mutate(x => x.ConvertColorProfileToSrgb());

        // transicc gives 146.1484 61.7724 23.4316 for Adobe RGB (128, 64, 32) to sRGB.
        Assert.Equal(new Rgba32(146, 62, 23, 0), image[0, 0]);
        Assert.Equal(new Rgba32(146, 62, 23, 77), image[1, 0]);
        Assert.Equal(new Rgba32(146, 62, 23, 255), image[2, 0]);
    }

    [Fact]
    public void ConvertColorProfile_SetsTargetAsPixelProfile()
    {
        IccProfile target = LoadProfile(TestImages.Icc.CompactDisplayP3V4);
        using Image<Rgb24> image = new(2, 2);
        image.Metadata.IccProfile = LoadProfile(TestImages.Icc.CompactAdobeRgbV2);
        image.Frames.RootFrame.Metadata.IccProfile = image.Metadata.IccProfile;

        image.Mutate(x => x.ConvertColorProfile(target));

        Assert.Equal(target.ToByteArray(), image.Metadata.IccProfile.ToByteArray());
        Assert.Equal(target.ToByteArray(), image.Frames.RootFrame.Metadata.IccProfile.ToByteArray());

        // Each image gets its own copy, so editing it can't change the caller's profile.
        Assert.NotSame(target, image.Metadata.IccProfile);
    }

    [Fact]
    public void ConvertColorProfileToSrgb_RemovesPixelProfile()
    {
        using Image<Rgb24> image = new(2, 2);
        image.Metadata.IccProfile = LoadProfile(TestImages.Icc.CompactAdobeRgbV2);
        image.Frames.RootFrame.Metadata.IccProfile = image.Metadata.IccProfile;

        image.Mutate(x => x.ConvertColorProfileToSrgb());

        Assert.Null(image.Metadata.IccProfile);
        Assert.Null(image.Frames.RootFrame.Metadata.IccProfile);
    }

    [Fact]
    public void ConvertColorProfileToSrgb_WithoutPixelProfile_LeavesPixelsAsTheyAre()
    {
        using Image<Rgb24> image = new(1, 1, new Rgb24(128, 64, 32));

        image.Mutate(x => x.ConvertColorProfileToSrgb());

        Assert.Equal(new Rgb24(128, 64, 32), image[0, 0]);
        Assert.Null(image.Metadata.IccProfile);
    }

    [Fact]
    public void ConvertColorProfile_TruncatedSourceProfile_ThrowsInvalidImageContentException()
    {
        byte[] data = LoadProfile(TestImages.Icc.CompactAdobeRgbV2).ToByteArray();
        using Image<Rgb24> image = new(1, 1);
        image.Metadata.IccProfile = new IccProfile(data.AsSpan(0, 200).ToArray());

        Assert.Throws<InvalidImageContentException>(() => image.Mutate(x => x.ConvertColorProfileToSrgb()));
    }

    [Theory]
    [InlineData(IccProfileTag.RedMatrixColumn)]
    [InlineData(IccProfileTag.GreenTrc)]
    public void ConvertColorProfile_SourceProfileWithoutRequiredTag_ThrowsInvalidImageContentException(IccProfileTag missing)
    {
        IccProfile profile = LoadProfile(TestImages.Icc.CompactAdobeRgbV2);
        using Image<Rgb24> image = new(1, 1);
        image.Metadata.IccProfile = new IccProfile(profile.Header, Array.FindAll(profile.Entries, e => e.TagSignature != missing));

        Assert.Throws<InvalidImageContentException>(() => image.Mutate(x => x.ConvertColorProfileToSrgb()));
    }

    [Fact]
    public void ConvertColorProfile_GrayTargetWithoutGrayCurve_ThrowsInvalidImageContentException()
    {
        IccProfile profile = LoadProfile(TestImages.Icc.CompactGrayV4);
        IccProfile target = new(profile.Header, Array.FindAll(profile.Entries, e => e.TagSignature != IccProfileTag.GrayTrc));
        using Image<Rgb24> image = new(1, 1);

        Assert.Throws<InvalidImageContentException>(() => image.Mutate(x => x.ConvertColorProfile(target)));
    }

    [Theory]
    [InlineData(TestImages.Icc.LittleCmsRgbLut8V2)]
    [InlineData(TestImages.Icc.LittleCmsRgbLut16V2)]
    [InlineData(TestImages.Icc.LittleCmsRgbLutAToBV4)]
    public void ConvertColorProfileToSrgb_LutBasedRgbProfile_MatchesLittleCms(string sourceProfile)
    {
        LittleCmsReference reference = LittleCmsReferences.Get(sourceProfile, null);
        using Image<Rgb24> image = CreateRgb24(reference);
        image.Metadata.IccProfile = LoadProfile(sourceProfile);

        image.Mutate(x => x.ConvertColorProfileToSrgb());

        AssertPixels(image, reference, EightBitTolerance);
    }

    [Fact]
    public void ConvertColorProfile_CmykTarget_ThrowsNotSupportedException()
    {
        IccProfile cmyk = Image.Identify(TestFile.GetInputFileFullPath(TestImages.Jpeg.Baseline.Cmyk)).Metadata.IccProfile;
        using Image<Rgb24> image = new(1, 1);

        Assert.Throws<NotSupportedException>(() => image.Mutate(x => x.ConvertColorProfile(cmyk)));
    }

    [Fact]
    public void ConvertColorProfile_CmykPixelProfile_ThrowsNotSupportedException()
    {
        // A decoded CMYK JPEG keeps its CMYK profile next to RGB pixels; the processor must not read them as CMYK.
        using Image<Rgb24> image = Image.Load<Rgb24>(TestFile.GetInputFileFullPath(TestImages.Jpeg.Baseline.Cmyk));
        Rgb24 before = image[0, 0];

        Assert.Throws<NotSupportedException>(() => image.Mutate(x => x.ConvertColorProfileToSrgb()));
        Assert.Equal(before, image[0, 0]);
    }

    [Theory]
    [InlineData(TestImages.Icc.LittleCmsRgbIntentsV2, null, IccRenderingIntent.Saturation, false)]
    [InlineData(TestImages.Icc.LittleCmsRgbIntentsV2, null, IccRenderingIntent.AbsoluteColorimetric, false)]
    [InlineData(null, TestImages.Icc.LittleCmsRgbIntentsV2, IccRenderingIntent.MediaRelativeColorimetric, true)]
    [InlineData(TestImages.Icc.LittleCmsRgbIntentsMatrixV2, TestImages.Icc.LittleCmsSrgbV2, IccRenderingIntent.Perceptual, true)]
    public void ConvertColorProfile_IntentAndBlackPointCompensation_MatchLittleCms(string sourceProfile, string targetProfile, IccRenderingIntent intent, bool blackPointCompensation)
    {
        LittleCmsReference reference = LittleCmsReferences.Get(sourceProfile, targetProfile, intent, blackPointCompensation);
        IccColorTransformOptions options = new() { RenderingIntent = intent, BlackPointCompensation = blackPointCompensation };
        using Image<Rgb24> image = CreateRgb24(reference);
        image.Metadata.IccProfile = sourceProfile is null ? null : LoadProfile(sourceProfile);

        if (targetProfile is null)
        {
            image.Mutate(x => x.ConvertColorProfileToSrgb(options));
        }
        else
        {
            image.Mutate(x => x.ConvertColorProfile(LoadProfile(targetProfile), options));
        }

        AssertPixels(image, reference, EightBitTolerance);
    }

    /// <summary>
    /// Loads a test profile: an ICC file, or the profile embedded in an image file.
    /// </summary>
    internal static IccProfile LoadProfile(string file)
        => file.EndsWith(".icc", StringComparison.Ordinal)
            ? new(File.ReadAllBytes(TestFile.GetInputFileFullPath(file)))
            : Image.Identify(TestFile.GetInputFileFullPath(file)).Metadata.IccProfile;

    private static Vector3 ToVector(CieLab lab) => new(lab.L, lab.A, lab.B);

    private static Image<Rgb24> CreateRgb24(LittleCmsReference reference)
    {
        Image<Rgb24> image = new(reference.Count, 1);
        for (int i = 0; i < reference.Count; i++)
        {
            (float r, float g, float b) = reference.Input(i);
            image[i, 0] = new Rgb24((byte)r, (byte)g, (byte)b);
        }

        return image;
    }

    private static void AssertPixels(Image<Rgb24> image, LittleCmsReference reference, float tolerance)
    {
        for (int i = 0; i < reference.Count; i++)
        {
            Rgb24 actual = image[i, 0];
            AssertClose(i, reference.Output(i), (actual.R, actual.G, actual.B), tolerance);
        }
    }

    private static void AssertClose(int pixel, (float R, float G, float B) expected, (float R, float G, float B) actual, float tolerance)
        => Assert.True(
            MathF.Abs(actual.R - expected.R) <= tolerance && MathF.Abs(actual.G - expected.G) <= tolerance && MathF.Abs(actual.B - expected.B) <= tolerance,
            $"Pixel {pixel}: expected {expected}, got {actual}.");
}
