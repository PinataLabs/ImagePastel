// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Numerics;
using PinataLabs.ImagePastel.ColorManagement;
using PinataLabs.ImagePastel.ColorSpaces.Conversion;
using PinataLabs.ImagePastel.Formats.Png;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.ColorManagement;

/// <summary>
/// Checks the profiles <see cref="IccProfileFactory"/> builds against the profiles Little CMS builds for the
/// same spaces (<c>cmsCreate_sRGBProfile</c>, <c>cmsCreateRGBProfile</c>, <c>cmsCreateGrayProfile</c>; see
/// <c>tests/Images/Input/Icc/README.md</c>), tag by tag, as the JPEG XL colour-encoding tests compare
/// profiles with <c>djxl</c>'s.
/// </summary>
[Trait("Profile", "Icc")]
public class IccProfileFactoryTests
{
    // Two steps of s15Fixed16Number (2^-16): the two builders round their own maths (Little CMS in double,
    // the factory as libjxl in float with double intermediates) before the fixed-point encoding.
    private const float Tolerance = 2F / 65536;

    // Curves of different function types (gamma 1 against a linear type 3 curve) are compared by value.
    private const float CurveTolerance = 1e-4F;

    // The values the Little CMS program in tests/Images/Input/Icc/README.md passes for the ProPhoto profiles.
    private static readonly RgbPrimariesChromaticityCoordinates ProPhotoPrimaries = new(
        new CieXyChromaticityCoordinates(0.7347F, 0.2653F),
        new CieXyChromaticityCoordinates(0.1596F, 0.8404F),
        new CieXyChromaticityCoordinates(0.0366F, 0.0001F));

    private static readonly CieXyChromaticityCoordinates D50 = new(0.3457F, 0.3585F);

    private static readonly IccProfileTag[] ColourTags =
    {
        IccProfileTag.MediaWhitePoint,
        IccProfileTag.ChromaticAdaptation,
        IccProfileTag.RedMatrixColumn,
        IccProfileTag.GreenMatrixColumn,
        IccProfileTag.BlueMatrixColumn,
        IccProfileTag.RedTrc,
        IccProfileTag.GreenTrc,
        IccProfileTag.BlueTrc,
        IccProfileTag.GrayTrc,
    };

    public static TheoryData<string, string> StandardProfiles { get; } = new()
    {
        { nameof(IccProfileFactory.CreateSrgb), TestImages.Icc.LittleCmsSrgbV4 },
        { nameof(IccProfileFactory.CreateLinearSrgb), TestImages.Icc.LittleCmsLinearSrgbV4 },
        { nameof(IccProfileFactory.CreateDisplayP3), TestImages.Icc.LittleCmsDisplayP3V4 },
        { nameof(IccProfileFactory.CreateRec2020), TestImages.Icc.LittleCmsRec2020V4 },
        { nameof(IccProfileFactory.CreateAdobeRgbCompatible), TestImages.Icc.LittleCmsAdobeRgbV4 },
        { "CreateGray(Srgb)", TestImages.Icc.LittleCmsGraySrgbV4 },
        { "CreateGray(Linear)", TestImages.Icc.LittleCmsGrayLinearV4 },
        { "CreateGray(Gamma(2.2))", TestImages.Icc.LittleCmsGrayGamma22V4 },
    };

    [Theory]
    [MemberData(nameof(StandardProfiles))]
    public void StandardProfile_MatchesLittleCms(string factory, string reference)
        => AssertSameColour(Load(reference), Create(factory));

    [Fact]
    public void CreateRgb_CustomPrimariesWhiteAndIntent_MatchesLittleCms()
    {
        IccProfile profile = IccProfileFactory.CreateRgb(ProPhotoPrimaries, D50, IccToneCurve.Gamma(1.8F), IccRenderingIntent.MediaRelativeColorimetric);

        AssertSameColour(Load(TestImages.Icc.LittleCmsProPhoto18V4), profile);
    }

    [Fact]
    public void CreateRgb_ACurvePerChannel_MatchesLittleCms()
    {
        IccProfile profile = IccProfileFactory.CreateRgb(
            ProPhotoPrimaries,
            D50,
            IccToneCurve.Gamma(1.8F),
            IccToneCurve.Gamma(2.2F),
            IccToneCurve.Srgb,
            IccRenderingIntent.Saturation);

        AssertSameColour(Load(TestImages.Icc.LittleCmsProPhotoMixedV4), profile);
    }

