// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using ImageMagick;
using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Png;
using SixLabors.ImageSharp.Tests.Processing.Processors.ColorManagement;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats;

/// <summary>
/// Decodes JPEG, PNG, TIFF and WebP files with the <c>ColorProfileConversion</c> decoder option.
/// Expected colours come from Little CMS: the <c>transicc</c> tables in <see cref="LittleCmsReferences"/>
/// for files encoded losslessly here, and Little CMS inside Magick.NET for the lossy JPEG.
/// </summary>
[Trait("Profile", "Icc")]
public class DecoderColorProfileConversionTests
{
    // About one code value at 8 bits: the conversion runs in float and rounds once at the end.
    private const float EightBitTolerance = 1F;

    public static TheoryData<string> LosslessFormats { get; } = new() { "png", "tiff", "webp" };

    public static TheoryData<string> AllFormats { get; } = new() { "jpeg", "png", "tiff", "webp" };

    [Theory]
    [InlineData("png", TestImages.Icc.CompactAdobeRgbV2)]
    [InlineData("tiff", TestImages.Icc.CompactProPhotoV4)]
    [InlineData("webp", TestImages.Icc.CompactDisplayP3V4)]
    [InlineData("png", TestImages.Icc.QcmsRec2020)]
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
    [InlineData("gAMA 1", TestImages.Icc.CompactLinearSrgbV2)]
    public void Decode_Png_ColorSpaceChunks_ToSrgb_MatchesLittleCms(string chunks, string sameSpaceProfile)
    {
        LittleCmsReference reference = LittleCmsReferences.Get(sameSpaceProfile, null);
        byte[][] colorChunks = chunks switch
        {
            "cICP Display P3" => new[] { PngTestFileBuilder.Cicp(12, 13) },
            "cHRM+gAMA Adobe RGB" => new[] { PngTestFileBuilder.Chromaticities(31270, 32900, 64000, 33000, 21000, 71000, 15000, 6000), PngTestFileBuilder.Gamma(45471) },
            _ => new[] { PngTestFileBuilder.Gamma(100000) },
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
    public void Decode_LutProfile_LeavesPixelsAndProfileAsTheyAre(string format)
    {
        // The sRGB v4 ICC preference profile (mAB/mBA tables) from a committed WebP file: not supported until LUT profiles are.
        IccProfile lut = Image.Identify(TestFile.GetInputFileFullPath(TestImages.Webp.Lossy.WithIccp)).Metadata.IccProfile;
        byte[] file = CreateFileWithProfile(format, lut);
        using Image<Rgb24> expected = Decode<Rgb24>(format, file, ignoreMetadata: true);

        using Image<Rgb24> image = Decode<Rgb24>(format, file, ColorProfileConversion.ToSrgb);

        AssertSamePixels(expected, image);
        Assert.Equal(lut.ToByteArray(), image.Metadata.IccProfile.ToByteArray());
    }

    [Fact]
    public void Decode_Jpeg_CmykProfile_LeavesPixelsAndProfileAsTheyAre()
    {
        // ISO Coated v2 (ECI): a CMYK profile, which the decoded RGB pixels no longer match.
        byte[] file = File.ReadAllBytes(TestFile.GetInputFileFullPath(TestImages.Jpeg.Baseline.Cmyk));
        using Image<Rgb24> expected = Decode<Rgb24>("jpeg", file);

        using Image<Rgb24> image = Decode<Rgb24>("jpeg", file, ColorProfileConversion.ToSrgb);

        AssertSamePixels(expected, image);
        Assert.Equal(IccColorSpaceType.Cmyk, image.Metadata.IccProfile.DataColorSpace);
        Assert.Equal(expected.Metadata.IccProfile.ToByteArray(), image.Metadata.IccProfile.ToByteArray());
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
        IccProfile cmyk = Image.Identify(TestFile.GetInputFileFullPath(TestImages.Jpeg.Baseline.Cmyk)).Metadata.IccProfile;
        byte[] file = CreateFileWithProfile(format, TestImages.Icc.CompactAdobeRgbV2);

        Assert.Throws<NotSupportedException>(() => Decode<Rgb24>(format, file, ColorProfileConversion.ToTargetProfile, cmyk));
    }

    private static IccProfile LoadProfile(string file) => ConvertColorProfileProcessorTests.LoadProfile(file);

    private static IImageDecoder CreateDecoder(string format, ColorProfileConversion conversion, IccProfile target, bool ignoreMetadata)
        => format switch
        {
            "jpeg" => new JpegDecoder { ColorProfileConversion = conversion, ColorProfileConversionTarget = target, IgnoreMetadata = ignoreMetadata },
            "png" => new PngDecoder { ColorProfileConversion = conversion, ColorProfileConversionTarget = target, IgnoreMetadata = ignoreMetadata },
            "tiff" => new TiffDecoder { ColorProfileConversion = conversion, ColorProfileConversionTarget = target, IgnoreMetadata = ignoreMetadata },
            "webp" => new WebpDecoder { ColorProfileConversion = conversion, ColorProfileConversionTarget = target, IgnoreMetadata = ignoreMetadata },
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    private static Image<TPixel> Decode<TPixel>(string format, byte[] file, ColorProfileConversion conversion = ColorProfileConversion.None, IccProfile target = null, bool ignoreMetadata = false)
        where TPixel : unmanaged, ImageSharp.PixelFormats.IPixel<TPixel>
        => Image.Load<TPixel>(file, CreateDecoder(format, conversion, target, ignoreMetadata));

    private static IImageEncoder CreateEncoder(string format)
        => format switch
        {
            "jpeg" => new JpegEncoder { Quality = 90 },
            "png" => new PngEncoder(),
            "tiff" => new TiffEncoder(),
            "webp" => new WebpEncoder { FileFormat = WebpFileFormatType.Lossless },
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    private static byte[] Encode(string format, Image<Rgb24> image, IccProfile profile)
    {
        using (image)
        {
            image.Metadata.IccProfile = profile;
            using MemoryStream stream = new();
            image.Save(stream, CreateEncoder(format));
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

    private static void AssertSamePixels<TPixel>(Image<TPixel> expected, Image<TPixel> actual)
        where TPixel : unmanaged, ImageSharp.PixelFormats.IPixel<TPixel>
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
