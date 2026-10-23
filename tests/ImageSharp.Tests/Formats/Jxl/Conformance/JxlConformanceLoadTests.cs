// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;

/// <summary>
/// The lossy conformance vectors through the public entry points and <see cref="Configuration.Default"/>, which
/// registers JPEG XL: <c>Image.Load</c> picks the pixel type and
/// gives pixels within one quantisation step of the clamped reference (docs/jxl/spec.md, "Tolerance for lossy
/// output"), and <c>Identify</c> reports the size, channels and colour encoding.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlConformanceLoadTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    /// <summary>How the reference planes relate to the pixels of an <see cref="Image{TPixel}"/>.</summary>
    public enum ReferenceSpace
    {
        /// <summary>The planes hold the pixels' encoding.</summary>
        AsCoded,

        /// <summary>
        /// The planes are linear sRGB, as djxl renders an XYB image with an ICC profile; the pixels are sRGB (ADR 0002).
        /// </summary>
        LinearSrgb,

        /// <summary>
        /// The colour planes are premultiplied by alpha; the pixels hold straight alpha. Dividing by a small alpha
        /// magnifies any error without bound, so the pixels are premultiplied again by the reference alpha, and
        /// compared with the reference colour clamped to [0, alpha].
        /// </summary>
        Premultiplied,
    }

    // Measured: opsin_inverse, animation_icos4d, grayscale_public_university, cafe, grayscale_jpeg and
    // bench_oriented_brg are within 0.50 of one step, which is the rounding; upsampling within 0.504. patches
    // (linear sRGB planes, sRGB pixels) is within 0.50 and bike (BT.709) within 0.501. noise is within 0.50
    // and animation_spline (all 60 frames) within 0.502. progressive (linear sRGB planes, sRGB pixels; its
    // DC frame and reference-only frame are not shown, so one frame) is within 0.506. alpha_premultiplied is
    // within 0.60 of a 16-bit step. grayscale is within 0.79: its reference differs from djxl v0.12.0, which
    // this decoder matches, by up to 2.3e-4 in linear light, and the sRGB curve magnifies that in the
    // shadows.
    [Theory]
    [InlineData("opsin_inverse", typeof(Rgb24), ReferenceSpace.AsCoded)]
    [InlineData("grayscale", typeof(L8), ReferenceSpace.LinearSrgb)]
    [InlineData("alpha_premultiplied", typeof(Rgba64), ReferenceSpace.Premultiplied)]
    [InlineData("animation_icos4d", typeof(Rgba32), ReferenceSpace.AsCoded)]
    [InlineData("grayscale_public_university", typeof(L8), ReferenceSpace.AsCoded)]
    [InlineData("upsampling", typeof(Rgba32), ReferenceSpace.AsCoded)]
    [InlineData("cafe", typeof(Rgb24), ReferenceSpace.AsCoded)]
    [InlineData("grayscale_jpeg", typeof(L8), ReferenceSpace.AsCoded)]
    [InlineData("bench_oriented_brg", typeof(Rgb24), ReferenceSpace.AsCoded)]
    [InlineData("patches", typeof(Rgba32), ReferenceSpace.LinearSrgb)]
    [InlineData("bike", typeof(Rgb24), ReferenceSpace.AsCoded)]
    [InlineData("noise", typeof(Rgb24), ReferenceSpace.AsCoded)]
    [InlineData("animation_spline", typeof(Rgb24), ReferenceSpace.AsCoded)]
    [InlineData("progressive", typeof(Rgb24), ReferenceSpace.LinearSrgb)]
    public async Task Load_PicksThePixelTypeAndIsWithinOneStepOfTheReference(string id, Type pixelType, ReferenceSpace space)
    {
        JxlConformanceVector vector = JxlConformanceVector.Load(id);
        NpyArray expected = await JxlConformanceData.LoadReferenceImageAsync(vector, CancellationToken);
        string input = await JxlConformanceData.GetInputPathAsync(vector, CancellationToken);

        using Image image = Image.Load(Configuration.Default, input);

        Assert.Equal(typeof(Image<>).MakeGenericType(pixelType), image.GetType());
        Assert.Equal(expected.Shape[0], image.Frames.Count);
        int channels = expected.Shape[3];
        using Image<RgbaVector> pixels = image.CloneAs<RgbaVector>();
        NpyArray actual = JxlConformanceImages.ToNpy(pixels, channels);
        Assert.Equal(expected.Shape, actual.Shape);

        double step = 1.0 / ((1 << (image.PixelType.BitsPerPixel / channels)) - 1);
        double worst = 0;
        for (int i = 0; i < expected.Data.Length; i++)
        {
            (double a, double e) = Compared(expected.Data, actual.Data, i, channels, space);
            worst = Math.Max(worst, Math.Abs(a - e) / step);
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Largest difference {worst:G3} of one step");
        Assert.True(worst <= 1 + 1e-4, $"Largest difference {worst:G4} steps");
    }

    // The colour space and transfer function are the names of their enum values.
    // A null colour space means an ICC profile.
    [Theory]
    [InlineData("opsin_inverse", 500, 606, 24, "Rgb", "Srgb", 0, true)]
    [InlineData("grayscale", 200, 200, 8, null, null, 0, true)]
    [InlineData("alpha_premultiplied", 1024, 1024, 52, "Rgb", "Srgb", 1, true)] // 12-bit RGB, 16-bit alpha
    [InlineData("animation_icos4d", 128, 128, 32, "Rgb", "Srgb", 1, true)]
    [InlineData("bike", 2048, 2560, 24, "Rgb", "Bt709", 0, true)]
    [InlineData("grayscale_public_university", 2880, 1620, 8, "Gray", "Srgb", 0, false)]
    [InlineData("upsampling", 800, 600, 32, "Rgb", "Srgb", 1, true)] // The size after the 4x upsampling, not the coded 200x150.
    [InlineData("cafe", 1280, 1600, 24, null, null, 0, false)]
    [InlineData("grayscale_jpeg", 200, 200, 8, null, null, 0, false)]
    [InlineData("bench_oriented_brg", 606, 500, 24, null, null, 0, false)] // Orientation 5: the coded 500x606 is transposed.
    [InlineData("patches", 1600, 1096, 32, null, null, 1, true)]
    [InlineData("noise", 500, 606, 24, "Rgb", "Srgb", 0, true)]
    [InlineData("animation_spline", 320, 320, 24, "Rgb", "Srgb", 0, true)]
    [InlineData("progressive", 4064, 2704, 24, null, null, 0, true)] // The image size, not the DC frame's.
    public async Task Identify_ReportsSizeChannelsAndColourEncoding(string id, int width, int height, int bitsPerPixel, string colorSpace, string transferFunction, int extraChannels, bool xyb)
    {
        string input = await JxlConformanceData.GetInputPathAsync(JxlConformanceVector.Load(id), CancellationToken);

        IImageInfo info = Image.Identify(Configuration.Default, input, out _);

        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
        Assert.Equal(bitsPerPixel, info.PixelType.BitsPerPixel);
        JxlMetadata metadata = info.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.Equal(xyb, metadata.IsXybEncoded);
        Assert.Equal(extraChannels, metadata.ExtraChannels.Count);
        if (colorSpace is null)
        {
            Assert.Null(metadata.ColorEncoding);
            Assert.NotNull(metadata.EmbeddedIccProfile);
        }
        else
        {
            Assert.Equal(colorSpace, metadata.ColorEncoding.ColorSpace.ToString());
            Assert.Equal(transferFunction, metadata.ColorEncoding.TransferFunction.ToString());
            Assert.Null(metadata.EmbeddedIccProfile);
        }
    }

    // IImageInfo has no frame count, so Identify cannot report one: Load_PicksThePixelTypeAndIsWithinOneStepOfTheReference
    // checks that Image.Load gives every frame of the reference. Identify reports the animation header of
    // animation_spline (60 frames of 2 ticks of 10 ms each), and none for a still image.
    [Theory]
    [InlineData("animation_spline", 100u, 1u, 0)]
    [InlineData("noise", 0u, 0u, 0)]
    public async Task Identify_ReportsTheAnimationHeader(string id, uint ticksNumerator, uint ticksDenominator, int loopCount)
    {
        string input = await JxlConformanceData.GetInputPathAsync(JxlConformanceVector.Load(id), CancellationToken);

        IImageInfo info = Image.Identify(Configuration.Default, input, out _);

        JxlMetadata metadata = info.Metadata.GetFormatMetadata(JxlFormat.Instance);
        Assert.Equal(ticksNumerator, metadata.TicksPerSecondNumerator);
        Assert.Equal(ticksDenominator, metadata.TicksPerSecondDenominator);
        Assert.Equal(loopCount, metadata.LoopCount);
    }

    // bench_oriented_brg has codestream orientation 5 and an Exif box. Identify reports the image as Load
    // gives it: transposed, with the Exif orientation reset to 1, as djxl writes it.
    [Fact]
    public async Task Identify_OrientedJpegTranscode_ReportsTheOrientedImage()
    {
        string input = await JxlConformanceData.GetInputPathAsync(JxlConformanceVector.Load("bench_oriented_brg"), CancellationToken);

        IImageInfo info = Image.Identify(Configuration.Default, input, out _);

        Assert.Equal(606, info.Width);
        Assert.Equal(500, info.Height);
        Assert.Equal((ushort)1, info.Metadata.ExifProfile.GetValue(ExifTag.Orientation).Value);
    }

    // Sample i of the pixels and of the reference, in the space where they are compared.
    private static (double Actual, double Expected) Compared(float[] expected, float[] actual, int i, int channels, ReferenceSpace space)
    {
        int c = i % channels;
        double reference = Math.Clamp(expected[i], 0F, 1F);
        switch (space)
        {
            case ReferenceSpace.LinearSrgb:
                return (actual[i], LinearToSrgb(reference));
            case ReferenceSpace.Premultiplied when c < channels - 1:
                double alpha = Math.Clamp(expected[i - c + channels - 1], 0F, 1F);
                return (actual[i] * alpha, Math.Min(reference, alpha));
            default:
                return (actual[i], reference);
        }
    }

    // The sRGB encoding of IEC 61966-2-1, in double precision.
    private static double LinearToSrgb(double v) => v <= 0.0031308 ? 12.92 * v : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;
}
