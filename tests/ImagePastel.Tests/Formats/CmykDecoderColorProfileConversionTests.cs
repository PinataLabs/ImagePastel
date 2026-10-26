// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using ImageMagick;
using PinataLabs.ImagePastel.ColorManagement;
using PinataLabs.ImagePastel.Formats;
using PinataLabs.ImagePastel.Formats.Jpeg;
using PinataLabs.ImagePastel.Formats.Jpeg.Components;
using PinataLabs.ImagePastel.Formats.Jpeg.Components.Decoder;
using PinataLabs.ImagePastel.Formats.Jpeg.Components.Decoder.ColorConverters;
using PinataLabs.ImagePastel.Formats.Tiff;
using PinataLabs.ImagePastel.Formats.Tiff.Constants;
using PinataLabs.ImagePastel.IO;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Processing.Processors.ColorManagement;
using Xunit;
using ExifTag = PinataLabs.ImagePastel.Metadata.Profiles.Exif.ExifTag;

namespace PinataLabs.ImagePastel.Tests.Formats;

/// <summary>
/// Decodes Adobe CMYK and YCCK JPEGs and <c>Separated</c> TIFFs with the <c>ColorProfileConversion</c>
/// decoder option. With an embedded CMYK profile the samples go through that profile; without one
/// the CMYK-to-RGB formula stays. Expected colours come from Little CMS inside Magick.NET, which
/// converts the CMYK samples from the embedded profile, perceptual, without asking for black-point
/// compensation (which Little CMS, like the engine, still applies when the target is version 4).
/// </summary>
/// <remarks>
/// <para>
/// For TIFF, ImageMagick reads the file itself. For JPEG, libjpeg (in ImageMagick) and this decoder
/// differ by a few sample values after the inverse DCT and chroma upsampling, so the samples are the
/// ones this decoder produces before its color conversion: Adobe CMYK samples are inverted
/// (0 is full ink, Adobe Technical Note 5116), and YCCK is turned into inverted CMY with the JFIF
/// (ITU-T T.871) equations, rounded and range-limited as libjpeg does.
/// </para>
/// <para>
/// The target for the references is Little CMS's sRGB written as a version 2 profile: with a
/// version 4 target and the perceptual intent, Little CMS always turns black-point compensation on.
/// </para>
/// </remarks>
[Trait("Profile", "Icc")]
public class CmykDecoderColorProfileConversionTests
{
    // About one code value at 8 bits.
    private const float EightBitTolerance = 1F;

    // CMYK and YCCK JPEGs with a press profile (ISO Coated v2 and U.S. Web Coated (SWOP) v2), and a
    // TIFF with the synthetic profile of Tiff/Cmyk.
    public static TheoryData<string> FilesWithCmykProfile { get; } = new()
    {
        TestImages.Jpeg.Baseline.Cmyk,
        TestImages.Jpeg.Baseline.Ycck,
        TestImages.Tiff.Cmyk8BitWithIccProfile,
    };

    // A CMYK JPEG, a YCCK JPEG (4:2:2 chroma) and 8 and 16-bit TIFFs, none with a profile.
    public static TheoryData<string> FilesWithoutProfile { get; } = new()
    {
        TestImages.Jpeg.Baseline.MultiScanBaselineCMYK,
        TestImages.Jpeg.Baseline.YcckSubsample1222,
        TestImages.Tiff.Cmyk8BitContiguous,
        TestImages.Tiff.Cmyk16BitPlanarBigEndian,
    };

    [Theory]
    [MemberData(nameof(FilesWithCmykProfile))]
    public void Decode_ToTargetProfile_MatchesLittleCmsAndSetsTargetAsPixelProfile(string file)
    {
        byte[] bytes = ReadFile(file);
        IccProfile target = LoadProfile(TestImages.Icc.LittleCmsSrgbV2);
        float[] expected = Expected(bytes, target);

        using Image<Rgb24> image = Decode<Rgb24>(bytes, ColorProfileConversion.ToTargetProfile, target);

        AssertPixels(image, expected);
        Assert.Equal(target.ToByteArray(), image.Metadata.IccProfile.ToByteArray());
    }

