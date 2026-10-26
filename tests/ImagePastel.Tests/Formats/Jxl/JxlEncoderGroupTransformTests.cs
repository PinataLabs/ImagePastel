// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The per-group transforms of lossless Default, as libjxl effort 5 runs them: the RCT search and the
/// local palette. Each test reads the encoder's choice back through the decoder's parse. An image in one
/// group has its transforms in the global stream; a larger one has them in each group's stream.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderGroupTransformTests
{
    private static JxlEncoder LosslessDefault => JxlEncoderRoundTrip.Lossless(JxlEffort.Default);

    [Fact]
    public void Encode_DefaultPhotoInOneGroup_WritesAnRct()
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        List<JxlTransform> transforms = JxlEncodedFrame.ReadGlobalTransforms(JxlEncoderRoundTrip.Encode(image, LosslessDefault));

        // cjxl -e 5 (v0.12.0) picks RCT 10 for this crop too.
        JxlTransform rct = Assert.Single(transforms, t => t.Id == JxlTransformId.Rct);
        Assert.Equal(10u, rct.RctType);
    }

    [Fact]
    public void Encode_DefaultPhotoOfSeveralGroups_WritesAnRctOnEachGroupWithoutAColourPalette()
    {
        // Bike is 6 groups. cjxl -e 5 (v0.12.0) gives one of them a palette of 22 colours, so that group
        // has one colour channel left and no RCT; it gives the other five RCT 10, as this encoder does.
        using Image<Rgb24> image = Image.Load<Rgb24>(TestFile.GetInputFileFullPath(TestImages.Png.Bike));

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, LosslessDefault);

        Assert.DoesNotContain(JxlEncodedFrame.ReadGlobalTransforms(encoded), t => t.Id == JxlTransformId.Rct);
        List<List<JxlTransform>> groups = JxlEncodedFrame.ReadGroupTransforms(encoded);
        Assert.Equal(6, groups.Count);
        List<List<JxlTransform>> withoutColourPalette = groups.FindAll(transforms => !transforms.Exists(t => t.Id == JxlTransformId.Palette && t.NumC == 3));
        Assert.Equal(5, withoutColourPalette.Count);
        Assert.All(withoutColourPalette, transforms => Assert.Equal(10u, Assert.Single(transforms, t => t.Id == JxlTransformId.Rct).RctType));
    }

    // libjxl turns the per-group RCT search off when the image has no spare bit, as float32 has none.
    [Fact]
    public void Encode_DefaultFloat_WritesNoRct()
    {
        using Image<RgbaVector> image = JxlEncoderRoundTrip.CreateImage<RgbaVector>("gradient", 64, 64);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, LosslessDefault);

        Assert.DoesNotContain(JxlEncodedFrame.ReadGlobalTransforms(encoded), t => t.Id == JxlTransformId.Rct);
    }

    // At Thorough the palette's colour cap comes from each group's estimated cost, as at libjxl effort 7.
    [Theory]
    [InlineData(nameof(JxlEffort.Default), nameof(Rgb24), "group-icons", 3)]
    [InlineData(nameof(JxlEffort.Default), nameof(Rgba32), "translucent-group-icons", 4)]
    [InlineData(nameof(JxlEffort.Thorough), nameof(Rgb24), "group-icons", 3)]
    [InlineData(nameof(JxlEffort.Thorough), nameof(Rgba32), "translucent-group-icons", 4)]
    public void Encode_GroupsOfFewColours_WritesALocalPaletteOnEachGroup(string effort, string pixelType, string pattern, int expectedChannels)
    {
        // Four groups of 300 colours each: too many colours in all for the global palette. A channel
        // palette (here on alpha) may still apply to the whole image.
        byte[] encoded = JxlPixelTypes.Call(pixelType, new EncodePattern(pattern, 512, 512, JxlEncoderRoundTrip.Lossless(Enum.Parse<JxlEffort>(effort))));

        Assert.DoesNotContain(JxlEncodedFrame.ReadGlobalTransforms(encoded), t => t.Id == JxlTransformId.Palette && t.NumC > 1);
        List<List<JxlTransform>> groups = JxlEncodedFrame.ReadGroupTransforms(encoded);
        Assert.Equal(4, groups.Count);
        Assert.All(groups, transforms =>
        {
            JxlTransform palette = transforms[0];
            Assert.Equal(JxlTransformId.Palette, palette.Id);
            Assert.Equal(0u, palette.BeginC);
            Assert.Equal((uint)expectedChannels, palette.NumC);
            Assert.InRange(palette.NbColors, 2u, 300u);
        });
    }

    [Theory]
    [InlineData(64, 64)]
    [InlineData(300, 513)]
    public void Encode_DefaultGrey_WritesNoRct(int width, int height)
    {
        using Image<L8> image = JxlEncoderRoundTrip.CreateImage<L8>("noise", width, height);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, LosslessDefault);

        Assert.DoesNotContain(JxlEncodedFrame.ReadGlobalTransforms(encoded), t => t.Id == JxlTransformId.Rct);
        Assert.All(JxlEncodedFrame.ReadGroupTransforms(encoded), transforms => Assert.DoesNotContain(transforms, t => t.Id == JxlTransformId.Rct));
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
