// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Metadata.Profiles.Icc;

/// <summary>
/// Tests the questions <see cref="IccProfile"/> answers without the caller knowing the tag layout.
/// Expected descriptions, colour spaces and colorants are what Little CMS 2.18 (through Pillow's
/// ImageCms) reports for the same profiles.
/// </summary>
[Trait("Profile", "Icc")]
public class IccProfileQueryTests
{
    [Theory]
    [InlineData(TestImages.Icc.LittleCmsSrgbV4, "sRGB built-in")]
    [InlineData(TestImages.Icc.LittleCmsGray22V4, "gray built-in")]
    [InlineData(TestImages.Icc.QcmsRec2020, "Rec. ITU-R BT.2020-1")]
    [InlineData(TestImages.Icc.CompactDisplayP3V4, "sP3")]
    [InlineData(TestImages.Jpeg.Issues.IdentifyMultiFrame1211, "Display P3")]
    public void Description_V4MultiLocalizedUnicode_ReturnsText(string file, string expected)
        => Assert.Equal(expected, LoadProfile(file).Description);

    [Theory]
    [InlineData(TestImages.Icc.LittleCmsSrgbV2, "sRGB built-in")]
    [InlineData(TestImages.Icc.CompactAdobeRgbV2, "A98C")]
    [InlineData(TestImages.Jpeg.Baseline.Lake, "Adobe RGB (1998)")]
    [InlineData(TestImages.Png.GrayA8BitInterlaced, "Graustufen - Gamma 2.2")]
    public void Description_V2TextDescription_ReturnsText(string file, string expected)
        => Assert.Equal(expected, LoadProfile(file).Description);

    [Fact]
    public void Description_WithoutDescriptionTag_ReturnsNull()
        => Assert.Null(new IccProfile().Description);

    [Theory]
    [InlineData(TestImages.Icc.LittleCmsSrgbV4, IccColorSpaceType.Rgb, IccProfileClass.DisplayDevice)]
    [InlineData(TestImages.Icc.LittleCmsGray22V4, IccColorSpaceType.Gray, IccProfileClass.DisplayDevice)]
    [InlineData(TestImages.Jpeg.Baseline.Cmyk, IccColorSpaceType.Cmyk, IccProfileClass.OutputDevice)]
    public void DataColorSpaceAndClass_ReturnHeaderValues(string file, IccColorSpaceType colorSpace, IccProfileClass profileClass)
    {
        IccProfile profile = LoadProfile(file);

        Assert.Equal(colorSpace, profile.DataColorSpace);
        Assert.Equal(profileClass, profile.Class);
    }

    public static TheoryData<string, IccProfileTag[]> TagTables() => new()
    {
        // Tag tables in file order, read from the files with a separate Python parser.
        // Both profiles store rTRC, gTRC and bTRC once and point all three tags at that data.
        {
            TestImages.Icc.LittleCmsSrgbV4,
            [
                IccProfileTag.ProfileDescription, IccProfileTag.Copyright, IccProfileTag.MediaWhitePoint,
                IccProfileTag.ChromaticAdaptation, IccProfileTag.RedMatrixColumn, IccProfileTag.BlueMatrixColumn,
                IccProfileTag.GreenMatrixColumn, IccProfileTag.RedTrc, IccProfileTag.GreenTrc, IccProfileTag.BlueTrc,
                IccProfileTag.Chromaticity
            ]
        },
        {
            TestImages.Icc.QcmsRec709,
            [
                IccProfileTag.ProfileDescription, IccProfileTag.Copyright, IccProfileTag.MediaWhitePoint,
                IccProfileTag.RedMatrixColumn, IccProfileTag.GreenMatrixColumn, IccProfileTag.BlueMatrixColumn,
                IccProfileTag.RedTrc, IccProfileTag.ChromaticAdaptation, IccProfileTag.BlueTrc, IccProfileTag.GreenTrc
            ]
        },
    };

    [Theory]
    [MemberData(nameof(TagTables))]
    public void TryGetTag_EveryTagInTable_FoundByItsOwnSignature(string file, IccProfileTag[] signatures)
    {
        IccProfile profile = LoadProfile(file);

        foreach (IccProfileTag signature in signatures)
        {
            Assert.True(profile.TryGetTag(signature, out IccTagDataEntry entry), $"{signature} not found");
            Assert.Equal(signature, entry.TagSignature);
        }
    }

    [Theory]
    [InlineData(TestImages.Icc.LittleCmsSrgbV4, 0.43603515625F, 0.2224884033203125F, 0.013916015625F)]
    [InlineData(TestImages.Icc.CompactDisplayP3V4, 0.5151214599609375F, 0.2411956787109375F, -0.0010528564453125F)]
    [InlineData(TestImages.Jpeg.Baseline.Lake, 0.6097412109375F, 0.3111114501953125F, 0.01947021484375F)]
    public void TryGetTag_RedColorant_ReturnsXyz(string file, float x, float y, float z)
    {
        IccProfile profile = LoadProfile(file);

        Assert.True(profile.TryGetTag(IccProfileTag.RedMatrixColumn, out IccXyzTagDataEntry red));
        Assert.Equal(IccProfileTag.RedMatrixColumn, red.TagSignature);
        Assert.Equal(x, red.Data[0].X, 1e-6F);
        Assert.Equal(y, red.Data[0].Y, 1e-6F);
        Assert.Equal(z, red.Data[0].Z, 1e-6F);
    }

