// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Modular;
using SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The effort levels: each maps to a libjxl speed tier, and the encoder's choices at each tier are read
/// back through the decoder's parse.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderEffortTests
{
    // SHA-256 of the Fastest output that E1 (feat/jxl-e2 at a00deefef) wrote for each case. Fastest must
    // not change while the higher efforts are added.
    [Theory]
    [InlineData(nameof(L8), "gradient", 17, 23, "DD7C8882D0F8F4A0D8791A495C8C360DD353A04C0C585711F52933C32C419944")]
    [InlineData(nameof(Rgb24), "noise", 256, 256, "166916BFB855961AC5530FF8CE9BC07C948F587E5B0F2EEEF2665017893660D1")]
    [InlineData(nameof(Rgb24), "icon", 64, 64, "5D115BCD280CB9C23101D025F6E4897259817A00EA2AF7A8EE7742EB4D827CB1")]
    [InlineData(nameof(Rgb24), "gradient", 300, 513, "D5A027E86DD98D5E20CD472EA8109EA2D28B1430224200A3896677FF07B2C4E5")]
    [InlineData(nameof(L16), "noise", 17, 23, "73F19946E7D91980952459FCE73EB44C9B8C507427CC25DE4EBF02BB2B08179E")]
    [InlineData(nameof(Rgba64), "translucent", 256, 256, "A43FF2217BE77CEFD00ED786CB89FEFA8E901D7BC438ACE952EB290EE05F3C2F")]
    [InlineData(nameof(RgbaVector), "translucent", 17, 23, "0F95453F6378A0851578D73E97D94F1434ED86CFB74484BEEDFBF36C86FE7044")]
    [InlineData(nameof(La16), "translucent-icon", 64, 64, "8E427C5E09A89E9F26AE6782AAB86A5BA23B1AA59C408FB04FB691F51F47753C")]
    public void Encode_Fastest_IsByteIdenticalToE1(string pixelType, string pattern, int width, int height, string expectedSha256)
    {
        byte[] encoded = JxlPixelTypes.Call(pixelType, new EncodePattern(pattern, width, height, JxlEncoderRoundTrip.LosslessFastest));

        Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(encoded)));
    }

    [Fact]
    public void Encode_FastestPhoto_IsByteIdenticalToE1()
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, JxlEncoderRoundTrip.LosslessFastest);

        Assert.Equal("CAC3BA22594C71A7E1E139DCD945BEC08190014E6B113D23D6F39DD288187A26", Convert.ToHexString(SHA256.HashData(encoded)));
    }

    [Fact]
    public void Encode_DefaultPhoto_HasALearnedTree()
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        List<JxlPropertyDecisionNode> tree = JxlEncodedFrame.ReadGlobalTree(JxlEncoderRoundTrip.Encode(image, JxlEncoderRoundTrip.Lossless(JxlEffort.Default)));

        Assert.True(tree.Count(n => n.Property == -1) > 1, $"{tree.Count} nodes");
        Assert.True(tree.Where(n => n.Property >= 0).Select(n => n.Property).Distinct().Count() > 1, "The splits use one property");
    }

    // libjxl effort 5 learns the tree over properties 0, 15, 9 and 10 (with 1, the group, for frames
    // of 30 streams or more); effort 7 adds 11 (NW - N), 12 (N - NE) and 13 (N - NN).
    [Theory]
    [InlineData(nameof(JxlEffort.Default), false)]
    [InlineData(nameof(JxlEffort.Thorough), true)]
    public void Encode_Photo_SplitsOnTheEffort7PropertiesOnlyAtThorough(string effort, bool expectEffort7Properties)
    {
        int[] effort7Properties = { 11, 12, 13 };
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        List<JxlPropertyDecisionNode> tree = JxlEncodedFrame.ReadGlobalTree(JxlEncoderRoundTrip.Encode(image, JxlEncoderRoundTrip.Lossless(Enum.Parse<JxlEffort>(effort))));

        List<int> properties = tree.Where(n => n.Property >= 0).Select(n => (int)n.Property).Distinct().Order().ToList();
        Assert.Equal(expectEffort7Properties, properties.Intersect(effort7Properties).Any());
        Assert.All(properties, p => Assert.Contains(p, expectEffort7Properties ? new[] { 0, 9, 10, 11, 12, 13, 15 } : new[] { 0, 9, 10, 15 }));
    }

    [Fact]
    public void Encode_FastestPhoto_HasTheWpFixedDcTree()
    {
        // libjxl's kWPFixedDC split values; a photo of 2^14 pixels or more gets every one of them.
        int[] wpFixedDcCutoffs =
        {
            -500, -392, -255, -191, -127, -95, -63, -47, -31, -23, -15, -11, -7, -4, -3, -1, 0,
            1, 3, 5, 7, 11, 15, 23, 31, 47, 63, 95, 127, 191, 255, 392, 500,
        };
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        List<JxlPropertyDecisionNode> tree = JxlEncodedFrame.ReadGlobalTree(JxlEncoderRoundTrip.Encode(image, JxlEncoderRoundTrip.LosslessFastest));

        List<JxlPropertyDecisionNode> splits = tree.Where(n => n.Property >= 0).ToList();
        Assert.All(splits, n => Assert.Equal(JxlModularPredict.WPProp, n.Property));
        Assert.Equal(wpFixedDcCutoffs, splits.Select(n => n.SplitVal).Order());
        Assert.All(tree.Where(n => n.Property == -1), n => Assert.Equal(JxlPredictor.Weighted, n.Predictor));
    }

    // libjxl caps the global palette at cost * 0.0005 + pixels / 128 + 128 colours, and at most 1024.
    // Below effort 7 the cost is a guess of 4.8 bits per pixel for 8-bit RGB, which caps a 256x256 image
    // at 797 colours; from effort 7 it is EstimateCost, about 1.5 Mbit for noise, which caps it at 1024.
    [Theory]
    [InlineData(nameof(JxlEffort.Default), false)]
    [InlineData(nameof(JxlEffort.Thorough), true)]
    public void Encode_NoiseOf900Colours_HasAGlobalPaletteOnlyAtThorough(string effort, bool expectPalette)
    {
        using Image<Rgb24> image = CreateNoiseOfColours(256, 256, 900);
        int colours = CountColours(image);
        Assert.InRange(colours, 798, 1024);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, JxlEncoderRoundTrip.Lossless(Enum.Parse<JxlEffort>(effort)));

        List<JxlTransform> palettes = JxlEncodedFrame.ReadGlobalTransforms(encoded).Where(t => t.Id == JxlTransformId.Palette && t.NumC == 3).ToList();
        if (expectPalette)
        {
            Assert.Equal((uint)colours, Assert.Single(palettes).NbColors);
        }
        else
        {
            Assert.Empty(palettes);
        }
    }

    // More colours than the cap's 1024 take no palette at Thorough, whatever the cost estimate.
    [Fact]
    public void Encode_ThoroughNoiseOfManyColours_WritesNoPalette()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", 256, 256);
        Assert.True(CountColours(image) > 1024);

        List<JxlTransform> transforms = JxlEncodedFrame.ReadGlobalTransforms(JxlEncoderRoundTrip.Encode(image, JxlEncoderRoundTrip.Lossless(JxlEffort.Thorough)));

        Assert.DoesNotContain(transforms, t => t.Id == JxlTransformId.Palette);
    }

    [Fact]
    public void Encode_ThoroughEightColourIcon_WritesAGlobalPaletteOfEightColours()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("icon", 64, 64);

        List<JxlTransform> transforms = JxlEncodedFrame.ReadGlobalTransforms(JxlEncoderRoundTrip.Encode(image, JxlEncoderRoundTrip.Lossless(JxlEffort.Thorough)));

        JxlTransform palette = Assert.Single(transforms, t => t.Id == JxlTransformId.Palette);
        Assert.Equal(3u, palette.NumC);
        Assert.Equal(8u, palette.NbColors);
    }

    // The spec checks the totals over the corpus, not each image.
    [Fact]
    public void Encode_Corpus_TotalSizeFallsWithEachEffort()
    {
        long fastest = 0;
        long @default = 0;
        long thorough = 0;
        foreach (ITheoryDataRow row in JxlEncoderRoundTrip.CorpusCases)
        {
            object[] data = row.GetData();
            if ((string)data[0] != nameof(JxlEffort.Fastest))
            {
                continue;
            }

            string file = (string)data[1];
            int[] sizes = JxlPixelTypes.Call((string)data[2], new EncodeFile(file, JxlEncoderRoundTrip.LosslessFastest, JxlEncoderRoundTrip.Lossless(JxlEffort.Default), JxlEncoderRoundTrip.Lossless(JxlEffort.Thorough)));
            TestContext.Current.TestOutputHelper?.WriteLine($"{file}: Fastest {sizes[0]} B, Default {sizes[1]} B, Thorough {sizes[2]} B");
            fastest += sizes[0];
            @default += sizes[1];
            thorough += sizes[2];
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Total: Fastest {fastest} B, Default {@default} B, Thorough {thorough} B");
        Assert.True(@default < fastest, $"Default {@default} B, Fastest {fastest} B");
        Assert.True(thorough <= @default, $"Thorough {thorough} B, Default {@default} B");
    }

    // Without an RCT, lossless Default wrote these RGB photos larger than Fastest, which has YCoCg.
    [Fact]
    public void Encode_RgbPhotos_AreSmallerAtDefaultThanAtFastest()
    {
        using Image<Rgb24> photo = JxlEncoderTransformTests.LoadPhoto();
        int[] photoSizes = new EncodeFile(null, JxlEncoderRoundTrip.LosslessFastest, JxlEncoderRoundTrip.Lossless(JxlEffort.Default)).Encode(photo);
        int[] bikeSizes = JxlPixelTypes.Call(nameof(Rgb24), new EncodeFile(TestImages.Png.Bike, JxlEncoderRoundTrip.LosslessFastest, JxlEncoderRoundTrip.Lossless(JxlEffort.Default)));

        Assert.True(photoSizes[1] < photoSizes[0], $"Photo: Default {photoSizes[1]} B, Fastest {photoSizes[0]} B");
        Assert.True(bikeSizes[1] < bikeSizes[0], $"Bike: Default {bikeSizes[1]} B, Fastest {bikeSizes[0]} B");
    }

    // Each pixel is one of the first `colours` of a fixed-seed list of random 8-bit colours.
    private static Image<Rgb24> CreateNoiseOfColours(int width, int height, int colours)
    {
        Random random = new(colours);
        Rgb24[] palette = new Rgb24[colours];
        for (int i = 0; i < colours; i++)
        {
            palette[i] = new Rgb24((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
        }

        Image<Rgb24> image = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image[x, y] = palette[random.Next(colours)];
            }
        }

        return image;
    }

    private static int CountColours(Image<Rgb24> image)
    {
        HashSet<Rgb24> colours = new();
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                colours.Add(image[x, y]);
            }
        }

        return colours.Count;
    }

    private sealed record EncodeFile(string File, params JxlEncoder[] Encoders) : IJxlPixelTypeCall<int[]>
    {
        public int[] Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> image = Image.Load<TPixel>(TestFile.GetInputFileFullPath(this.File));
            return this.Encode(image);
        }

        public int[] Encode<TPixel>(Image<TPixel> image)
            where TPixel : unmanaged, IPixel<TPixel>
            => this.Encoders.Select(encoder => JxlEncoderRoundTrip.Encode(image, encoder).Length).ToArray();
    }

    private sealed record EncodePattern(string Pattern, int Width, int Height, JxlEncoder Encoder) : IJxlPixelTypeCall<byte[]>
    {
        public byte[] Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> image = JxlEncoderRoundTrip.CreateImage<TPixel>(this.Pattern, this.Width, this.Height);
            return JxlEncoderRoundTrip.Encode(image, this.Encoder);
        }
    }
}
