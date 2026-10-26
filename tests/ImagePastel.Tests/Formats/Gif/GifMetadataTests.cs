// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageMagick;
using PinataLabs.ImagePastel.Formats.Gif;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using PinataLabs.ImagePastel.PixelFormats;

using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Gif;

[Trait("Format", "Gif")]
public class GifMetadataTests
{
    public static readonly TheoryData<string, int, int, PixelResolutionUnit> RatioFiles =
        new()
        {
            { TestImages.Gif.Rings, (int)ImageMetadata.DefaultHorizontalResolution, (int)ImageMetadata.DefaultVerticalResolution, PixelResolutionUnit.PixelsPerInch },
            { TestImages.Gif.Ratio1x4, 1, 4, PixelResolutionUnit.AspectRatio },
            { TestImages.Gif.Ratio4x1, 4, 1, PixelResolutionUnit.AspectRatio }
        };

    public static readonly TheoryData<string, uint> RepeatFiles =
        new()
        {
            { TestImages.Gif.Cheers, 0 },
            { TestImages.Gif.Receipt, 1 },
            { TestImages.Gif.Rings, 1 }
        };

    public static readonly TheoryData<string, byte[]> MalformedIccExtensions = new()
    {
        // The extension holds 200 zero bytes, which are not a profile.
        { "not a profile", IccExtension(new byte[200], terminated: true) },

        // The profile header declares 560 bytes, but the extension holds only the first 300.
        { "short profile", IccExtension(SnakeIccProfile().AsSpan(0, 300).ToArray(), terminated: true) },
    };

    [Fact]
    public void CloneIsDeep()
    {
        var meta = new GifMetadata
        {
            RepeatCount = 1,
            ColorTableMode = GifColorTableMode.Global,
            GlobalColorTableLength = 2,
            Comments = new List<string> { "Foo" }
        };

        var clone = (GifMetadata)meta.DeepClone();

        clone.RepeatCount = 2;
        clone.ColorTableMode = GifColorTableMode.Local;
        clone.GlobalColorTableLength = 1;

        Assert.False(meta.RepeatCount.Equals(clone.RepeatCount));
        Assert.False(meta.ColorTableMode.Equals(clone.ColorTableMode));
        Assert.False(meta.GlobalColorTableLength.Equals(clone.GlobalColorTableLength));
        Assert.False(meta.Comments.Equals(clone.Comments));
        Assert.True(meta.Comments.SequenceEqual(clone.Comments));
    }

    [Fact]
    public void Decode_IgnoreMetadataIsFalse_CommentsAreRead()
    {
        var options = new GifDecoder
        {
            IgnoreMetadata = false
        };

        var testFile = TestFile.Create(TestImages.Gif.Rings);

        using (Image<Rgba32> image = testFile.CreateRgba32Image(options))
        {
            GifMetadata metadata = image.Metadata.GetGifMetadata();
            Assert.Equal(1, metadata.Comments.Count);
            Assert.Equal("ImageSharp", metadata.Comments[0]);
        }
    }

    [Fact]
    public void Decode_IgnoreMetadataIsTrue_CommentsAreIgnored()
    {
        var options = new GifDecoder
        {
            IgnoreMetadata = true
        };

        var testFile = TestFile.Create(TestImages.Gif.Rings);

        using (Image<Rgba32> image = testFile.CreateRgba32Image(options))
        {
            GifMetadata metadata = image.Metadata.GetGifMetadata();
            Assert.Equal(0, metadata.Comments.Count);
        }
    }

    [Fact]
    public void Decode_CanDecodeLargeTextComment()
    {
        var options = new GifDecoder();
        var testFile = TestFile.Create(TestImages.Gif.LargeComment);

        using (Image<Rgba32> image = testFile.CreateRgba32Image(options))
        {
            GifMetadata metadata = image.Metadata.GetGifMetadata();
            Assert.Equal(2, metadata.Comments.Count);
            Assert.Equal(new string('c', 349), metadata.Comments[0]);
            Assert.Equal("ImageSharp", metadata.Comments[1]);
        }
    }

