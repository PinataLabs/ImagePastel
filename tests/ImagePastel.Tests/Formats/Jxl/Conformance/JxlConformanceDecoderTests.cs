// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Jxl;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;

/// <summary>
/// Checks this decoder against the conformance references, through its unclamped float planes.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlConformanceDecoderTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("delta_palette")]
    [InlineData("lz77_flower")]
    [InlineData("alpha_nonpremultiplied")]
    [InlineData("alpha_triangles")]
    [InlineData("lossless_pfm")]
    [InlineData("mul_no_extra_channels")]
    [InlineData("blendmodes")]
    [InlineData("blendmodes_5")]
    [InlineData("sunset_logo")]
    [InlineData("animation_newtons_cradle")]
    [InlineData("spot")]
    [InlineData("cmyk_layers")]
    [InlineData("patches_lossless")]
    [InlineData("bicycles")] // Modular with XYB, in Levels 5 and 10. Measured: RMSE 2.5e-7, peak 8.4e-6.
    [InlineData("grayscale_public_university")] // Grey Modular, not XYB, with Gaborish and EPF (3 iterations, sigma 20), Level 10. Measured: RMSE 5.1e-8, peak 8.1e-7.
    [InlineData("opsin_inverse")] // VarDCT with XYB, a custom opsin inverse matrix, Gaborish and EPF. Measured: RMSE 6.2e-7, peak 7.6e-6.
    [InlineData("opsin_inverse_5")] // The same reference, at Level 5.
    [InlineData("grayscale")] // Lossy grey, XYB with an ICC profile, so the planes are linear sRGB. Measured: RMSE 7.8e-6, peak 2.3e-4, as for djxl v0.12.0, which this decoder matches within 9.5e-7.
    [InlineData("grayscale_5")] // The same reference, at Level 5.
    [InlineData("bench_oriented_brg")] // A 4:4:4 JPEG transcode: VarDCT YCbCr, a raw DCT8 table, non-zero chroma-from-luma, orientation 5. Threshold RMSE 1e-5. Measured: RMSE 2.7e-7, peak 1.9e-6 (djxl v0.12.0: 2e-8, 1.2e-7).
    [InlineData("grayscale_jpeg")] // A 4:4:4 JPEG transcode of a grey image: three YCbCr planes, then R as the grey plane. Threshold RMSE 1e-5. Measured: RMSE 1.3e-7, peak 1.8e-6 (djxl v0.12.0: exact).
    [InlineData("bench_oriented_brg_5")] // The same reference, at Level 5.
    [InlineData("grayscale_jpeg_5")] // The same reference, at Level 5.
    [InlineData("cafe")] // Fetched. A 4:2:0 JPEG transcode, 1280x1600 in 35 groups: chroma upsampling, then YCbCr. Threshold RMSE 1e-5. Measured: RMSE 4.6e-7, peak 5.0e-6 (djxl v0.12.0: 2.2e-8, 1.2e-7).
    [InlineData("cafe_5")] // The same reference, at Level 5.
    [InlineData("upsampling")] // XYB VarDCT with alpha, coded at 200x150 with Gaborish and EPF, colour and alpha upsampled 4x together (late). Measured: RMSE 1.3e-6, peak 4.7e-5 (djxl v0.12.0: 9.2e-7, 4.2e-5).
    [InlineData("upsampling_5")] // A Level 5 reference of its own. Measured: RMSE 2.6e-4, peak 1.6e-2, as for djxl v0.12.0 (2.6e-4, 1.6e-2).
    [InlineData("alpha_premultiplied")] // VarDCT with XYB, Gaborish and EPF; 12-bit, premultiplied 16-bit alpha. Measured: RMSE 2.2e-7, peak 3.1e-6 (threshold 3.8e-6).
    [InlineData("animation_icos4d")] // 48 VarDCT frames with alpha, Gaborish and EPF (2 iterations), cropped and blended. Measured, worst frame: RMSE 6.6e-7, peak 2.6e-5.
    [InlineData("noise")] // XYB VarDCT with noise (D12c). Measured: RMSE 5.8e-7, peak 2.8e-5 (djxl v0.12.0: 3.8e-7, 9.2e-6).
    [InlineData("noise_5")] // The same reference, at Level 5.
    [InlineData("animation_icos4d_5")] // A Level 5 reference of its own, which djxl v0.12.0 also misses by up to 5.6e-2. Measured, worst frame: RMSE 1.1e-3, peak 5.6e-2.
    [InlineData("patches")] // XYB VarDCT with alpha and patches from a Modular reference-only frame saved before its colour transform, so in XYB. Measured: RMSE 1.5e-7, peak 6.6e-6 (djxl v0.12.0: 9.2e-8, 2.7e-6).
    [InlineData("patches_5")] // A Level 5 reference of its own, which djxl v0.12.0 misses by as much. Measured: RMSE 2.6e-4, peak 2.4e-2 (djxl v0.12.0: 2.6e-4, 2.4e-2).
    [InlineData("bike")] // Fetched. XYB VarDCT with patches, rendered to the BT.709 curve. Measured: RMSE 4.7e-7, peak 2.5e-4 (djxl v0.12.0: 3.0e-7, 6.3e-6). The peak is one sample at the curve's switch from 4.5x to the power segment, where it jumps from 0.0810 to 0.0812 between two adjacent floats: the FMA differences (ADR 0004) put the linear sample on the other side of 0.018. No other sample is more than 1.6e-5 away.
    [InlineData("bike_5")] // The same reference, at Level 5.
    [InlineData("animation_spline")] // 60 VarDCT frames with XYB and splines, each drawn with the frame's chroma-from-luma base. Measured, worst frame: RMSE 2.8e-7, peak 1.4e-5 (djxl v0.12.0: 2.8e-7, 1.4e-5).
    [InlineData("animation_spline_5")] // The same reference, at Level 5.
    [InlineData("progressive")] // Fetched. A Modular reference-only frame, a Modular DC frame (level 1) with a permuted TOC, then a two-pass XYB VarDCT frame with patches that uses the DC frame, with a centre-first TOC (D13a, D13b, D13c). 4064x2704, so about a minute in a Debug build, most of it in the three EPF passes. Measured: RMSE 4.4e-7, peak 2.0e-5 (djxl v0.12.0: 2.8e-7, 4.9e-6).
    [InlineData("progressive_5")] // The same reference, at Level 5.
    public async Task DecodeToPlanes_PassesThresholds(string id)
    {
        JxlConformanceVector vector = JxlConformanceVector.Load(id);
        NpyArray expected = await JxlConformanceData.LoadReferenceImageAsync(vector, CancellationToken);
        string input = await JxlConformanceData.GetInputPathAsync(vector, CancellationToken);

        await using FileStream stream = File.OpenRead(input);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        foreach (JxlConformanceFrameResult result in JxlConformanceComparer.Measure(expected, actual))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"Frame {result.Frame}: worst channel RMSE {result.WorstChannelRmse:G3}, peak {result.PeakError:G3}");
        }

        JxlConformanceComparer.AssertWithinThresholds(vector, expected, actual);
    }

    // libjxl test data that no conformance vector covers, against a djxl v0.12.0 .npy reference (see the
    // README next to the file). A lossless file decodes exactly.
    [Theory]
    [InlineData("pq_gradient")] // PQ, 16-bit grey, lossless Modular, not XYB: the samples are the PQ-encoded ones. Measured: exact.
    public void DecodeToPlanes_LibjxlTestData_MatchesDjxlExactly(string name)
    {
        NpyArray expected = NpyArray.Load(Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".npy"));

        using FileStream stream = File.OpenRead(Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "libjxl", "jxl", name + ".jxl"));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        Assert.Equal(expected.Shape, actual.Shape);
        Assert.Equal(expected.Data, actual.Data);
    }
}
