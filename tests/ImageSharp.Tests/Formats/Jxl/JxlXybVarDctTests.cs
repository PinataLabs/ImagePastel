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
/// XYB VarDCT frames, D8's done-check: the files made with <c>cjxl -e 2 --gaborish=0 --epf=0</c>
/// (tests/Images/Input/Jxl/Generated/README.md), whose blocks are all DCT8 with the default quant tables;
/// and D9e's: the files made with <c>cjxl -e 7 --gaborish=0 --epf=0</c>, which mix the DCT sizes from 2x2
/// to 64x64, with their library quant tables and custom coefficient orders; and D9f's
/// <c>vardct_xyb_identity_afv</c>, at the same settings, which adds Identity and AFV blocks, an adaptive quant
/// field and a non-zero chroma-from-luma map. The planes are compared with djxl's float output at the lossy thresholds (docs/jxl/spec.md, "Tolerance
/// for lossy output"), and each case records the error measured when it was written.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlXybVarDctTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Measured on x64 against djxl v0.12.0 (AVX2): the worst channel RMSE and the peak error.
    [Theory]
    [InlineData("vardct_xyb_64", 3)] // RMSE 1.1e-6, peak 1.2e-5
    [InlineData("vardct_xyb_300x200_d05", 3)] // RMSE 1.1e-6, peak 2.6e-5
    [InlineData("vardct_xyb_600x400_d3", 3)] // RMSE 1.3e-6, peak 7.8e-5
    [InlineData("vardct_xyb_grey", 1)] // RMSE 2.6e-7, peak 1.6e-6
    [InlineData("vardct_xyb_alpha", 4)] // RMSE 3.0e-6, peak 4.1e-5
    [InlineData("vardct_xyb_icc", 3)] // RMSE 1.4e-6, peak 7.7e-6
    [InlineData("vardct_xyb_grey_d50", 1)] // RMSE 1.1e-7, peak 7.8e-7
    [InlineData("vardct_xyb_strategies", 3)] // RMSE 9.8e-7, peak 1.3e-5
    [InlineData("vardct_xyb_sizes_noisy", 3)] // RMSE 6.7e-7, peak 2.5e-5
    [InlineData("vardct_xyb_sizes_fine", 3)] // RMSE 7.2e-7, peak 3.0e-5
    [InlineData("vardct_xyb_sizes_smooth", 3)] // RMSE 6.1e-7, peak 1.0e-5
    [InlineData("vardct_xyb_identity_afv", 4)] // RMSE 4.1e-7, peak 2.8e-5
    [InlineData("vardct_xyb_e7", 3)] // Gaborish, EPF (1 iteration) and other block sizes: RMSE 1.1e-6, peak 1.7e-5
    public void DecodeToPlanes_MatchesDjxl(string name, int planes)
    {
        NpyArray expected = LoadReference(name);

        using FileStream stream = File.OpenRead(InputPath(name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);

        Assert.Equal(planes, decoded.Frames[0].Planes.Count);
        Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded)));
    }

    // Measured: the largest difference is 0.50 of one 8-bit step, which is the rounding.
    [Theory]
    [InlineData("vardct_xyb_64")]
    [InlineData("vardct_xyb_300x200_d05")]
    [InlineData("vardct_xyb_600x400_d3")]
    [InlineData("vardct_xyb_grey")]
    [InlineData("vardct_xyb_strategies")]
    [InlineData("vardct_xyb_sizes_noisy")]
    [InlineData("vardct_xyb_sizes_fine")]
    [InlineData("vardct_xyb_sizes_smooth")]
    [InlineData("vardct_xyb_identity_afv")]
    public void LoadRgb24_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = LoadReference(name);
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name), out _);

        // A grey image is R = G = B, so its one reference channel is compared with each.
        int channels = expected.Shape[3];
        AssertWithinOneStep(expected, JxlConformanceImages.ToNpy(image, 3), 3, (i, c) => expected.Data[(i * channels) + (channels == 1 ? 0 : c)]);
    }

    // Measured: the largest difference is 0.50 of one 8-bit step, which is the rounding.
    [Theory]
    [InlineData("vardct_xyb_alpha")]
    [InlineData("vardct_xyb_identity_afv")]
    public void LoadRgba32_Alpha_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = LoadReference(name);
        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), InputPath(name));

        AssertWithinOneStep(expected, JxlConformanceImages.ToNpy(image, 4), 4, (i, c) => expected.Data[(i * 4) + c]);
    }

    // Measured: the largest difference is 0.50 of one 8-bit step, which is the rounding.
    [Fact]
    public void LoadRgb24_IccProfile_IsTheSrgbEncodingOfDjxlsLinearOutput()
    {
        // djxl's planes are linear sRGB; Image<TPixel> holds sRGB with no pixel profile (ADR 0002).
        NpyArray expected = LoadReference("vardct_xyb_icc");
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath("vardct_xyb_icc"));

        AssertWithinOneStep(expected, JxlConformanceImages.ToNpy(image, 3), 3, (i, c) => (float)LinearToSrgb(Math.Clamp(expected.Data[(i * 3) + c], 0F, 1F)));
        Assert.Null(image.Metadata.IccProfile);
        Assert.NotNull(image.Metadata.GetFormatMetadata(JxlFormat.Instance).OriginalIccProfile);
    }

    // A grey image with a D50 white point is one the XYB stage cannot output, so it renders to linear
    // sRGB like an ICC profile, and the pixels are sRGB: no pixel profile describes the D50 encoding
    // (ADR 0002), which JxlMetadata still reports.
    // Measured: the largest difference is 0.50 of one 8-bit step, which is the rounding.
    [Fact]
    public void Load_GreyWithAWhitePointTheXybStageCannotOutput_IsSrgbWithNoPixelProfile()
    {
        NpyArray expected = LoadReference("vardct_xyb_grey_d50");
        using Image<L8> image = Image.Load<L8>(CreateConfiguration(), InputPath("vardct_xyb_grey_d50"));

        AssertWithinOneStep(expected, JxlConformanceImages.ToNpy(image, 1), 1, (i, _) => (float)LinearToSrgb(Math.Clamp(expected.Data[i], 0F, 1F)));
        Assert.Null(image.Metadata.IccProfile);
        JxlMetadata metadata = image.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.Equal(JxlWhitePoint.Custom, metadata.ColorEncoding.WhitePoint);
        Assert.Null(metadata.OriginalIccProfile);
    }

    [Fact]
    public void Identify_GreyWithAWhitePointTheXybStageCannotOutput_HasNoPixelProfile()
    {
        IImageInfo info = Image.Identify(CreateConfiguration(), InputPath("vardct_xyb_grey_d50"), out _);

        Assert.Null(info.Metadata.IccProfile);
    }

    [Fact]
    public void Load_Grey_GivesAGreyscalePixelType()
    {
        using Image image = Image.Load(CreateConfiguration(), InputPath("vardct_xyb_grey"));

        Assert.IsType<Image<L8>>(image);
    }

    [Theory]
    [InlineData("vardct_xyb_64", 64, 64, 24, false)]
    [InlineData("vardct_xyb_300x200_d05", 300, 200, 24, false)]
    [InlineData("vardct_xyb_600x400_d3", 600, 400, 24, false)]
    [InlineData("vardct_xyb_grey", 300, 200, 8, true)]
    [InlineData("vardct_xyb_alpha", 72, 56, 32, false)]
    public void Identify_ReportsSizeChannelsAndColourEncoding(string name, int width, int height, int bitsPerPixel, bool grey)
    {
        IImageInfo info = Image.Identify(CreateConfiguration(), InputPath(name), out _);

        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
        Assert.Equal(bitsPerPixel, info.PixelType.BitsPerPixel);
        JxlMetadata metadata = info.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.True(metadata.IsXybEncoded);
        Assert.Equal(grey ? JxlColorSpace.Gray : JxlColorSpace.Rgb, metadata.ColorEncoding.ColorSpace);
        Assert.Equal(JxlTransferFunction.Srgb, metadata.ColorEncoding.Tf.TransferFunction);
        Assert.Null(metadata.OriginalIccProfile);
    }

    [Fact]
    public void Identify_IccProfile_ReportsTheProfileAndNoColourEncoding()
    {
        IImageInfo info = Image.Identify(CreateConfiguration(), InputPath("vardct_xyb_icc"), out _);

        Assert.Equal(32, info.Width);
        Assert.Equal(32, info.Height);
        Assert.Equal(24, info.PixelType.BitsPerPixel);
        JxlMetadata metadata = info.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.True(metadata.IsXybEncoded);
        Assert.Null(metadata.ColorEncoding);
        Assert.NotNull(metadata.OriginalIccProfile);
        Assert.Null(info.Metadata.IccProfile);
    }

    // The 64 x 64 image with one of the features of a later slice each.
    [Theory]
    [InlineData("vardct_xyb_passes", "Progressive passes")]
    [InlineData("vardct_xyb_dc_frame", "a DC frame")]
    public void Decode_FeatureOfALaterSlice_IsNotSupported(string name, string feature)
    {
        using FileStream stream = File.OpenRead(InputPath(name));

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken));
        Assert.Contains(feature, ex.Message, StringComparison.Ordinal);
    }

    // Compares each sample with the clamped reference, which reference(pixel, channel) gives.
    private static void AssertWithinOneStep(NpyArray expected, NpyArray actual, int channels, Func<int, int, float> reference)
    {
        Assert.Equal(expected.Shape[1], actual.Shape[1]);
        Assert.Equal(expected.Shape[2], actual.Shape[2]);
        int pixels = actual.Shape[1] * actual.Shape[2];
        double worst = 0;
        for (int i = 0; i < pixels; i++)
        {
            for (int c = 0; c < channels; c++)
            {
                double e = Math.Clamp(reference(i, c), 0F, 1F);
                worst = Math.Max(worst, Math.Abs(actual.Data[(i * channels) + c] - e) * 255);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Largest difference {worst:G3} of one 8-bit step");
        Assert.True(worst <= 1 + 1e-4, $"Largest difference {worst:G4} 8-bit steps");
    }

    // The sRGB encoding of IEC 61966-2-1, in double precision.
    private static double LinearToSrgb(double v) => v <= 0.0031308 ? 12.92 * v : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }

    private static string InputPath(string name) => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", name + ".jxl");

    private static NpyArray LoadReference(string name) => NpyArray.Load(Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".npy"));
}
