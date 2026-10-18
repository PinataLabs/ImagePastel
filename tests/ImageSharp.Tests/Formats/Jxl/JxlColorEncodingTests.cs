// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

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
        IccProfileTag.RedMatrixColumn,
        IccProfileTag.GreenMatrixColumn,
        IccProfileTag.BlueMatrixColumn,
        IccProfileTag.RedTrc,
        IccProfileTag.GreenTrc,
        IccProfileTag.BlueTrc,
        IccProfileTag.GrayTrc,
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
        Assert.Equal(JxlTransferFunction.Srgb, jxl.ColorEncoding.Tf.TransferFunction);
        Assert.False(jxl.IsXybEncoded);
        Assert.Null(metadata.IccProfile);
    }

    [Fact]
    public void Identify_XybWithEnumSrgb_IsXybEncodedWithNoPixelProfile()
    {
        ImageMetadata metadata = Identify(JxlConformanceVector.Load("bicycles").CommittedInputPath);
        JxlMetadata jxl = metadata.GetFormatMetadata(JxlFormat.Instance);

        Assert.True(jxl.IsXybEncoded);
        Assert.Equal(JxlTransferFunction.Srgb, jxl.ColorEncoding.Tf.TransferFunction);
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
        AssertSameColour(reference.Metadata.IccProfile, image.Metadata.IccProfile);

        // The built profile survives being written and read back.
        AssertSameColour(reference.Metadata.IccProfile, new IccProfile(image.Metadata.IccProfile.ToByteArray()));
    }

    [Fact]
    public void Load_Hlg_DecodesWithNoPixelProfileUntilD11()
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), Path.Combine(GeneratedDirectory, "colour_hlg.jxl"));
        using Image<Rgba32> reference = Image.Load<Rgba32>(ReferencePath("hlg"));

        ImageComparer.Exact.VerifySimilarity(reference, image);
        Assert.Equal(JxlTransferFunction.Hlg, image.Metadata.GetFormatMetadata(JxlFormat.Instance).ColorEncoding.Tf.TransferFunction);
        Assert.Null(image.Metadata.IccProfile);
    }

    [Fact]
    public void Decode_IgnoreMetadata_BuildsNoProfileButKeepsTheColourEncoding()
    {
        using FileStream stream = File.OpenRead(Path.Combine(GeneratedDirectory, "colour_display_p3.jxl"));

        using Image<Rgba32> image = new JxlDecoder { IgnoreMetadata = true }.Decode<Rgba32>(Configuration.Default, stream, CancellationToken.None);

        Assert.Null(image.Metadata.IccProfile);
        Assert.Equal(JxlPrimaries.P3, image.Metadata.GetFormatMetadata(JxlFormat.Instance).ColorEncoding.Primaries);
    }

    [Fact]
    public void DeepClone_CopiesTheColourEncoding()
    {
        JxlMetadata jxl = Identify(Path.Combine(GeneratedDirectory, "colour_custom.jxl")).GetFormatMetadata(JxlFormat.Instance);

        JxlMetadata clone = (JxlMetadata)jxl.DeepClone();

        Assert.NotSame(jxl.ColorEncoding, clone.ColorEncoding);
        Assert.Equal(jxl.IsXybEncoded, clone.IsXybEncoded);
        Assert.Equal(JxlPrimaries.Custom, clone.ColorEncoding.Primaries);
        Assert.Equal(jxl.ColorEncoding.Red.X, clone.ColorEncoding.Red.X);
        Assert.Equal(jxl.ColorEncoding.White.Y, clone.ColorEncoding.White.Y);
        Assert.Equal(JxlRenderingIntent.Absolute, clone.ColorEncoding.RenderingIntent);
    }

    // The header fields and tags that describe the colour, within the tolerance of the fixed-point
    // encoding. The description, copyright and cicp tags are left out.
    private static void AssertSameColour(IccProfile expected, IccProfile actual)
    {
        Assert.Equal(expected.Header.DataColorSpace, actual.Header.DataColorSpace);
        Assert.Equal(expected.Header.ProfileConnectionSpace, actual.Header.ProfileConnectionSpace);
        Assert.Equal(expected.Header.Class, actual.Header.Class);
        Assert.Equal(expected.Header.RenderingIntent, actual.Header.RenderingIntent);

        // IccReader gives tags that share their data one entry, with the signature of the last of them,
        // so the tone curves are compared as curves rather than by tag.
        IccParametricCurve expectedCurve = Assert.Single(expected.Entries.OfType<IccParametricCurveTagDataEntry>().Distinct()).Curve;
        foreach (IccParametricCurveTagDataEntry entry in actual.Entries.OfType<IccParametricCurveTagDataEntry>())
        {
            Assert.Equal(expectedCurve.Type, entry.Curve.Type);
            AssertClose(entry.TagSignature, new[] { expectedCurve.G, expectedCurve.A, expectedCurve.B, expectedCurve.C, expectedCurve.D }, new[] { entry.Curve.G, entry.Curve.A, entry.Curve.B, entry.Curve.C, entry.Curve.D });
        }

        IccProfileTag[] channelCurves = expected.Header.DataColorSpace == IccColorSpaceType.Gray
            ? new[] { IccProfileTag.GrayTrc }
            : new[] { IccProfileTag.RedTrc, IccProfileTag.GreenTrc, IccProfileTag.BlueTrc };
        Assert.All(actual.Entries.OfType<IccParametricCurveTagDataEntry>(), e => Assert.Contains(e.TagSignature, channelCurves));

        foreach (IccProfileTag tag in ColourTags.Except(new[] { IccProfileTag.RedTrc, IccProfileTag.GreenTrc, IccProfileTag.BlueTrc, IccProfileTag.GrayTrc }))
        {
            IccTagDataEntry e = expected.Entries.FirstOrDefault(t => t.TagSignature == tag);
            IccTagDataEntry a = actual.Entries.FirstOrDefault(t => t.TagSignature == tag);
            if (e == null)
            {
                Assert.Null(a);
                continue;
            }

            Assert.True(a != null, $"The built profile has no {tag} tag.");
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
                case IccParametricCurveTagDataEntry curve:
                    IccParametricCurve c = curve.Curve;
                    IccParametricCurve d = Assert.IsType<IccParametricCurveTagDataEntry>(a).Curve;
                    Assert.Equal(c.Type, d.Type);
                    AssertClose(tag, new[] { c.G, c.A, c.B, c.C, c.D, c.E, c.F }, new[] { d.G, d.A, d.B, d.C, d.D, d.E, d.F });
                    break;
                default:
                    throw new InvalidOperationException($"No comparison for the {e.Signature} type of the {tag} tag.");
            }
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

    private static ImageMetadata Identify(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken.None).Metadata;
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
