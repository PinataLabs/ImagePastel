// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.TestUtilities.ReferenceCodecs;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;

/// <summary>
/// Checks the conformance harness itself against independent decoders, before any ImageSharp JPEG XL
/// code exists: if libjxl's own output passes, the reader, the comparer and the data are right.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlConformanceHarnessTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Magick.NET outputs integers, so it cannot hold the references' out-of-range samples:
    // both sides are clamped to [0, 1] before the comparison.
    [Theory]
    [InlineData("delta_palette", 3)]

    // 12-bit samples: this passes only if the reference decoder keeps more than 8 bits.
    [InlineData("alpha_nonpremultiplied", 4)]
    public async Task MagickDecode_PassesClampedThresholds(string id, int channels)
    {
        JxlConformanceVector vector = JxlConformanceVector.Load(id);
        NpyArray expected = await JxlConformanceData.LoadReferenceImageAsync(vector, CancellationToken);
        string input = await JxlConformanceData.GetInputPathAsync(vector, CancellationToken);

        await using FileStream stream = File.OpenRead(input);
        using Image<Rgba64> image = MagickReferenceDecoder.HighBitDepthInstance.Decode<Rgba64>(Configuration.Default, stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(image, channels);

        JxlConformanceComparer.AssertWithinThresholds(vector, expected, actual, JxlConformanceComparison.ClampToUnitRange);
    }

    [Theory]
    [InlineData("bike")]
    [InlineData("cafe")]
    [InlineData("progressive")]
    public async Task FetchedInput_MatchesPinnedHash(string id)
    {
        string path = await JxlConformanceData.GetInputPathAsync(JxlConformanceVector.Load(id), CancellationToken);

        // Hash the cached bytes too, not only the download, in case the cache was altered.
        Assert.Equal(JxlConformanceVector.Source.FetchedInputs[id], await JxlConformanceData.ComputeSha256Async(path, CancellationToken));
    }

    [Fact]
    public async Task DjxlDecode_PassesThresholds()
    {
        JxlConformanceVector vector = JxlConformanceVector.Load("delta_palette");
        NpyArray expected = await JxlConformanceData.LoadReferenceImageAsync(vector, CancellationToken);
        string output = Path.Combine(TestEnvironment.CreateOutputDirectory("Jxl"), "delta_palette.djxl.npy");

        await JxlTools.RunAsync("djxl", CancellationToken, vector.CommittedInputPath, output);

        JxlConformanceComparer.AssertWithinThresholds(vector, expected, NpyArray.Load(output));
    }
}