    [Theory]
    [InlineData(IccProfileTag.RedTrc)]
    [InlineData(IccProfileTag.GreenTrc)]
    [InlineData(IccProfileTag.BlueTrc)]
    public void TryGetTag_SharedParametricTrc_ReturnsSrgbCurve(IccProfileTag signature)
    {
        // Little CMS writes the sRGB curve of IEC 61966-2-1 as parametric function type 3:
        // g = 2.4, a = 1/1.055, b = 0.055/1.055, c = 1/12.92, d = 0.04045, rounded to s15Fixed16.
        const float tolerance = 2e-5F;
        IccProfile profile = LoadProfile(TestImages.Icc.LittleCmsSrgbV4);

        Assert.True(profile.TryGetTag(signature, out IccParametricCurveTagDataEntry trc));

        Assert.Equal(signature, trc.TagSignature);
        Assert.Equal(IccParametricCurveType.SRgb, trc.Curve.Type);
        Assert.Equal(2.4F, trc.Curve.G, tolerance);
        Assert.Equal(1 / 1.055F, trc.Curve.A, tolerance);
        Assert.Equal(0.055F / 1.055F, trc.Curve.B, tolerance);
        Assert.Equal(1 / 12.92F, trc.Curve.C, tolerance);
        Assert.Equal(0.04045F, trc.Curve.D, tolerance);
    }

    [Theory]
    [InlineData(IccProfileTag.RedTrc)]
    [InlineData(IccProfileTag.GreenTrc)]
    [InlineData(IccProfileTag.BlueTrc)]
    public void TryGetTag_SharedTableTrc_ReturnsCurve(IccProfileTag signature)
    {
        // The version 2 profile from Little CMS stores the sRGB curve as one 4096-entry table.
        IccProfile profile = LoadProfile(TestImages.Icc.LittleCmsSrgbV2);

        Assert.True(profile.TryGetTag(signature, out IccCurveTagDataEntry trc));

        Assert.Equal(signature, trc.TagSignature);
        Assert.Equal(4096, trc.CurveData.Length);
    }

    [Fact]
    public void TryGetTag_OtherType_ReturnsFalse()
    {
        IccProfile profile = LoadProfile(TestImages.Icc.LittleCmsSrgbV4);

        Assert.False(profile.TryGetTag(IccProfileTag.RedTrc, out IccCurveTagDataEntry trc));
        Assert.Null(trc);
    }

    [Fact]
    public void TryGetTag_MissingTag_ReturnsFalse()
    {
        IccProfile profile = LoadProfile(TestImages.Icc.LittleCmsSrgbV4);

        Assert.False(profile.TryGetTag(IccProfileTag.GrayTrc, out IccTagDataEntry trc));
        Assert.Null(trc);
    }

    [Theory]
    [InlineData(TestImages.Icc.LittleCmsSrgbV4)] // Little CMS built-in sRGB, parametric curve
    [InlineData(TestImages.Icc.LittleCmsSrgbV2)] // the same written as version 2, 4096-entry table
    [InlineData(TestImages.Icc.QcmsSrgb)] // the sRGB profile shipped with qcms
    [InlineData(TestImages.Icc.CompactSrgbV2Micro)] // sRGB with a short, interpolated table
    [InlineData(TestImages.Png.Ducky)] // "sRGB IEC61966-2.1", D65 media white, 1024-entry table
    public void IsSrgb_SrgbProfiles_ReturnsTrue(string file)
        => Assert.True(LoadProfile(file).IsSrgb());

    [Theory]
    [InlineData(TestImages.Icc.CompactDisplayP3V4)] // Display P3: other primaries, sRGB curve
    [InlineData(TestImages.Jpeg.Issues.IdentifyMultiFrame1211)] // "Display P3"
    [InlineData(TestImages.Icc.CompactAdobeRgbV2)] // Adobe RGB (1998)
    [InlineData(TestImages.Jpeg.Baseline.Lake)] // "Adobe RGB (1998)"
    [InlineData(TestImages.Icc.QcmsRec2020)] // Rec. ITU-R BT.2020
    [InlineData(TestImages.Icc.QcmsRec709)] // sRGB primaries, Rec. 709 curve
    [InlineData(TestImages.Icc.LittleCmsGray22V4)] // gray, gamma 2.2
    [InlineData(TestImages.Icc.CompactGrayV4)] // gray with the sRGB curve
    [InlineData(TestImages.Png.GrayA8BitInterlaced)] // "Graustufen - Gamma 2.2"
    [InlineData(TestImages.Jpeg.Baseline.Cmyk)] // CMYK
    public void IsSrgb_OtherProfiles_ReturnsFalse(string file)
        => Assert.False(LoadProfile(file).IsSrgb());

    [Fact]
    public void IsSrgb_EmptyProfile_ReturnsFalse()
        => Assert.False(new IccProfile().IsSrgb());

    private static IccProfile LoadProfile(string file)
    {
        string path = TestFile.GetInputFileFullPath(file);
        if (Path.GetExtension(path) == ".icc")
        {
            return new IccProfile(File.ReadAllBytes(path));
        }

        // A CMYK JPEG's profile is only its embedded profile, not its pixel profile.
        ImageMetadata metadata = Image.Identify(path).Metadata;
        return metadata.IccProfile ?? metadata.GetJpegMetadata().EmbeddedIccProfile;
    }
}
