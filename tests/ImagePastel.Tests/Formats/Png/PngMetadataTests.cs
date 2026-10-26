// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp.Compression.Zlib;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Png;

[Trait("Format", "Png")]
public partial class PngMetadataTests
{
    public static readonly TheoryData<string, int, int, PixelResolutionUnit> RatioFiles =
        new TheoryData<string, int, int, PixelResolutionUnit>
        {
            { TestImages.Png.Splash, 11810, 11810, PixelResolutionUnit.PixelsPerMeter },
            { TestImages.Png.Ratio1x4, 1, 4, PixelResolutionUnit.AspectRatio },
            { TestImages.Png.Ratio4x1, 4, 1, PixelResolutionUnit.AspectRatio }
        };

    [Fact]
    public void CloneIsDeep()
    {
        var meta = new PngMetadata
        {
            BitDepth = PngBitDepth.Bit16,
            ColorType = PngColorType.GrayscaleWithAlpha,
            InterlaceMethod = PngInterlaceMode.Adam7,
            Gamma = 2,
            TextData = new List<PngTextData> { new PngTextData("name", "value", "foo", "bar") }
        };

        var clone = (PngMetadata)meta.DeepClone();

        clone.BitDepth = PngBitDepth.Bit2;
        clone.ColorType = PngColorType.Palette;
        clone.InterlaceMethod = PngInterlaceMode.None;
        clone.Gamma = 1;

        Assert.False(meta.BitDepth == clone.BitDepth);
        Assert.False(meta.ColorType == clone.ColorType);
        Assert.False(meta.InterlaceMethod == clone.InterlaceMethod);
        Assert.False(meta.Gamma.Equals(clone.Gamma));
        Assert.False(meta.TextData.Equals(clone.TextData));
        Assert.True(meta.TextData.SequenceEqual(clone.TextData));
    }

