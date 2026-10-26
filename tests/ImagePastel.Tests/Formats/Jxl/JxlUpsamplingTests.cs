// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Upsampling (D10b): frames coded at a half, a quarter or an eighth of the image size, and extra
/// channels coded smaller than the colour, which the upsampling stage brings to the image size.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlUpsamplingTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Each output pixel is a weighted sum of the 5x5 input pixels around its source pixel, with one
    // kernel per output phase: 4, 16 or 64 kernels of 25 weights. The default weights are made so that
    // each kernel keeps a flat area flat (checked with the libjxl weights in the D10 research, to 6 digits).
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Kernel_DefaultWeights_EachPhaseSumsToOne(int shift)
    {
        int n = 1 << shift;
        float[] kernel = JxlUpsamplingStage.CreateKernel(new JxlCustomTransformData(), shift);

        Assert.Equal(n * n * 25, kernel.Length);
        for (int k = 0; k < n * n; k++)
        {
            double sum = 0;
            for (int i = 0; i < 25; i++)
            {
                sum += kernel[(k * 25) + i];
            }

            Assert.True(Math.Abs(sum - 1) < 1e-5, $"Phase {k} sums to {sum:G9}");
        }
    }

    // libjxl stores the weights of one quadrant's phases, over the triangle of the window on and above
    // its diagonal, and mirrors them: left to right, top to bottom, and across the diagonal.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Kernel_DefaultWeights_HasQuadrantSymmetry(int shift)
    {
        int n = 1 << shift;
        float[] kernel = JxlUpsamplingStage.CreateKernel(new JxlCustomTransformData(), shift);
        float K(int oy, int ox, int py, int px) => kernel[(((oy * n) + ox) * 25) + (py * 5) + px];

        for (int oy = 0; oy < n; oy++)
        {
            for (int ox = 0; ox < n; ox++)
            {
                for (int py = 0; py < 5; py++)
                {
                    for (int px = 0; px < 5; px++)
                    {
                        float w = K(oy, ox, py, px);
                        Assert.Equal(w, K(oy, n - 1 - ox, py, 4 - px));
                        Assert.Equal(w, K(n - 1 - oy, ox, 4 - py, px));
                        Assert.Equal(w, K(ox, oy, px, py));
                    }
                }
            }
        }
    }

    // A lossless 37x21 image upsampled 4x with custom nearest-neighbour weights in the image header: each
    // kernel has one weight of 1 and 24 of 0, so the output repeats each coded pixel in a 4x4 block,
    // exactly. The coded pixels are those of the source image, which is the reference.
    [Fact]
    public void Decode_NearestNeighbourCustomWeights_RepeatsEachCodedPixel()
    {
        using Image<Rgb24> source = Image.Load<Rgb24>(Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", "upsampling_4x_nn_lossless.png"));
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath("upsampling_4x_nn_lossless"));

        Assert.Equal(4 * source.Width, image.Width);
        Assert.Equal(4 * source.Height, image.Height);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Assert.Equal(source[x / 4, y / 4], image[x, y]);
            }
        }
    }

    // The float planes are the coded samples, so they match djxl's exactly.
    [Fact]
    public void DecodeToPlanes_NearestNeighbourCustomWeights_MatchesDjxlExactly()
    {
        NpyArray expected = LoadReference("upsampling_4x_nn_lossless");

        using FileStream stream = File.OpenRead(InputPath("upsampling_4x_nn_lossless"));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        Assert.Equal(expected.Shape, actual.Shape);
        Assert.Equal(expected.Data, actual.Data);
    }

    // The generated files (Generated/README.md) against djxl v0.12.0's float32 output, within 1e-4 RMSE and
    // 4e-3 peak per channel. Measured on x64 against djxl with AVX2: the worst channel's RMSE and the peak.
    [Theory]
    [InlineData("upsampling_2x_xyb")] // 5.9e-7, 3.5e-6
    [InlineData("upsampling_4x_xyb")] // 4.4e-7, 3.1e-6
    [InlineData("upsampling_8x_xyb")] // 5.4e-7, 3.3e-6
    [InlineData("upsampling_2x_xyb_nolf")] // 5.0e-7, 5.3e-6
    [InlineData("upsampling_2x_lossless")] // 1.9e-8, 1.2e-7
    [InlineData("upsampling_4x_lossless")] // 1.8e-8, 1.2e-7
    [InlineData("upsampling_8x_lossless")] // 1.8e-8, 1.2e-7
    [InlineData("upsampling_2x_custom")] // 5.2e-7, 2.9e-6
    [InlineData("upsampling_4x_custom")] // 3.9e-7, 2.9e-6
    [InlineData("upsampling_8x_custom")] // 4.2e-7, 2.9e-6
    [InlineData("upsampling_2x_alpha_late")] // 5.7e-7, 3.6e-6
    [InlineData("upsampling_2x_alpha_4x")] // 5.9e-7, 3.5e-6
    [InlineData("upsampling_4x_alpha_8x")] // 4.4e-7, 3.1e-6
    [InlineData("upsampling_1x_alpha_2x")] // 3.7e-7, 8.9e-6
    public void DecodeToPlanes_MatchesDjxl(string name)
    {
        NpyArray expected = LoadReference(name);

        using FileStream stream = File.OpenRead(InputPath(name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, actual));
    }

    [Theory]
    [InlineData("upsampling_2x_xyb")]
    [InlineData("upsampling_4x_xyb")]
    [InlineData("upsampling_8x_xyb")]
    [InlineData("upsampling_2x_xyb_nolf")]
    [InlineData("upsampling_2x_lossless")]
    [InlineData("upsampling_4x_lossless")]
    [InlineData("upsampling_8x_lossless")]
    [InlineData("upsampling_2x_custom")]
    [InlineData("upsampling_4x_custom")]
    [InlineData("upsampling_8x_custom")]
    [InlineData("upsampling_2x_alpha_late")]
    [InlineData("upsampling_2x_alpha_4x")]
    [InlineData("upsampling_4x_alpha_8x")]
    [InlineData("upsampling_1x_alpha_2x")]
    public void Load_Rgba32_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = LoadReference(name);

        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), InputPath(name));
        NpyArray actual = JxlConformanceImages.ToNpy(image, expected.Shape[3]);

        JxlConformanceImages.AssertWithinOneStepOfTheClampedReference(expected, actual);
    }

    // Patches on an upsampled frame: the patches are drawn at the coded size, before the upsampling, and
    // the extra channels with them when upsampled late (as the colour) or after their own upsampling when
    // the colour is not upsampled. A 4x3 piece of the Ramp layer, whose samples all differ, is drawn at
    // (2, 1) of the coded frame, over the constant ColourA layer.
    [Theory]
    [InlineData(2u, 2u)]
    [InlineData(4u, 4u)]
    [InlineData(1u, 2u)]
    [InlineData(1u, 8u)]
    public async Task DecodeToPlanes_PatchesOnAnUpsampledFrame_MatchDjxl(uint upsampling, uint extraChannelUpsampling)
    {
        JxlTestPatchBlending replace = new(JxlPatchBlendMode.Replace);
        JxlTestRefPatch refPatch = new(3, 5, 4, 4, 3, new JxlTestPatch(2, 1, replace, replace));
        byte[] codestream = new JxlTestLayers(24, 16)
            .Add(new JxlTestFrame(JxlTestLayers.Ramp) { Type = JxlFrameType.ReferenceOnly, SaveAsReference = 3, SaveBeforeColorTransform = true })
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { Patches = new JxlTestPatchDictionary(refPatch), Upsampling = upsampling, ExtraChannelUpsampling = extraChannelUpsampling })
            .ToArray();
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlUpsamplingTests));
        string name = $"patches_{upsampling}x_alpha_{extraChannelUpsampling}x";
        string input = Path.Combine(directory, name + ".jxl");
        string output = Path.Combine(directory, name + ".npy");
        await File.WriteAllBytesAsync(input, codestream, CancellationToken);

        await JxlTools.RunAsync("djxl", CancellationToken, input, output);

        NpyArray expected = NpyArray.Load(output);
        using MemoryStream stream = new(codestream);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded)));
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
