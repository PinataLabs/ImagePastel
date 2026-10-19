// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
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
    [InlineData(nameof(JxlEffort.Default))]
    [InlineData(nameof(JxlEffort.Thorough))]
    public void Encode_LossyEffort_ThrowsNotSupportedNamingE3(string effort)
    {
        using Image<Rgb24> image = new(1, 1);
        using MemoryStream stream = new();

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => image.Save(stream, new JxlEncoder { Distance = 1, Effort = Enum.Parse<JxlEffort>(effort) }));

        Assert.Contains("E3", ex.Message);
    }

    // No lossless effort throws.
    [Theory]
    [InlineData(nameof(JxlEffort.Fastest))]
    [InlineData(nameof(JxlEffort.Default))]
    [InlineData(nameof(JxlEffort.Thorough))]
    public void Encode_LosslessAtEveryEffort_Encodes(string effort)
    {
        using Image<Rgb24> image = new(1, 1);
        image[0, 0] = new Rgb24(12, 34, 56);
        using MemoryStream stream = new();

        image.Save(stream, JxlEncoderRoundTrip.Lossless(Enum.Parse<JxlEffort>(effort)));

        using Image<Rgb24> actual = Image.Load<Rgb24>(JxlEncoderRoundTrip.CreateConfiguration(), stream.ToArray());
        Assert.Equal(image[0, 0], actual[0, 0]);
    }

    [Theory]
    [MemberData(nameof(JxlEncoderRoundTrip.Cases), MemberType = typeof(JxlEncoderRoundTrip))]
    public Task RoundTrip_OurDecoder_IsExact(string effort, string pixelType, string pattern, int width, int height)
        => JxlEncoderRoundTrip.AssertRoundTripAsync(JxlEncoderRoundTrip.Reader.Ours, effort, pixelType, pattern, width, height);

    [Theory]
    [MemberData(nameof(JxlEncoderRoundTrip.Cases), MemberType = typeof(JxlEncoderRoundTrip))]
    public Task RoundTrip_Magick_IsExact(string effort, string pixelType, string pattern, int width, int height)
        => JxlEncoderRoundTrip.AssertRoundTripAsync(JxlEncoderRoundTrip.Reader.Magick, effort, pixelType, pattern, width, height);

    [Theory]
    [MemberData(nameof(JxlEncoderRoundTrip.Cases), MemberType = typeof(JxlEncoderRoundTrip))]
    public Task RoundTrip_Djxl_IsExact(string effort, string pixelType, string pattern, int width, int height)
        => JxlEncoderRoundTrip.AssertRoundTripAsync(JxlEncoderRoundTrip.Reader.Djxl, effort, pixelType, pattern, width, height);

    [Theory]
    [MemberData(nameof(JxlEncoderRoundTrip.CorpusCases), MemberType = typeof(JxlEncoderRoundTrip))]
    public Task RoundTripCorpus_OurDecoder_IsExact(string effort, string file, string pixelType)
        => JxlEncoderRoundTrip.AssertCorpusRoundTripAsync(JxlEncoderRoundTrip.Reader.Ours, effort, file, pixelType);

    [Theory]
    [MemberData(nameof(JxlEncoderRoundTrip.CorpusCases), MemberType = typeof(JxlEncoderRoundTrip))]
    public Task RoundTripCorpus_Magick_IsExact(string effort, string file, string pixelType)
        => JxlEncoderRoundTrip.AssertCorpusRoundTripAsync(JxlEncoderRoundTrip.Reader.Magick, effort, file, pixelType);

    [Theory]
    [MemberData(nameof(JxlEncoderRoundTrip.CorpusCases), MemberType = typeof(JxlEncoderRoundTrip))]
    public Task RoundTripCorpus_Djxl_IsExact(string effort, string file, string pixelType)
        => JxlEncoderRoundTrip.AssertCorpusRoundTripAsync(JxlEncoderRoundTrip.Reader.Djxl, effort, file, pixelType);

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

    [Theory]
    [InlineData(401, 1, 2, 1)]
    [InlineData(1, 401, 2, 1)]
    [InlineData(256, 512, 2, 1)]
    [InlineData(300, 513, 6, 1)]
    [InlineData(2049, 2049, 81, 4)]
    public void Encode_MultiGroup_TocHasOneEntryPerSectionCoveringTheFrame(int width, int height, int expectedGroups, int expectedLfGroups)
    {
        using Image<L8> image = JxlEncoderRoundTrip.CreateImage<L8>("gradient", width, height);
        byte[] encoded = JxlEncoderRoundTrip.Encode(image);

        // Parse the frame header and TOC as the decoder does.
        JxlBitReader reader = new(encoded, 2, encoded.Length - 2);
        JxlCodecMetadata metadata = new();
        JxlBundle.Read(reader, metadata.Size);
        JxlBundle.Read(reader, metadata.M);
        JxlBundle.Read(reader, metadata.TransformData);
        reader.JumpToByteBoundary();
        JxlFrameHeader frameHeader = new(metadata);
        JxlBundle.Read(reader, frameHeader);
        JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();
        Assert.Equal(expectedGroups, frameDim.NumGroups);
        Assert.Equal(expectedLfGroups, frameDim.NumDcGroups);
        uint[] sizes = JxlToc.ReadToc(JxlToc.NumTocEntries(frameDim.NumGroups, frameDim.NumDcGroups, 1), reader);
        long frameDataStart = 2 + (reader.TotalBitsConsumed / 8);
        reader.Close();

        // LF global, then each LF group, HF global and each group, as the decoder reads them.
        Assert.Equal(2 + expectedLfGroups + expectedGroups, sizes.Length);
        Assert.NotEqual(0u, sizes[0]);

        // Lossless with no squeeze leaves the LF groups and HF global empty; every group has pixels.
        Assert.All(sizes[1..(2 + expectedLfGroups)], size => Assert.Equal(0u, size));
        Assert.All(sizes[(2 + expectedLfGroups)..], size => Assert.NotEqual(0u, size));

        // The sections fill the rest of the codestream, with nothing left over.
        long total = 0;
        foreach (uint size in sizes)
        {
            total += size;
        }

        Assert.Equal(encoded.Length, frameDataStart + total);
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