    [Theory]
    [MemberData(nameof(FilesWithCmykProfile))]
    public void Decode_ToSrgb_MatchesLittleCmsAndLeavesNoPixelProfile(string file)
    {
        // The engine's sRGB counts as a version 4 profile (perceptual forces black-point compensation,
        // as in Little CMS), so the reference targets the v4 sRGB; the two agree to well under a tenth
        // of a code value.
        byte[] bytes = ReadFile(file);
        float[] expected = Expected(bytes, LoadProfile(TestImages.Icc.LittleCmsSrgbV4));

        using Image<Rgb24> image = Decode<Rgb24>(bytes, ColorProfileConversion.ToSrgb);

        AssertPixels(image, expected);
        Assert.Null(image.Metadata.IccProfile);
        if (file.EndsWith(".tiff", StringComparison.Ordinal))
        {
            Assert.Null(image.Frames.RootFrame.Metadata.IccProfile);
            Assert.Null(image.Frames.RootFrame.Metadata.ExifProfile.GetValue(ExifTag.IccProfile));
        }
    }

    [Theory]
    [InlineData(TestImages.Jpeg.Baseline.MultiScanBaselineCMYK)]
    [InlineData(TestImages.Jpeg.Baseline.YcckSubsample1222)]
    public void Decode_JpegWithSyntheticProfile_ToSrgb_MatchesLittleCms(string file)
    {
        // The profile-less JPEGs, with the redistributable test profile of Tiff/Cmyk added as APP2.
        byte[] bytes = WithJpegIccProfile(ReadFile(file), ReadFile(TestImages.Icc.TiffCmykTestProfile));
        float[] expected = Expected(bytes, LoadProfile(TestImages.Icc.LittleCmsSrgbV4));

        using Image<Rgb24> image = Decode<Rgb24>(bytes, ColorProfileConversion.ToSrgb);

        AssertPixels(image, expected);
        Assert.Null(image.Metadata.IccProfile);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decode_Tiff16Bit_ToSrgb_KeepsPrecision(bool planar)
    {
        // A planar file with the lut16 test profile, and a chunky one that ImageMagick writes from a
        // 16-bit test file with the synthetic FakeCmyk-v4 profile (lutAToBType).
        byte[] bytes = planar
            ? ReadFile(TestImages.Tiff.Cmyk16BitPlanarWithIccProfile)
            : WithTiffIccProfile(ReadFile(TestImages.Tiff.Cmyk16BitContiguousBigEndian), ReadFile(TestImages.Icc.LittleCmsFakeCmykV4));
        ushort[] expected = ConvertWithMagick16(bytes, LoadProfile(TestImages.Icc.LittleCmsSrgbV4));

        using Image<Rgb48> image = Decode<Rgb48>(bytes, ColorProfileConversion.ToSrgb);

        // A quarter of an 8-bit code value: the 16-bit samples are not narrowed to 8 bits on the way.
        const float tolerance = 257 / 4F;
        float worst = 0;
        int i = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++, i += 3)
            {
                Rgb48 actual = image[x, y];
                worst = MathF.Max(worst, MathF.Abs(actual.R - expected[i]));
                worst = MathF.Max(worst, MathF.Abs(actual.G - expected[i + 1]));
                worst = MathF.Max(worst, MathF.Abs(actual.B - expected[i + 2]));
            }
        }

