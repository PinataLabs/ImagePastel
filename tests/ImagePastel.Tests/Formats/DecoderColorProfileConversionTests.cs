// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ImageMagick;
using PinataLabs.ImagePastel.ColorManagement;
using PinataLabs.ImagePastel.Formats;
using PinataLabs.ImagePastel.Formats.Bmp;
using PinataLabs.ImagePastel.Formats.Gif;
using PinataLabs.ImagePastel.Formats.Jpeg;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Png;
using PinataLabs.ImagePastel.Formats.Tiff;
using PinataLabs.ImagePastel.Formats.Webp;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing.Processors.Quantization;
using PinataLabs.ImagePastel.Tests.Formats.Png;
using PinataLabs.ImagePastel.Tests.Processing.Processors.ColorManagement;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats;

/// <summary>
/// Decodes JPEG, PNG, TIFF, WebP, GIF, BMP and JPEG XL files with the <c>ColorProfileConversion</c>
/// decoder option. Expected colours come from Little CMS: the <c>transicc</c> tables in
/// <see cref="LittleCmsReferences"/> for files encoded losslessly here, and Little CMS inside Magick.NET
/// for the lossy JPEG and for the JPEG XL files (converting the pixels and profile that <c>djxl</c> gives).
/// </summary>
[Trait("Profile", "Icc")]
public class DecoderColorProfileConversionTests
{
    // About one code value at 8 bits: the conversion runs in float and rounds once at the end.
    private const float EightBitTolerance = 1F;

    public static TheoryData<string> LosslessFormats { get; } = new() { "png", "tiff", "webp", "gif", "bmp" };

    public static TheoryData<string> AllFormats { get; } = new() { "jpeg", "png", "tiff", "webp", "gif", "bmp" };

    [Theory]
    [InlineData("png", TestImages.Icc.CompactAdobeRgbV2)]
    [InlineData("tiff", TestImages.Icc.CompactProPhotoV4)]
    [InlineData("webp", TestImages.Icc.CompactDisplayP3V4)]
    [InlineData("png", TestImages.Icc.QcmsRec2020)]
    [InlineData("gif", TestImages.Icc.CompactAdobeRgbV2)]
    [InlineData("bmp", TestImages.Icc.QcmsRec2020)]
    [InlineData("png", TestImages.Icc.LittleCmsRgbLutAToBV4)]
    [InlineData("tiff", TestImages.Icc.LittleCmsRgbLut16V2)]
    [InlineData("webp", TestImages.Icc.LittleCmsRgbLut8V2)]
    public void Decode_ToSrgb_MatchesLittleCms(string format, string embeddedProfile)
    {
        LittleCmsReference reference = LittleCmsReferences.Get(embeddedProfile, null);
        byte[] file = Encode(format, CreateRgb24(reference), LoadProfile(embeddedProfile));

        using Image<Rgb24> image = Decode<Rgb24>(format, file, ColorProfileConversion.ToSrgb);

        AssertPixels(image, reference);
        Assert.Null(image.Metadata.IccProfile);
    }