    [Fact]
    public void CreateGray_CustomWhite_MatchesLittleCms()
    {
        // cmsD50_xyY(): D50 (0.9642, 1, 0.8249) as xy.
        IccProfile profile = IccProfileFactory.CreateGray(new CieXyChromaticityCoordinates(0.345702915F, 0.358538597F), IccToneCurve.Gamma(2.2F));

        AssertSameColour(Load(TestImages.Icc.LittleCmsGray22V4), profile);
    }

    [Theory]
    [MemberData(nameof(StandardProfiles))]
    public void StandardProfile_RoundTripsThroughTheWriterAndReader(string factory, string reference)
    {
        _ = reference;
        IccProfile profile = Create(factory);

        byte[] written = new IccWriter().Write(profile);
        IccProfile read = new IccReader().Read(written);

        Assert.True(profile.CheckIsValid());
        Assert.Equal((uint)written.Length, profile.Header.Size);
        Assert.Equal(profile.ToByteArray(), written);
        Assert.Equal(profile.Entries, read.Entries);
        AssertSameColour(profile, read);
    }

    [Theory]
    [MemberData(nameof(StandardProfiles))]
    public void StandardProfile_SurvivesAPngSave(string factory, string reference)
    {
        _ = reference;
        IccProfile profile = Create(factory);
        using Image<Rgba32> image = new(1, 1);
        image.Metadata.IccProfile = profile;

        // PNG keeps a gray profile only in a grayscale image.
        bool gray = profile.Header.DataColorSpace == IccColorSpaceType.Gray;

        using MemoryStream png = new();
        image.SaveAsPng(png, new PngEncoder { ColorType = gray ? PngColorType.Grayscale : PngColorType.Rgb });
        png.Position = 0;
        using Image<Rgba32> saved = Image.Load<Rgba32>(png);

        Assert.NotNull(saved.Metadata.IccProfile);
        AssertSameColour(profile, saved.Metadata.IccProfile);
    }

    // ITU-T H.273 code points: ColourPrimaries 1 = BT.709 (sRGB), 9 = BT.2020, 12 = SMPTE EG 432-1 (P3 with a
    // D65 white); TransferCharacteristics 1 = BT.709, 8 = linear, 13 = IEC 61966-2-1 (sRGB).
    public static TheoryData<string, int, int> SpacesWithCodePoints { get; } = new()
    {
        { nameof(IccProfileFactory.CreateSrgb), 1, 13 },
        { nameof(IccProfileFactory.CreateLinearSrgb), 1, 8 },
        { nameof(IccProfileFactory.CreateDisplayP3), 12, 13 },
        { nameof(IccProfileFactory.CreateRec2020), 9, 1 },
    };

    // Adobe RGB and a pure gamma curve have no H.273 code points; libjxl writes cicp only for RGB.
    public static TheoryData<string> SpacesWithoutCodePoints { get; } = new()
    {
        nameof(IccProfileFactory.CreateAdobeRgbCompatible),
        "CreateGray(Srgb)",
        "CreateGray(Linear)",
        "CreateGray(Gamma(2.2))",
    };

    // The profiles djxl (libjxl v0.12.0) builds for the same spaces, in the iCCP chunks of the JPEG XL
    // reference PNGs (tests/Images/Input/Jxl/Generated/README.md).
    public static TheoryData<string, string> DjxlProfiles { get; } = new()
    {
        { nameof(IccProfileFactory.CreateLinearSrgb), "colour_linear_srgb.png" },
        { nameof(IccProfileFactory.CreateDisplayP3), "colour_display_p3.png" },
        { nameof(IccProfileFactory.CreateRec2020), "colour_rec2020.png" },
    };

    [Theory]
    [MemberData(nameof(SpacesWithCodePoints))]
    public void StandardProfile_WithCodePoints_HasCicpTag(string factory, int primaries, int transfer)
    {
        IccProfile profile = Create(factory);

        Assert.True(profile.TryGetTag(IccProfileTag.Cicp, out IccCicpTagDataEntry cicp), "The profile has no cicp tag.");
        Assert.Equal(primaries, cicp.ColorPrimaries);
        Assert.Equal(transfer, cicp.TransferCharacteristics);
        Assert.Equal(0, cicp.MatrixCoefficients);
        Assert.Equal(1, cicp.VideoFullRangeFlag);
    }

    [Theory]
    [MemberData(nameof(SpacesWithoutCodePoints))]
    public void StandardProfile_WithoutCodePoints_HasNoCicpTag(string factory)
        => Assert.False(Create(factory).TryGetTag(IccProfileTag.Cicp, out IccTagDataEntry _));

