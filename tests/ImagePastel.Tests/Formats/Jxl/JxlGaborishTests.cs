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
/// The Gaborish loop filter, D9b's tracer: XYB VarDCT files made with <c>cjxl -e 2 --gaborish=1 --epf=0</c>
/// (tests/Images/Input/Jxl/Generated/README.md), whose blocks are all DCT8. The planes are compared with
/// djxl's float output at the lossy thresholds (docs/jxl/spec.md, "Tolerance for lossy output"), and each
/// case records the error measured when it was written.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlGaborishTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Measured on x64 against djxl v0.12.0 (AVX2): the worst channel RMSE and the peak error.
    // vardct_xyb_gaborish_custom has custom weights (set_gaborish_weights.py), which move djxl's output
    // from that of the default weights by an RMSE of 0.039: the match shows they are read and used.
    [Theory]
    [InlineData("vardct_xyb_gaborish", 3)] // RMSE 1.0e-6, peak 1.7e-5
    [InlineData("vardct_xyb_gaborish_600x400", 3)] // RMSE 1.1e-6, peak 3.8e-5
    [InlineData("vardct_xyb_gaborish_grey", 1)] // RMSE 1.9e-7, peak 9.5e-7
    [InlineData("vardct_xyb_gaborish_custom", 3)] // RMSE 1.1e-6, peak 2.0e-5
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
    [InlineData("vardct_xyb_gaborish")]
    [InlineData("vardct_xyb_gaborish_600x400")]
    [InlineData("vardct_xyb_gaborish_grey")]
    [InlineData("vardct_xyb_gaborish_custom")]
    public void LoadRgb24_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = LoadReference(name);
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name), out _);
        JxlConformanceImages.AssertRgbWithinOneStepOfTheClampedReference(expected, JxlConformanceImages.ToNpy(image, 3));
    }

    // Custom x weights of -0.125 and -0.125 make the unnormalized kernel 1 + 4 * (w1 + w2) = 0, which
    // libjxl's LoopFilter::VisitFields rejects (djxl fails on the file too).
    [Fact]
    public void Decode_CustomWeightsWithAZeroKernel_IsInvalid()
    {
        using FileStream stream = File.OpenRead(InputPath("vardct_xyb_gaborish_zero_kernel"));

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken));
        Assert.Contains("Gaborish x weights lead to near 0 unnormalized kernel", ex.Message, StringComparison.Ordinal);
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
