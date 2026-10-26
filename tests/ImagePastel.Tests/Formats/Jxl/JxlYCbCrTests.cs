// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The YCbCr colour transform (D10c), on 4:4:4 frames. The generated files are Modular YCbCr frames
/// written by <c>jxl_from_tree</c>, which need neither VarDCT nor chroma upsampling. The hand-built frames
/// check the channel order and the constants, and grey images (and JxlGreyYCbCrTests the grey frames that
/// need three planes before the colour transform). The 4:4:4 JPEG transcodes, VarDCT YCbCr
/// frames, are conformance vectors (<see cref="Conformance.JxlConformanceDecoderTests"/>).
/// </summary>
[Trait("Format", "Jxl")]
public class JxlYCbCrTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Measured on x64 against djxl v0.12.0 (AVX2): worst channel RMSE 1.5e-8 (modular_ycbcr) and 1.6e-8
    // (modular_ycbcr_alpha), peak 1.2e-7 for both.
    [Theory]
    [InlineData("modular_ycbcr")]
    [InlineData("modular_ycbcr_alpha")]
    public void DecodeToPlanes_MatchesDjxl(string name)
    {
        NpyArray expected = LoadReference(name);

        using FileStream stream = File.OpenRead(InputPath(name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);

        Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded)));
    }

    [Theory]
    [InlineData("modular_ycbcr")]
    [InlineData("modular_ycbcr_alpha")]
    public void Load_Rgb24_IsWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = LoadReference(name);

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name));
        NpyArray actual = JxlConformanceImages.ToNpy(image, 3);

        // The reference may have alpha after R, G and B, which Rgb24 drops.
        int channels = expected.Shape[3];
        int pixels = actual.Shape[1] * actual.Shape[2];
        Assert.Equal(expected.Data.Length / channels, pixels);
        double worst = 0;
        for (int i = 0; i < pixels; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                double e = Math.Clamp(expected.Data[(i * channels) + c], 0F, 1F);
                worst = Math.Max(worst, Math.Abs(actual.Data[(i * 3) + c] - e) * 255);
            }
        }

        Assert.True(worst <= 1 + 1e-4, $"Largest difference {worst:G4} 8-bit steps");
    }

    // A YCbCr frame whose section codes Cb = 200, Y = 100, Cr = 50 and alpha 128, in 8-bit samples. JFIF's
    // full-range conversion (ITU-T T.871 clause 7), with Y offset by 128:
    // R = 228 + 1.402 Cr = 298.1, G = 228 - 0.344136 Cb - 0.714136 Cr = 123.465929, B = 228 + 1.772 Cb = 582.4.
    // Alpha is not converted.
    [Fact]
    public void DecodeToPlanes_YCbCrFrame_GivesRgb()
    {
        JxlTestLayers layers = new JxlTestLayers(16, 16).Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { YCbCr = true });

        using JxlDecodedImage decoded = DecodeToPlanes(layers.ToArray());

        JxlDecodedFrame frame = Assert.Single(decoded.Frames);
        Assert.Equal(4, frame.Planes.Count);
        float[] expected = { 1.16901961F, 0.48418011F, 2.28392157F, 128F / 255 };
        for (int c = 0; c < expected.Length; c++)
        {
            Assert.Equal(expected[c], frame.Planes[c][3, 11], 1e-5F);
        }
    }

    // The image header of the generated 8-bit grey file, with no alpha, and a YCbCr frame whose section
    // codes Cb = 200, Y = 100 and Cr = 50. libjxl decodes three channels for a grey image whose frame has
    // a colour transform, converts them, and outputs R: (Y + 128 + 1.402 Cr) / 255 = 298.1 / 255. Taking
    // the grey plane before the colour transform would give Cb, 200 / 255.
    [Fact]
    public void DecodeToPlanes_GreyImageWithAYCbCrFrame_GivesR()
    {
        JxlTestLayers layers = GreyLayers().Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { YCbCr = true });

        using JxlDecodedImage decoded = DecodeToPlanes(layers.ToArray());

        Buffer2D<float> grey = Assert.Single(Assert.Single(decoded.Frames).Planes);
        for (int y = 0; y < decoded.Height; y++)
        {
            for (int x = 0; x < decoded.Width; x++)
            {
                Assert.Equal(298.1F / 255, grey[x, y], 1e-6F);
            }
        }
    }

    // A grey YCbCr frame saved after its colour transform keeps one plane, R, which the next frame adds to.
    [Fact]
    public void DecodeToPlanes_GreyYCbCrFrameSavedAfterTheColourTransform_IsReferencedAsR()
    {
        JxlTestLayers layers = GreyLayers()
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { YCbCr = true, SaveAsReference = 1 })
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { YCbCr = true, Color = new(JxlBlendMode.Add, Source: 1) });

        using JxlDecodedImage decoded = DecodeToPlanes(layers.ToArray());

        Buffer2D<float> grey = Assert.Single(Assert.Single(decoded.Frames).Planes);
        Assert.Equal(2 * 298.1F / 255, grey[5, 7], 1e-6F);
    }

    // The generated 64 x 64 8-bit grey file's image header, with no frame.
    private static JxlTestLayers GreyLayers()
        => JxlTestLayers.FromFile(File.ReadAllBytes(Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", "greyscale.jxl")), out _);

    private static JxlDecodedImage DecodeToPlanes(byte[] codestream)
    {
        using MemoryStream stream = new(codestream);
        return new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
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
