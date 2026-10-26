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
        uint[] sizes = JxlToc.ReadGroupOffsets(JxlToc.NumTocEntries(frameDim.NumGroups, frameDim.NumDcGroups, 1), reader).Sizes;
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

    // libjxl's JxlEncoderSetBasicInfo takes the intensity target for lossless as for lossy, and without one
    // SetIntensityTarget gives an sRGB image 255 nits. The pixels stay exact.
    [Theory]
    [InlineData(0f, 255f)]
    [InlineData(400f, 400f)]
    [InlineData(4000f, 4000f)]
    public void Encode_Lossless_WritesTheIntensityTargetOfTheMetadata(float metadataTarget, float expected)
    {
        using Image<Rgb24> image = new(1, 1);
        image[0, 0] = new Rgb24(12, 34, 56);
        image.Metadata.GetFormatMetadata(JxlFormat.Instance).IntensityTarget = metadataTarget;

        byte[] encoded = JxlEncoderRoundTrip.Encode(image, JxlEncoderRoundTrip.LosslessFastest);

        using Image<Rgb24> decoded = Image.Load<Rgb24>(JxlEncoderRoundTrip.CreateConfiguration(), encoded);
        Assert.Equal(expected, decoded.Metadata.GetFormatMetadata(JxlFormat.Instance).IntensityTarget);
        Assert.Equal(image[0, 0], decoded[0, 0]);
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
        // A null description would mean the stream signals an ICC profile.
        Assert.NotNull(colorEncoding);
        Assert.Equal(JxlWhitePoint.D65, colorEncoding.WhitePoint);
        Assert.Null(colorEncoding.Gamma);
        Assert.Equal(JxlTransferFunction.Srgb, colorEncoding.TransferFunction);
    }
}