    // PNG cICP, cHRM and gAMA chunks give a built pixel profile, which converts like an embedded one. The
    // reference is Little CMS converting from a profile file for the same space.
    [Theory]
    [InlineData("cICP Display P3", TestImages.Icc.CompactDisplayP3V4)]
    [InlineData("cHRM+gAMA Adobe RGB", TestImages.Icc.CompactAdobeRgbV2)]
    [InlineData("cHRM+gAMA linear sRGB", TestImages.Icc.CompactLinearSrgbV2)]
    public void Decode_Png_ColorSpaceChunks_ToSrgb_MatchesLittleCms(string chunks, string sameSpaceProfile)
    {
        LittleCmsReference reference = LittleCmsReferences.Get(sameSpaceProfile, null);
        byte[][] colorChunks = chunks switch
        {
            "cICP Display P3" => new[] { PngTestFileBuilder.Cicp(12, 13) },
            "cHRM+gAMA Adobe RGB" => new[] { PngTestFileBuilder.Chromaticities(31270, 32900, 64000, 33000, 21000, 71000, 15000, 6000), PngTestFileBuilder.Gamma(45471) },
            _ => new[] { PngTestFileBuilder.Chromaticities(31270, 32900, 64000, 33000, 30000, 60000, 15000, 6000), PngTestFileBuilder.Gamma(100000) },
        };
        byte[] file = PngTestFileBuilder.InsertAfterHeader(Encode("png", CreateRgb24(reference), null), colorChunks);

        using (Image<Rgb24> unconverted = Decode<Rgb24>("png", file))
        {
            Assert.NotNull(unconverted.Metadata.IccProfile);
        }

        using Image<Rgb24> image = Decode<Rgb24>("png", file, ColorProfileConversion.ToSrgb);

        AssertPixels(image, reference);
        Assert.Null(image.Metadata.IccProfile);
    }

    [Theory]
    [MemberData(nameof(LosslessFormats))]
    public void Decode_ToTargetProfile_MatchesLittleCmsAndSetsTargetAsPixelProfile(string format)
    {
        LittleCmsReference reference = LittleCmsReferences.Get(TestImages.Icc.LittleCmsSrgbV4, TestImages.Icc.CompactDisplayP3V4);
        IccProfile target = LoadProfile(TestImages.Icc.CompactDisplayP3V4);
        byte[] file = Encode(format, CreateRgb24(reference), LoadProfile(TestImages.Icc.LittleCmsSrgbV4));

        using Image<Rgb24> image = Decode<Rgb24>(format, file, ColorProfileConversion.ToTargetProfile, target);

        AssertPixels(image, reference);
        Assert.Equal(target.ToByteArray(), image.Metadata.IccProfile.ToByteArray());
        Assert.NotSame(target, image.Metadata.IccProfile);
    }

