// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

[Trait("Format", "Jxl")]
public class JxlEncoderTests
{
    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void Encode_LossyDistance_ThrowsNotSupportedNamingE3(float distance)
    {
        using Image<Rgb24> image = new(1, 1);
        using MemoryStream stream = new();

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => image.Save(stream, new JxlEncoder { Distance = distance, Effort = JxlEffort.Fastest }));

        Assert.Contains("E3", ex.Message);
    }

    [Theory]
    [InlineData((int)JxlEffort.Default)]
    [InlineData((int)JxlEffort.Thorough)]
    public void Encode_LosslessAboveFastest_ThrowsNotSupportedNamingE2(int effort)
    {
        using Image<Rgb24> image = new(1, 1);
        using MemoryStream stream = new();

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => image.Save(stream, new JxlEncoder { Distance = 0, Effort = (JxlEffort)effort }));

        Assert.Contains("E2", ex.Message);
    }

    [Theory]
    [MemberData(nameof(JxlEncoderRoundTrip.Cases), MemberType = typeof(JxlEncoderRoundTrip))]
    public Task RoundTrip_OurDecoder_IsExact(string pixelType, string pattern, int width, int height)
        => JxlEncoderRoundTrip.RunAsync(JxlEncoderRoundTrip.Reader.Ours, pixelType, pattern, width, height);

    [Theory]
    [MemberData(nameof(JxlEncoderRoundTrip.Cases), MemberType = typeof(JxlEncoderRoundTrip))]
    public Task RoundTrip_Magick_IsExact(string pixelType, string pattern, int width, int height)
        => JxlEncoderRoundTrip.RunAsync(JxlEncoderRoundTrip.Reader.Magick, pixelType, pattern, width, height);

    [Theory]
    [MemberData(nameof(JxlEncoderRoundTrip.Cases), MemberType = typeof(JxlEncoderRoundTrip))]
    public Task RoundTrip_Djxl_IsExact(string pixelType, string pattern, int width, int height)
        => JxlEncoderRoundTrip.RunAsync(JxlEncoderRoundTrip.Reader.Djxl, pixelType, pattern, width, height);

    [Fact]
    public void Identify_Grey_ReportsOneEightBitChannelAndGreySrgb()
    {
        using Image<L8> image = JxlEncoderRoundTrip.CreateImage<L8>("gradient", 17, 23);

        using MemoryStream stream = new(JxlEncoderRoundTrip.Encode(image));
        IImageInfo info = Image.Identify(JxlEncoderRoundTrip.CreateConfiguration(), stream);

        Assert.Equal(17, info.Width);
        Assert.Equal(23, info.Height);
        Assert.Equal(8, info.PixelType.BitsPerPixel);
        JxlColorEncoding colorEncoding = info.Metadata.GetFormatMetadata(JxlFormat.Instance).ColorEncoding;
        Assert.Equal(JxlColorSpace.Gray, colorEncoding.ColorSpace);
        AssertSrgb(colorEncoding);
    }

    [Fact]
    public void Identify_Rgb_ReportsThreeEightBitChannelsAndSrgb()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("gradient", 17, 23);

        using MemoryStream stream = new(JxlEncoderRoundTrip.Encode(image));
        IImageInfo info = Image.Identify(JxlEncoderRoundTrip.CreateConfiguration(), stream);

        Assert.Equal(17, info.Width);
        Assert.Equal(23, info.Height);
        Assert.Equal(24, info.PixelType.BitsPerPixel);
        JxlColorEncoding colorEncoding = info.Metadata.GetFormatMetadata(JxlFormat.Instance).ColorEncoding;
        Assert.Equal(JxlColorSpace.Rgb, colorEncoding.ColorSpace);
        Assert.Equal(JxlPrimaries.Srgb, colorEncoding.Primaries);
        AssertSrgb(colorEncoding);
    }

    [Fact]
    public void Encode_IsABareCodestream()
    {
        using Image<Rgb24> image = new(1, 1);

        byte[] encoded = JxlEncoderRoundTrip.Encode(image);

        Assert.Equal(new byte[] { 0xFF, 0x0A }, encoded[..2]);
    }

    [Theory]
    [InlineData(401, 1)]
    [InlineData(1, 401)]
    [InlineData(512, 512)]
    public void Encode_LargerThanOneGroup_ThrowsNotSupportedNamingE1c(int width, int height)
    {
        using Image<L8> image = new(width, height);
        using MemoryStream stream = new();

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => image.Save(stream, JxlEncoderRoundTrip.LosslessFastest));

        Assert.Contains("E1c", ex.Message);
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData(400, 400)]
    [InlineData(257, 3)]
    public void Encode_OneLargerGroup_RoundTripsThroughOurDecoder(int width, int height)
    {
        // libjxl's group size grows to 512 for images up to 400 pixels on each side, so these are
        // still one group.
        using Image<L8> image = JxlEncoderRoundTrip.CreateImage<L8>("noise", width, height);

        using Image<L8> decoded = Image.Load<L8>(JxlEncoderRoundTrip.CreateConfiguration(), JxlEncoderRoundTrip.Encode(image));

        ImageComparer.Exact.VerifySimilarity(image, decoded);
    }

    [Fact]
    public void Encoder_Defaults_AreDistanceOneAndEffortDefault()
    {
        JxlEncoder encoder = new();

        Assert.Equal(1.0f, encoder.Distance);
        Assert.Equal(JxlEffort.Default, encoder.Effort);
    }

    private static void AssertSrgb(JxlColorEncoding colorEncoding)
    {
        Assert.False(colorEncoding.WantIcc);
        Assert.Equal(JxlWhitePoint.D65, colorEncoding.WhitePoint);
        Assert.False(colorEncoding.Tf.HaveGamma);
        Assert.Equal(JxlTransferFunction.Srgb, colorEncoding.Tf.TransferFunction);
    }
}