    [Fact]
    public void CreateRgb_HasNoCicpTag()
    {
        // Even with the sRGB values: a custom space is not identified by code points.
        RgbPrimariesChromaticityCoordinates rec709 = new(new(0.64F, 0.33F), new(0.30F, 0.60F), new(0.15F, 0.06F));
        IccProfile profile = IccProfileFactory.CreateRgb(rec709, new CieXyChromaticityCoordinates(0.3127F, 0.3290F), IccToneCurve.Srgb);

        Assert.False(profile.TryGetTag(IccProfileTag.Cicp, out IccTagDataEntry _));
    }

    [Theory]
    [MemberData(nameof(DjxlProfiles))]
    public void StandardProfile_CicpTag_MatchesDjxl(string factory, string referencePng)
    {
        using Image<Rgba32> reference = Image.Load<Rgba32>(
            Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", referencePng));
        Assert.True(reference.Metadata.IccProfile.TryGetTag(IccProfileTag.Cicp, out IccCicpTagDataEntry expected));

        Assert.True(Create(factory).TryGetTag(IccProfileTag.Cicp, out IccCicpTagDataEntry actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TwoCalls_ReturnIndependentProfiles()
    {
        IccProfile first = IccProfileFactory.CreateDisplayP3();
        IccProfile second = IccProfileFactory.CreateDisplayP3();

        Assert.NotSame(first, second);
        first.Header.RenderingIntent = IccRenderingIntent.Saturation;
        Assert.True(first.TryGetTag(IccProfileTag.RedMatrixColumn, out IccXyzTagDataEntry red));
        red.Data[0] = Vector3.Zero;

        Assert.Equal(IccRenderingIntent.Perceptual, second.Header.RenderingIntent);
        Assert.True(second.TryGetTag(IccProfileTag.RedMatrixColumn, out IccXyzTagDataEntry otherRed));
        Assert.NotEqual(Vector3.Zero, otherRed.Data[0]);
        AssertSameColour(Load(TestImages.Icc.LittleCmsDisplayP3V4), IccProfileFactory.CreateDisplayP3());
    }

    [Fact]
    public void Description_NamesTheSpace()
    {
        Assert.Equal("sRGB", IccProfileFactory.CreateSrgb().Description);
        Assert.Equal("Display P3", IccProfileFactory.CreateDisplayP3().Description);
        Assert.Equal("My space", IccProfileFactory.CreateRgb(ProPhotoPrimaries, D50, IccToneCurve.Linear, description: "My space").Description);
    }

    [Fact]
    public void CreateSrgb_IsSrgb()
    {
        Assert.True(IccProfileFactory.CreateSrgb().IsSrgb());
        Assert.False(IccProfileFactory.CreateDisplayP3().IsSrgb());
        Assert.False(IccProfileFactory.CreateLinearSrgb().IsSrgb());
    }

    [Theory]
    [InlineData(0.3127F, 0F)]
    [InlineData(-0.1F, 0.3290F)]
    [InlineData(0.3127F, 1.5F)]
    public void CreateRgb_InvalidWhitePoint_Throws(float x, float y)
        => Assert.ThrowsAny<ArgumentException>(
            () => IccProfileFactory.CreateRgb(ProPhotoPrimaries, new CieXyChromaticityCoordinates(x, y), IccToneCurve.Srgb));

    [Fact]
    public void CreateRgb_DegeneratePrimaries_Throws()
    {
        CieXyChromaticityCoordinates grey = new(0.3127F, 0.3290F);

        Assert.ThrowsAny<ArgumentException>(
            () => IccProfileFactory.CreateRgb(new RgbPrimariesChromaticityCoordinates(grey, grey, grey), D50, IccToneCurve.Srgb));
    }

    [Fact]
    public void CreateGray_ZeroWhiteY_Throws()
        => Assert.ThrowsAny<ArgumentException>(() => IccProfileFactory.CreateGray(new CieXyChromaticityCoordinates(0.3F, 0), IccToneCurve.Srgb));

    [Fact]
    public void NullToneCurve_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => IccProfileFactory.CreateGray(null));
        Assert.Throws<ArgumentNullException>(() => IccProfileFactory.CreateRgb(ProPhotoPrimaries, D50, IccToneCurve.Srgb, null, IccToneCurve.Srgb));
    }

    [Theory]
    [InlineData(0F)]
    [InlineData(-1F)]
    [InlineData(float.NaN)]
    [InlineData(40000F)]
    public void Gamma_OutOfRange_Throws(float gamma)
        => Assert.Throws<ArgumentOutOfRangeException>(() => IccToneCurve.Gamma(gamma));

    [Fact]
    public void Parametric_NotFinite_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => IccToneCurve.Parametric(2.4F, float.PositiveInfinity, 0, 1, 0));

    private static IccProfile Create(string factory) => factory switch
    {
        nameof(IccProfileFactory.CreateSrgb) => IccProfileFactory.CreateSrgb(),
        nameof(IccProfileFactory.CreateLinearSrgb) => IccProfileFactory.CreateLinearSrgb(),
        nameof(IccProfileFactory.CreateDisplayP3) => IccProfileFactory.CreateDisplayP3(),
        nameof(IccProfileFactory.CreateRec2020) => IccProfileFactory.CreateRec2020(),
        nameof(IccProfileFactory.CreateAdobeRgbCompatible) => IccProfileFactory.CreateAdobeRgbCompatible(),
        "CreateGray(Srgb)" => IccProfileFactory.CreateGray(IccToneCurve.Srgb),
        "CreateGray(Linear)" => IccProfileFactory.CreateGray(IccToneCurve.Linear),
        "CreateGray(Gamma(2.2))" => IccProfileFactory.CreateGray(IccToneCurve.Gamma(2.2F)),
        _ => throw new ArgumentOutOfRangeException(nameof(factory)),
    };

    internal static IccProfile Load(string path)
        => new(File.ReadAllBytes(Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, path)));

