// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

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
    public async Task DecodeToPlanes_PassesThresholds(string id)
    {
        JxlConformanceVector vector = JxlConformanceVector.Load(id);
        NpyArray expected = await JxlConformanceData.LoadReferenceImageAsync(vector, CancellationToken);
        string input = await JxlConformanceData.GetInputPathAsync(vector, CancellationToken);

        await using FileStream stream = File.OpenRead(input);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        JxlConformanceComparer.AssertWithinThresholds(vector, expected, actual);
    }
}