        Assert.True(worst <= tolerance, $"Largest difference from Little CMS: {worst} 16-bit code values.");
        Assert.Null(image.Metadata.IccProfile);
        TiffPlanarConfiguration layout = planar ? TiffPlanarConfiguration.Planar : TiffPlanarConfiguration.Chunky;
        Assert.Equal((ushort)layout, image.Frames.RootFrame.Metadata.ExifProfile.GetValue(ExifTag.PlanarConfiguration).Value);
        Assert.Equal(16, image.Frames.RootFrame.Metadata.GetTiffMetadata().BitsPerSample.Value.Channel0);
    }

    [Theory]
    [MemberData(nameof(FilesWithoutProfile))]
    public void Decode_WithoutProfile_UsesTheFormulaAsBefore(string file)
    {
        byte[] bytes = ReadFile(file);
        using Image<Rgba64> expected = Decode<Rgba64>(bytes, ColorProfileConversion.None);

        using Image<Rgba64> toSrgb = Decode<Rgba64>(bytes, ColorProfileConversion.ToSrgb);
        using Image<Rgba64> toTarget = Decode<Rgba64>(bytes, ColorProfileConversion.ToTargetProfile, LoadProfile(TestImages.Icc.CompactDisplayP3V4));

        AssertSamePixels(expected, toSrgb);
        AssertSamePixels(expected, toTarget);
        Assert.Null(toSrgb.Metadata.IccProfile);
        Assert.Null(toTarget.Metadata.IccProfile);
    }

    [Theory]
    [MemberData(nameof(FilesWithCmykProfile))]
    public void Decode_DefaultOption_UsesTheFormulaAndKeepsTheProfile(string file)
    {
        byte[] bytes = ReadFile(file);
        using Image<Rgb24> formula = Decode<Rgb24>(bytes, ignoreMetadata: true);

        using Image<Rgb24> image = Decode<Rgb24>(bytes, ColorProfileConversion.None, LoadProfile(TestImages.Icc.LittleCmsSrgbV2));

        AssertSamePixels(formula, image);
        AssertCmykProfileIsOnlyEmbedded(image, bytes);
    }

    [Theory]
    [MemberData(nameof(FilesWithCmykProfile))]
    public void Decode_IgnoreMetadata_UsesTheFormula(string file)
    {
        byte[] bytes = ReadFile(file);
        using Image<Rgb24> formula = Decode<Rgb24>(bytes);

        using Image<Rgb24> image = Decode<Rgb24>(bytes, ColorProfileConversion.ToSrgb, ignoreMetadata: true);

        AssertSamePixels(formula, image);
        Assert.Null(image.Metadata.IccProfile);
    }

    [Theory]
    [InlineData(TestImages.Jpeg.Baseline.MultiScanBaselineCMYK)]
    [InlineData(TestImages.Tiff.Cmyk8BitContiguous)]
    public void Decode_UnusableCmykProfile_UsesTheFormulaAndKeepsTheProfile(string file)
    {
        // The synthetic CMYK profile without its A2B0 table, which a CMYK profile needs.
        IccProfile complete = LoadProfile(TestImages.Icc.TiffCmykTestProfile);
        byte[] unusable = new IccProfile(complete.Header, Array.FindAll(complete.Entries, e => e.TagSignature != IccProfileTag.AToB0)).ToByteArray();
        byte[] bytes = file.EndsWith(".jpg", StringComparison.Ordinal)
            ? WithJpegIccProfile(ReadFile(file), unusable)
            : WithTiffIccProfile(ReadFile(file), unusable);
        using Image<Rgb24> formula = Decode<Rgb24>(bytes);

        using Image<Rgb24> image = Decode<Rgb24>(bytes, ColorProfileConversion.ToSrgb);

        AssertSamePixels(formula, image);
        AssertCmykProfileIsOnlyEmbedded(image, bytes);
        Assert.Equal(unusable, EmbeddedIccProfile(image.Metadata, bytes).ToByteArray());
    }

    [Theory]
    [MemberData(nameof(FilesWithCmykProfile))]
    public void Identify_WithConversion_ReportsTheEmbeddedProfile(string file)
    {
        byte[] bytes = ReadFile(file);
        IImageInfoDetector detector = (IImageInfoDetector)CreateDecoder(bytes, ColorProfileConversion.ToSrgb, null, false);

        using MemoryStream stream = new(bytes);
        IImageInfo info = detector.Identify(Configuration.Default, stream, TestContext.Current.CancellationToken);

        Assert.Null(info.Metadata.IccProfile);
        Assert.Equal(IccColorSpaceType.Cmyk, EmbeddedIccProfile(info.Metadata, bytes).DataColorSpace);
    }

    [Theory]
    [MemberData(nameof(FilesWithCmykProfile))]
    public void Decode_WithConversion_KeepsTheEmbeddedProfile(string file)
    {
        byte[] bytes = ReadFile(file);
        IccProfile target = LoadProfile(TestImages.Icc.LittleCmsSrgbV2);

        using Image<Rgb24> toSrgb = Decode<Rgb24>(bytes, ColorProfileConversion.ToSrgb);
        using Image<Rgb24> toTarget = Decode<Rgb24>(bytes, ColorProfileConversion.ToTargetProfile, target);

        Assert.Equal(IccColorSpaceType.Cmyk, EmbeddedIccProfile(toSrgb.Metadata, bytes).DataColorSpace);
        Assert.Equal(IccColorSpaceType.Cmyk, EmbeddedIccProfile(toTarget.Metadata, bytes).DataColorSpace);
    }

    // A decoded CMYK image has RGB pixels, so its CMYK profile is never the pixel profile; it stays
    // reachable as the format's embedded ICC profile.
    private static void AssertCmykProfileIsOnlyEmbedded(Image image, byte[] bytes)
    {
        Assert.Null(image.Metadata.IccProfile);
        Assert.Equal(IccColorSpaceType.Cmyk, EmbeddedIccProfile(image.Metadata, bytes).DataColorSpace);
        if (bytes[0] != 0xFF)
        {
            ImagePastel.Metadata.ImageFrameMetadata frame = image.Frames.RootFrame.Metadata;
            Assert.Null(frame.IccProfile);
            Assert.Null(frame.ExifProfile.GetValue(ExifTag.IccProfile));
            Assert.Equal(IccColorSpaceType.Cmyk, frame.GetTiffMetadata().EmbeddedIccProfile.DataColorSpace);
        }
    }

    private static IccProfile EmbeddedIccProfile(ImagePastel.Metadata.ImageMetadata metadata, byte[] bytes)
        => bytes[0] == 0xFF ? metadata.GetJpegMetadata().EmbeddedIccProfile : metadata.GetTiffMetadata().EmbeddedIccProfile;

    private static byte[] ReadFile(string file) => File.ReadAllBytes(TestFile.GetInputFileFullPath(file));

    private static IccProfile LoadProfile(string file) => ConvertColorProfileProcessorTests.LoadProfile(file);

    private static IImageDecoder CreateDecoder(byte[] bytes, ColorProfileConversion conversion, IccProfile target, bool ignoreMetadata)
        => bytes[0] == 0xFF
            ? new JpegDecoder { ColorProfileConversion = conversion, ColorProfileConversionTarget = target, IgnoreMetadata = ignoreMetadata }
            : new TiffDecoder { ColorProfileConversion = conversion, ColorProfileConversionTarget = target, IgnoreMetadata = ignoreMetadata };

    private static Image<TPixel> Decode<TPixel>(byte[] bytes, ColorProfileConversion conversion = ColorProfileConversion.None, IccProfile target = null, bool ignoreMetadata = false)
        where TPixel : unmanaged, ImagePastel.PixelFormats.IPixel<TPixel>
        => Image.Load<TPixel>(bytes, CreateDecoder(bytes, conversion, target, ignoreMetadata));

    // Adds an ICC profile to a JPEG as APP2 ICC_PROFILE segments right after SOI (ICC.1 annex B.4).
    private static byte[] WithJpegIccProfile(byte[] jpeg, byte[] profile)
    {
        const int maxChunk = 65535 - 2 - 14;
        int count = (profile.Length + maxChunk - 1) / maxChunk;
        using MemoryStream stream = new();
        stream.Write(jpeg, 0, 2);
        for (int i = 0; i < count; i++)
        {
            int start = i * maxChunk;
            int length = Math.Min(maxChunk, profile.Length - start);
            int segmentLength = 2 + 14 + length;
            stream.Write([0xFF, 0xE2, (byte)(segmentLength >> 8), (byte)segmentLength]);
            stream.Write(Encoding.ASCII.GetBytes("ICC_PROFILE\0"));
            stream.Write([(byte)(i + 1), (byte)count]);
            stream.Write(profile, start, length);
        }

        stream.Write(jpeg, 2, jpeg.Length - 2);
        return stream.ToArray();
    }

    // Little CMS's colors for the CMYK samples of a JPEG or TIFF file, as R, G, B per pixel in 8-bit code values.
    private static float[] Expected(byte[] file, IccProfile target)
    {
        if (file[0] != 0xFF)
        {
            return ConvertWithMagick(file, target);
        }

        byte[] inks = ReadJpegInks(file, out int width, out int height);
        IccProfile embedded = Image.Identify(file).Metadata.GetJpegMetadata().EmbeddedIccProfile;
        MagickReadSettings settings = new() { Format = MagickFormat.Cmyk, Width = (uint)width, Height = (uint)height, Depth = 8 };
        using MagickImage magick = new(inks, settings);
        return ConvertWithMagick(magick, new ColorProfile(embedded.ToByteArray()), target);
    }

    // The ink amounts (0 = no ink, 255 = full ink) of a CMYK or YCCK JPEG, from the samples this
    // decoder has after the inverse DCT and upsampling, row by row as C, M, Y, K.
    private static byte[] ReadJpegInks(byte[] jpeg, out int width, out int height)
    {
        using MemoryStream memory = new(jpeg);
        using BufferedReadStream stream = new(Configuration.Default, memory);
        using SampleRecorder recorder = new();
        using JpegDecoderCore decoder = new(Configuration.Default, new JpegDecoder());
        decoder.ParseStream(stream, new HuffmanScanDecoder(stream, recorder, CancellationToken.None), CancellationToken.None);
        using (Buffer2D<Rgb24> pixels = recorder.GetPixelBuffer(CancellationToken.None))
        {
            width = pixels.Width;
            height = pixels.Height;
        }

        Assert.Equal(height, recorder.Rows.Count);
        byte[] inks = new byte[width * height * 4];
        int i = 0;
        foreach (float[][] row in recorder.Rows)
        {
            for (int x = 0; x < width; x++, i += 4)
            {
                if (recorder.ColorSpace == JpegColorSpace.Ycck)
                {
                    float y = row[0][x];
                    float cb = row[1][x] - 128;
                    float cr = row[2][x] - 128;
                    inks[i] = RangeLimit(y + (1.402F * cr));
                    inks[i + 1] = RangeLimit(y - (0.344136F * cb) - (0.714136F * cr));
                    inks[i + 2] = RangeLimit(y + (1.772F * cb));
                }
                else
                {
                    Assert.Equal(JpegColorSpace.Cmyk, recorder.ColorSpace);
                    inks[i] = (byte)(255 - row[0][x]);
                    inks[i + 1] = (byte)(255 - row[1][x]);
                    inks[i + 2] = (byte)(255 - row[2][x]);
                }

                inks[i + 3] = (byte)(255 - row[3][x]);
            }
        }

        return inks;
    }

    private static byte RangeLimit(float value) => (byte)Math.Clamp(MathF.Round(value, MidpointRounding.AwayFromZero), 0, 255);

    // ImageMagick writes a Separated TIFF again, chunky, with the given profile, keeping its samples and depth.
    private static byte[] WithTiffIccProfile(byte[] tiff, byte[] profile)
    {
        using MagickImage magick = new(tiff);
        Assert.Equal(ColorSpace.CMYK, magick.ColorSpace);
        magick.SetProfile(new ColorProfile(profile));
        return magick.ToByteArray(MagickFormat.Tiff);
    }

    // Little CMS, inside Magick.NET, converts the file's CMYK samples from its embedded profile.
    private static ushort[] ConvertWithMagick16(byte[] file, IccProfile target)
    {
        using MagickImage magick = new(file);
        IColorProfile embedded = magick.GetColorProfile();
        Assert.NotNull(embedded);
        return ConvertWithMagick16(magick, embedded, target);
    }

    private static ushort[] ConvertWithMagick16(MagickImage magick, IColorProfile source, IccProfile target)
    {
        Assert.Equal(ColorSpace.CMYK, magick.ColorSpace);
        magick.RenderingIntent = RenderingIntent.Perceptual;
        magick.BlackPointCompensation = false;
        magick.TransformColorSpace(source, new ColorProfile(target.ToByteArray()), ColorTransformMode.HighRes);

        using IUnsafePixelCollection<ushort> pixels = magick.GetPixelsUnsafe();
        return pixels.ToShortArray(PixelMapping.RGB);
    }

    // Returns R, G, B per pixel in 8-bit code values (from 16-bit results).
    private static float[] ConvertWithMagick(byte[] file, IccProfile target) => ToEightBit(ConvertWithMagick16(file, target));

    private static float[] ConvertWithMagick(MagickImage magick, IColorProfile source, IccProfile target) => ToEightBit(ConvertWithMagick16(magick, source, target));

    private static float[] ToEightBit(ushort[] values)
    {
        float[] rgb = new float[values.Length];
        for (int i = 0; i < rgb.Length; i++)
        {
            rgb[i] = values[i] / 257F;
        }

        return rgb;
    }

    private static void AssertPixels(Image<Rgb24> image, float[] expected)
    {
        Assert.Equal(image.Width * image.Height * 3, expected.Length);
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

    // Records the samples the JPEG decoder hands to its color converter, one row at a time.
    private sealed class SampleRecorder : SpectralConverter<Rgb24>
    {
        public SampleRecorder()
            : base(Configuration.Default)
        {
        }

        public List<float[][]> Rows { get; } = [];

        public JpegColorSpace ColorSpace { get; private set; }

        protected override JpegColorConverterBase GetColorConverter(JpegFrame frame, IRawJpegData jpegData)
        {
            this.ColorSpace = jpegData.ColorSpace;
            return new Converter(this, jpegData.ColorSpace, frame.Precision);
        }

        private sealed class Converter : JpegColorConverterBase
        {
            private readonly SampleRecorder recorder;

            public Converter(SampleRecorder recorder, JpegColorSpace colorSpace, int precision)
                : base(colorSpace, precision)
                => this.recorder = recorder;

            public override bool IsAvailable => true;

            public override void ConvertToRgbInplace(in ComponentValues values)
                => this.recorder.Rows.Add([values.Component0.ToArray(), values.Component1.ToArray(), values.Component2.ToArray(), values.Component3.ToArray()]);
        }
    }
}
