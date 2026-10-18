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

    // The pixel types that can hold CMYK, and the number of rows in each Little CMS reference table.
    private const PixelTypes CmykPixelTypes = PixelTypes.Rgba32 | PixelTypes.Rgba64 | PixelTypes.RgbaVector;
    private const int ReferenceRows = 12;

    // RGB after a round trip through CMYK held in 16 bits or float, in 8-bit code values: the converter-level
    // difference from Little CMS for LUT profiles (about 0.2).
    private const float CmykRoundTripTolerance = 0.25F;

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
    public void ConvertColorProfileToSrgb_FramesWithOwnProfiles_ConvertsEachFrameFromItsProfile()
    {
        // As a TIFF decodes: each frame has its own pixel profile, and the image's is the first frame's.
        LittleCmsReference adobe = LittleCmsReferences.Get(TestImages.Icc.CompactAdobeRgbV2, null);
        LittleCmsReference displayP3 = LittleCmsReferences.Get(TestImages.Icc.CompactDisplayP3V4, null);
        using Image<Rgb24> image = new(Math.Max(adobe.Count, displayP3.Count), 1);
        image.Frames.CreateFrame();
        image.Frames.CreateFrame();
        SetInputs(image.Frames[0], adobe);
        SetInputs(image.Frames[1], displayP3);
        image.Frames[2][0, 0] = new Rgb24(128, 64, 32);
        image.Frames[0].Metadata.IccProfile = LoadProfile(TestImages.Icc.CompactAdobeRgbV2);
        image.Frames[1].Metadata.IccProfile = LoadProfile(TestImages.Icc.CompactDisplayP3V4);
        image.Metadata.IccProfile = image.Frames[0].Metadata.IccProfile;

        image.Mutate(x => x.ConvertColorProfileToSrgb());

        AssertPixels(image.Frames[0], adobe, EightBitTolerance);
        AssertPixels(image.Frames[1], displayP3, EightBitTolerance);

        // A frame without a profile already holds sRGB.
        Assert.Equal(new Rgb24(128, 64, 32), image.Frames[2][0, 0]);
        Assert.Null(image.Metadata.IccProfile);
        Assert.All<ImageFrame<Rgb24>>(image.Frames, frame => Assert.Null(frame.Metadata.IccProfile));
    }

    [Fact]
    public void ConvertColorProfile_FramesWithOwnProfiles_GivesEveryFrameTheTarget()
    {
        LittleCmsReference fromSrgb = LittleCmsReferences.Get(null, TestImages.Icc.CompactDisplayP3V4);
        IccProfile target = LoadProfile(TestImages.Icc.CompactDisplayP3V4);
        using Image<Rgb24> image = new(fromSrgb.Count, 1);
        image.Frames.CreateFrame();
        SetInputs(image.Frames[1], fromSrgb);
        image.Frames[0].Metadata.IccProfile = LoadProfile(TestImages.Icc.CompactAdobeRgbV2);
        image.Metadata.IccProfile = image.Frames[0].Metadata.IccProfile;

        image.Mutate(x => x.ConvertColorProfile(target));

        // The second frame has no profile, so its pixels are sRGB.
        AssertPixels(image.Frames[1], fromSrgb, EightBitTolerance);
        Assert.Equal(target.ToByteArray(), image.Metadata.IccProfile.ToByteArray());
        Assert.All<ImageFrame<Rgb24>>(image.Frames, frame => Assert.Equal(target.ToByteArray(), frame.Metadata.IccProfile?.ToByteArray()));
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

    [Theory]
    [WithBlankImages(ReferenceRows, 1, CmykPixelTypes)]
    public void ConvertColorProfile_SrgbToCmykProfile_MatchesLittleCms<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        LittleCmsReference reference = LittleCmsReferences.Get(null, TestImages.Icc.LittleCmsFakeCmykV4);
        using Image<TPixel> image = provider.GetImage();
        SetInputs(image, reference);

        image.Mutate(x => x.ConvertColorProfile(LoadProfile(TestImages.Icc.LittleCmsFakeCmykV4)));

        // C, M, Y and K land in the four components; the reference is in percent of ink.
        float tolerance = CmykTolerance<TPixel>();
        for (int i = 0; i < reference.Count; i++)
        {
            AssertClose(i, reference.ScaledOutput(i, 0), image[i, 0].ToScaledVector4(), tolerance);
        }
    }

    [Theory]
    [WithBlankImages(ReferenceRows, 1, CmykPixelTypes)]
    public void ConvertColorProfileToSrgb_CmykImage_MatchesLittleCmsRoundTrip<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        bool eightBit = typeof(TPixel) == typeof(Rgba32);
        LittleCmsReference reference = LittleCmsReferences.GetRoundTrip(TestImages.Icc.LittleCmsFakeCmykV4, eightBit);
        using Image<TPixel> image = provider.GetImage();
        SetInputs(image, reference);

        image.Mutate(x => x.ConvertColorProfile(LoadProfile(TestImages.Icc.LittleCmsFakeCmykV4)));
        image.Mutate(x => x.ConvertColorProfileToSrgb());

        float tolerance = eightBit ? EightBitTolerance : CmykRoundTripTolerance;
        for (int i = 0; i < reference.Count; i++)
        {
            Vector4 actual = image[i, 0].ToScaledVector4() * 255;
            AssertClose(i, reference.Output(i), (actual.X, actual.Y, actual.Z), tolerance);

            // CMYK has no alpha, so the colours come back opaque.
            Assert.Equal(255F, actual.W);
        }
    }

    [Fact]
    public void ConvertColorProfile_CmykTarget_SetsTargetAsPixelProfile()
    {
        IccProfile target = LoadProfile(TestImages.Icc.LittleCmsFakeCmykV4);
        using Image<Rgba32> image = new(2, 2);
        image.Frames.RootFrame.Metadata.IccProfile = LoadProfile(TestImages.Icc.CompactAdobeRgbV2);
        image.Metadata.IccProfile = image.Frames.RootFrame.Metadata.IccProfile;

        image.Mutate(x => x.ConvertColorProfile(target));

        Assert.Equal(IccColorSpaceType.Cmyk, image.Metadata.IccProfile.DataColorSpace);
        Assert.Equal(target.ToByteArray(), image.Metadata.IccProfile.ToByteArray());
        Assert.Equal(target.ToByteArray(), image.Frames.RootFrame.Metadata.IccProfile.ToByteArray());
    }

    [Theory]
    [WithBlankImages(1, 1, PixelTypes.Rgb24 | PixelTypes.Bgra32 | PixelTypes.Argb32 | PixelTypes.L16 | PixelTypes.Rgb48 | PixelTypes.HalfVector4)]
    public void ConvertColorProfile_CmykTargetWithOtherPixelType_ThrowsNotSupportedException<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        IccProfile cmyk = LoadProfile(TestImages.Icc.LittleCmsFakeCmykV4);

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => image.Mutate(x => x.ConvertColorProfile(cmyk)));

        AssertNamesCmykPixelTypes(ex);
        Assert.Null(image.Metadata.IccProfile);
    }

    [Theory]
    [WithBlankImages(1, 1, PixelTypes.Rgb24 | PixelTypes.Bgra32 | PixelTypes.L8)]
    public void ConvertColorProfileToSrgb_CmykPixelProfileWithOtherPixelType_ThrowsNotSupportedException<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        image.Metadata.IccProfile = LoadProfile(TestImages.Icc.LittleCmsFakeCmykV4);

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => image.Mutate(x => x.ConvertColorProfileToSrgb()));

        AssertNamesCmykPixelTypes(ex);
        Assert.Equal(IccColorSpaceType.Cmyk, image.Metadata.IccProfile.DataColorSpace);
    }

    [Fact]
    public void ConvertColorProfile_DefaultDecodedCmykTiffAsRgba32_TreatsPixelsAsSrgb()
    {
        // A CMYK TIFF decoded without conversion has formula RGB pixels and no pixel profile (its CMYK
        // profile is only the embedded one), so the processor reads the pixels as sRGB, not as CMYK:
        // converting to sRGB changes nothing, and converting to Display P3 gives what an image with
        // the same pixels and an explicit sRGB pixel profile gives.
        using Image<Rgba32> image = Image.Load<Rgba32>(TestFile.GetInputFileFullPath(TestImages.Tiff.Cmyk8BitWithIccProfile));
        Assert.Null(image.Metadata.IccProfile);
        Assert.Equal(IccColorSpaceType.Cmyk, image.Metadata.GetTiffMetadata().EmbeddedIccProfile.DataColorSpace);
        IccProfile p3 = LoadProfile(TestImages.Icc.CompactDisplayP3V4);
        using Image<Rgba32> asSrgb = image.Clone();
        asSrgb.Metadata.IccProfile = LoadProfile(TestImages.Icc.LittleCmsSrgbV4);
        asSrgb.Mutate(x => x.ConvertColorProfile(p3));

        using (Image<Rgba32> toSrgb = image.Clone(x => x.ConvertColorProfileToSrgb()))
        {
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    Assert.Equal(image[x, y], toSrgb[x, y]);
                }
            }
        }

        image.Mutate(x => x.ConvertColorProfile(p3));

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 a = asSrgb[x, y];
                Rgba32 b = image[x, y];
                Assert.True(Math.Abs(a.R - b.R) <= 1 && Math.Abs(a.G - b.G) <= 1 && Math.Abs(a.B - b.B) <= 1 && a.A == b.A, $"({x}, {y}): {a} and {b}.");
            }
        }

        Assert.Equal(p3.ToByteArray(), image.Metadata.IccProfile.ToByteArray());
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

    // Writes the reference inputs into the first row, opaque.
    private static void SetInputs<TPixel>(Image<TPixel> image, LittleCmsReference reference)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        for (int i = 0; i < reference.Count; i++)
        {
            TPixel pixel = default;
            pixel.FromScaledVector4(reference.ScaledInput(i, 1));
            image[i, 0] = pixel;
        }
    }

    // CMYK results, per component scaled to [0, 1]. Little CMS and the engine differ by up to 0.2 % of
    // ink near black (lcms quantises table-curve inputs to 16 bits); 8-bit storage adds half a code.
    private static float CmykTolerance<TPixel>()
        where TPixel : unmanaged, IPixel<TPixel>
        => typeof(TPixel) == typeof(Rgba32) ? (0.5F / 255) + 0.002F : 0.002F;

    private static void AssertNamesCmykPixelTypes(NotSupportedException ex)
    {
        Assert.Contains(nameof(Rgba32), ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(Rgba64), ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(RgbaVector), ex.Message, StringComparison.Ordinal);
    }

    private static void AssertClose(int pixel, Vector4 expected, Vector4 actual, float tolerance)
    {
        Vector4 difference = Vector4.Abs(expected - actual);
        Assert.True(
            difference.X <= tolerance && difference.Y <= tolerance && difference.Z <= tolerance && difference.W <= tolerance,
            $"Pixel {pixel}: expected {expected}, got {actual}.");
    }

    private static void AssertPixels(Image<Rgb24> image, LittleCmsReference reference, float tolerance)
        => AssertPixels(image.Frames.RootFrame, reference, tolerance);

    private static void SetInputs(ImageFrame<Rgb24> frame, LittleCmsReference reference)
    {
        for (int i = 0; i < reference.Count; i++)
        {
            (float r, float g, float b) = reference.Input(i);
            frame[i, 0] = new Rgb24((byte)r, (byte)g, (byte)b);
        }
    }

    private static void AssertPixels(ImageFrame<Rgb24> image, LittleCmsReference reference, float tolerance)
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
