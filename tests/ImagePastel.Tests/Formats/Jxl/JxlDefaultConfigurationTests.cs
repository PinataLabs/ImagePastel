// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The public decoder through <see cref="Configuration.Default"/>, as a user reaches it (D15b, #311).
/// The expected values come from jxlinfo v0.12.0.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlDefaultConfigurationTests
{
    // delta_palette is a bare codestream; spot is a container (JXL , ftyp, jxll, jxlc).
    [Theory]
    [InlineData("delta_palette", 555, 751)]
    [InlineData("spot", 600, 400)]
    public void Load_WithDefaultConfiguration_DecodesBareCodestreamAndContainer(string id, int width, int height)
    {
        using Image image = Image.Load(InputPath(id), out IImageFormat format);

        Assert.Same(JxlFormat.Instance, format);
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
    }

    // Identify counts the coded bits of every channel: spot is 16-bit RGB, alpha and two spot colours.
    [Theory]
    [InlineData("delta_palette", 555, 751, 24)]
    [InlineData("spot", 600, 400, 96)]
    public void Identify_WithDefaultConfiguration_HandlesBareCodestreamAndContainer(string id, int width, int height, int bitsPerPixel)
    {
        IImageInfo info = Image.Identify(InputPath(id), out IImageFormat format);

        Assert.Same(JxlFormat.Instance, format);
        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
        Assert.Equal(bitsPerPixel, info.PixelType.BitsPerPixel);
    }

    [Theory]
    [InlineData("delta_palette")]
    [InlineData("spot")]
    public void DetectFormat_WithDefaultConfiguration_HandlesBareCodestreamAndContainer(string id)
        => Assert.Same(JxlFormat.Instance, Image.DetectFormat(InputPath(id)));

    [Fact]
    public void FindFormatByFileExtension_And_MimeType_GiveJxlFormat()
    {
        Assert.Same(JxlFormat.Instance, Configuration.Default.ImageFormatsManager.FindFormatByFileExtension("jxl"));
        Assert.Same(JxlFormat.Instance, Configuration.Default.ImageFormatsManager.FindFormatByMimeType("image/jxl"));
    }

    [Fact]
    public void GetJxlMetadata_Spot_GivesTheExtraChannelsAndTheEmbeddedProfile()
    {
        using Image image = Image.Load(InputPath("spot"));

        JxlMetadata jxl = image.Metadata.GetJxlMetadata();
        Assert.Equal(
            new[] { JxlExtraChannelType.Alpha, JxlExtraChannelType.SpotColor, JxlExtraChannelType.SpotColor },
            jxl.ExtraChannels.Select(c => c.Type));
        Assert.All(jxl.ExtraChannels, c => Assert.Equal(16, c.BitsPerSample));
        Assert.Equal(16, jxl.BitsPerSample);
        Assert.Equal(0, jxl.ExponentBitsPerSample);

        // The stream signals a 940-byte ICC profile, so there is no enumerated colour encoding.
        Assert.Null(jxl.ColorEncoding);
        Assert.Equal(940, jxl.EmbeddedIccProfile.ToByteArray().Length);

        // The frames are coalesced into one, which takes the name of the last layer.
        ImageFrame frame = Assert.Single(image.Frames);
        Assert.Equal("TEST", frame.Metadata.GetJxlFrameMetadata().Name);
        Assert.Equal(0u, frame.Metadata.GetJxlFrameMetadata().Duration);
    }

    [Fact]
    public void GetJxlMetadata_AnimationSpline_GivesTheTimingOfEveryFrame()
    {
        using Image image = Image.Load(InputPath("animation_spline"));

        JxlMetadata jxl = image.Metadata.GetJxlMetadata();
        Assert.Equal(100u, jxl.TicksPerSecondNumerator);
        Assert.Equal(1u, jxl.TicksPerSecondDenominator);
        Assert.Equal(0, jxl.LoopCount);
        Assert.Equal(JxlTransferFunction.Srgb, jxl.ColorEncoding.TransferFunction);

        // 60 frames of 20 ms each: 2 ticks at 100 ticks per second.
        Assert.Equal(60, image.Frames.Count);
        Assert.All(image.Frames, f => Assert.Equal(2u, f.Metadata.GetJxlFrameMetadata().Duration));
    }

    [Fact]
    public void MaxFrames_ThroughThePublicDecoder_StopsTheAnimation()
    {
        using Image image = Image.Load(InputPath("animation_spline"), new JxlDecoder { MaxFrames = 5 });

        Assert.Equal(5, image.Frames.Count);
    }

    [Fact]
    public void IgnoreMetadata_ThroughThePublicDecoder_DropsTheProfiles()
    {
        using Image kept = Image.Load(InputPath("spot"), new JxlDecoder());
        using Image ignored = Image.Load(InputPath("spot"), new JxlDecoder { IgnoreMetadata = true });

        Assert.NotNull(kept.Metadata.IccProfile);
        Assert.NotNull(kept.Metadata.GetJxlMetadata().EmbeddedIccProfile);
        Assert.Null(ignored.Metadata.IccProfile);
        Assert.Null(ignored.Metadata.GetJxlMetadata().EmbeddedIccProfile);
    }

    [Fact]
    public void ColorProfileConversion_ThroughThePublicDecoder_ConvertsToSrgb()
    {
        // custom_icc embeds an RGB profile that the colour engine supports; DecoderColorProfileConversionTests
        // checks the converted pixels against LittleCMS.
        string path = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", "custom_icc.jxl");

        using Image<Rgb24> asStored = Image.Load<Rgb24>(path, new JxlDecoder());
        using Image<Rgb24> converted = Image.Load<Rgb24>(path, new JxlDecoder { ColorProfileConversion = ColorProfileConversion.ToSrgb });

        Assert.NotNull(asStored.Metadata.IccProfile);
        Assert.Null(converted.Metadata.IccProfile);
        Assert.NotNull(converted.Metadata.GetJxlMetadata().EmbeddedIccProfile);
        Assert.NotEqual(Pixels(asStored), Pixels(converted));
    }

    // The default encoder is lossy (Distance 1, Effort Default), so the pixels are close, not equal.
    [Fact]
    public void Save_AsJxl_WithDefaultConfiguration_DecodesBack()
    {
        string path = Path.Combine(TestEnvironment.CreateOutputDirectory(nameof(JxlDefaultConfigurationTests)), "default.jxl");
        using (Image<Rgba32> image = new(17, 9, new Rgba32(200, 120, 40, 255)))
        {
            image.Save(path);
        }

        Assert.Same(JxlFormat.Instance, Image.DetectFormat(path));
        using Image<Rgba32> decoded = Image.Load<Rgba32>(path);
        Assert.Equal(17, decoded.Width);
        Assert.Equal(9, decoded.Height);
        Rgba32 pixel = decoded[8, 4];
        Assert.InRange(pixel.R, 195, 205);
        Assert.InRange(pixel.G, 115, 125);
        Assert.InRange(pixel.B, 35, 45);
        Assert.Equal(255, pixel.A);
    }

    private static string InputPath(string id) => JxlConformanceVector.Load(id).CommittedInputPath;

    private static Rgb24[] Pixels(Image<Rgb24> image)
    {
        Rgb24[] pixels = new Rgb24[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);
        return pixels;
    }
}
