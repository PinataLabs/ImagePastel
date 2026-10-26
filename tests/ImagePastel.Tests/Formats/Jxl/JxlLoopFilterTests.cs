// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The loop filters on Modular frames, and Gaborish and EPF in one frame (D9d): files made with
/// <c>cjxl -m 1</c> and <c>cjxl -e 2</c> (tests/Images/Input/Jxl/Generated/README.md, "How the Modular
/// and both-filter files were made"). The planes are compared with djxl's float output at the lossy
/// thresholds (docs/jxl/spec.md, "Tolerance for lossy output"), and each case records the error
/// measured when it was written.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlLoopFilterTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Measured on x64 against djxl v0.12.0 (AVX2): the worst channel RMSE and the peak error. In
    // brackets: how far the reference is from the same file decoded by djxl with a filter patched off
    // (peak, worst channel RMSE), so a test that skipped that filter would fail where it is above the
    // tolerance.
    [Theory]
    [InlineData("xyb_modular_gaborish", 3)] // RMSE 4.8e-7, peak 8.4e-6 [Gaborish 8.5e-1, 7.6e-2]
    [InlineData("xyb_modular_epf2", 3)] // RMSE 4.7e-7, peak 1.8e-5 [EPF 1.2e-1, 3.7e-3]
    [InlineData("xyb_modular_epf2_custom", 3)] // RMSE 5.6e-7, peak 2.3e-5 [custom fields vs default 3.9e-1, 7.4e-3]
    [InlineData("xyb_modular_epf_sigma_tiny", 3)] // RMSE 3.9e-7, peak 1.5e-5; sigma below kMinSigma: every pixel passes through
    [InlineData("xyb_modular_gab_epf3", 3)] // RMSE 9.0e-7, peak 2.3e-5 [EPF 3.3e-1, 2.7e-2; pass 0 3.2e-1, 2.2e-2; Gaborish 1.1, 9.9e-2]
    [InlineData("xyb_modular_gab_epf1_600x400", 3)] // RMSE 5.5e-7, peak 2.7e-5 [EPF 5.0e-2, 3.0e-4; Gaborish 1.2, 1.1e-1]
    [InlineData("modular_grey_gab_epf3", 1)] // RMSE 2.9e-8, peak 3.0e-7 [EPF 1.9e-3, 1.4e-4; Gaborish 3.7e-1, 5.6e-2]
    [InlineData("modular_grey_gab_epf3_custom", 1)] // RMSE 9.3e-8, peak 1.4e-6 [EPF 8.5e-2, 2.7e-2; pass 0 8.0e-2, 1.9e-2; weights 1.0e-1, 2.2e-2]
    [InlineData("vardct_xyb_gab_epf1", 3)] // RMSE 1.0e-6, peak 1.7e-5 [EPF 3.2e-6, 8.8e-8: invisible at -d 1; Gaborish 9.3e-1, 8.2e-2]
    [InlineData("vardct_xyb_gab_epf3_300x200_d3", 3)] // RMSE 1.3e-6, peak 4.6e-5 [EPF 3.0e-1, 8.8e-3; pass 0 2.3e-1, 6.8e-3; Gaborish 1.2, 9.9e-2]
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
    [InlineData("xyb_modular_gab_epf3")]
    [InlineData("xyb_modular_gab_epf1_600x400")]
    [InlineData("modular_grey_gab_epf3_custom")]
    [InlineData("vardct_xyb_gab_epf3_300x200_d3")]
    public void LoadRgb24_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = LoadReference(name);
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name), out _);
        JxlConformanceImages.AssertRgbWithinOneStepOfTheClampedReference(expected, JxlConformanceImages.ToNpy(image, 3));
    }

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
