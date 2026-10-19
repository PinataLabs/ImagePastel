// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;

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
    [InlineData("alpha_premultiplied")] // VarDCT with XYB, Gaborish and EPF; 12-bit, premultiplied 16-bit alpha. Measured: RMSE 2.2e-7, peak 3.1e-6 (threshold 3.8e-6).
    [InlineData("animation_icos4d")] // 48 VarDCT frames with alpha, Gaborish and EPF (2 iterations), cropped and blended. Measured, worst frame: RMSE 6.6e-7, peak 2.6e-5.
    [InlineData("animation_icos4d_5")] // A Level 5 reference of its own, which djxl v0.12.0 also misses by up to 5.6e-2. Measured, worst frame: RMSE 1.1e-3, peak 5.6e-2.
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

    // A vector that needs a feature of a later slice, which the spec lists under that slice. bike is XYB
    // with the BT.709 transfer function (D11). With a trial BT.709 stage it matched djxl v0.12.0 within a
    // peak error of 1.2e-5, so it needs nothing else. patches is a VarDCT frame with patches (D12).
    [Theory]
    [InlineData("bike", "Bt709")]
    [InlineData("bike_5", "Bt709")]
    [InlineData("patches", "Patches on VarDCT frames")]
    public async Task DecodeToPlanes_VectorOfALaterSlice_IsNotSupported(string id, string feature)
    {
        JxlConformanceVector vector = JxlConformanceVector.Load(id);
        string input = await JxlConformanceData.GetInputPathAsync(vector, CancellationToken);

        await using FileStream stream = File.OpenRead(input);
        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken));
        Assert.Contains(feature, ex.Message, StringComparison.Ordinal);
    }
}
