// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using PinataLabs.ImagePastel.Tests.Formats.Png;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Checks how an image whose colour is an enumerated colour encoding describes it: the colour encoding
/// in <see cref="JxlMetadata"/>, and an ICC profile built for every space but sRGB, compared with the
/// profile <c>djxl</c> builds.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlColorEncodingTests
{
    // s15Fixed16Number has a resolution of 2^-16; the two builders round their own float maths.
    private const float Tolerance = 2e-4F;

    private static readonly IccProfileTag[] ColourTags =
    {
        IccProfileTag.MediaWhitePoint,
        IccProfileTag.ChromaticAdaptation,
        IccProfileTag.Cicp,
        IccProfileTag.RedMatrixColumn,
        IccProfileTag.GreenMatrixColumn,
        IccProfileTag.BlueMatrixColumn,
        IccProfileTag.RedTrc,
        IccProfileTag.GreenTrc,
        IccProfileTag.BlueTrc,
        IccProfileTag.GrayTrc,
        IccProfileTag.AToB0,
        IccProfileTag.BToA0,
    };

    private static string GeneratedDirectory => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated");

    [Fact]
    public void Identify_EnumSrgb_GivesTheColourEncodingAndNoPixelProfile()
    {
        ImageMetadata metadata = Identify(JxlConformanceVector.Load("delta_palette").CommittedInputPath);
        JxlMetadata jxl = metadata.GetFormatMetadata(JxlFormat.Instance);

        Assert.NotNull(jxl.ColorEncoding);
        Assert.Equal(JxlColorSpace.Rgb, jxl.ColorEncoding.ColorSpace);
        Assert.Equal(JxlWhitePoint.D65, jxl.ColorEncoding.WhitePoint);
        Assert.Equal(JxlPrimaries.Srgb, jxl.ColorEncoding.Primaries);
        Assert.Equal(JxlTransferFunction.Srgb, jxl.ColorEncoding.TransferFunction);
        Assert.False(jxl.IsXybEncoded);
        Assert.Null(metadata.IccProfile);
    }

    [Fact]
    public void Identify_XybWithEnumSrgb_IsXybEncodedWithNoPixelProfile()
    {
        ImageMetadata metadata = Identify(JxlConformanceVector.Load("bicycles").CommittedInputPath);
        JxlMetadata jxl = metadata.GetFormatMetadata(JxlFormat.Instance);

        Assert.True(jxl.IsXybEncoded);
        Assert.Equal(JxlTransferFunction.Srgb, jxl.ColorEncoding.TransferFunction);
        Assert.Null(metadata.IccProfile);
    }

    [Fact]
    public void Identify_EmbeddedIcc_HasNoColourEncoding()
    {
        JxlMetadata jxl = Identify(JxlConformanceVector.Load("patches").CommittedInputPath).GetFormatMetadata(JxlFormat.Instance);

        Assert.Null(jxl.ColorEncoding);
        Assert.True(jxl.IsXybEncoded);
    }

    [Theory]
    [InlineData("srgb")]
    [InlineData("grey_srgb")]
    public void Load_SrgbOrGreyWithTheSrgbCurve_HasNoPixelProfile(string name)
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), Path.Combine(GeneratedDirectory, "colour_" + name + ".jxl"));
        using Image<Rgba32> reference = Image.Load<Rgba32>(ReferencePath(name));

        ImageComparer.Exact.VerifySimilarity(reference, image);
        Assert.Null(image.Metadata.IccProfile);
    }

    [Theory]
    [InlineData("rec2020")]
    [InlineData("display_p3")]
    [InlineData("linear_srgb")]
    [InlineData("gamma22")]
    [InlineData("grey_linear")]
    [InlineData("custom")]
    [InlineData("dci")]
    public void Load_OtherEnumSpace_GivesAProfileLikeDjxls(string name)
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), Path.Combine(GeneratedDirectory, "colour_" + name + ".jxl"));
        using Image<Rgba32> reference = Image.Load<Rgba32>(ReferencePath(name));

        ImageComparer.Exact.VerifySimilarity(reference, image);
        Assert.NotNull(image.Metadata.IccProfile);
        IccProfile djxlProfile = DjxlProfile(name);
        AssertSameColour(djxlProfile, image.Metadata.IccProfile);

        // The built profile survives being written and read back.
        AssertSameColour(djxlProfile, new IccProfile(image.Metadata.IccProfile.ToByteArray()));
    }

    [Fact]
    public void SaveAsPng_BuiltProfile_IsWrittenToThePng()
    {
        // The PNG encoder writes only a profile that passes CheckIsValid.
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), Path.Combine(GeneratedDirectory, "colour_display_p3.jxl"));
        Assert.True(image.Metadata.IccProfile.CheckIsValid());

        using MemoryStream png = new();
        image.SaveAsPng(png);
        png.Position = 0;
        using Image<Rgba32> saved = Image.Load<Rgba32>(png);

        Assert.NotNull(saved.Metadata.IccProfile);
        AssertSameColour(image.Metadata.IccProfile, saved.Metadata.IccProfile);
    }

    // An XYB image with a gamma or the DCI curve renders to its own encoding, so it gets that encoding's
    // profile, as the lossless file with the same encoding does.
    [Theory]
    [InlineData("xyb_modular_gamma22", "gamma22")]
    [InlineData("vardct_xyb_dci", "dci")]
    public void Load_XybOtherEnumSpace_GivesTheProfileOfItsEncoding(string file, string reference)
    {
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), Path.Combine(GeneratedDirectory, file + ".jxl"));

        Assert.NotNull(image.Metadata.IccProfile);
        AssertSameColour(DjxlProfile(reference), image.Metadata.IccProfile);
    }

    [Fact]
    public void Load_XybBt709_GivesAProfileWithSrgbPrimariesAndTheBt709Curve()
    {
        // RGB_D65_SRG_Rel_709: colour_gamma22 has the same primaries and white point, and colour_rec2020
        // the same curve and intent.
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), Path.Combine(GeneratedDirectory, "vardct_xyb_bt709.jxl"));
        IccProfile profile = image.Metadata.IccProfile;

        Assert.NotNull(profile);
        IccProfile rec2020 = DjxlProfile("rec2020");
        Assert.Equal(rec2020.Header.RenderingIntent, profile.Header.RenderingIntent);
        AssertSameColour(DjxlProfile("gamma22"), profile, IccProfileTag.MediaWhitePoint, IccProfileTag.ChromaticAdaptation, IccProfileTag.RedMatrixColumn, IccProfileTag.GreenMatrixColumn, IccProfileTag.BlueMatrixColumn);
        AssertSameColour(rec2020, profile, IccProfileTag.RedTrc, IccProfileTag.GreenTrc, IccProfileTag.BlueTrc);

        // ITU-T H.273: BT.709 primaries (1) and transfer characteristics (1), RGB, full range.
        Assert.True(profile.TryGetTag(IccProfileTag.Cicp, out IccTagDataEntry cicp));
        Assert.Equal(new IccCicpTagDataEntry(1, 1, 0, 1), cicp);
    }

    // An RGB PQ or HLG encoding that a cicp tag can name gets libjxl's 3D-LUT profile: a Lab PCS, an mft1
    // A2B0 that tone-maps to PCS Lab, and a no-op mBA B2A0.
    // Measured: every LUT entry equal to djxl's (largest difference 0 of one 8-bit step).
    [Theory]
    [InlineData("hlg", JxlTransferFunction.Hlg)]
    [InlineData("pq", JxlTransferFunction.Pq)]
    [InlineData("p3_hlg", JxlTransferFunction.Hlg)]
    public void Load_RgbPqOrHlgThatACicpTagNames_GivesA3DLutProfileLikeDjxls(string name, JxlTransferFunction transferFunction)
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), Path.Combine(GeneratedDirectory, "colour_" + name + ".jxl"));
        using Image<Rgba32> reference = Image.Load<Rgba32>(ReferencePath(name));

        ImageComparer.Exact.VerifySimilarity(reference, image);
        Assert.Equal(transferFunction, image.Metadata.GetFormatMetadata(JxlFormat.Instance).ColorEncoding.TransferFunction);
        AssertLutProfileLikeDjxls(DjxlProfile(name), image.Metadata.IccProfile);
    }

    // Identify builds the profile from the header, as Load does.
    [Theory]
    [InlineData("hlg")]
    [InlineData("pq")]
    [InlineData("p3_hlg")]
    public void Identify_RgbPqOrHlgThatACicpTagNames_GivesTheProfileLoadGives(string name)
    {
        string path = Path.Combine(GeneratedDirectory, "colour_" + name + ".jxl");
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), path);

        IccProfile identified = Identify(path).IccProfile;

        Assert.NotNull(identified);
        Assert.Equal(image.Metadata.IccProfile.ToByteArray(), identified.ToByteArray());
    }

    [Theory]
    [InlineData("hlg")]
    [InlineData("pq")]
    [InlineData("p3_hlg")]
    public void SaveAsPng_3DLutProfile_IsWrittenToThePng(string name)
        => AssertProfileIsWrittenToThePng<Rgba32>(Path.Combine(GeneratedDirectory, "colour_" + name + ".jxl"));

    // A PQ or HLG encoding that no cicp tag can name (grey, or custom primaries) gets a 64-entry table curve.
    // Measured: every curve entry equal to djxl's (largest difference 0 steps).
    [Theory]
    [InlineData("grey_hlg")]
    [InlineData("custom_pq")]
    public void Load_PqOrHlgThatNoCicpTagNames_GivesATableCurveProfileLikeDjxls(string name)
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), Path.Combine(GeneratedDirectory, "colour_" + name + ".jxl"));
        using Image<Rgba32> reference = Image.Load<Rgba32>(ReferencePath(name));

        ImageComparer.Exact.VerifySimilarity(reference, image);
        AssertTableCurveProfileLikeDjxls(DjxlProfile(name), image.Metadata.IccProfile);
    }

    // Measured: every curve entry equal to djxl's (largest difference 0 steps).
    [Fact]
    public void Load_PqGradient_GivesATableCurveProfileLikeDjxls()
    {
        string path = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "libjxl", "jxl", "pq_gradient.jxl");
        string referencePath = Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", "pq_gradient.png");
        using Image<L16> image = Image.Load<L16>(CreateConfiguration(), path);
        using Image<L16> reference = Image.Load<L16>(referencePath);

        ImageComparer.Exact.VerifySimilarity(reference, image);
        AssertTableCurveProfileLikeDjxls(PngProfile(referencePath), image.Metadata.IccProfile);
    }

    // pq_gradient's samples are 16-bit grey, so Image.Load picks L16, and Identify gives the profile Load gives.
    [Fact]
    public void LoadAndIdentify_PqGradient_ReportSizeChannelsAndColourEncoding()
    {
        string path = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "libjxl", "jxl", "pq_gradient.jxl");

        using Image image = Image.Load(CreateConfiguration(), path);
        IImageInfo info = Image.Identify(CreateConfiguration(), path, out _);

        Assert.IsType<Image<L16>>(image);
        Assert.Equal(1088, info.Width);
        Assert.Equal(64, info.Height);
        Assert.Equal(16, info.PixelType.BitsPerPixel);
        JxlMetadata jxl = info.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.False(jxl.IsXybEncoded);
        Assert.Equal(JxlColorSpace.Gray, jxl.ColorEncoding.ColorSpace);
        Assert.Equal(JxlTransferFunction.Pq, jxl.ColorEncoding.TransferFunction);
        Assert.Equal(image.Metadata.IccProfile.ToByteArray(), info.Metadata.IccProfile.ToByteArray());
    }

    // The PNG encoder writes only a profile that passes CheckIsValid, and a grey profile only to a grey PNG.
    [Fact]
    public void SaveAsPng_GreyHlgProfile_IsWrittenToThePng()
        => AssertProfileIsWrittenToThePng<L8>(Path.Combine(GeneratedDirectory, "colour_grey_hlg.jxl"));

    [Fact]
    public void SaveAsPng_CustomPqProfile_IsWrittenToThePng()
        => AssertProfileIsWrittenToThePng<Rgba32>(Path.Combine(GeneratedDirectory, "colour_custom_pq.jxl"));

    [Fact]
    public void SaveAsPng_PqGradientProfile_IsWrittenToThePng()
        => AssertProfileIsWrittenToThePng<L16>(Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "libjxl", "jxl", "pq_gradient.jxl"));

    [Fact]
    public void Decode_IgnoreMetadata_BuildsNoProfileButKeepsTheColourEncoding()
    {
        using FileStream stream = File.OpenRead(Path.Combine(GeneratedDirectory, "colour_display_p3.jxl"));

        using Image<Rgba32> image = new JxlDecoder { IgnoreMetadata = true }.Decode<Rgba32>(Configuration.Default, stream, TestContext.Current.CancellationToken);

        Assert.Null(image.Metadata.IccProfile);
        Assert.Equal(JxlPrimaries.P3, image.Metadata.GetFormatMetadata(JxlFormat.Instance).ColorEncoding.Primaries);
    }

    [Fact]
    public void DeepClone_CopiesTheColourEncoding()
    {
        JxlMetadata jxl = Identify(Path.Combine(GeneratedDirectory, "colour_custom.jxl")).GetFormatMetadata(JxlFormat.Instance);

        JxlMetadata clone = (JxlMetadata)jxl.DeepClone();

        // The description is immutable, so the clone may share it.
        Assert.Equal(jxl.IsXybEncoded, clone.IsXybEncoded);
        Assert.Equal(JxlPrimaries.Custom, clone.ColorEncoding.Primaries);
        Assert.Equal(jxl.ColorEncoding.CustomRedPrimary, clone.ColorEncoding.CustomRedPrimary);
        Assert.Equal(jxl.ColorEncoding.CustomWhitePoint, clone.ColorEncoding.CustomWhitePoint);
        Assert.Equal(JxlRenderingIntent.Absolute, clone.ColorEncoding.RenderingIntent);
    }

    // The header fields and tags that describe the colour, within the tolerance of the fixed-point
    // encoding; the cicp code points exactly. The description and copyright tags are left out.
    private static void AssertSameColour(IccProfile expected, IccProfile actual)
    {
        Assert.Equal(expected.Header.DataColorSpace, actual.Header.DataColorSpace);
        Assert.Equal(expected.Header.ProfileConnectionSpace, actual.Header.ProfileConnectionSpace);
        Assert.Equal(expected.Header.Class, actual.Header.Class);
        Assert.Equal(expected.Header.RenderingIntent, actual.Header.RenderingIntent);
        AssertSameColour(expected, actual, ColourTags);
    }

    // The given tags only, compared as above.
    private static void AssertSameColour(IccProfile expected, IccProfile actual, params IccProfileTag[] tags)
    {
        foreach (IccProfileTag tag in tags)
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
                case IccCicpTagDataEntry cicp:
                    Assert.Equal(cicp, Assert.IsType<IccCicpTagDataEntry>(a));
                    break;
                case IccParametricCurveTagDataEntry curve:
                    IccParametricCurve c = curve.Curve;
                    IccParametricCurve d = Assert.IsType<IccParametricCurveTagDataEntry>(a).Curve;
                    Assert.Equal(c.Type, d.Type);
                    AssertClose(tag, new[] { c.G, c.A, c.B, c.C, c.D, c.E, c.F }, new[] { d.G, d.A, d.B, d.C, d.D, d.E, d.F });
                    break;
                case IccCurveTagDataEntry table:
                    int steps = LargestTableDifference(table, Assert.IsType<IccCurveTagDataEntry>(a));
                    TestContext.Current.TestOutputHelper?.WriteLine($"{tag}: largest difference {steps} steps");
                    Assert.True(steps <= 1, $"{tag}: an entry differs by {steps} steps.");
                    break;
                case IccLut8TagDataEntry lut:
                    int lutSteps = LargestLut8Difference(lut, Assert.IsType<IccLut8TagDataEntry>(a));
                    TestContext.Current.TestOutputHelper?.WriteLine($"{tag}: largest LUT difference {lutSteps} steps");
                    Assert.True(lutSteps <= 1, $"{tag}: a LUT entry differs by {lutSteps} steps.");
                    break;
                case IccLutBToATagDataEntry bToA:
                    IccLutBToATagDataEntry actualBToA = Assert.IsType<IccLutBToATagDataEntry>(a);
                    Assert.Equal(bToA.InputChannelCount, actualBToA.InputChannelCount);
                    Assert.Equal(bToA.OutputChannelCount, actualBToA.OutputChannelCount);
                    Assert.Equal(bToA.Matrix3x3, actualBToA.Matrix3x3);
                    Assert.Equal(bToA.Matrix3x1, actualBToA.Matrix3x1);
                    Assert.Equal(bToA.ClutValues, actualBToA.ClutValues);
                    Assert.Equal(bToA.CurveA, actualBToA.CurveA);
                    Assert.Equal(bToA.CurveM, actualBToA.CurveM);
                    Assert.Equal(bToA.CurveB, actualBToA.CurveB);
                    break;
                default:
                    throw new InvalidOperationException($"No comparison for the {e.Signature} type of the {tag} tag.");
            }
        }
    }

    // The built profile has a table curve where djxl's does, matches it, and survives being written and
    // read back.
    private static void AssertTableCurveProfileLikeDjxls(IccProfile djxlProfile, IccProfile actual)
    {
        Assert.NotNull(actual);
        IccProfileTag trc = actual.Header.DataColorSpace == IccColorSpaceType.Gray ? IccProfileTag.GrayTrc : IccProfileTag.RedTrc;
        Assert.True(actual.TryGetTag(trc, out IccTagDataEntry curve));
        Assert.Equal(64, Assert.IsType<IccCurveTagDataEntry>(curve).CurveData.Length);
        AssertSameColour(djxlProfile, actual);
        AssertSameColour(djxlProfile, new IccProfile(actual.ToByteArray()));
    }

    // The built profile has the tags of djxl's 3D-LUT profile in the same order, a Lab PCS, matches it,
    // passes CheckIsValid and survives being written and read back.
    private static void AssertLutProfileLikeDjxls(IccProfile djxlProfile, IccProfile actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(IccColorSpaceType.CieLab, actual.Header.ProfileConnectionSpace);
        Assert.Equal(djxlProfile.Entries.Select(e => e.TagSignature), actual.Entries.Select(e => e.TagSignature));
        Assert.True(actual.CheckIsValid());
        AssertSameColour(djxlProfile, actual);
        AssertSameColour(djxlProfile, new IccProfile(actual.ToByteArray()));
    }

    private static void AssertProfileIsWrittenToThePng<TPixel>(string path)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = Image.Load<TPixel>(CreateConfiguration(), path);
        Assert.True(image.Metadata.IccProfile.CheckIsValid());

        using MemoryStream png = new();
        image.SaveAsPng(png);
        png.Position = 0;
        using Image<TPixel> saved = Image.Load<TPixel>(png);

        Assert.NotNull(saved.Metadata.IccProfile);
        AssertSameColour(image.Metadata.IccProfile, saved.Metadata.IccProfile);
    }

    // The largest difference between two table curves, in steps of the 16-bit table.
    private static int LargestTableDifference(IccCurveTagDataEntry expected, IccCurveTagDataEntry actual)
    {
        Assert.Equal(expected.CurveData.Length, actual.CurveData.Length);
        int largest = 0;
        for (int i = 0; i < expected.CurveData.Length; i++)
        {
            int e = (int)MathF.Round(expected.CurveData[i] * 65535F);
            int a = (int)MathF.Round(actual.CurveData[i] * 65535F);
            largest = Math.Max(largest, Math.Abs(e - a));
        }

        return largest;
    }

    // The largest difference between the CLUTs of two lut8 tables, in 8-bit steps. The matrix, the input and
    // output tables and the grid must be equal.
    private static int LargestLut8Difference(IccLut8TagDataEntry expected, IccLut8TagDataEntry actual)
    {
        Assert.Equal(expected.Matrix, actual.Matrix);
        Assert.Equal(expected.InputValues, actual.InputValues);
        Assert.Equal(expected.OutputValues, actual.OutputValues);
        Assert.Equal(expected.ClutValues.GridPointCount, actual.ClutValues.GridPointCount);
        Assert.Equal(expected.ClutValues.Values.Length, actual.ClutValues.Values.Length);
        int largest = 0;
        for (int i = 0; i < expected.ClutValues.Values.Length; i++)
        {
            float[] e = expected.ClutValues.Values[i];
            float[] a = actual.ClutValues.Values[i];
            Assert.Equal(e.Length, a.Length);
            for (int j = 0; j < e.Length; j++)
            {
                largest = Math.Max(largest, Math.Abs((int)MathF.Round(e[j] * 255F) - (int)MathF.Round(a[j] * 255F)));
            }
        }

        return largest;
    }

    private static void AssertClose(IccProfileTag tag, float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(expected[i] - actual[i]) <= Tolerance, $"{tag}[{i}]: expected {expected[i]}, got {actual[i]}.");
        }
    }

    private static ImageMetadata Identify(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return new JxlDecoder().Identify(Configuration.Default, stream, TestContext.Current.CancellationToken).Metadata;
    }

    // The profile libjxl built, from the iCCP chunk of djxl's PNG. For spaces that ITU-T H.273 names, djxl
    // also writes a cICP chunk, which takes precedence over iCCP when the PNG decodes, so it is dropped here.
    private static IccProfile DjxlProfile(string name) => PngProfile(ReferencePath(name));

    private static IccProfile PngProfile(string path)
    {
        byte[][] chunks = PngTestFileBuilder.ReadChunks(File.ReadAllBytes(path))
            .Where(c => c.Type != "cICP")
            .Select(c => PngTestFileBuilder.Chunk(c.Type, c.Data))
            .ToArray();

        IccProfile profile = Image.Identify(PngTestFileBuilder.Build(chunks)).Metadata.IccProfile;
        Assert.NotNull(profile);
        return profile;
    }

    private static string ReferencePath(string name) => Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", "colour_" + name + ".png");

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }
}
