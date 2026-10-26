// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using PinataLabs.ImagePastel.ColorSpaces.Conversion;
using PinataLabs.ImagePastel.Formats.Jxl;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// D15a (#308): <see cref="JxlMetadata.ColorEncoding"/> is the public description of the colour encoding
/// that the image header codes. The expected values are what <c>jxlinfo</c> v0.12.0 prints for each file.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlColorEncodingDescriptionTests
{
    // The custom chromaticities are coded in millionths; the description holds them as floats.
    private const float XyTolerance = 1e-6F;

    // The gamma is coded in units of 10^-7.
    private const double GammaTolerance = 1e-7;

    [Theory]
    [InlineData("Conformance/delta_palette/input.jxl", JxlColorSpace.Rgb, JxlWhitePoint.D65, JxlPrimaries.Srgb, JxlTransferFunction.Srgb, JxlRenderingIntent.Relative, null)]
    [InlineData("Conformance/lz77_flower/input.jxl", JxlColorSpace.Rgb, JxlWhitePoint.D65, JxlPrimaries.Srgb, JxlTransferFunction.Gamma, JxlRenderingIntent.Relative, 0.45455)]
    [InlineData("Conformance/grayscale_public_university/input.jxl", JxlColorSpace.Gray, JxlWhitePoint.D65, JxlPrimaries.Srgb, JxlTransferFunction.Srgb, JxlRenderingIntent.Relative, null)]
    [InlineData("Generated/colour_gamma22.jxl", JxlColorSpace.Rgb, JxlWhitePoint.D65, JxlPrimaries.Srgb, JxlTransferFunction.Gamma, JxlRenderingIntent.Perceptual, 0.45455)]
    [InlineData("Generated/colour_pq.jxl", JxlColorSpace.Rgb, JxlWhitePoint.D65, JxlPrimaries.Bt2100, JxlTransferFunction.Pq, JxlRenderingIntent.Relative, null)]
    [InlineData("Generated/colour_hlg.jxl", JxlColorSpace.Rgb, JxlWhitePoint.D65, JxlPrimaries.Bt2100, JxlTransferFunction.Hlg, JxlRenderingIntent.Relative, null)]
    [InlineData("Generated/colour_grey_hlg.jxl", JxlColorSpace.Gray, JxlWhitePoint.D65, JxlPrimaries.Srgb, JxlTransferFunction.Hlg, JxlRenderingIntent.Relative, null)]
    [InlineData("Generated/colour_grey_linear.jxl", JxlColorSpace.Gray, JxlWhitePoint.D65, JxlPrimaries.Srgb, JxlTransferFunction.Linear, JxlRenderingIntent.Relative, null)]
    [InlineData("Generated/colour_rec2020.jxl", JxlColorSpace.Rgb, JxlWhitePoint.D65, JxlPrimaries.Bt2100, JxlTransferFunction.Bt709, JxlRenderingIntent.Relative, null)]
    [InlineData("Generated/colour_dci.jxl", JxlColorSpace.Rgb, JxlWhitePoint.Dci, JxlPrimaries.P3, JxlTransferFunction.Dci, JxlRenderingIntent.Relative, null)]
    [InlineData("Generated/colour_display_p3.jxl", JxlColorSpace.Rgb, JxlWhitePoint.D65, JxlPrimaries.P3, JxlTransferFunction.Srgb, JxlRenderingIntent.Relative, null)]
    public void Identify_EnumeratedEncoding_DescribesTheHeader(
        string file,
        JxlColorSpace colorSpace,
        JxlWhitePoint whitePoint,
        JxlPrimaries primaries,
        JxlTransferFunction transferFunction,
        JxlRenderingIntent renderingIntent,
        double? gamma)
    {
        JxlColorEncoding encoding = IdentifyColorEncoding(file);

        Assert.NotNull(encoding);
        Assert.Equal(colorSpace, encoding.ColorSpace);
        Assert.Equal(whitePoint, encoding.WhitePoint);
        Assert.Equal(primaries, encoding.Primaries);
        Assert.Equal(transferFunction, encoding.TransferFunction);
        Assert.Equal(renderingIntent, encoding.RenderingIntent);
        if (gamma is null)
        {
            Assert.Null(encoding.Gamma);
        }
        else
        {
            Assert.NotNull(encoding.Gamma);
            Assert.Equal(gamma.Value, encoding.Gamma.Value, GammaTolerance);
        }

        Assert.Null(encoding.CustomWhitePoint);
        Assert.Null(encoding.CustomRedPrimary);
        Assert.Null(encoding.CustomGreenPrimary);
        Assert.Null(encoding.CustomBluePrimary);
    }

    [Fact]
    public void Identify_CustomWhitePointAndPrimaries_GivesTheirChromaticities()
    {
        JxlColorEncoding encoding = IdentifyColorEncoding("Generated/colour_custom.jxl");

        Assert.Equal(JxlColorSpace.Rgb, encoding.ColorSpace);
        Assert.Equal(JxlWhitePoint.Custom, encoding.WhitePoint);
        Assert.Equal(JxlPrimaries.Custom, encoding.Primaries);
        Assert.Equal(JxlTransferFunction.Srgb, encoding.TransferFunction);
        Assert.Equal(JxlRenderingIntent.Absolute, encoding.RenderingIntent);
        Assert.Null(encoding.Gamma);
        AssertXy(0.3457F, 0.3585F, encoding.CustomWhitePoint);
        AssertXy(0.70F, 0.29F, encoding.CustomRedPrimary);
        AssertXy(0.18F, 0.72F, encoding.CustomGreenPrimary);
        AssertXy(0.14F, 0.05F, encoding.CustomBluePrimary);
    }

    [Fact]
    public void Identify_CustomPrimariesWithAStandardWhitePoint_HasNoCustomWhitePoint()
    {
        JxlColorEncoding encoding = IdentifyColorEncoding("Generated/colour_custom_pq.jxl");

        Assert.Equal(JxlWhitePoint.D65, encoding.WhitePoint);
        Assert.Null(encoding.CustomWhitePoint);
        Assert.Equal(JxlPrimaries.Custom, encoding.Primaries);
        Assert.Equal(JxlTransferFunction.Pq, encoding.TransferFunction);
        AssertXy(0.70F, 0.29F, encoding.CustomRedPrimary);
        AssertXy(0.18F, 0.72F, encoding.CustomGreenPrimary);
        AssertXy(0.14F, 0.05F, encoding.CustomBluePrimary);
    }

    [Theory]
    [InlineData("Conformance/grayscale/input.jxl")]
    [InlineData("Conformance/patches/input.jxl")]
    public void Identify_IccProfile_HasNoColourEncoding(string file)
        => Assert.Null(IdentifyColorEncoding(file));

    private static void AssertXy(float x, float y, CieXyChromaticityCoordinates? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(x, actual.Value.X, XyTolerance);
        Assert.Equal(y, actual.Value.Y, XyTolerance);
    }

    private static JxlColorEncoding IdentifyColorEncoding(string file)
    {
        using FileStream stream = File.OpenRead(Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", file));
        return new JxlDecoder().Identify(Configuration.Default, stream, TestContext.Current.CancellationToken)
            .Metadata.GetFormatMetadata(JxlFormat.Instance).ColorEncoding;
    }
}
