// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The edge-preserving filter (EPF) on VarDCT frames: the files made with <c>cjxl -e 2 --gaborish=0</c>
/// and an <c>--epf</c> (tests/Images/Input/Jxl/Generated/README.md), whose blocks are all DCT8. The
/// planes are compared with djxl's float output at the lossy thresholds (docs/jxl/spec.md, "Tolerance
/// for lossy output"), and each case records the error measured when it was written.
/// </summary>
/// <remarks>
/// At <c>-d 1</c> the 64 x 64 image is barely filtered: <c>vardct_xyb_epf</c> (one iteration) changes by
/// 2.9e-6 when EPF is turned off, inside the tolerance, and pass 0 changes it by 6.4e-4. The <c>_d3</c>
/// files are filtered well above the tolerance by every pass, so they fail if a pass is skipped or wrong.
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlEpfTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Measured on x64 against djxl v0.12.0 (AVX2): the worst channel RMSE and the peak error. In
    // brackets: how far the reference is from the same file decoded with EPF off (peak, RMSE).
    [Theory]
    [InlineData("vardct_xyb_epf")] // RMSE 1.1e-6, peak 1.2e-5 [2.9e-6, 6.5e-8]
    [InlineData("vardct_xyb_epf2")] // RMSE 1.1e-6, peak 1.2e-5 [2.1e-2, 4.4e-4]
    [InlineData("vardct_xyb_epf3")] // RMSE 1.1e-6, peak 1.2e-5 [2.1e-2, 4.4e-4]
    [InlineData("vardct_xyb_epf1_d3")] // RMSE 1.0e-6, peak 1.6e-5 [3.6e-2, 2.6e-3]
    [InlineData("vardct_xyb_epf2_d3")] // RMSE 9.9e-7, peak 1.6e-5 [6.3e-2, 4.5e-3]
    [InlineData("vardct_xyb_epf3_d3")] // RMSE 1.1e-6, peak 2.5e-5 [1.1e-1, 8.5e-3]
    [InlineData("vardct_xyb_epf3_300x200_d3")] // RMSE 1.2e-6, peak 4.1e-5 [2.5e-1, 1.0e-2]
    [InlineData("vardct_xyb_epf3_13x37_d3")] // RMSE 1.6e-6, peak 7.8e-6 [8.9e-2, 1.1e-2]
    [InlineData("vardct_xyb_epf3_600x400_d3")] // RMSE 1.3e-6, peak 7.8e-5 [4.4e-1, 9.6e-3]
    [InlineData("vardct_xyb_epf3_custom")] // RMSE 1.1e-6, peak 1.2e-5 [6.3e-2, 1.9e-3]
    [InlineData("vardct_xyb_epf_sharp0")] // RMSE 1.1e-6, peak 1.2e-5; every block passes through
    [InlineData("vardct_xyb_epf_quant_mul0")] // RMSE 1.1e-6, peak 1.2e-5; every block passes through
    public void DecodeToPlanes_MatchesDjxl(string name)
    {
        NpyArray expected = LoadReference(name);

        using FileStream stream = File.OpenRead(InputPath(name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);

        Assert.Equal(3, decoded.Frames[0].Planes.Count);
        Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded)));
    }

    // Measured: the largest difference is 0.51 of one 8-bit step: the rounding, and the error above.
    [Theory]
    [InlineData("vardct_xyb_epf")]
    [InlineData("vardct_xyb_epf2")]
    [InlineData("vardct_xyb_epf3")]
    [InlineData("vardct_xyb_epf3_d3")]
    [InlineData("vardct_xyb_epf3_300x200_d3")]
    [InlineData("vardct_xyb_epf3_13x37_d3")]
    [InlineData("vardct_xyb_epf3_600x400_d3")]
    public void LoadRgb24_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = LoadReference(name);
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name), out _);

        JxlConformanceImages.AssertRgbWithinOneStepOfTheClampedReference(expected, JxlConformanceImages.ToNpy(image, 3));
    }

    // loop_filter.cc: epf_sigma_for_modular below 1e-8 fails, as djxl does for both files.
    [Theory]
    [InlineData("xyb_modular_epf_sigma0")]
    [InlineData("xyb_modular_epf_sigma_neg")]
    public void Decode_SigmaForModularBelowTheBound_IsInvalid(string name)
    {
        using FileStream stream = File.OpenRead(InputPath(name));

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken));
        Assert.Contains("EPF: sigma for modular is too small", ex.Message, StringComparison.Ordinal);
    }

    // libjxl's JXL_ENSURE(epf_iters > 0) in ComputeSigma. No file reaches it, as only a frame with EPF
    // gets the EPF stages that compute the sigma, so it is called on the DC groups of one such frame.
    [Fact]
    public void ComputeSigma_EpfOff_IsInvalid()
    {
        using JxlDecodedDcGroups dc = JxlDecodedDcGroups.Decode(File.ReadAllBytes(InputPath("vardct_xyb_epf")));
        JxlLoopFilter lf = dc.FrameHeader.LoopFilter;
        Assert.Equal(1u, lf.EpfIters);
        Assert.NotNull(JxlEpfSigma.Compute(lf, dc.State));

        lf.EpfIters = 0;

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => JxlEpfSigma.Compute(lf, dc.State));
        Assert.Contains("EPF sigma computed with EPF off", ex.Message, StringComparison.Ordinal);
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
