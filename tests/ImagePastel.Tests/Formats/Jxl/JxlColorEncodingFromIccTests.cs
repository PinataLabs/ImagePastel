// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using System.IO;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.Formats.Jxl.Icc;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// E3e: <see cref="JxlColorEncodingHeader.TryFromIccProfile"/> recognises a profile as a JPEG XL colour encoding,
/// in libjxl's order (<c>JxlCmsSetFieldsFromICC</c>): a <c>cicp</c> tag, then a matrix/TRC (RGB) or
/// <c>kTRC</c> (grey) profile, whose white point and primaries snap to a named enum within 1e-3 and
/// whose curve matches a transfer function within 2e-4.
/// </summary>
/// <remarks>
/// The enums are written in libjxl's description form (<c>ColorEncodingDescription</c>): colour space,
/// white point, primaries (RGB only), rendering intent and transfer function, separated by underscores.
/// Custom values are <c>x;y</c> for a white point and six numbers for primaries; a gamma is <c>g</c> and
/// the encoding exponent.
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlColorEncodingFromIccTests
{
    // A profile stores the gamma as an s15Fixed16Number (2^-16), so a gamma of 0.4545 comes back within
    // 0.4545² × 2^-17 ≈ 1.6e-6 of the encoding's, and so does an xy value through the profile's XYZ.
    private const double GammaTolerance = 4e-6;

    private const double XyTolerance = 1e-4;

    /// <summary>
    /// Gets every kind of colour encoding the decoder builds a pixel profile for (sRGB too, which it leaves
    /// out): each named primaries, white point and transfer function, the PQ and HLG 3D-LUT profiles a
    /// <c>cicp</c> tag names, their table-curve twins (grey and custom primaries), custom values, and a
    /// rendering intent other than the default.
    /// </summary>
    public static TheoryData<string> DecoderEncodings { get; } = new()
    {
        "RGB_D65_SRG_Rel_SRG",
        "RGB_D65_SRG_Per_SRG",
        "RGB_D65_SRG_Abs_SRG",
        "RGB_D65_DCI_Rel_SRG",
        "RGB_D65_202_Rel_709",
        "RGB_D65_202_Sat_SRG",
        "RGB_D65_SRG_Rel_Lin",
        "RGB_D65_202_Rel_Lin",
        "RGB_D65_SRG_Rel_g0.4545455",
        "RGB_D65_202_Rel_PeQ",
        "RGB_D65_202_Rel_HLG",
        "RGB_D65_DCI_Rel_PeQ",
        "RGB_DCI_DCI_Rel_HLG",
        "RGB_DCI_DCI_Rel_DCI",
        "RGB_EER_SRG_Rel_SRG",
        "RGB_D65_0.7;0.29;0.18;0.72;0.14;0.05_Rel_PeQ",
        "RGB_0.345669;0.358496_0.734699;0.265301;0.159597;0.840403;0.036598;0.000105_Per_g0.5555556",
        "Gra_D65_Rel_SRG",
        "Gra_D65_Rel_Lin",
        "Gra_D65_Rel_g0.4545455",
        "Gra_D65_Rel_709",
        "Gra_D65_Rel_PeQ",
        "Gra_D65_Rel_HLG",
        "Gra_D65_Rel_DCI",
        "Gra_0.345669;0.358496_Rel_SRG",
    };

    [Theory]
    [MemberData(nameof(DecoderEncodings))]
    public void TryFromIccProfile_DecoderBuiltProfile_MapsBackToTheSameEnum(string description)
    {
        JxlColorEncodingHeader expected = Parse(description);
        IccProfile profile = JxlIccBuilder.Create(expected);

        Assert.True(JxlColorEncodingHeader.TryFromIccProfile(profile, out JxlColorEncodingHeader actual), description);

        AssertSameEncoding(expected, actual);
    }

    /// <summary>
    /// Gets common profiles from other software, and the encoding each one describes: the definitions in
    /// the programs that wrote them (<c>tests/Images/Input/Icc/README.md</c>), or the space a Compact ICC
    /// profile is named after. Little CMS writes the perceptual intent unless the program sets another.
    /// ProPhoto's white is D50, which is not a named white point; Adobe RGB's gamma is 563 / 256.
    /// </summary>
    public static TheoryData<string, string> CommonProfiles { get; } = new()
    {
        { TestImages.Icc.LittleCmsSrgbV2, "RGB_D65_SRG_Per_SRG" },
        { TestImages.Icc.LittleCmsSrgbV4, "RGB_D65_SRG_Per_SRG" },
        { TestImages.Icc.LittleCmsDisplayP3V4, "RGB_D65_DCI_Per_SRG" },
        { TestImages.Icc.CompactDisplayP3V4, "RGB_D65_DCI_Per_SRG" },
        { TestImages.Icc.LittleCmsAdobeRgbV4, "RGB_D65_0.64;0.33;0.21;0.71;0.15;0.06_Per_g0.4547069" },
        { TestImages.Icc.CompactAdobeRgbV2, "RGB_D65_0.64;0.33;0.21;0.71;0.15;0.06_Per_g0.4547069" },
        { TestImages.Icc.LittleCmsProPhoto18V4, "RGB_0.3457;0.3585_0.7347;0.2653;0.1596;0.8404;0.0366;0.0001_Rel_g0.5555556" },
        { TestImages.Icc.LittleCmsRec2020V4, "RGB_D65_202_Per_709" },
        { TestImages.Icc.LittleCmsLinearSrgbV4, "RGB_D65_SRG_Per_Lin" },
        { TestImages.Icc.CompactLinearSrgbV2, "RGB_D65_SRG_Per_Lin" },
        { TestImages.Icc.LittleCmsGrayGamma22V4, "Gra_D65_Per_g0.4545455" },
        { TestImages.Icc.LittleCmsGray22V4, "Gra_0.3457;0.3585_Per_g0.4545455" },
        { TestImages.Icc.LittleCmsGraySrgbV4, "Gra_D65_Per_SRG" },
        { TestImages.Icc.LittleCmsGrayLinearV4, "Gra_D65_Per_Lin" },
        { TestImages.Icc.CompactGrayV4, "Gra_D65_Per_SRG" },
    };

    /// <summary>
    /// Gets profiles that are not recognised: lookup tables (RGB and grey), a Lab connection space,
    /// curves that differ by channel, a CMYK profile, and a matrix/TRC profile that also has tables.
    /// </summary>
    public static TheoryData<string> UnrecognisedProfiles { get; } = new()
    {
        TestImages.Icc.LittleCmsRgbLut8V2,
        TestImages.Icc.LittleCmsRgbLut16V2,
        TestImages.Icc.LittleCmsRgbLutAToBV4,
        TestImages.Icc.LittleCmsGrayLutV4,
        TestImages.Icc.LittleCmsGrayLab18V4,
        TestImages.Icc.LittleCmsP3Gamma182224V4,
        TestImages.Icc.LittleCmsProPhotoMixedV4,
        TestImages.Icc.LittleCmsFakeCmykV4,
        TestImages.Icc.LittleCmsRgbIntentsMatrixV2,
    };

    [Theory]
    [MemberData(nameof(CommonProfiles))]
    public void TryFromIccProfile_CommonProfile_RecognisesItsSpace(string path, string description)
    {
        Assert.True(JxlColorEncodingHeader.TryFromIccProfile(ReadProfile(path), out JxlColorEncodingHeader actual), path);

        AssertSameEncoding(Parse(description), actual);
    }

    [Theory]
    [MemberData(nameof(UnrecognisedProfiles))]
    public void TryFromIccProfile_LutLabOrMixedProfile_IsNotRecognised(string path)
    {
        Assert.False(JxlColorEncodingHeader.TryFromIccProfile(ReadProfile(path), out JxlColorEncodingHeader actual));
        Assert.Null(actual);
    }

    // A profile whose only colour tag is cicp, as an HDR workflow might write it: libjxl reads the code
    // points before anything else (ApplyCICP).
    [Theory]
    [InlineData(9, 16, "RGB_D65_202_Per_PeQ")]
    [InlineData(9, 18, "RGB_D65_202_Per_HLG")]
    [InlineData(12, 13, "RGB_D65_DCI_Per_SRG")]
    [InlineData(11, 17, "RGB_DCI_DCI_Per_DCI")]
    [InlineData(1, 8, "RGB_D65_SRG_Per_Lin")]
    [InlineData(1, 1, "RGB_D65_SRG_Per_709")]
    public void TryFromIccProfile_CicpOnlyProfile_RecognisesTheCodePoints(byte primaries, byte transfer, string description)
    {
        Assert.True(JxlColorEncodingHeader.TryFromIccProfile(CicpOnlyProfile(primaries, transfer, 0, 1), out JxlColorEncodingHeader actual));

        AssertSameEncoding(Parse(description), actual);
    }

    // ApplyCICP refuses YCbCr matrices, narrow range, and code points the enums do not name (BT.601
    // primaries, the BT.2020 10-bit curve); the profile then has nothing else to recognise.
    [Theory]
    [InlineData(9, 16, 9, 1)]
    [InlineData(9, 16, 0, 0)]
    [InlineData(6, 13, 0, 1)]
    [InlineData(9, 14, 0, 1)]
    public void TryFromIccProfile_CicpTheEnumsCannotName_IsNotRecognised(byte primaries, byte transfer, byte matrix, byte fullRange)
        => Assert.False(JxlColorEncodingHeader.TryFromIccProfile(CicpOnlyProfile(primaries, transfer, matrix, fullRange), out _));

    // The rendering intent is the header's, carried into the encoding (so a perceptual sRGB profile is not
    // libjxl's all-default sRGB).
    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 1 }, 1)]
    [InlineData(new byte[] { 0, 0, 0, 2 }, 2)]
    [InlineData(new byte[] { 0, 0, 0, 3 }, 3)]
    public void TryFromIccProfile_RenderingIntent_IsTheHeaders(byte[] intentBytes, int expected)
    {
        byte[] bytes = File.ReadAllBytes(TestFile.GetInputFileFullPath(TestImages.Icc.LittleCmsSrgbV4));
        intentBytes.CopyTo(bytes, 64);

        Assert.True(JxlColorEncodingHeader.TryFromIccProfile(new IccProfile(bytes), out JxlColorEncodingHeader actual));

        Assert.Equal((JxlRenderingIntent)expected, actual.RenderingIntent);
    }

    [Fact]
    public void TryFromIccProfile_RenderingIntentOutOfRange_IsNotRecognised()
    {
        byte[] bytes = File.ReadAllBytes(TestFile.GetInputFileFullPath(TestImages.Icc.LittleCmsSrgbV4));
        bytes[67] = 4;

        Assert.False(JxlColorEncodingHeader.TryFromIccProfile(new IccProfile(bytes), out _));
    }

    /// <summary>
    /// Builds an RGB display profile whose only tags are a description and a <c>cicp</c> tag.
    /// </summary>
    /// <param name="primaries">The H.273 ColourPrimaries.</param>
    /// <param name="transfer">The H.273 TransferCharacteristics.</param>
    /// <param name="matrix">The H.273 MatrixCoefficients.</param>
    /// <param name="fullRange">The H.273 VideoFullRangeFlag.</param>
    /// <returns>The profile, written and read back.</returns>
    internal static IccProfile CicpOnlyProfile(byte primaries, byte transfer, byte matrix, byte fullRange)
    {
        IccProfileHeader header = new()
        {
            Version = new IccVersion(4, 4, 0),
            Class = IccProfileClass.DisplayDevice,
            DataColorSpace = IccColorSpaceType.Rgb,
            ProfileConnectionSpace = IccColorSpaceType.CieXyz,
            FileSignature = "acsp",
            RenderingIntent = IccRenderingIntent.Perceptual,
            PcsIlluminant = new System.Numerics.Vector3(0.9642f, 1, 0.8249f),
        };
        IccTagDataEntry[] entries =
        {
            new IccMultiLocalizedUnicodeTagDataEntry(new[] { new IccLocalizedString(CultureInfo.InvariantCulture, "cicp only") }, IccProfileTag.ProfileDescription),
            new IccCicpTagDataEntry(primaries, transfer, matrix, fullRange, IccProfileTag.Cicp),
        };
        return new IccProfile(new IccProfile(header, entries).ToByteArray());
    }

    /// <summary>
    /// Parses an encoding in libjxl's description form (see the remarks of the class).
    /// </summary>
    /// <param name="description">The description, for example <c>RGB_D65_SRG_Rel_SRG</c>.</param>
    /// <returns>The encoding.</returns>
    internal static JxlColorEncodingHeader Parse(string description)
    {
        string[] parts = description.Split('_');
        JxlColorEncodingHeader c = new() { ColorSpace = parts[0] == "Gra" ? JxlColorSpace.Gray : JxlColorSpace.Rgb };
        int i = 1;
        switch (parts[i++])
        {
            case "D65":
                c.WhitePoint = JxlWhitePoint.D65;
                break;
            case "DCI":
                c.WhitePoint = JxlWhitePoint.Dci;
                break;
            case "EER":
                c.WhitePoint = JxlWhitePoint.E;
                break;
            case string custom:
                c.WhitePoint = JxlWhitePoint.Custom;
                SetXy(c.White, custom.Split(';'), 0);
                break;
        }

        if (c.ColorSpace == JxlColorSpace.Rgb)
        {
            switch (parts[i++])
            {
                case "SRG":
                    c.Primaries = JxlPrimaries.Srgb;
                    break;
                case "202":
                    c.Primaries = JxlPrimaries.Bt2100;
                    break;
                case "DCI":
                    c.Primaries = JxlPrimaries.P3;
                    break;
                case string custom:
                    string[] xy = custom.Split(';');
                    c.Primaries = JxlPrimaries.Custom;
                    SetXy(c.Red, xy, 0);
                    SetXy(c.Green, xy, 2);
                    SetXy(c.Blue, xy, 4);
                    break;
            }
        }

        c.RenderingIntent = parts[i++] switch
        {
            "Per" => JxlRenderingIntent.Perceptual,
            "Sat" => JxlRenderingIntent.Saturation,
            "Abs" => JxlRenderingIntent.Absolute,
            _ => JxlRenderingIntent.Relative,
        };

        string tf = parts[i];
        c.Tf.NonserializedColorSpace = c.ColorSpace;
        if (tf.StartsWith('g'))
        {
            c.Tf.HaveGamma = true;
            c.Tf.Gamma = (uint)Math.Round(double.Parse(tf.AsSpan(1), CultureInfo.InvariantCulture) * JxlCustomTransferFunction.GammaMul);
            c.Tf.TransferFunction = JxlTransferFunction.Unknown;
        }
        else
        {
            c.Tf.TransferFunction = tf switch
            {
                "SRG" => JxlTransferFunction.Srgb,
                "Lin" => JxlTransferFunction.Linear,
                "709" => JxlTransferFunction.Bt709,
                "PeQ" => JxlTransferFunction.Pq,
                "HLG" => JxlTransferFunction.Hlg,
                _ => JxlTransferFunction.Dci,
            };
        }

        return c;

        static void SetXy(JxlCustomXy target, string[] values, int index)
        {
            target.X = (int)Math.Round(double.Parse(values[index], CultureInfo.InvariantCulture) * 1e6);
            target.Y = (int)Math.Round(double.Parse(values[index + 1], CultureInfo.InvariantCulture) * 1e6);
        }
    }

    /// <summary>
    /// Asserts that <paramref name="actual"/> is <paramref name="expected"/>: the same enums, and custom
    /// values and a gamma within what a profile's fixed-point numbers keep.
    /// </summary>
    /// <param name="expected">The expected encoding.</param>
    /// <param name="actual">The recognised encoding.</param>
    internal static void AssertSameEncoding(JxlColorEncodingHeader expected, JxlColorEncodingHeader actual)
    {
        Assert.False(actual.WantIcc);
        Assert.Equal(expected.ColorSpace, actual.ColorSpace);
        Assert.Equal(expected.WhitePoint, actual.WhitePoint);
        if (expected.WhitePoint == JxlWhitePoint.Custom)
        {
            AssertXy(expected.White, actual.White);
        }

        if (expected.HasPrimaries)
        {
            Assert.Equal(expected.Primaries, actual.Primaries);
            if (expected.Primaries == JxlPrimaries.Custom)
            {
                AssertXy(expected.Red, actual.Red);
                AssertXy(expected.Green, actual.Green);
                AssertXy(expected.Blue, actual.Blue);
            }
        }

        Assert.Equal(expected.RenderingIntent, actual.RenderingIntent);
        Assert.Equal(expected.Tf.HaveGamma, actual.Tf.HaveGamma);
        if (expected.Tf.HaveGamma)
        {
            Assert.Equal(expected.Tf.Gamma / (double)JxlCustomTransferFunction.GammaMul, actual.Tf.Gamma / (double)JxlCustomTransferFunction.GammaMul, GammaTolerance);
        }
        else
        {
            Assert.Equal(expected.Tf.TransferFunction, actual.Tf.TransferFunction);
        }

        static void AssertXy(JxlCustomXy e, JxlCustomXy a)
        {
            Assert.Equal(e.Value.X, a.Value.X, XyTolerance);
            Assert.Equal(e.Value.Y, a.Value.Y, XyTolerance);
        }
    }

    private static IccProfile ReadProfile(string path) => new(File.ReadAllBytes(TestFile.GetInputFileFullPath(path)));
}