    [Fact]
    public void Encode_PreservesTextData()
    {
        var decoder = new GifDecoder();
        var testFile = TestFile.Create(TestImages.Gif.LargeComment);

        using (Image<Rgba32> input = testFile.CreateRgba32Image(decoder))
        using (var memoryStream = new MemoryStream())
        {
            input.Save(memoryStream, new GifEncoder());
            memoryStream.Position = 0;

            using (Image<Rgba32> image = decoder.Decode<Rgba32>(Configuration.Default, memoryStream, TestContext.Current.CancellationToken))
            {
                GifMetadata metadata = image.Metadata.GetGifMetadata();
                Assert.Equal(2, metadata.Comments.Count);
                Assert.Equal(new string('c', 349), metadata.Comments[0]);
                Assert.Equal("ImageSharp", metadata.Comments[1]);
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
            var decoder = new GifDecoder();
            IImageInfo image = decoder.Identify(Configuration.Default, stream, TestContext.Current.CancellationToken);
            ImageMetadata meta = image.Metadata;
            Assert.Equal(xResolution, meta.HorizontalResolution);
            Assert.Equal(yResolution, meta.VerticalResolution);
            Assert.Equal(resolutionUnit, meta.ResolutionUnits);
        }
    }

    [Theory]
    [MemberData(nameof(RatioFiles))]
    public void Decode_VerifyRatio(string imagePath, int xResolution, int yResolution, PixelResolutionUnit resolutionUnit)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            var decoder = new GifDecoder();
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
    [MemberData(nameof(RepeatFiles))]
    public void Identify_VerifyRepeatCount(string imagePath, uint repeatCount)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            var decoder = new GifDecoder();
            IImageInfo image = decoder.Identify(Configuration.Default, stream, TestContext.Current.CancellationToken);
            GifMetadata meta = image.Metadata.GetGifMetadata();
            Assert.Equal(repeatCount, meta.RepeatCount);
        }
    }

    [Theory]
    [MemberData(nameof(RepeatFiles))]
    public void Decode_VerifyRepeatCount(string imagePath, uint repeatCount)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            var decoder = new GifDecoder();
            using (Image<Rgba32> image = decoder.Decode<Rgba32>(Configuration.Default, stream, TestContext.Current.CancellationToken))
            {
                GifMetadata meta = image.Metadata.GetGifMetadata();
                Assert.Equal(repeatCount, meta.RepeatCount);
            }
        }
    }

    [Theory]
    [InlineData(TestImages.Gif.Issues.BadMaxLzwBits, 8)]
    [InlineData(TestImages.Gif.Issues.Issue2012BadMinCode, 1)]
    public void Identify_Frames_Bad_Lzw(string imagePath, int framesCount)
    {
        TestFile testFile = TestFile.Create(imagePath);
        using MemoryStream stream = new(testFile.Bytes, false);

        IImageInfo imageInfo = Image.Identify(stream);

        Assert.NotNull(imageInfo);
        GifMetadata gifMetadata = imageInfo.Metadata.GetGifMetadata();
        Assert.NotNull(gifMetadata);
    }

    [Fact]
    public void Decode_GifWithIccProfileWrittenByImageMagick_ReadsProfile()
    {
        // ImageMagick writes the profile as an ICCRGBG1 application extension.
        byte[] expected = ReadIccProfileWithMagick(TestFile.Create(TestImages.Jpeg.Baseline.Snake).Bytes);
        byte[] gif;
        using (var magickImage = new MagickImage(MagickColors.Red, 4, 4))
        {
            magickImage.SetProfile(new ColorProfile(expected));
            gif = magickImage.ToByteArray(MagickFormat.Gif);
        }

        using (Image image = Image.Load(gif))
        {
            Assert.Equal(expected, image.Metadata.IccProfile?.ToByteArray());
        }

        Assert.Equal(expected, Image.Identify(gif).Metadata.IccProfile?.ToByteArray());
    }

    [Fact]
    public void Decode_IgnoreMetadataIsTrue_IccProfileIsIgnored()
    {
        byte[] gif = EncodeWithIccProfile(TestImages.Jpeg.Baseline.Snake, out _);
        var options = new GifDecoder { IgnoreMetadata = true };

        using (Image image = options.Decode(Configuration.Default, new MemoryStream(gif), TestContext.Current.CancellationToken))
        {
            Assert.Null(image.Metadata.IccProfile);
        }

        Assert.Null(options.Identify(Configuration.Default, new MemoryStream(gif), TestContext.Current.CancellationToken).Metadata.IccProfile);
    }

    [Theory]
    [InlineData(TestImages.Jpeg.Baseline.Snake)]
    [InlineData(TestImages.Webp.Lossy.WithIccp)]
    public void Encode_ImageWithIccProfile_KeepsProfileByteForByte(string imagePath)
    {
        byte[] gif = EncodeWithIccProfile(imagePath, out byte[] expected);

        using (Image image = Image.Load(gif))
        {
            Assert.Equal(expected, image.Metadata.IccProfile?.ToByteArray());
        }

        Assert.Equal(expected, Image.Identify(gif).Metadata.IccProfile?.ToByteArray());

        // ImageMagick reads the ICCRGBG1 extension independently.
        Assert.Equal(expected, ReadIccProfileWithMagick(gif));
    }

    [Theory]
    [InlineData(TestImages.Gif.Rings)]
    [InlineData(TestImages.Png.GrayA8BitInterlaced)]
    public void Encode_ImageWithoutRgbIccProfile_WritesNoIccExtension(string imagePath)
    {
        // Rings has no profile. The PNG has a gray profile, which cannot describe the RGB colors of a GIF.
        using Image source = Image.Load(TestFile.Create(imagePath).Bytes);
        Assert.NotEqual(IccColorSpaceType.Rgb, source.Metadata.IccProfile?.Header.DataColorSpace);

        using var stream = new MemoryStream();
        source.SaveAsGif(stream);
        byte[] gif = stream.ToArray();

        Assert.Equal(-1, gif.AsSpan().IndexOf("ICCRGBG1"u8));
        using Image image = Image.Load(gif);
        Assert.Null(image.Metadata.IccProfile);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decode_IccExtensionBeforeOrAfterImage_ReadsProfile(bool beforeImage)
    {
        byte[] profile = SnakeIccProfile();
        byte[] extension = IccExtension(profile, terminated: true);
        byte[] gif = beforeImage
            ? OnePixelGif(extension, Array.Empty<byte>(), terminated: true)
            : OnePixelGif(Array.Empty<byte>(), extension, terminated: true);

        using (Image image = Image.Load(gif))
        {
            Assert.Equal(profile, image.Metadata.IccProfile?.ToByteArray());
        }

        Assert.Equal(profile, Image.Identify(gif).Metadata.IccProfile?.ToByteArray());
    }

    [Theory]
    [MemberData(nameof(MalformedIccExtensions))]
    public void Decode_MalformedIccExtension_IsSkipped(string description, byte[] extension)
    {
        Assert.NotNull(description);
        byte[] gif = OnePixelGif(extension, Array.Empty<byte>(), terminated: true);

        using (Image<Rgba32> image = Image.Load<Rgba32>(gif))
        {
            Assert.Null(image.Metadata.IccProfile);
            Assert.Equal(new Rgba32(255, 0, 0), image[0, 0]);
        }

        Assert.Null(Image.Identify(gif).Metadata.IccProfile);
    }

    [Theory]
    [InlineData(100)] // The stream ends inside a data sub-block.
    [InlineData(-1)] // The stream ends before the block terminator.
    public void Decode_TruncatedIccExtensionAfterImage_IsSkipped(int truncatedLength)
    {
        byte[] extension = IccExtension(SnakeIccProfile(), terminated: false);
        if (truncatedLength > 0)
        {
            extension = extension.AsSpan(0, truncatedLength).ToArray();
        }

        byte[] gif = OnePixelGif(Array.Empty<byte>(), extension, terminated: false);

        using (Image<Rgba32> image = Image.Load<Rgba32>(gif))
        {
            Assert.Null(image.Metadata.IccProfile);
            Assert.Equal(new Rgba32(255, 0, 0), image[0, 0]);
        }

        Assert.Null(Image.Identify(gif).Metadata.IccProfile);
    }

    [Theory]
    [InlineData(TestImages.Gif.Issues.BadAppExtLength)]
    [InlineData(TestImages.Gif.Issues.BadAppExtLength_2)]
    public void Decode_IccExtensionWithBadBlockSize_IsSkipped(string imagePath)
    {
        // These files declare an application block size of 252 instead of 11 for their ICCRGBG1 extension.
        byte[] gif = TestFile.Create(imagePath).Bytes;

        using (Image image = Image.Load(gif))
        {
            Assert.Null(image.Metadata.IccProfile);
        }

        Assert.Null(Image.Identify(gif).Metadata.IccProfile);
    }

    private static byte[] SnakeIccProfile() => ReadIccProfileWithMagick(TestFile.Create(TestImages.Jpeg.Baseline.Snake).Bytes);

    // Builds an ICCRGBG1 application extension as the ICC specification's annex on embedding profiles describes it.
    private static byte[] IccExtension(byte[] profile, bool terminated)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 0x21, 0xFF, 11 });
        stream.Write("ICCRGBG1012"u8);
        for (int i = 0; i < profile.Length; i += 255)
        {
            int length = Math.Min(255, profile.Length - i);
            stream.WriteByte((byte)length);
            stream.Write(profile, i, length);
        }

        if (terminated)
        {
            stream.WriteByte(0);
        }

        return stream.ToArray();
    }

    // Builds a 1x1 GIF89a whose single red pixel is preceded and followed by the given blocks.
    private static byte[] OnePixelGif(byte[] blocksBeforeImage, byte[] blocksAfterImage, bool terminated)
    {
        using var stream = new MemoryStream();
        stream.Write("GIF89a"u8);

        // Logical screen descriptor: 1x1 with a 2-entry global color table of red and black.
        stream.Write(new byte[] { 1, 0, 1, 0, 0x80, 0, 0, 255, 0, 0, 0, 0, 0 });
        stream.Write(blocksBeforeImage);

        // Image descriptor, then LZW minimum code size 2 and one sub-block holding the codes clear, 0, end.
        stream.Write(new byte[] { 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0 });
        stream.Write(new byte[] { 2, 2, 0x44, 0x01, 0 });
        stream.Write(blocksAfterImage);

        if (terminated)
        {
            stream.WriteByte(0x3B);
        }

        return stream.ToArray();
    }

    private static byte[] EncodeWithIccProfile(string imagePath, out byte[] profile)
    {
        using Image source = Image.Load(TestFile.Create(imagePath).Bytes);
        profile = source.Metadata.IccProfile.ToByteArray();

        using var stream = new MemoryStream();
        source.SaveAsGif(stream);
        return stream.ToArray();
    }

    private static byte[] ReadIccProfileWithMagick(byte[] encoded)
    {
        using var magickImage = new MagickImage(encoded);
        return magickImage.GetColorProfile()?.ToByteArray();
    }
}
