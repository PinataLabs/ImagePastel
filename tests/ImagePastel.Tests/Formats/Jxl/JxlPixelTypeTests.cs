// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Checks the pixel type that the non-generic <c>Decode</c> picks, against the table decided in issue #111.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlPixelTypeTests
{
    [Theory]
    [InlineData("delta_palette", typeof(Rgb24))]
    [InlineData("lz77_flower", typeof(Rgb24))]
    [InlineData("alpha_nonpremultiplied", typeof(Rgba64))]
    [InlineData("alpha_triangles", typeof(Rgba64))]
    [InlineData("lossless_pfm", typeof(RgbaVector))]
    public void Load_PicksThePixelTypeAndDecodesTheSamePixels(string id, Type pixelType)
    {
        string path = JxlConformanceVector.Load(id).CommittedInputPath;

        using Image image = Image.Load(CreateConfiguration(), path);

        Assert.Equal(typeof(Image<>).MakeGenericType(pixelType), image.GetType());
        using Image<RgbaVector> actual = image.CloneAs<RgbaVector>();
        using Image<RgbaVector> expected = LoadAs(pixelType, path);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    [Theory]
    [InlineData(true, false, 8u, false, typeof(L8))]
    [InlineData(true, false, 16u, false, typeof(L16))]
    [InlineData(true, false, 32u, true, typeof(RgbaVector))]
    [InlineData(true, false, 24u, false, typeof(RgbaVector))]
    [InlineData(true, true, 1u, false, typeof(La16))]
    [InlineData(true, true, 9u, false, typeof(La32))]
    [InlineData(true, true, 16u, true, typeof(RgbaVector))]
    [InlineData(false, false, 8u, false, typeof(Rgb24))]
    [InlineData(false, false, 12u, false, typeof(Rgb48))]
    [InlineData(false, false, 17u, false, typeof(RgbaVector))]
    [InlineData(false, true, 8u, false, typeof(Rgba32))]
    [InlineData(false, true, 16u, false, typeof(Rgba64))]
    [InlineData(false, true, 32u, true, typeof(RgbaVector))]
    public void ChoosePixelType_FollowsTheTable(bool gray, bool alpha, uint bits, bool floatingPoint, Type expected)
        => Assert.Equal(expected, JxlDecoderCore.ChoosePixelType(gray, alpha, bits, floatingPoint));

    [Fact]
    public void ChoosePixelType_Xyb_UsesTheSignalledBitDepth()
    {
        JxlTestBitWriter writer = Metadata(bitsSelector: 2); // 12-bit
        writer.Write(2, 0); // no extra channels
        writer.Bit(true).Bit(true).Write(2, 0); // XYB, default colour encoding, no extensions

        Assert.Equal(typeof(Rgb48), JxlDecoderCore.ChoosePixelType(Read(writer)));
    }

    [Theory]
    [InlineData(false, typeof(Rgb24))]
    [InlineData(true, typeof(Rgba32))]
    public void ChoosePixelType_Cmyk_IsConvertedToRgb(bool withAlpha, Type expected)
    {
        JxlTestBitWriter writer = Metadata(bitsSelector: 0); // 8-bit
        if (withAlpha)
        {
            writer.Write(2, 2).Write(4, 0); // two extra channels
        }
        else
        {
            writer.Write(2, 1); // one extra channel
        }

        writer.Bit(false).Write(2, 2).Write(4, 2); // not all default, type 4 (black)
        writer.Bit(false).Write(2, 0); // 8-bit integer samples
        writer.Write(2, 0).Write(2, 0); // no dim shift, no name
        if (withAlpha)
        {
            writer.Bit(true); // all default: 8-bit straight alpha
        }

        writer.Bit(false).Bit(true).Write(2, 0); // not XYB, default colour encoding, no extensions

        Assert.Equal(expected, JxlDecoderCore.ChoosePixelType(Read(writer)));
    }

    [Fact]
    public void ChoosePixelType_AlphaDeeperThanTheColour_KeepsTheAlphaBits()
    {
        JxlTestBitWriter writer = Metadata(bitsSelector: 0); // 8-bit colour
        writer.Write(2, 1); // one extra channel
        writer.Bit(false).Write(2, 0); // not all default, alpha
        writer.Bit(false).Write(2, 3).Write(6, 15); // 16-bit integer samples
        writer.Write(2, 0).Write(2, 0).Bit(false); // no dim shift, no name, straight
        writer.Bit(false).Bit(true).Write(2, 0); // not XYB, default colour encoding, no extensions

        Assert.Equal(typeof(Rgba64), JxlDecoderCore.ChoosePixelType(Read(writer)));
    }

    // Image metadata up to the extra channel count: not all default, no extra fields, integer samples
    // with the bits_per_sample selector (0: 8, 1: 10, 2: 12), 16-bit buffers suffice.
    private static JxlTestBitWriter Metadata(uint bitsSelector)
        => new JxlTestBitWriter().Bit(false).Bit(false).Bit(false).Write(2, bitsSelector).Bit(true);

    private static JxlImageMetadata Read(JxlTestBitWriter writer)
    {
        writer.Bits(64, false);
        JxlBitReader reader = writer.ToReader();
        JxlImageMetadata metadata = new();
        JxlBundle.Read(reader, metadata);
        reader.EnsureAllReadsWithinBounds("image metadata");
        reader.Close();
        return metadata;
    }

    private static Image<RgbaVector> LoadAs(Type pixelType, string path)
    {
        Configuration configuration = CreateConfiguration();
        using Image image = pixelType == typeof(Rgb24) ? Image.Load<Rgb24>(configuration, path)
            : pixelType == typeof(Rgba64) ? Image.Load<Rgba64>(configuration, path)
            : Image.Load<RgbaVector>(configuration, path);
        return image.CloneAs<RgbaVector>();
    }

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }
}