    [Theory]
    [WithFile(TestImages.Png.PngWithMetadata, PixelTypes.Rgba32)]
    public void Decoder_CanReadTextData<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(new PngDecoder()))
        {
            PngMetadata meta = image.Metadata.GetFormatMetadata(PngFormat.Instance);
            VerifyTextDataIsPresent(meta);
        }
    }

    [Theory]
    [WithFile(TestImages.Png.PngWithMetadata, PixelTypes.Rgba32)]
    public void Encoder_PreservesTextData<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var decoder = new PngDecoder();
        using (Image<TPixel> input = provider.GetImage(decoder))
        using (var memoryStream = new MemoryStream())
        {
            input.Save(memoryStream, new PngEncoder());

            memoryStream.Position = 0;
            using (Image<Rgba32> image = decoder.Decode<Rgba32>(Configuration.Default, memoryStream, TestContext.Current.CancellationToken))
            {
                PngMetadata meta = image.Metadata.GetFormatMetadata(PngFormat.Instance);
                VerifyTextDataIsPresent(meta);
            }
        }
    }

    [Theory]
    [WithFile(TestImages.Png.InvalidTextData, PixelTypes.Rgba32)]
    public void Decoder_IgnoresInvalidTextData<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(new PngDecoder()))
        {
            PngMetadata meta = image.Metadata.GetFormatMetadata(PngFormat.Instance);
            Assert.DoesNotContain(meta.TextData, m => m.Value is "leading space");
            Assert.DoesNotContain(meta.TextData, m => m.Value is "trailing space");
            Assert.DoesNotContain(meta.TextData, m => m.Value is "space");
            Assert.DoesNotContain(meta.TextData, m => m.Value is "empty");
            Assert.DoesNotContain(meta.TextData, m => m.Value is "invalid characters");
            Assert.DoesNotContain(meta.TextData, m => m.Value is "too large");
        }
    }

    [Theory]
    [WithFile(TestImages.Png.PngWithMetadata, PixelTypes.Rgba32)]
    public void Encode_UseCompression_WhenTextIsGreaterThenThreshold_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var decoder = new PngDecoder();
        using (Image<TPixel> input = provider.GetImage(decoder))
        using (var memoryStream = new MemoryStream())
        {
            // This will be a zTXt chunk.
            var expectedText = new PngTextData("large-text", new string('c', 100), string.Empty, string.Empty);

            // This will be a iTXt chunk.
            var expectedTextNoneLatin = new PngTextData("large-text-non-latin", new string('Ф', 100), "language-tag", "translated-keyword");
            PngMetadata inputMetadata = input.Metadata.GetFormatMetadata(PngFormat.Instance);
            inputMetadata.TextData.Add(expectedText);
            inputMetadata.TextData.Add(expectedTextNoneLatin);
            input.Save(memoryStream, new PngEncoder
            {
                TextCompressionThreshold = 50
            });

            memoryStream.Position = 0;
            using (Image<Rgba32> image = decoder.Decode<Rgba32>(Configuration.Default, memoryStream, TestContext.Current.CancellationToken))
            {
                PngMetadata meta = image.Metadata.GetFormatMetadata(PngFormat.Instance);
                Assert.Contains(meta.TextData, m => m.Equals(expectedText));
                Assert.Contains(meta.TextData, m => m.Equals(expectedTextNoneLatin));
            }
        }
    }

    [Theory]
    [WithFile(TestImages.Png.PngWithMetadata, PixelTypes.Rgba32)]
    public void Decode_ReadsExifData<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var decoder = new PngDecoder
        {
            IgnoreMetadata = false
        };

        using (Image<TPixel> image = provider.GetImage(decoder))
        {
            Assert.NotNull(image.Metadata.ExifProfile);
            ExifProfile exif = image.Metadata.ExifProfile;
            VerifyExifDataIsPresent(exif);
        }
    }

    [Theory]
    [WithFile(TestImages.Png.PngWithMetadata, PixelTypes.Rgba32)]
    public void Decode_IgnoresExifData_WhenIgnoreMetadataIsTrue<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var decoder = new PngDecoder
        {
            IgnoreMetadata = true
        };

        using (Image<TPixel> image = provider.GetImage(decoder))
        {
            Assert.Null(image.Metadata.ExifProfile);
        }
    }

    [Fact]
    public void Decode_IgnoreMetadataIsFalse_TextChunkIsRead()
    {
        var options = new PngDecoder
        {
            IgnoreMetadata = false
        };

        var testFile = TestFile.Create(TestImages.Png.Blur);

        using (Image<Rgba32> image = testFile.CreateRgba32Image(options))
        {
            PngMetadata meta = image.Metadata.GetFormatMetadata(PngFormat.Instance);

            Assert.Equal(1, meta.TextData.Count);
            Assert.Equal("Software", meta.TextData[0].Keyword);
            Assert.Equal("paint.net 4.0.6", meta.TextData[0].Value);
            Assert.Equal(0.4545d, meta.Gamma, precision: 4);
        }
    }

    [Fact]
    public void Decode_IgnoreMetadataIsTrue_TextChunksAreIgnored()
    {
        var options = new PngDecoder
        {
            IgnoreMetadata = true
        };

        var testFile = TestFile.Create(TestImages.Png.PngWithMetadata);

        using (Image<Rgba32> image = testFile.CreateRgba32Image(options))
        {
            PngMetadata meta = image.Metadata.GetFormatMetadata(PngFormat.Instance);
            Assert.Equal(0, meta.TextData.Count);
        }
    }

    [Theory]
    [MemberData(nameof(RatioFiles))]
    public void Decode_VerifyRatio(string imagePath, int xResolution, int yResolution, PixelResolutionUnit resolutionUnit)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            var decoder = new PngDecoder();
            using (Image<Rgba32> image = decoder.Decode<Rgba32>(Configuration.Default, stream, TestContext.Current.CancellationToken))
            {
                ImageMetadata meta = image.Metadata;
                Assert.Equal(xResolution, meta.HorizontalResolution);
                Assert.Equal(yResolution, meta.VerticalResolution);
                Assert.Equal(resolutionUnit, meta.ResolutionUnits);
            }
        }
    }

    [Theory]
    [MemberData(nameof(RatioFiles))]
    public void Identify_VerifyRatio(string imagePath, int xResolution, int yResolution, PixelResolutionUnit resolutionUnit)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            var decoder = new PngDecoder();
            IImageInfo image = decoder.Identify(Configuration.Default, stream, TestContext.Current.CancellationToken);
            ImageMetadata meta = image.Metadata;
            Assert.Equal(xResolution, meta.HorizontalResolution);
            Assert.Equal(yResolution, meta.VerticalResolution);
            Assert.Equal(resolutionUnit, meta.ResolutionUnits);
        }
    }

    [Theory]
    [InlineData(TestImages.Png.PngWithMetadata)]
    public void Identify_ReadsTextData(string imagePath)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            IImageInfo imageInfo = Image.Identify(stream);
            Assert.NotNull(imageInfo);
            PngMetadata meta = imageInfo.Metadata.GetFormatMetadata(PngFormat.Instance);
            VerifyTextDataIsPresent(meta);
        }
    }

    [Theory]
    [InlineData(TestImages.Png.PngWithMetadata)]
    public void Identify_ReadsExifData(string imagePath)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            IImageInfo imageInfo = Image.Identify(stream);
            Assert.NotNull(imageInfo);
            Assert.NotNull(imageInfo.Metadata.ExifProfile);
            ExifProfile exif = imageInfo.Metadata.ExifProfile;
            VerifyExifDataIsPresent(exif);
        }
    }

    private static void VerifyExifDataIsPresent(ExifProfile exif)
    {
        Assert.Equal(1, exif.Values.Count);
        IExifValue<string> software = exif.GetValue(ExifTag.Software);
        Assert.NotNull(software);
        Assert.Equal("ImageSharp", software.Value);
    }

    private static void VerifyTextDataIsPresent(PngMetadata meta)
    {
        Assert.NotNull(meta);
        Assert.Contains(meta.TextData, m => m.Keyword is "Comment" && m.Value is "comment");
        Assert.Contains(meta.TextData, m => m.Keyword is "Author" && m.Value is "ImageSharp");
        Assert.Contains(meta.TextData, m => m.Keyword is "Copyright" && m.Value is "ImageSharp");
        Assert.Contains(meta.TextData, m => m.Keyword is "Title" && m.Value is "unittest");
        Assert.Contains(meta.TextData, m => m.Keyword is "Description" && m.Value is "compressed-text");
        Assert.Contains(meta.TextData, m => m.Keyword is "International" && m.Value is "'e', mu'tlheghvam, ghaH yu'" && m.LanguageTag is "x-klingon" && m.TranslatedKeyword is "warning");
        Assert.Contains(meta.TextData, m => m.Keyword is "International2" && m.Value is "ИМАГЕШАРП" && m.LanguageTag is "rus");
        Assert.Contains(meta.TextData, m => m.Keyword is "CompressedInternational" && m.Value is "la plume de la mante" && m.LanguageTag is "fra" && m.TranslatedKeyword is "foobar");
        Assert.Contains(meta.TextData, m => m.Keyword is "CompressedInternational2" && m.Value is "這是一個考驗" && m.LanguageTag is "chinese");
        Assert.Contains(meta.TextData, m => m.Keyword is "NoLang" && m.Value is "this text chunk is missing a language tag");
        Assert.Contains(meta.TextData, m => m.Keyword is "NoTranslatedKeyword" && m.Value is "dieser chunk hat kein übersetztes Schlüßelwort");
    }

    [Theory]
    [InlineData(TestImages.Png.Issue1875)]
    public void Identify_ReadsLegacyExifData(string imagePath)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            IImageInfo imageInfo = Image.Identify(stream);
            Assert.NotNull(imageInfo);
            Assert.NotNull(imageInfo.Metadata.ExifProfile);

            PngMetadata meta = imageInfo.Metadata.GetFormatMetadata(PngFormat.Instance);
            Assert.DoesNotContain(meta.TextData, t => t.Keyword.Equals("Raw profile type exif", StringComparison.OrdinalIgnoreCase));

            ExifProfile exif = imageInfo.Metadata.ExifProfile;
            Assert.Equal(0, exif.InvalidTags.Count);
            Assert.Equal(3, exif.Values.Count);

            Assert.Equal(
                "A colorful tiling of blue, red, yellow, and green 4x4 pixel blocks.",
                exif.GetValue(ExifTag.ImageDescription).Value);
            Assert.Equal(
                "Duplicated from basn3p02.png, then image metadata modified with exiv2",
                exif.GetValue(ExifTag.ImageHistory).Value);

            Assert.Equal(42, (int)exif.GetValue(ExifTag.ImageNumber).Value);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decode_PaletteImage_ReportsTransparencyFromTransparencyChunk(bool transparent)
    {
        using var image = new Image<Rgba32>(32, 32, transparent ? Color.Transparent : Color.Blue);
        for (int y = 8; y < 24; y++)
        {
            for (int x = 8; x < 24; x++)
            {
                image[x, y] = Color.Red;
            }
        }

        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder { ColorType = PngColorType.Palette });
        byte[] png = stream.ToArray();
        Assert.Equal(transparent, PngTestFileBuilder.ReadChunks(png).Any(c => c.Type == "tRNS"));

        PngMetadata identified = Image.Identify(png).Metadata.GetPngMetadata();
        Assert.Equal(PngColorType.Palette, identified.ColorType);
        Assert.Equal(transparent, identified.HasTransparency);

        using Image loaded = Image.Load(png);
        PngMetadata decoded = loaded.Metadata.GetPngMetadata();
        Assert.Equal(PngColorType.Palette, decoded.ColorType);
        Assert.Equal(transparent, decoded.HasTransparency);
    }

    [Fact]
    public void Decode_16BitGrayscaleTransparency_IsBigEndian()
    {
        byte[] png = PngTestFileBuilder.Build(
            PngTestFileBuilder.Header(2, 1, 16, PngColorType.Grayscale),
            PngTestFileBuilder.Chunk("tRNS", new byte[] { 0x01, 0x02 }),
            PngTestFileBuilder.Chunk("IDAT", PngTestFileBuilder.Zlib(new byte[] { 0, 0x01, 0x02, 0x02, 0x01 })),
            PngTestFileBuilder.End());

        Assert.Equal(new L16(0x0102), Image.Identify(png).Metadata.GetPngMetadata().TransparentL16);

        using Image<Rgba64> image = Image.Load<Rgba64>(png);
        Assert.Equal(new L16(0x0102), image.Metadata.GetPngMetadata().TransparentL16);
        Assert.Equal(ushort.MinValue, image[0, 0].A);
        Assert.Equal(ushort.MaxValue, image[1, 0].A);
    }

    [Fact]
    public void Decode_16BitRgbTransparency_IsBigEndian()
    {
        byte[] pixels = { 0, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x02, 0x01, 0x04, 0x03, 0x06, 0x05 };
        byte[] png = PngTestFileBuilder.Build(
            PngTestFileBuilder.Header(2, 1, 16, PngColorType.Rgb),
            PngTestFileBuilder.Chunk("tRNS", new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 }),
            PngTestFileBuilder.Chunk("IDAT", PngTestFileBuilder.Zlib(pixels)),
            PngTestFileBuilder.End());

        var expected = new Rgb48(0x0102, 0x0304, 0x0506);
        Assert.Equal(expected, Image.Identify(png).Metadata.GetPngMetadata().TransparentRgb48);

        using Image<Rgba64> image = Image.Load<Rgba64>(png);
        Assert.Equal(expected, image.Metadata.GetPngMetadata().TransparentRgb48);
        Assert.Equal(ushort.MinValue, image[0, 0].A);
        Assert.Equal(ushort.MaxValue, image[1, 0].A);
    }

    [Theory]
    [InlineData(TestImages.Png.Ducky, 3144, IccColorSpaceType.Rgb)]
    [InlineData(TestImages.Png.GrayA8BitInterlaced, 400, IccColorSpaceType.Gray)]
    public void Decode_ReadsIccProfile(string imagePath, uint expectedSize, IccColorSpaceType expectedColorSpace)
    {
        var testFile = TestFile.Create(imagePath);

        using (Image<Rgba32> image = testFile.CreateRgba32Image())
        {
            IccProfile profile = image.Metadata.IccProfile;
            Assert.NotNull(profile);
            Assert.Equal(expectedSize, profile.Header.Size);
            Assert.Equal(expectedColorSpace, profile.Header.DataColorSpace);
        }

        IImageInfo info = Image.Identify(testFile.Bytes);
        Assert.Equal(expectedSize, info.Metadata.IccProfile?.Header.Size);
    }

    [Theory]
    [InlineData(TestImages.Jpeg.Baseline.Snake)]
    [InlineData(TestImages.Webp.Lossy.WithIccp)]
    [InlineData(TestImages.Webp.Lossless.WithIccp)]
    public void Encode_ImageWithIccProfile_KeepsProfileByteForByte(string imagePath)
    {
        using Image source = Image.Load(TestFile.Create(imagePath).Bytes);
        IccProfile sourceProfile = source.Metadata.IccProfile;
        Assert.NotNull(sourceProfile);
        byte[] expected = sourceProfile.ToByteArray();

        using var stream = new MemoryStream();
        source.SaveAsPng(stream);
        byte[] png = stream.ToArray();

        using (Image image = Image.Load(png))
        {
            Assert.Equal(expected, image.Metadata.IccProfile?.ToByteArray());
        }

        Assert.Equal(expected, Image.Identify(png).Metadata.IccProfile?.ToByteArray());
    }

    [Fact]
    public void Encode_TiffWithIccProfile_KeepsProfileByteForByte()
    {
        byte[] expected;
        using var tiffStream = new MemoryStream();
        using (Image jpeg = Image.Load(TestFile.Create(TestImages.Jpeg.Baseline.Snake).Bytes))
        {
            expected = jpeg.Metadata.IccProfile.ToByteArray();
            jpeg.SaveAsTiff(tiffStream);
        }

        using Image tiff = Image.Load(tiffStream.ToArray());
        Assert.Equal(expected, tiff.Metadata.IccProfile?.ToByteArray());

        using var stream = new MemoryStream();
        tiff.SaveAsPng(stream);

        using Image image = Image.Load(stream.ToArray());
        Assert.Equal(expected, image.Metadata.IccProfile?.ToByteArray());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Identify_Palette_SetsHasTransparency(bool transparent, bool makeTrnsOpaque)
    {
        using MemoryStream stream = EncodePalettePng(transparent, makeTrnsOpaque);

        IImageInfo imageInfo = Image.Identify(stream);
        PngMetadata meta = imageInfo.Metadata.GetPngMetadata();

        Assert.Equal(PngColorType.Palette, meta.ColorType);
        Assert.Equal(transparent && !makeTrnsOpaque, meta.HasTransparency);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Decode_Palette_SetsHasTransparency(bool transparent, bool makeTrnsOpaque)
    {
        using MemoryStream stream = EncodePalettePng(transparent, makeTrnsOpaque);

        using Image image = Image.Load(stream);
        PngMetadata meta = image.Metadata.GetPngMetadata();

        Assert.Equal(PngColorType.Palette, meta.ColorType);
        Assert.Equal(transparent && !makeTrnsOpaque, meta.HasTransparency);
    }

    private static MemoryStream EncodePalettePng(bool transparent, bool makeTrnsOpaque)
    {
        using Image<Rgba32> image = new(4, 4, Color.Red.ToPixel<Rgba32>());
        if (transparent)
        {
            image[1, 1] = Color.Transparent.ToPixel<Rgba32>();
        }

        using MemoryStream encoded = new();
        image.SaveAsPng(encoded, new PngEncoder { ColorType = PngColorType.Palette });
        byte[] bytes = encoded.ToArray();

        if (makeTrnsOpaque)
        {
            // Keep the tRNS chunk but mark every palette entry as fully opaque.
            int typeIndex = bytes.AsSpan().IndexOf("tRNS"u8);
            Assert.True(typeIndex >= 4);

            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(typeIndex - 4, 4));
            Span<byte> data = bytes.AsSpan(typeIndex + 4, length);
            data.Fill(byte.MaxValue);

            uint crc = Crc32.Calculate(bytes.AsSpan(typeIndex, 4));
            crc = Crc32.Calculate(crc, data);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(typeIndex + 4 + length, 4), crc);
        }

        return new MemoryStream(bytes, false);
    }
}
