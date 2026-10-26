// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Text;
using PinataLabs.ImagePastel.Formats;
using PinataLabs.ImagePastel.Formats.Bmp;
using PinataLabs.ImagePastel.Formats.Gif;
using PinataLabs.ImagePastel.Formats.Jpeg;
using PinataLabs.ImagePastel.Formats.Png;
using PinataLabs.ImagePastel.Formats.Tiff;
using PinataLabs.ImagePastel.Formats.Tiff.Constants;
using PinataLabs.ImagePastel.Formats.Webp;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Formats.Png;
using PinataLabs.ImagePastel.Tests.Processing.Processors.ColorManagement;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats;

/// <summary>
/// An embedded profile is the pixel profile only when its data color space can describe the decoded
/// pixels: an RGB profile for a color image, a gray profile for a grayscale one. Other profiles stay
/// reachable where the format exposes the embedded ICC profile (JPEG, TIFF).
/// </summary>
[Trait("Profile", "Icc")]
public class PixelProfileColorSpaceTests
{
    public static TheoryData<string, string> ColorFormatsAndWrongSpaces { get; } = new()
    {
        { "jpeg", "GRAY" },
        { "jpeg", "CMYK" },
        { "png", "GRAY" },
        { "png", "CMYK" },
        { "tiff", "GRAY" },
        { "tiff", "CMYK" },
        { "webp", "GRAY" },
        { "webp", "CMYK" },
        { "gif", "GRAY" },
        { "gif", "CMYK" },
        { "bmp", "GRAY" },
        { "bmp", "CMYK" },
    };

    public static TheoryData<string> GrayFormats { get; } = new() { "jpeg", "png", "tiff" };

    [Theory]
    [MemberData(nameof(ColorFormatsAndWrongSpaces))]
    public void Decode_ColorImageWithNonRgbProfile_HasNoPixelProfile(string format, string colorSpace)
    {
        byte[] file = CreateColorFile(format, colorSpace, out byte[] profile);

        using Image<Rgb24> image = Image.Load<Rgb24>(file);
        IImageInfo info = Image.Identify(file);

        Assert.Null(image.Metadata.IccProfile);
        Assert.Null(info.Metadata.IccProfile);
        AssertEmbeddedProfile(format, image, profile);
    }

    [Theory]
    [MemberData(nameof(GrayFormats))]
    public void Decode_GrayImageWithRgbProfile_HasNoPixelProfile(string format)
    {
        byte[] profile = LoadProfile(TestImages.Icc.CompactAdobeRgbV2).ToByteArray();
        byte[] file = CreateGrayFile(format, profile);

        using Image<L8> image = Image.Load<L8>(file);

        Assert.Null(image.Metadata.IccProfile);
        AssertEmbeddedProfile(format, image, profile);
    }

    [Theory]
    [MemberData(nameof(GrayFormats))]
    public void Decode_GrayImageWithGrayProfile_KeepsPixelProfile(string format)
    {
        byte[] profile = LoadProfile(TestImages.Icc.LittleCmsGray22V4).ToByteArray();
        byte[] file = CreateGrayFile(format, profile);

        using Image<L8> image = Image.Load<L8>(file);

        Assert.Equal(profile, image.Metadata.IccProfile?.ToByteArray());
    }

    private static IccProfile LoadProfile(string file) => ConvertColorProfileProcessorTests.LoadProfile(file);

    private static void AssertEmbeddedProfile(string format, Image image, byte[] expected)
    {
        switch (format)
        {
            case "jpeg":
                Assert.Equal(expected, image.Metadata.GetJpegMetadata().EmbeddedIccProfile?.ToByteArray());
                break;
            case "tiff":
                Assert.Equal(expected, image.Metadata.GetTiffMetadata().EmbeddedIccProfile?.ToByteArray());
                Assert.Equal(expected, image.Frames.RootFrame.Metadata.GetTiffMetadata().EmbeddedIccProfile?.ToByteArray());
                Assert.Null(image.Frames.RootFrame.Metadata.IccProfile);
                break;
        }
    }

    // A color file whose embedded RGB profile has its data color space changed to the given one.
    // The encoders write only profiles that match their pixels, so the bytes are changed in the file.
    private static byte[] CreateColorFile(string format, string colorSpace, out byte[] profile)
    {
        profile = LoadProfile(TestImages.Icc.CompactAdobeRgbV2).ToByteArray();
        using Image<Rgb24> image = new(8, 8, new Rgb24(128, 64, 32));
        image[1, 1] = new Rgb24(10, 200, 30);
        if (format == "png")
        {
            byte[] png = Encode(image, new PngEncoder());
            SetColorSpace(profile, 16, colorSpace);
            return PngTestFileBuilder.InsertAfterHeader(png, PngTestFileBuilder.EmbeddedProfile(profile));
        }

        image.Metadata.IccProfile = new IccProfile(profile);
        IImageEncoder encoder = format switch
        {
            "jpeg" => new JpegEncoder(),
            "tiff" => new TiffEncoder(),
            "webp" => new WebpEncoder { FileFormat = WebpFileFormatType.Lossless },
            "gif" => new GifEncoder(),
            "bmp" => new BmpEncoder { BitsPerPixel = BmpBitsPerPixel.Pixel24 },
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        byte[] file = Encode(image, encoder);

        // The data color space (offset 16) and PCS (offset 20) of the profile header: 'RGB ' 'XYZ '.
        int at = file.AsSpan().IndexOf(Encoding.ASCII.GetBytes("RGB XYZ "));
        Assert.True(at >= 0, "The encoder wrote the profile.");
        SetColorSpace(file, at, colorSpace);
        SetColorSpace(profile, 16, colorSpace);
        return file;
    }

    private static byte[] CreateGrayFile(string format, byte[] profile)
    {
        using Image<L8> image = new(8, 8, new L8(100));
        image[1, 1] = new L8(200);
        if (format == "png")
        {
            byte[] png = Encode(image, new PngEncoder { ColorType = PngColorType.Grayscale });
            return PngTestFileBuilder.InsertAfterHeader(png, PngTestFileBuilder.EmbeddedProfile(profile));
        }

        image.Metadata.IccProfile = new IccProfile(profile);
        IImageEncoder encoder = format switch
        {
            "jpeg" => new JpegEncoder { ColorType = JpegColorType.Luminance },
            "tiff" => new TiffEncoder { PhotometricInterpretation = TiffPhotometricInterpretation.BlackIsZero, BitsPerPixel = TiffBitsPerPixel.Bit8 },
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        return Encode(image, encoder);
    }

    private static void SetColorSpace(byte[] data, int at, string colorSpace) => Encoding.ASCII.GetBytes(colorSpace).CopyTo(data, at);

    private static byte[] Encode<TPixel>(Image<TPixel> image, IImageEncoder encoder)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using MemoryStream stream = new();
        image.Save(stream, encoder);
        return stream.ToArray();
    }
}
