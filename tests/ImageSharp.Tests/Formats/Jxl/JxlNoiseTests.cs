// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    [InlineData("vardct_xyb_patches_noise", 1)] // Patches and noise (D12d): 5.8e-7, 1.4e-5
    public void DecodeToPlanes_MatchesDjxl(string name, int frames)
    {
        NpyArray expected = LoadReference(name);

        using FileStream stream = File.OpenRead(InputPath(name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        JxlConformanceFrameResult[] results = JxlLossyComparer.AssertWithinThresholds(expected, actual);
        foreach (JxlConformanceFrameResult result in results)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"Frame {result.Frame}: worst channel RMSE {result.WorstChannelRmse:G3}, peak {result.PeakError:G3}");
        }

        Assert.Equal(frames, results.Length);
    }

    // Measured: largest difference 0.50 of one 8-bit step, which is the rounding.
    [Theory]
    [InlineData("noise_resampling_2x")]
    [InlineData("noise_animation")]
    [InlineData("vardct_xyb_patches_noise")]
    [InlineData("vardct_grey_noise")]
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

    // The file cjxl made with --patches=1 --photon_noise_iso=3200 has both features on its displayed frame.
    // A Modular reference-only frame holds the glyphs; cjxl sets the noise flag on it too, so its noise,
    // seeded by the non-visible frame counter, is saved with the glyphs. Then the VarDCT frame pastes them
    // with patches and adds noise of its own over the whole frame.
    [Fact]
    public void PatchesAndNoise_IsAReferenceFrameThenAVarDctFrameWithPatchesAndNoise()
    {
        List<JxlFrameHeader> frames = JxlCodestreamFrames.Read(File.ReadAllBytes(InputPath("vardct_xyb_patches_noise")));

        Assert.Equal(2, frames.Count);
        Assert.Equal(JxlFrameType.ReferenceOnly, frames[0].FrameType);
        Assert.Equal(JxlFrameEncoding.Modular, frames[0].Encoding);
        Assert.Equal(JxlFrameFlags.Noise, frames[0].Flags & JxlFrameFlags.Noise);
        Assert.Equal(JxlFrameEncoding.VarDct, frames[1].Encoding);
        Assert.Equal(JxlFrameFlags.Patches | JxlFrameFlags.Noise, frames[1].Flags & (JxlFrameFlags.Patches | JxlFrameFlags.Noise));
        Assert.True(frames[1].IsLast);
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

    // vardct_grey_noise is a grey image coded without XYB (cjxl --disable_perceptual_optimizations), so its
    // VarDCT frame has three decoded colour planes that differ by their quantization. libjxl's add-noise
    // stage takes the noise strength from the first two, so the decoder keeps the decoded second plane up
    // to the noise. A decoder that copies the first plane into the others for the noise misses djxl by an
    // RMSE of 2.5e-5 (peak 2.4e-4): within the lossy threshold, so the bound here is tighter.
    // Measured: worst channel RMSE 1.2e-7, peak 1.0e-6.
    [Fact]
    public void DecodeToPlanes_GreyVarDctFrameNotXyb_TakesTheNoiseStrengthFromItsDecodedPlanes()
    {
        NpyArray expected = LoadReference("vardct_grey_noise");

        using FileStream stream = File.OpenRead(InputPath("vardct_grey_noise"));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        JxlConformanceFrameResult result = Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded)));

        TestContext.Current.TestOutputHelper?.WriteLine($"Worst channel RMSE {result.WorstChannelRmse:G3}, peak {result.PeakError:G3}");
        Assert.True(result.WorstChannelRmse <= 4e-6, $"Worst channel RMSE {result.WorstChannelRmse:G3}");
    }

    // A Modular frame of a grey image that is not XYB, with noise, against djxl v0.12.0. libjxl copies the
    // grey channel into three colour channels; the splines then add their X, Y and B colours to them and
    // the patches blend each, and the add-noise stage reads the first two. "splines": a curve whose X and Y
    // colours differ, so the planes differ where it is drawn. "patches": a reference frame of the same image,
    // pasted over the frame. A decoder that adds only the X colour and copies it into the other planes for
    // the noise misses djxl on "splines" by an RMSE of 5.4e-5 (peak 1.2e-3), within the lossy threshold, so
    // the bound here is tighter.
    // Measured: worst channel RMSE 1.5e-8, peak 1.2e-7 (splines); 1.3e-8, 1.2e-7 (patches).
    [Theory]
    [InlineData("splines")]
    [InlineData("patches")]
    public async Task DecodeToPlanes_GreyModularFrameWithNoise_MatchesDjxl(string name)
    {
        string path = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", "greyscale.jxl");
        JxlTestLayers layers = JxlTestLayers.FromFile(File.ReadAllBytes(path), out byte[] section);
        JxlTestFrame frame = JxlTestFrame.Last(section) with { NoiseLut = new uint[] { 0, 40, 80, 120, 160, 200, 240, 280 } };
        if (name == "splines")
        {
            frame = frame with
            {
                Splines = new JxlTestSplines(new JxlTestSpline(6, 9, (12, 6), (-3, 9), (-6, -18))
                {
                    ColorX = new[] { 40, -10, 5 },
                    ColorY = new[] { 6, 2, -1 },
                    ColorB = new[] { -8, 0, 4 },
                    Sigma = new[] { 6, 1 },
                }),
            };
        }
        else
        {
            layers.Add(new JxlTestFrame(section) { Type = JxlFrameType.ReferenceOnly, SaveAsReference = 1, SaveBeforeColorTransform = true });
            frame = frame with { Patches = new JxlTestPatchDictionary(new JxlTestRefPatch(1, 2, 3, 20, 12, new JxlTestPatch(30, 40, new JxlTestPatchBlending(JxlPatchBlendMode.Add)))) };
        }

        byte[] codestream = layers.Add(frame).ToArray();
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlNoiseTests));
        string input = Path.Combine(directory, "grey_" + name + ".jxl");
        string output = Path.Combine(directory, "grey_" + name + ".npy");
        await File.WriteAllBytesAsync(input, codestream, CancellationToken);

        await JxlTools.RunAsync("djxl", CancellationToken, input, output);

        NpyArray expected = NpyArray.Load(output);
        using MemoryStream stream = new(codestream);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        JxlConformanceFrameResult result = Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded)));
        TestContext.Current.TestOutputHelper?.WriteLine($"Worst channel RMSE {result.WorstChannelRmse:G3}, peak {result.PeakError:G3}");
        Assert.True(result.WorstChannelRmse <= 4e-6, $"Worst channel RMSE {result.WorstChannelRmse:G3}");
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