    [Fact]
    public void Decode_Jpeg_ToSrgb_MatchesLittleCmsInMagick()
    {
        // Snake.jpg embeds Adobe RGB (1998), a matrix/TRC profile. Little CMS (in Magick.NET) converts
        // the unconverted decode to the Little CMS built-in sRGB, which the engine's sRGB matches.
        byte[] file = File.ReadAllBytes(TestFile.GetInputFileFullPath(TestImages.Jpeg.Baseline.Snake));
        using Image<Rgb24> unconverted = Decode<Rgb24>("jpeg", file, ColorProfileConversion.None);
        byte[] embedded = unconverted.Metadata.IccProfile.ToByteArray();
        byte[] srgb = File.ReadAllBytes(TestFile.GetInputFileFullPath(TestImages.Icc.LittleCmsSrgbV4));
        float[] expected = ConvertWithMagick(unconverted, embedded, srgb);

        using Image<Rgb24> image = Decode<Rgb24>("jpeg", file, ColorProfileConversion.ToSrgb);

        Assert.Null(image.Metadata.IccProfile);
        AssertPixels(image, expected);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void Decode_DefaultOption_LeavesPixelsAndProfileAsTheyAre(string format)
    {
        byte[] file = CreateFileWithProfile(format, TestImages.Icc.CompactAdobeRgbV2);
        using Image<Rgb24> expected = Decode<Rgb24>(format, file, ignoreMetadata: true);

        using Image<Rgb24> image = Image.Load<Rgb24>(file, CreateDecoder(format, ColorProfileConversion.None, LoadProfile(TestImages.Icc.CompactDisplayP3V4), false));

        AssertSamePixels(expected, image);
        Assert.Equal(LoadProfile(TestImages.Icc.CompactAdobeRgbV2).ToByteArray(), image.Metadata.IccProfile.ToByteArray());
    }

    // A profile equivalent to sRGB (IccProfile.IsSrgb) already describes sRGB pixels, so converting
    // to sRGB keeps every 16-bit value and only removes the pixel profile.
    [Theory]
    [InlineData(TestImages.Icc.LittleCmsSrgbV4)]
    [InlineData(TestImages.Icc.QcmsSrgb)]
    public void Decode_SrgbEquivalentProfile_ToSrgb_LeavesPixelsAsTheyAre(string profile)
    {
        using Image<Rgba64> source = new(256, 256);
        for (int y = 0; y < 256; y++)
        {
            for (int x = 0; x < 256; x++)
            {
                source[x, y] = new Rgba64((ushort)((x * 256) + y), (ushort)((y * 256) + x), (ushort)(65535 - (x * 256) - y), 65535);
            }
        }

        source.Metadata.IccProfile = LoadProfile(profile);
        Assert.True(source.Metadata.IccProfile.IsSrgb());
        using MemoryStream stream = new();
        source.Save(stream, new PngEncoder { BitDepth = PngBitDepth.Bit16, ColorType = PngColorType.RgbWithAlpha });

        using Image<Rgba64> image = Decode<Rgba64>("png", stream.ToArray(), ColorProfileConversion.ToSrgb);

        AssertSamePixels(source, image);
        Assert.Null(image.Metadata.IccProfile);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void Decode_IgnoreMetadata_DisablesConversion(string format)
    {
        byte[] file = CreateFileWithProfile(format, TestImages.Icc.CompactAdobeRgbV2);
        using Image<Rgb24> unconverted = Decode<Rgb24>(format, file, ColorProfileConversion.None);

        using Image<Rgb24> image = Image.Load<Rgb24>(file, CreateDecoder(format, ColorProfileConversion.ToSrgb, null, true));

        AssertSamePixels(unconverted, image);
        Assert.Null(image.Metadata.IccProfile);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void Identify_WithConversion_ReportsEmbeddedProfile(string format)
    {
        byte[] file = CreateFileWithProfile(format, TestImages.Icc.CompactAdobeRgbV2);
        IImageInfoDetector detector = (IImageInfoDetector)CreateDecoder(format, ColorProfileConversion.ToSrgb, null, false);

        using MemoryStream stream = new(file);
        IImageInfo info = detector.Identify(Configuration.Default, stream, TestContext.Current.CancellationToken);

        Assert.Equal(LoadProfile(TestImages.Icc.CompactAdobeRgbV2).ToByteArray(), info.Metadata.IccProfile.ToByteArray());
    }

    [Theory]
    [MemberData(nameof(LosslessFormats))]
    public void Decode_WithoutEmbeddedProfile_LeavesPixelsAsTheyAre(string format)
    {
        using Image<Rgb24> source = new(1, 1, new Rgb24(128, 64, 32));
        byte[] file = Encode(format, source, null);

        using Image<Rgb24> image = Decode<Rgb24>(format, file, ColorProfileConversion.ToTargetProfile, LoadProfile(TestImages.Icc.CompactDisplayP3V4));

        Assert.Equal(new Rgb24(128, 64, 32), image[0, 0]);
        Assert.Null(image.Metadata.IccProfile);
    }

    [Theory]
    [MemberData(nameof(AllFormats))]
    public void Decode_CorruptProfile_LeavesPixelsAndProfileAsTheyAre(string format)
    {
        // A valid header, but without the red colorant the engine needs.
        IccProfile adobe = LoadProfile(TestImages.Icc.CompactAdobeRgbV2);
        IccProfile corrupt = new(new IccProfile(adobe.Header, Array.FindAll(adobe.Entries, e => e.TagSignature != IccProfileTag.RedMatrixColumn)).ToByteArray());
        byte[] file = CreateFileWithProfile(format, corrupt);
        using Image<Rgb24> expected = Decode<Rgb24>(format, file, ignoreMetadata: true);

        using Image<Rgb24> image = Decode<Rgb24>(format, file, ColorProfileConversion.ToSrgb);

        AssertSamePixels(expected, image);
        Assert.Equal(corrupt.ToByteArray(), image.Metadata.IccProfile.ToByteArray());
    }

    [Fact]
    public void Decode_Tiff_TruncatedProfile_LeavesPixelsAndProfileAsTheyAre()
    {
        // The TIFF decoder keeps profiles without checking them, so the engine sees the truncated bytes.
        byte[] truncated = LoadProfile(TestImages.Icc.CompactAdobeRgbV2).ToByteArray().AsSpan(0, 200).ToArray();
        using Image<Rgb24> source = new(1, 1, new Rgb24(128, 64, 32));
        byte[] file = Encode("tiff", source, new IccProfile(truncated));

        using Image<Rgb24> image = Decode<Rgb24>("tiff", file, ColorProfileConversion.ToSrgb);

        Assert.Equal(new Rgb24(128, 64, 32), image[0, 0]);
        Assert.Equal(truncated, image.Metadata.IccProfile.ToByteArray());
    }

    [Fact]
    public void Decode_Tiff_ConvertsEachFrameWithItsOwnProfile()
    {
        // transicc: Adobe RGB (128, 64, 32) -> sRGB 146.1484 61.7724 23.4316; ProPhoto -> 190.9341 53.3552 29.3194.
        using Image<Rgb24> source = new(1, 1, new Rgb24(128, 64, 32));
        source.Frames.AddFrame(source.Frames.RootFrame);
        source.Frames[0].Metadata.IccProfile = LoadProfile(TestImages.Icc.CompactAdobeRgbV2);
        source.Frames[1].Metadata.IccProfile = LoadProfile(TestImages.Icc.CompactProPhotoV4);
        using MemoryStream stream = new();
        source.Save(stream, new TiffEncoder());

        using Image<Rgb24> image = Decode<Rgb24>("tiff", stream.ToArray(), ColorProfileConversion.ToSrgb);

        Assert.Equal(2, image.Frames.Count);
        Assert.Equal(new Rgb24(146, 62, 23), image.Frames[0][0, 0]);
        Assert.Equal(new Rgb24(191, 53, 29), image.Frames[1][0, 0]);
        Assert.Null(image.Metadata.IccProfile);
        Assert.Null(image.Frames[0].Metadata.IccProfile);
        Assert.Null(image.Frames[1].Metadata.IccProfile);
    }

    [Theory]
    [MemberData(nameof(LosslessFormats))]
    public void Decode_ToTargetProfileWithoutTarget_Throws(string format)
    {
        byte[] file = CreateFileWithProfile(format, TestImages.Icc.CompactAdobeRgbV2);

        Assert.Throws<InvalidOperationException>(() => Decode<Rgb24>(format, file, ColorProfileConversion.ToTargetProfile));
    }

    [Theory]
    [MemberData(nameof(LosslessFormats))]
    public void Decode_UnsupportedTargetProfile_ThrowsNotSupportedException(string format)
    {
        IccProfile cmyk = Image.Identify(TestFile.GetInputFileFullPath(TestImages.Jpeg.Baseline.Cmyk)).Metadata.GetJpegMetadata().EmbeddedIccProfile;
        byte[] file = CreateFileWithProfile(format, TestImages.Icc.CompactAdobeRgbV2);

        Assert.Throws<NotSupportedException>(() => Decode<Rgb24>(format, file, ColorProfileConversion.ToTargetProfile, cmyk));
    }

    [Fact]
    public void Decode_Gif_ConvertsEveryFrameWithTheImageProfile()
    {
        // transicc: Adobe RGB (128, 64, 32) -> sRGB 146.1484 61.7724 23.4316; (51, 102, 153) -> 0.0000 102.1591 156.2031.
        using Image<Rgb24> source = new(2, 2, new Rgb24(128, 64, 32));
        using (Image<Rgb24> second = new(2, 2, new Rgb24(51, 102, 153)))
        {
            source.Frames.AddFrame(second.Frames.RootFrame);
        }

        source.Metadata.IccProfile = LoadProfile(TestImages.Icc.CompactAdobeRgbV2);
        using MemoryStream stream = new();
        source.Save(stream, new GifEncoder { Quantizer = new PaletteQuantizer(new[] { new Color(new Rgb24(128, 64, 32)), new Color(new Rgb24(51, 102, 153)) }, new QuantizerOptions { Dither = null }) });

        using Image<Rgb24> image = Decode<Rgb24>("gif", stream.ToArray(), ColorProfileConversion.ToSrgb);

        Assert.Equal(2, image.Frames.Count);
        Assert.Equal(new Rgb24(146, 62, 23), image.Frames[0][1, 1]);
        Assert.Equal(new Rgb24(0, 102, 156), image.Frames[1][1, 1]);
        Assert.Null(image.Metadata.IccProfile);
    }

    // colour_display_p3 has a profile built from its colour encoding; custom_icc embeds one (made-up
    // primaries, tabulated curve). The non-generic Decode picks the pixel type itself (Rgb24 here).
    [Theory]
    [InlineData("colour_display_p3", true)]
    [InlineData("colour_display_p3", false)]
    [InlineData("custom_icc", true)]
    [InlineData("custom_icc", false)]
    public void Decode_Jxl_ToSrgb_MatchesLittleCmsInMagick(string name, bool generic)
    {
        float[] expected = ConvertJxlReferenceWithMagick(name, TestImages.Icc.LittleCmsSrgbV4);

        using Image<Rgb24> image = DecodeJxl(name, new JxlDecoder { ColorProfileConversion = ColorProfileConversion.ToSrgb }, generic);

        AssertPixels(image, expected);
        Assert.Null(image.Metadata.IccProfile);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decode_Jxl_ToTargetProfile_MatchesLittleCmsInMagickAndSetsTargetAsPixelProfile(bool generic)
    {
        IccProfile target = LoadProfile(TestImages.Icc.CompactDisplayP3V4);
        float[] expected = ConvertJxlReferenceWithMagick("custom_icc", TestImages.Icc.CompactDisplayP3V4);

        using Image<Rgb24> image = DecodeJxl("custom_icc", new JxlDecoder { ColorProfileConversion = ColorProfileConversion.ToTargetProfile, ColorProfileConversionTarget = target }, generic);

        AssertPixels(image, expected);
        Assert.Equal(target.ToByteArray(), image.Metadata.IccProfile.ToByteArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decode_Jxl_DefaultOption_LeavesPixelsAndProfileAsTheyAre(bool generic)
    {
        using Image<Rgb24> reference = Image.Load<Rgb24>(JxlReferencePath("custom_icc"));

        using Image<Rgb24> image = DecodeJxl("custom_icc", new JxlDecoder { ColorProfileConversionTarget = LoadProfile(TestImages.Icc.CompactDisplayP3V4) }, generic);

        AssertSamePixels(reference, image);
        Assert.Equal(reference.Metadata.IccProfile.ToByteArray(), image.Metadata.IccProfile.ToByteArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decode_Jxl_IgnoreMetadata_DisablesConversion(bool generic)
    {
        using Image<Rgb24> reference = Image.Load<Rgb24>(JxlReferencePath("custom_icc"));

        using Image<Rgb24> image = DecodeJxl("custom_icc", new JxlDecoder { ColorProfileConversion = ColorProfileConversion.ToSrgb, IgnoreMetadata = true }, generic);

        AssertSamePixels(reference, image);
        Assert.Null(image.Metadata.IccProfile);
    }

    private static string JxlReferencePath(string name) => Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".png");

    // The JPEG XL decoder is called directly rather than through Image.Load, so each test chooses the generic or the
    // non-generic Decode.
    private static Image<Rgb24> DecodeJxl(string name, JxlDecoder decoder, bool generic)
    {
        using FileStream stream = File.OpenRead(Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", name + ".jxl"));
        return generic
            ? decoder.Decode<Rgb24>(Configuration.Default, stream, CancellationToken.None)
            : Assert.IsType<Image<Rgb24>>(decoder.Decode(Configuration.Default, stream, CancellationToken.None));
    }

    // The pixels and profile djxl gives for a generated JPEG XL file, converted by Little CMS in Magick.NET.
    private static float[] ConvertJxlReferenceWithMagick(string name, string targetProfile)
    {
        using Image<Rgb24> reference = Image.Load<Rgb24>(JxlReferencePath(name));
        Assert.NotNull(reference.Metadata.IccProfile);
        return ConvertWithMagick(reference, reference.Metadata.IccProfile.ToByteArray(), File.ReadAllBytes(TestFile.GetInputFileFullPath(targetProfile)));
    }

    private static IccProfile LoadProfile(string file) => ConvertColorProfileProcessorTests.LoadProfile(file);

    private static IImageDecoder CreateDecoder(string format, ColorProfileConversion conversion, IccProfile target, bool ignoreMetadata)
        => format switch
        {
            "jpeg" => new JpegDecoder { ColorProfileConversion = conversion, ColorProfileConversionTarget = target, IgnoreMetadata = ignoreMetadata },
            "png" => new PngDecoder { ColorProfileConversion = conversion, ColorProfileConversionTarget = target, IgnoreMetadata = ignoreMetadata },
            "tiff" => new TiffDecoder { ColorProfileConversion = conversion, ColorProfileConversionTarget = target, IgnoreMetadata = ignoreMetadata },
            "webp" => new WebpDecoder { ColorProfileConversion = conversion, ColorProfileConversionTarget = target, IgnoreMetadata = ignoreMetadata },
            "gif" => new GifDecoder { ColorProfileConversion = conversion, ColorProfileConversionTarget = target, IgnoreMetadata = ignoreMetadata },
            "bmp" => new BmpDecoder { ColorProfileConversion = conversion, ColorProfileConversionTarget = target, IgnoreMetadata = ignoreMetadata },
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    private static Image<TPixel> Decode<TPixel>(string format, byte[] file, ColorProfileConversion conversion = ColorProfileConversion.None, IccProfile target = null, bool ignoreMetadata = false)
        where TPixel : unmanaged, ImagePastel.PixelFormats.IPixel<TPixel>
        => Image.Load<TPixel>(file, CreateDecoder(format, conversion, target, ignoreMetadata));

    private static IImageEncoder CreateEncoder(string format, Image<Rgb24> image)
        => format switch
        {
            "jpeg" => new JpegEncoder { Quality = 90 },
            "png" => new PngEncoder(),
            "tiff" => new TiffEncoder(),
            "webp" => new WebpEncoder { FileFormat = WebpFileFormatType.Lossless },
            "gif" => new GifEncoder { Quantizer = new PaletteQuantizer(DistinctColors(image), new QuantizerOptions { Dither = null }) },
            "bmp" => new BmpEncoder { BitsPerPixel = BmpBitsPerPixel.Pixel24 },
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    // The test images have at most 256 colours, so a GIF palette of exactly those colours keeps them all.
    private static Color[] DistinctColors(Image<Rgb24> image)
    {
        HashSet<Rgb24> colors = [];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                colors.Add(image[x, y]);
            }
        }

        Assert.True(colors.Count <= 256, "A GIF palette holds at most 256 colours.");
        Color[] palette = new Color[colors.Count];
        int i = 0;
        foreach (Rgb24 color in colors)
        {
            palette[i++] = new Color(color);
        }

        return palette;
    }

    private static byte[] Encode(string format, Image<Rgb24> image, IccProfile profile)
    {
        using (image)
        {
            image.Metadata.IccProfile = profile;
            using MemoryStream stream = new();
            image.Save(stream, CreateEncoder(format, image));
            return stream.ToArray();
        }
    }

    private static byte[] CreateFileWithProfile(string format, string profile) => CreateFileWithProfile(format, LoadProfile(profile));

    private static byte[] CreateFileWithProfile(string format, IccProfile profile)
    {
        Image<Rgb24> image = new(16, 16);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                image[x, y] = new Rgb24((byte)(x * 16), (byte)(y * 16), (byte)(255 - (x * 8)));
            }
        }

        return Encode(format, image, profile);
    }

    private static Image<Rgb24> CreateRgb24(LittleCmsReference reference)
    {
        Image<Rgb24> image = new(reference.Count, 1);
        for (int i = 0; i < reference.Count; i++)
        {
            (float r, float g, float b) = reference.Input(i);
            image[i, 0] = new Rgb24((byte)r, (byte)g, (byte)b);
        }

        return image;
    }

    private static void AssertPixels(Image<Rgb24> image, LittleCmsReference reference)
    {
        for (int i = 0; i < reference.Count; i++)
        {
            (float r, float g, float b) = reference.Output(i);
            Rgb24 actual = image[i, 0];
            Assert.True(
                MathF.Abs(actual.R - r) <= EightBitTolerance && MathF.Abs(actual.G - g) <= EightBitTolerance && MathF.Abs(actual.B - b) <= EightBitTolerance,
                $"Pixel {i} {reference.Input(i)}: expected {(r, g, b)}, got {(actual.R, actual.G, actual.B)}.");
        }
    }

    // Expected R, G, B per pixel, row by row, in 8-bit code values.
    private static void AssertPixels(Image<Rgb24> image, float[] expected)
    {
        float worst = 0;
        int i = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++, i += 3)
            {
                Rgb24 actual = image[x, y];
                worst = MathF.Max(worst, MathF.Abs(actual.R - expected[i]));
                worst = MathF.Max(worst, MathF.Abs(actual.G - expected[i + 1]));
                worst = MathF.Max(worst, MathF.Abs(actual.B - expected[i + 2]));
            }
        }

        Assert.True(worst <= EightBitTolerance, $"Largest difference from Little CMS: {worst} code values.");
    }

    private static void AssertSamePixels<TPixel>(Image<TPixel> expected, Image<TPixel> actual)
        where TPixel : unmanaged, ImagePastel.PixelFormats.IPixel<TPixel>
    {
        Assert.Equal(expected.Size(), actual.Size());
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                Assert.Equal(expected[x, y], actual[x, y]);
            }
        }
    }

    // Little CMS, inside Magick.NET, converts the pixels from one profile to another (16-bit results).
    // Returns R, G, B per pixel in 8-bit code values.
    private static float[] ConvertWithMagick(Image<Rgb24> image, byte[] sourceProfile, byte[] targetProfile)
    {
        byte[] raw = new byte[image.Width * image.Height * 3];
        image.CopyPixelDataTo(raw);
        MagickReadSettings settings = new() { Format = MagickFormat.Rgb, Width = (uint)image.Width, Height = (uint)image.Height, Depth = 8 };
        using MagickImage magick = new(raw, settings);
        magick.RenderingIntent = RenderingIntent.Perceptual;
        magick.BlackPointCompensation = false;
        magick.TransformColorSpace(new ColorProfile(sourceProfile), new ColorProfile(targetProfile), ColorTransformMode.HighRes);

        using IUnsafePixelCollection<ushort> pixels = magick.GetPixelsUnsafe();
        ushort[] values = pixels.ToShortArray(PixelMapping.RGB);
        float[] rgb = new float[values.Length];
        for (int i = 0; i < rgb.Length; i++)
        {
            rgb[i] = values[i] / 257F;
        }

        return rgb;
    }
}
