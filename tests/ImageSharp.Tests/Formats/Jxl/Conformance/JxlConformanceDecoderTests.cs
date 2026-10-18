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
