// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Lossy Modular files, which are XYB-encoded: the XYB stage, the output encoding and the sinks. The files
/// are made by <c>cjxl -m 1 -d 1</c> (tests/Images/Input/Jxl/Generated/README.md), and the planes are
/// compared with djxl's float output at the lossy thresholds. The measured errors are noted on each test.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlXybModularTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Measured: worst channel RMSE 3.6e-7, peak 7.2e-6.
    [Fact]
    public void DecodeToPlanes_Rgb_MatchesDjxl()
        => AssertPlanesMatchDjxl("xyb_modular_rgb").Dispose();

    // Measured: worst channel RMSE 9.6e-8, peak 3.6e-7.
    [Fact]
    public void DecodeToPlanes_Grey_GivesOnePlaneMatchingDjxl()
    {
        // djxl's reference has one channel: an XYB grey image drops to one plane after the XYB stage.
        using JxlDecodedImage decoded = AssertPlanesMatchDjxl("xyb_modular_grey");

        Assert.Single(decoded.Frames[0].Planes);
    }

    [Fact]
    public void Load_Grey_GivesAGreyscalePixelType()
    {
        using Image image = Image.Load(CreateConfiguration(), InputPath("xyb_modular_grey"));

        Assert.IsType<Image<L8>>(image);
    }

    // Measured: worst channel RMSE 3.0e-7, peak 1.3e-6.
    [Fact]
    public void DecodeToPlanes_IccProfile_GivesLinearSrgbMatchingDjxl()
        => AssertPlanesMatchDjxl("xyb_modular_icc").Dispose();

    // Measured: largest difference 0.50 of one 8-bit step, which is the rounding.
    [Theory]
    [InlineData("xyb_modular_rgb")]
    [InlineData("xyb_modular_grey")]
    [InlineData("xyb_modular_gamma22")]
    public void LoadRgb24_IsWithinOneStepOfDjxl(string name)
    {
        NpyArray expected = NpyArray.Load(ReferencePath(name));
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name));

        // A grey image is R = G = B, so its one reference channel is compared with each.
        int channels = expected.Shape[3];
        NpyArray actual = JxlConformanceImages.ToNpy(image, 3);
        AssertWithinOneStep(expected, actual, (i, c) => expected.Data[(i * channels) + (channels == 1 ? 0 : c)]);
    }

    // Measured: largest difference 0.50 of one 8-bit step, which is the rounding.
    [Fact]
    public void LoadRgb24_IccProfile_IsTheSrgbEncodingOfDjxlsLinearOutput()
    {
        // djxl's planes are linear sRGB; Image<TPixel> holds sRGB with no pixel profile (ADR 0002).
        NpyArray expected = NpyArray.Load(ReferencePath("xyb_modular_icc"));
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath("xyb_modular_icc"));

        AssertWithinOneStep(expected, JxlConformanceImages.ToNpy(image, 3), (i, c) => (float)LinearToSrgb(Math.Clamp(expected.Data[(i * 3) + c], 0F, 1F)));
        Assert.Null(image.Metadata.IccProfile);
        Assert.NotNull(image.Metadata.GetFormatMetadata(JxlFormat.Instance).OriginalIccProfile);
    }

    // Measured: worst channel RMSE 4.2e-7, peak 3.8e-6.
    [Fact]
    public void DecodeToPlanes_DisplayP3_AdaptsTheOpsinMatrixToItsPrimaries()
        => AssertPlanesMatchDjxl("xyb_modular_display_p3").Dispose();

    // Measured: worst channel RMSE 8.2e-7, peak 7.2e-6.
    [Fact]
    public void DecodeToPlanes_IntensityTarget_ScalesToRelativeLuminance()
        => AssertPlanesMatchDjxl("xyb_modular_intensity_target").Dispose();

    [Fact]
    public void DecodeToPlanes_BlendingWithAnIccProfile_IsInvalid()
    {
        // An XYB image with an ICC profile renders to linear sRGB, and libjxl does not blend there
        // ("Blending in unsupported color space"); djxl fails on this file too.
        using FileStream stream = File.OpenRead(InputPath("xyb_modular_icc_animation"));

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken));
        Assert.Contains("unsupported color space", ex.Message, StringComparison.Ordinal);
    }

    // The image renders to its own encoding, a 2.2 gamma (libjxl's OpGamma).
    // Measured: worst channel RMSE 5.8e-7, peak 6.6e-6.
    [Fact]
    public void DecodeToPlanes_GammaTransferFunction_MatchesDjxl()
        => AssertPlanesMatchDjxl("xyb_modular_gamma22").Dispose();

    // The file keeps the 8-bit samples of its source, so Image.Load picks an 8-bit pixel type.
    [Fact]
    public void Load_GammaTransferFunction_PicksRgb24()
    {
        using Image image = Image.Load(CreateConfiguration(), InputPath("xyb_modular_gamma22"));

        Assert.IsType<Image<Rgb24>>(image);
    }

    // RGB_D65_SRG_Per_g0.45455: the gamma is stored as 1 / 2.2 times 10^7.
    [Fact]
    public void Identify_GammaTransferFunction_ReportsSizeChannelsAndGamma()
    {
        IImageInfo info = Image.Identify(CreateConfiguration(), InputPath("xyb_modular_gamma22"), out _);

        Assert.Equal(32, info.Width);
        Assert.Equal(32, info.Height);
        Assert.Equal(24, info.PixelType.BitsPerPixel);
        JxlMetadata metadata = info.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.True(metadata.IsXybEncoded);
        Assert.Equal(JxlColorSpace.Rgb, metadata.ColorEncoding.ColorSpace);
        Assert.Equal(JxlTransferFunction.Gamma, metadata.ColorEncoding.TransferFunction);
        Assert.Equal(0.45455, metadata.ColorEncoding.Gamma.Value, 1e-7);
        Assert.Null(metadata.OriginalIccProfile);
    }

    // The image renders to its own gamma encoding, so its pixels get that encoding's parametric profile,
    // and Identify, which reads only the headers, gives the same profile.
    [Fact]
    public void LoadAndIdentify_GammaTransferFunction_GiveTheSamePixelProfile()
    {
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath("xyb_modular_gamma22"));
        IImageInfo info = Image.Identify(CreateConfiguration(), InputPath("xyb_modular_gamma22"), out _);

        Assert.NotNull(image.Metadata.IccProfile);
        Assert.NotNull(info.Metadata.IccProfile);
        Assert.Equal(image.Metadata.IccProfile.ToByteArray(), info.Metadata.IccProfile.ToByteArray());
    }

    // Compares each sample with the clamped reference, which reference(pixel, channel) gives.
    private static void AssertWithinOneStep(NpyArray expected, NpyArray actual, Func<int, int, float> reference)
    {
        Assert.Equal(expected.Shape[1], actual.Shape[1]);
        Assert.Equal(expected.Shape[2], actual.Shape[2]);
        int pixels = actual.Shape[1] * actual.Shape[2];
        double worst = 0;
        for (int i = 0; i < pixels; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                double e = Math.Clamp(reference(i, c), 0F, 1F);
                worst = Math.Max(worst, Math.Abs(actual.Data[(i * 3) + c] - e) * 255);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Largest difference {worst:G3} of one 8-bit step");
        Assert.True(worst <= 1 + 1e-4, $"Largest difference {worst:G4} 8-bit steps");
    }

    // The sRGB encoding of IEC 61966-2-1, in double precision.
    private static double LinearToSrgb(double v) => v <= 0.0031308 ? 12.92 * v : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;

    private static JxlDecodedImage AssertPlanesMatchDjxl(string name)
    {
        NpyArray expected = NpyArray.Load(ReferencePath(name));

        using FileStream stream = File.OpenRead(InputPath(name));
        JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        try
        {
            JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded));
            return decoded;
        }
        catch
        {
            decoded.Dispose();
            throw;
        }
    }

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }

    private static string InputPath(string name) => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", name + ".jxl");

    private static string ReferencePath(string name) => Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".npy");
}