    // The header fields and tags that describe the colour. The description, copyright and chromaticity
    // tags are left out.
    internal static void AssertSameColour(IccProfile expected, IccProfile actual)
    {
        Assert.Equal(expected.Header.DataColorSpace, actual.Header.DataColorSpace);
        Assert.Equal(expected.Header.ProfileConnectionSpace, actual.Header.ProfileConnectionSpace);
        Assert.Equal(expected.Header.Class, actual.Header.Class);
        Assert.Equal(expected.Header.RenderingIntent, actual.Header.RenderingIntent);

        foreach (IccProfileTag tag in ColourTags)
        {
            if (!expected.TryGetTag(tag, out IccTagDataEntry e))
            {
                Assert.False(actual.TryGetTag(tag, out IccTagDataEntry _), $"The built profile has an unexpected {tag} tag.");
                continue;
            }

            Assert.True(actual.TryGetTag(tag, out IccTagDataEntry a), $"The built profile has no {tag} tag.");
            switch (e)
            {
                case IccXyzTagDataEntry xyz:
                    Vector3 expectedXyz = Assert.Single(xyz.Data);
                    Vector3 actualXyz = Assert.Single(Assert.IsType<IccXyzTagDataEntry>(a).Data);
                    AssertClose(tag, new[] { expectedXyz.X, expectedXyz.Y, expectedXyz.Z }, new[] { actualXyz.X, actualXyz.Y, actualXyz.Z });
                    break;
                case IccFix16ArrayTagDataEntry matrix:
                    AssertClose(tag, matrix.Data, Assert.IsType<IccFix16ArrayTagDataEntry>(a).Data);
                    break;
                case IccParametricCurveTagDataEntry curve when a is IccParametricCurveTagDataEntry other && curve.Curve.Type == other.Curve.Type:
                    IccParametricCurve c = curve.Curve;
                    IccParametricCurve d = other.Curve;
                    AssertClose(tag, new[] { c.G, c.A, c.B, c.C, c.D, c.E, c.F }, new[] { d.G, d.A, d.B, d.C, d.D, d.E, d.F });
                    break;
                case IccParametricCurveTagDataEntry:
                    AssertSameCurve(tag, e, a);
                    break;
                default:
                    throw new InvalidOperationException($"No comparison for the {e.Signature} type of the {tag} tag.");
            }
        }
    }

    private static void AssertSameCurve(IccProfileTag tag, IccTagDataEntry expected, IccTagDataEntry actual)
    {
        for (int i = 0; i <= 256; i++)
        {
            float x = i / 256F;
            Assert.True(IccCurveEvaluator.TryEvaluate(expected, x, out float e));
            Assert.True(IccCurveEvaluator.TryEvaluate(actual, x, out float a), $"{tag} is not a curve.");
            Assert.True(Math.Abs(e - a) <= CurveTolerance, $"{tag}({x}): expected {e}, got {a}.");
        }
    }

    private static void AssertClose(IccProfileTag tag, float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(expected[i] - actual[i]) <= Tolerance, $"{tag}[{i}]: expected {expected[i]}, got {actual[i]}.");
        }
    }
}
