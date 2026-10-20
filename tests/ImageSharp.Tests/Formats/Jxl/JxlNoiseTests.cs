// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Linq;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Noise (D12c): frames with the noise flag, whose noise the decoder makes from libjxl's generator,
/// seeded by the frame counters and the position of each tile, and adds after the upsampling. The
/// generator itself is checked in <see cref="JxlXorshift128PlusTests"/>, and the conformance vector
/// <c>noise</c> in <see cref="JxlConformanceDecoderTests"/>.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlNoiseTests
{
    private const int Width = 24;
    private const int Height = 16;

    // The largest strength, 1023/1024, at every intensity.
    private static readonly uint[] StrongLut = Enumerable.Repeat(1023u, 8).ToArray();

    private static readonly JxlTestPatchBlending ReplacePatch = new(JxlPatchBlendMode.Replace);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The generated files (Generated/README.md) against djxl v0.12.0's float32 output, within 1e-4 RMSE and
    // 4e-3 peak per channel. Measured on x64 against djxl with AVX2: the worst channel's RMSE and the peak.
    [Theory]
    [InlineData("noise_resampling_2x", 1)] // 1.4e-6, 4.2e-5
    [InlineData("noise_animation", 3)] // Worst frame: 1.5e-6, 4.1e-5
    public void DecodeToPlanes_MatchesDjxl(string name, int frames)
    {
        NpyArray expected = LoadReference(name);

        using FileStream stream = File.OpenRead(InputPath(name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        Assert.Equal(frames, JxlLossyComparer.AssertWithinThresholds(expected, actual).Length);
    }

    // Measured: largest difference 0.50 of one 8-bit step, which is the rounding.
    [Theory]
    [InlineData("noise_resampling_2x")]
    [InlineData("noise_animation")]
    public void Load_Rgb24_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = LoadReference(name);

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name));
        NpyArray actual = JxlConformanceImages.ToNpy(image, expected.Shape[3]);

        // Every displayed frame of the animation.
        JxlConformanceFrameResult[] results = JxlConformanceComparer.Measure(expected, actual, JxlConformanceComparison.ClampToUnitRange);
        Assert.Equal(expected.Shape[0], results.Length);
        foreach (JxlConformanceFrameResult result in results)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"Frame {result.Frame}: peak error {result.PeakError * 255:G3} of one 8-bit step");
            Assert.True(result.PeakError <= (1.0 / 255) + 1e-6, $"Frame {result.Frame}: peak error {result.PeakError * 255:G4} steps");
        }
    }

    // A noise look-up table whose entries are all 0 adds nothing (libjxl's HasAny), so the frame decodes
    // exactly as it does without the flag. The table is read, so the rest of the section still decodes.
    [Fact]
    public void DecodeToPlanes_ZeroNoiseLut_DecodesAsWithoutNoise()
    {
        JxlTestFrame plain = JxlTestFrame.Last(JxlTestLayers.Ramp);

        NpyArray expected = DecodeToNpy(new JxlTestLayers(Width, Height), plain);
        NpyArray actual = DecodeToNpy(new JxlTestLayers(Width, Height), plain with { NoiseLut = new uint[8] });

        Assert.Equal(expected.Shape, actual.Shape);
        Assert.Equal(expected.Data, actual.Data);
    }

    // The noise goes into the three colour planes only: the alpha, an extra channel, is as it was, and the
    // three noise planes do not reach the output. The same file gives the same noise each time.
    [Fact]
    public void DecodeToPlanes_ModularFrameWithNoise_ChangesOnlyTheColour()
    {
        JxlTestFrame plain = JxlTestFrame.Last(JxlTestLayers.Ramp);

        NpyArray expected = DecodeToNpy(new JxlTestLayers(Width, Height), plain);
        NpyArray actual = DecodeToNpy(new JxlTestLayers(Width, Height), plain with { NoiseLut = StrongLut });
        NpyArray again = DecodeToNpy(new JxlTestLayers(Width, Height), plain with { NoiseLut = StrongLut });

        Assert.Equal(new[] { 1, Height, Width, 4 }, actual.Shape);
        Assert.Equal(actual.Data, again.Data);
        int changed = 0;
        for (int i = 0; i < expected.Data.Length; i++)
        {
            if (i % 4 == 3)
            {
                Assert.Equal(expected.Data[i], actual.Data[i]);
            }
            else if (expected.Data[i] != actual.Data[i])
            {
                changed++;
            }
        }

        Assert.True(changed > expected.Data.Length / 2, $"Only {changed} colour samples changed");
    }

    // The patches run before the noise, while the pipeline also holds the noise planes, which the patches
    // stage must not take for extra channels. With a zero table, the frame is the frame with patches alone.
    [Fact]
    public void DecodeToPlanes_PatchesAndZeroNoiseLut_DecodesAsPatchesAlone()
    {
        JxlTestPatchDictionary patches = new(new JxlTestRefPatch(3, 2, 1, 4, 3, new JxlTestPatch(5, 6, ReplacePatch, ReplacePatch)));
        JxlTestFrame reference = new(JxlTestLayers.Ramp) { Type = JxlFrameType.ReferenceOnly, SaveAsReference = 3, SaveBeforeColorTransform = true };
        JxlTestFrame frame = JxlTestFrame.Last(JxlTestLayers.ColourA) with { Patches = patches };

        NpyArray expected = DecodeToNpy(new JxlTestLayers(Width, Height).Add(reference), frame);
        NpyArray actual = DecodeToNpy(new JxlTestLayers(Width, Height).Add(reference), frame with { NoiseLut = new uint[8] });

        Assert.Equal(expected.Shape, actual.Shape);
        Assert.Equal(expected.Data, actual.Data);
    }

    // A grey image that is not XYB has one colour plane. As in libjxl, which keeps three colour channels,
    // the noise is added to three copies of it, and the output takes the first.
    [Fact]
    public void DecodeToPlanes_GreyModularFrameWithNoise_KeepsOnePlane()
    {
        string path = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", "greyscale.jxl");
        JxlTestLayers plain = JxlTestLayers.FromFile(File.ReadAllBytes(path), out byte[] section);
        JxlTestLayers noisy = JxlTestLayers.FromFile(File.ReadAllBytes(path), out _);

        NpyArray expected = DecodeToNpy(plain, JxlTestFrame.Last(section));
        NpyArray actual = DecodeToNpy(noisy, JxlTestFrame.Last(section) with { NoiseLut = StrongLut });

        Assert.Equal(expected.Shape, actual.Shape);
        Assert.Equal(1, actual.Shape[3]);
        Assert.Contains(Enumerable.Range(0, expected.Data.Length), i => expected.Data[i] != actual.Data[i]);
    }

    private static NpyArray DecodeToNpy(JxlTestLayers layers, JxlTestFrame frame)
    {
        using MemoryStream stream = new(layers.Add(frame).ToArray());
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        return JxlConformanceImages.ToNpy(decoded);
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
