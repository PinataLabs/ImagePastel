// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using PinataLabs.ImagePastel.Formats.Bmp;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Memory;
using PinataLabs.ImagePastel.Tests.TestUtilities;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using PinataLabs.ImagePastel.Tests.TestUtilities.ReferenceCodecs;

using Xunit;
using static PinataLabs.ImagePastel.Tests.TestImages.Bmp;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests.Formats.Bmp;

[Trait("Format", "Bmp")]
[ValidateDisposedMemoryAllocations]
public class BmpDecoderTests
{
    public const PixelTypes CommonNonDefaultPixelTypes = PixelTypes.Rgba32 | PixelTypes.Bgra32 | PixelTypes.RgbaVector;

    public static readonly string[] MiscBmpFiles = Miscellaneous;

    public static readonly string[] BitfieldsBmpFiles = BitFields;

    private static BmpDecoder BmpDecoder => new BmpDecoder();

    public static readonly TheoryData<string, int, int, PixelResolutionUnit> RatioFiles =
    new TheoryData<string, int, int, PixelResolutionUnit>
    {
        { Car, 3780, 3780, PixelResolutionUnit.PixelsPerMeter },
        { V5Header, 3780, 3780, PixelResolutionUnit.PixelsPerMeter },
        { RLE8, 2835, 2835, PixelResolutionUnit.PixelsPerMeter }
    };

    [Theory]
    [WithFileCollection(nameof(MiscBmpFiles), PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_MiscellaneousBitmaps<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage(BmpDecoder);
        image.DebugSave(provider);

        if (TestEnvironment.IsWindows)
        {
            image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(Car, PixelTypes.Rgba32)]
    [WithFile(F, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_MiscellaneousBitmaps_WithLimitedAllocatorBufferCapacity(
        TestImageProvider<Rgba32> provider)
    {
        static void RunTest(string providerDump, string nonContiguousBuffersStr)
        {
            TestImageProvider<Rgba32> provider = BasicSerializer.Deserialize<TestImageProvider<Rgba32>>(providerDump);

            provider.LimitAllocatorBufferCapacity().InPixelsSqrt(100);

            using Image<Rgba32> image = provider.GetImage(BmpDecoder);
            image.DebugSave(provider, nonContiguousBuffersStr);

            if (TestEnvironment.IsWindows)
            {
                image.CompareToOriginal(provider);
            }
        }

        string providerDump = BasicSerializer.Serialize(provider);
        RemoteExecutorRunner.Invoke(
                RunTest,
                providerDump,
                "Disco")
            .Dispose();
    }

    [Theory]
    [WithFile(Bit32Rgb, PixelTypes.Rgba32)]
    [WithFile(Bit16, PixelTypes.Rgba32)]
    public void BmpDecoder_DegenerateMemoryRequest_ShouldTranslateTo_ImageFormatException<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        provider.LimitAllocatorBufferCapacity().InPixelsSqrt(10);
        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => provider.GetImage(BmpDecoder));
        Assert.IsType<InvalidMemoryOperationException>(ex.InnerException);
    }

    [Theory]
    [WithFileCollection(nameof(BitfieldsBmpFiles), PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecodeBitfields<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            CompareToOriginalOrReferenceOutput(image, provider);
        }
    }

    [Theory]
    [WithFile(Bit16Inverted, PixelTypes.Rgba32)]
    [WithFile(Bit8Inverted, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_Inverted<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            CompareToOriginalOrReferenceOutput(image, provider);
        }
    }

    [Theory]
    [WithFile(Bit1, PixelTypes.Rgba32)]
    [WithFile(Bit1Pal1, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_1Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            if (TestEnvironment.IsWindows)
            {
                image.CompareToOriginal(provider, new SystemDrawingReferenceDecoder());
            }
        }
    }

    [Theory]
    [WithFile(Bit4, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_4Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(Bit8, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_8Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(Bit16, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_16Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            CompareToOriginalOrReferenceOutput(image, provider);
        }
    }

    [Theory]
    [WithFile(Bit32Rgb, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_32Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(Rgba32v4, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_32BitV4Header_Fast<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(RLE4Cut, PixelTypes.Rgba32)]
    [WithFile(RLE4Delta, PixelTypes.Rgba32)]
    [WithFile(Rle4Delta320240, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_RunLengthEncoded_4Bit_WithDelta<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        RleSkippedPixelHandling skippedPixelHandling = TestEnvironment.IsWindows ? RleSkippedPixelHandling.Black : RleSkippedPixelHandling.FirstColorOfPalette;
        using (Image<TPixel> image = provider.GetImage(new BmpDecoder { RleSkippedPixelHandling = skippedPixelHandling }))
        {
            image.DebugSave(provider);
            CompareToOriginalOrReferenceOutput(image, provider);
        }
    }

    [Theory]
    [WithFile(RLE4, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_RunLengthEncoded_4Bit<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        RleSkippedPixelHandling skippedPixelHandling = TestEnvironment.IsWindows ? RleSkippedPixelHandling.Black : RleSkippedPixelHandling.FirstColorOfPalette;
        using (Image<TPixel> image = provider.GetImage(new BmpDecoder { RleSkippedPixelHandling = skippedPixelHandling }))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(RLE8Cut, PixelTypes.Rgba32)]
    [WithFile(RLE8Delta, PixelTypes.Rgba32)]
    [WithFile(Rle8Delta320240, PixelTypes.Rgba32)]
    [WithFile(Rle8Blank160120, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_RunLengthEncoded_8Bit_WithDelta_SystemDrawingRefDecoder<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(new BmpDecoder { RleSkippedPixelHandling = RleSkippedPixelHandling.Black }))
        {
            image.DebugSave(provider);
            if (TestEnvironment.IsWindows)
            {
                image.CompareToOriginal(provider, new SystemDrawingReferenceDecoder());
            }
        }
    }

    [Theory]
    [WithFile(RLE8Delta, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_RunLengthEncoded_8Bit_WithDelta_MagickRefDecoder<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(new BmpDecoder { RleSkippedPixelHandling = RleSkippedPixelHandling.FirstColorOfPalette }))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider, new MagickReferenceDecoder());
        }
    }

    // Magick.NET 14+ rejects this truncated file, so the reference output was captured from
    // ImageSharp after it matched Magick.NET 11.1.2.
    [Theory]
    [WithFile(RLE8Cut, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_RunLengthEncoded_8Bit_Cut<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(new BmpDecoder { RleSkippedPixelHandling = RleSkippedPixelHandling.FirstColorOfPalette }))
        {
            image.DebugSave(provider);
            image.CompareToReferenceOutput(ImageComparer.Exact, provider);
        }
    }

    [Theory]
    [WithFile(RLE8, PixelTypes.Rgba32, false)]
    [WithFile(RLE8, PixelTypes.Rgba32, true)]
    public void BmpDecoder_CanDecode_RunLengthEncoded_8Bit<TPixel>(TestImageProvider<TPixel> provider, bool enforceDiscontiguousBuffers)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (enforceDiscontiguousBuffers)
        {
            provider.LimitAllocatorBufferCapacity().InBytesSqrt(400);
        }

        using (Image<TPixel> image = provider.GetImage(new BmpDecoder { RleSkippedPixelHandling = RleSkippedPixelHandling.FirstColorOfPalette }))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider, new MagickReferenceDecoder());
        }
    }

    // Magick.NET 14+ rejects top-down RLE bitmaps, so the reference output was captured from
    // ImageSharp after it matched Magick.NET 11.1.2.
    [Theory]
    [WithFile(RLE8Inverted, PixelTypes.Rgba32, false)]
    [WithFile(RLE8Inverted, PixelTypes.Rgba32, true)]
    public void BmpDecoder_CanDecode_RunLengthEncoded_8Bit_Inverted<TPixel>(TestImageProvider<TPixel> provider, bool enforceDiscontiguousBuffers)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (enforceDiscontiguousBuffers)
        {
            provider.LimitAllocatorBufferCapacity().InBytesSqrt(400);
        }

        using (Image<TPixel> image = provider.GetImage(new BmpDecoder { RleSkippedPixelHandling = RleSkippedPixelHandling.FirstColorOfPalette }))
        {
            image.DebugSave(provider);
            image.CompareToReferenceOutput(ImageComparer.Exact, provider);
        }
    }

    [Theory]
    [WithFile(RLE24, PixelTypes.Rgba32, false)]
    [WithFile(RLE24Cut, PixelTypes.Rgba32, false)]
    [WithFile(RLE24Delta, PixelTypes.Rgba32, false)]
    [WithFile(RLE24, PixelTypes.Rgba32, true)]
    [WithFile(RLE24Cut, PixelTypes.Rgba32, true)]
    [WithFile(RLE24Delta, PixelTypes.Rgba32, true)]
    public void BmpDecoder_CanDecode_RunLengthEncoded_24Bit<TPixel>(TestImageProvider<TPixel> provider, bool enforceNonContiguous)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (enforceNonContiguous)
        {
            provider.LimitAllocatorBufferCapacity().InBytesSqrt(400);
        }

        using (Image<TPixel> image = provider.GetImage(new BmpDecoder { RleSkippedPixelHandling = RleSkippedPixelHandling.Black }))
        {
            image.DebugSave(provider);

            // TODO: Neither System.Drawing nor MagickReferenceDecoder decode this file.
            // image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(RgbaAlphaBitfields, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecodeAlphaBitfields<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);

            // TODO: Neither System.Drawing nor MagickReferenceDecoder decode this file.
            // image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(Bit32Rgba, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecodeBitmap_WithAlphaChannel<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider, new MagickReferenceDecoder());
        }
    }

    [Theory]
    [WithFile(Rgba321010102, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecodeBitfields_WithUnusualBitmasks<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);

            // Choosing large tolerance of 6.1 here, because for some reason with the MagickReferenceDecoder the alpha channel
            // seems to be wrong. This bitmap has an alpha channel of two bits. In many cases this alpha channel has a value of 3,
            // which should be remapped to 255 for RGBA32, but the magick decoder has a value of 191 set.
            // The total difference without the alpha channel is still: 0.0204%
            // Exporting the image as PNG with GIMP yields to the same result as the ImageSharp implementation.
            image.CompareToOriginal(provider, ImageComparer.TolerantPercentage(6.1f), new MagickReferenceDecoder());
        }
    }

    [Theory]
    [WithFile(WinBmpv2, PixelTypes.Rgba32)]
    [WithFile(CoreHeader, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecodeBmpv2<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);

            // Do not validate. Reference files will fail validation.
            image.CompareToOriginal(provider, new MagickReferenceDecoder(false));
        }
    }

    [Theory]
    [WithFile(WinBmpv3, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecodeBmpv3<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(LessThanFullSizedPalette, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecodeLessThanFullPalette<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider, new MagickReferenceDecoder());
        }
    }

    [Theory]
    [WithFile(OversizedPalette, PixelTypes.Rgba32)]
    [WithFile(Rgb24LargePalette, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecodeOversizedPalette<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            if (TestEnvironment.IsWindows)
            {
                image.CompareToOriginal(provider);
            }
        }
    }

    [Theory]
    [WithFile(InvalidPaletteSize, PixelTypes.Rgba32)]
    public void BmpDecoder_ThrowsInvalidImageContentException_OnInvalidPaletteSize<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Assert.Throws<InvalidImageContentException>(() =>
        {
            using (provider.GetImage(BmpDecoder))
            {
            }
        });
    }

    [Theory]
    [WithFile(Rgb24jpeg, PixelTypes.Rgba32)]
    [WithFile(Rgb24png, PixelTypes.Rgba32)]
    public void BmpDecoder_ThrowsNotSupportedException_OnUnsupportedBitmaps<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Assert.Throws<NotSupportedException>(() =>
        {
            using (provider.GetImage(BmpDecoder))
            {
            }
        });
    }

    [Theory]
    [WithFile(Rgb32h52AdobeV3, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecodeAdobeBmpv3<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider, new MagickReferenceDecoder());
        }
    }

    [Theory]
    [WithFile(Rgba32bf56AdobeV3, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecodeAdobeBmpv3_WithAlpha<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider, new MagickReferenceDecoder());
        }
    }

    [Theory]
    [WithFile(WinBmpv4, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecodeBmpv4<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(WinBmpv5, PixelTypes.Rgba32)]
    [WithFile(V5Header, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecodeBmpv5<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(Pal8Offset, PixelTypes.Rgba32)]
    public void BmpDecoder_RespectsFileHeaderOffset<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(F, CommonNonDefaultPixelTypes)]
    public void BmpDecoder_IsNotBoundToSinglePixelType<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(Bit8Palette4, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode4BytePerEntryPalette<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [InlineData(Bit32Rgb, 32)]
    [InlineData(Bit32Rgba, 32)]
    [InlineData(Car, 24)]
    [InlineData(F, 24)]
    [InlineData(NegHeight, 24)]
    [InlineData(Bit16, 16)]
    [InlineData(Bit16Inverted, 16)]
    [InlineData(Bit8, 8)]
    [InlineData(Bit8Inverted, 8)]
    [InlineData(Bit4, 4)]
    [InlineData(Bit1, 1)]
    [InlineData(Bit1Pal1, 1)]
    public void Identify_DetectsCorrectPixelType(string imagePath, int expectedPixelSize)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            IImageInfo imageInfo = Image.Identify(stream);
            Assert.NotNull(imageInfo);
            Assert.Equal(expectedPixelSize, imageInfo.PixelType?.BitsPerPixel);
        }
    }

    [Theory]
    [InlineData(Bit32Rgb, 127, 64)]
    [InlineData(Car, 600, 450)]
    [InlineData(Bit16, 127, 64)]
    [InlineData(Bit16Inverted, 127, 64)]
    [InlineData(Bit8, 127, 64)]
    [InlineData(Bit8Inverted, 127, 64)]
    [InlineData(RLE8, 491, 272)]
    [InlineData(RLE8Inverted, 491, 272)]
    public void Identify_DetectsCorrectWidthAndHeight(string imagePath, int expectedWidth, int expectedHeight)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            IImageInfo imageInfo = Image.Identify(stream);
            Assert.NotNull(imageInfo);
            Assert.Equal(expectedWidth, imageInfo.Width);
            Assert.Equal(expectedHeight, imageInfo.Height);
        }
    }

    [Theory]
    [MemberData(nameof(RatioFiles))]
    public void Decode_VerifyRatio(string imagePath, int xResolution, int yResolution, PixelResolutionUnit resolutionUnit)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            var decoder = new BmpDecoder();
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
    [WithFile(Os2v2Short, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_Os2v2XShortHeader<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);

            // TODO: Neither System.Drawing or MagickReferenceDecoder can correctly decode this file.
            // image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(Os2v2, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_Os2v2Header<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);

            // TODO: System.Drawing can not decode this image. MagickReferenceDecoder can decode it,
            // but i think incorrectly. I have loaded the image with GIMP and exported as PNG.
            // The results are the same as the image sharp implementation.
            // image.CompareToOriginal(provider, new MagickReferenceDecoder());
        }
    }

    [Theory]
    [WithFile(Os2BitmapArray, PixelTypes.Rgba32)]
    [WithFile(Os2BitmapArray9s, PixelTypes.Rgba32)]
    [WithFile(Os2BitmapArrayDiamond, PixelTypes.Rgba32)]
    [WithFile(Os2BitmapArraySkater, PixelTypes.Rgba32)]
    [WithFile(Os2BitmapArraySpade, PixelTypes.Rgba32)]
    [WithFile(Os2BitmapArraySunflower, PixelTypes.Rgba32)]
    [WithFile(Os2BitmapArrayMarble, PixelTypes.Rgba32)]
    [WithFile(Os2BitmapArrayWarpd, PixelTypes.Rgba32)]
    [WithFile(Os2BitmapArrayPines, PixelTypes.Rgba32)]
    public void BmpDecoder_CanDecode_Os2BitmapArray<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(BmpDecoder))
        {
            image.DebugSave(provider);

            // TODO: Neither System.Drawing or MagickReferenceDecoder can correctly decode this file.
            // image.CompareToOriginal(provider);
        }
    }

    [Theory]
    [WithFile(Issue2696, PixelTypes.Rgba32)]
    public void BmpDecoder_ThrowsException_Issue2696<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // The width is above the maximum allowed by BmpInfoHeader.VerifyDimensions().
        Assert.Throws<InvalidImageContentException>(() =>
            {
                using Image<TPixel> image = provider.GetImage(BmpDecoder);
            });
    }

    // https://github.com/SixLabors/ImageSharp/issues/3074
    [Fact]
    public void BmpDecoder_ThrowsException_Issue3074()
    {
        // Crafted BMP: pixel data offset = 0x7FFFFFFF, actual file = 35 bytes
        byte[] data =
        {
            0x42, 0x4D,                         // "BM" signature
            0x3A, 0x00, 0x00, 0x00,             // file size: 58
            0x00, 0x00, 0x00, 0x00,             // reserved
            0xFF, 0xFF, 0xFF, 0x7F,             // pixel offset: 0x7FFFFFFF (2,147,483,647)
            0x28, 0x00, 0x00, 0x00,             // DIB header size: 40
            0x01, 0x00, 0x00, 0x00,             // width: 1
            0x01, 0xFF, 0x00, 0x00,             // height: 65281
            0x01, 0x00,                         // color planes: 1
            0x08, 0x00,                         // bits per pixel: 8
            0x00, 0x00, 0x00, 0x00,             // compression: RGB
            0x00, 0x00, 0x00 // (truncated)
        };

        using MemoryStream stream = new(data);

        Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(stream);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(64)]
    public void BmpDecoder_ThrowsInvalidImageContentException_OnInvalidBitsPerPixel(short bitsPerPixel)
    {
        byte[] data = CreateBmp(width: 2, height: 2, bitsPerPixel, pixelDataLength: 8);

        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));
        Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(data);
        });
    }

    [Theory]
    [InlineData((int)BmpCompression.RLE8, 4)]
    [InlineData((int)BmpCompression.RLE8, 24)]
    [InlineData((int)BmpCompression.RLE4, 8)]
    [InlineData((int)BmpCompression.BitFields, 1)]
    [InlineData((int)BmpCompression.BitFields, 24)]
    [InlineData((int)BmpCompression.BI_ALPHABITFIELDS, 8)]
    [InlineData((int)BmpCompression.JPEG, 3)]
    [InlineData((int)BmpCompression.PNG, 7)]
    public void BmpDecoder_ThrowsInvalidImageContentException_OnBitsPerPixelInvalidForCompression(int compression, short bitsPerPixel)
    {
        byte[] data = CreateBmp(width: 2, height: 2, bitsPerPixel, pixelDataLength: 64, compression: (BmpCompression)compression);

        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));
        Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(data);
        });
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(-5, 2)]
    [InlineData(int.MinValue, 2)]
    [InlineData(2, int.MinValue)]
    [InlineData(65536, 1)]
    [InlineData(1, 65536)]
    [InlineData(1, -65536)]
    [InlineData(100000, 2)]
    [InlineData(int.MaxValue, 2)]
    public void BmpDecoder_ThrowsInvalidImageContentException_OnInvalidDimensions(int width, int height)
    {
        byte[] data = CreateBmp(width, height, bitsPerPixel: 24, pixelDataLength: 72);

        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));
        Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(data);
        });
    }

    [Fact]
    public void BmpDecoder_ThrowsInvalidImageContentException_BeforeAllocating_WhenPixelDataIsTooShort()
    {
        // 4000x4000 at 24 bits per pixel declares 48 MB of pixel data, but the file has 72 bytes.
        byte[] data = CreateBmp(width: 4000, height: 4000, bitsPerPixel: 24, pixelDataLength: 72);

        var allocator = new TestMemoryAllocator();
        allocator.EnableNonThreadSafeLogging();
        Configuration configuration = Configuration.Default.Clone();
        configuration.MemoryAllocator = allocator;

        Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(configuration, data);
        });

        long allocatedBytes = 0;
        foreach (TestMemoryAllocator.AllocationRequest request in allocator.AllocationLog)
        {
            allocatedBytes += request.LengthInBytes;
        }

        Assert.True(allocatedBytes < 1024 * 1024, $"Allocated {allocatedBytes} bytes.");
    }

    [Theory]
    [InlineData(24, 3, 2, 24, true)] // Rows of 9 bytes, padded to 12.
    [InlineData(24, 3, 2, 21, true)] // Only the padding of the last row is missing.
    [InlineData(24, 3, 2, 20, false)]
    [InlineData(24, 3, 2, 12, false)]
    [InlineData(1, 33, 2, 13, true)] // Rows of 5 bytes, padded to 8.
    [InlineData(1, 33, 2, 12, false)]
    [InlineData(16, 3, 5, 38, true)] // Rows of 6 bytes, padded to 8.
    [InlineData(16, 3, 5, 37, false)]
    [InlineData(32, 2, 3, 24, true)] // Rows of 8 bytes, no padding.
    [InlineData(32, 2, 3, 23, false)]
    public void BmpDecoder_RequiresPixelDataForEveryRow(short bitsPerPixel, int width, int height, int pixelDataLength, bool isValid)
    {
        byte[] data = CreateBmp(width, height, bitsPerPixel, pixelDataLength, paletteLength: bitsPerPixel <= 8 ? 2 : 0);

        if (isValid)
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(data);
            Assert.Equal(new Size(width, height), image.Size());
        }
        else
        {
            Assert.Throws<InvalidImageContentException>(() =>
            {
                using Image<Rgba32> image = Image.Load<Rgba32>(data);
            });
        }
    }

    [Theory]
    [InlineData((int)BmpCompression.RLE8, 8)]
    [InlineData((int)BmpCompression.RLE4, 4)]
    [InlineData((int)BmpCompression.RLE24, 24)]
    public void BmpDecoder_ThrowsInvalidImageContentException_BeforeAllocating_WhenRleDataIsTooShort(int compression, short bitsPerPixel)
    {
        // 4000x4000 with only an end of bitmap command. The RLE8 file is 64 bytes.
        byte[] data = CreateRleBmp(width: 4000, height: 4000, bitsPerPixel, (BmpCompression)compression, 0x00, 0x01);

        var allocator = new TestMemoryAllocator();
        allocator.EnableNonThreadSafeLogging();
        Configuration configuration = Configuration.Default.Clone();
        configuration.MemoryAllocator = allocator;

        Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(configuration, data);
        });

        long allocatedBytes = 0;
        foreach (TestMemoryAllocator.AllocationRequest request in allocator.AllocationLog)
        {
            allocatedBytes += request.LengthInBytes;
        }

        Assert.True(allocatedBytes < 1024 * 1024, $"Allocated {allocatedBytes} bytes.");
    }

    [Theory]
    [InlineData((int)BmpCompression.RLE8, 8)]
    [InlineData((int)BmpCompression.RLE4, 4)]
    [InlineData((int)BmpCompression.RLE24, 24)]
    public void BmpDecoder_DecodesRleBitmap_ThatSkipsMostRowsWithDeltas(int compression, short bitsPerPixel)
    {
        // Four delta commands skip 999 of the 1000 rows, and the end of bitmap command skips the last row.
        // This is 18 bytes of RLE data. The same bitmap with only the end of bitmap command is rejected.
        byte[] rleData =
        {
            0x00, 0x02, 0x00, 0xFF,
            0x00, 0x02, 0x00, 0xFF,
            0x00, 0x02, 0x00, 0xFF,
            0x00, 0x02, 0x00, 0xEA,
            0x00, 0x01
        };
        byte[] deltas = CreateRleBmp(width: 100, height: 1000, bitsPerPixel, (BmpCompression)compression, rleData);
        byte[] endOfBitmapOnly = CreateRleBmp(width: 100, height: 1000, bitsPerPixel, (BmpCompression)compression, 0x00, 0x01);

        using (Image<Rgba32> image = Image.Load<Rgba32>(deltas))
        {
            Assert.Equal(new Size(100, 1000), image.Size());
        }

        Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(endOfBitmapOnly);
        });
    }

    [Theory]
    [InlineData(10, 280, true)] // 2 bytes of RLE data may cover 255 * (10 + 1) = 2805 pixels.
    [InlineData(10, 281, false)]
    [InlineData(1, 510, true)] // 255 * (1 + 1) = 510 pixels.
    [InlineData(1, 511, false)]
    public void BmpDecoder_BoundsRleBitmapSize_ByRleDataLength(int width, int height, bool isValid)
    {
        byte[] data = CreateRleBmp(width, height, bitsPerPixel: 8, BmpCompression.RLE8, 0x00, 0x01);

        if (isValid)
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(data);
            Assert.Equal(new Size(width, height), image.Size());
        }
        else
        {
            Assert.Throws<InvalidImageContentException>(() =>
            {
                using Image<Rgba32> image = Image.Load<Rgba32>(data);
            });
        }
    }

    [Theory]
    [InlineData((int)BmpCompression.RLE8, 8, 65535, 257)] // 65535 * 65535 bytes.
    [InlineData((int)BmpCompression.RLE24, 24, 26755, 105)] // 26755 * 26755 * 3 bytes.
    public void BmpDecoder_ThrowsInvalidImageContentException_BeforeAllocating_WhenRleBufferIsTooLarge(int compression, short bitsPerPixel, int size, int deltaCount)
    {
        // The deltas give the bitmap enough RLE data for its size, but the uncompressed data is over int.MaxValue bytes.
        byte[] rleData = new byte[(deltaCount * 4) + 2];
        for (int i = 0; i < deltaCount; i++)
        {
            rleData[(i * 4) + 1] = 0x02;
            rleData[(i * 4) + 3] = 0xFF;
        }

        rleData[^1] = 0x01;
        byte[] data = CreateRleBmp(size, size, bitsPerPixel, (BmpCompression)compression, rleData);

        var allocator = new TestMemoryAllocator();
        allocator.EnableNonThreadSafeLogging();
        Configuration configuration = Configuration.Default.Clone();
        configuration.MemoryAllocator = allocator;

        Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(configuration, data);
        });

        Assert.Empty(allocator.AllocationLog);
    }

    /// <summary>
    /// Creates a run length encoded bitmap with the given RLE data. RLE4 and RLE8 bitmaps get a 40 byte
    /// BITMAPINFOHEADER and a black palette of two colors. RLE24 bitmaps get a 64 byte OS/2 2.x header,
    /// which stores RLE24 as compression 4.
    /// </summary>
    private static byte[] CreateRleBmp(int width, int height, short bitsPerPixel, BmpCompression compression, params byte[] rleData)
    {
        bool isRle24 = compression == BmpCompression.RLE24;
        int infoHeaderSize = isRle24 ? BmpInfoHeader.Os2v2Size : BmpInfoHeader.SizeV3;
        int paletteLength = isRle24 ? 0 : 2;
        int pixelDataOffset = BmpFileHeader.Size + infoHeaderSize + (paletteLength * 4);
        byte[] data = new byte[pixelDataOffset + rleData.Length];
        Span<byte> span = data;

        // File header.
        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], pixelDataOffset);

        // Info header. The first 40 bytes of the OS/2 2.x header match the BITMAPINFOHEADER.
        Span<byte> infoHeader = span[BmpFileHeader.Size..];
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader, infoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader[8..], height);
        BinaryPrimitives.WriteInt16LittleEndian(infoHeader[12..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(infoHeader[14..], bitsPerPixel);
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader[16..], isRle24 ? 4 : (int)compression);
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader[32..], paletteLength);

        rleData.CopyTo(span[pixelDataOffset..]);
        return data;
    }

    [Theory]
    [InlineData(1, (int)BmpCompression.RGB, false, 0)]
    [InlineData(2, (int)BmpCompression.RGB, false, 0)]
    [InlineData(4, (int)BmpCompression.RGB, false, 0)]
    [InlineData(8, (int)BmpCompression.RGB, false, 0)]
    [InlineData(8, (int)BmpCompression.RGB, false, 2)] // Less than one palette entry.
    [InlineData(4, (int)BmpCompression.RLE4, false, 0)]
    [InlineData(8, (int)BmpCompression.RLE8, false, 0)]
    [InlineData(1, (int)BmpCompression.RGB, true, 0)]
    [InlineData(2, (int)BmpCompression.RGB, true, 0)]
    [InlineData(4, (int)BmpCompression.RGB, true, 0)]
    [InlineData(8, (int)BmpCompression.RGB, true, 0)]
    [InlineData(8, (int)BmpCompression.RGB, true, 2)] // Less than one palette entry.
    public void BmpDecoder_ThrowsInvalidImageContentException_WhenIndexedBitmapHasNoPalette(
        short bitsPerPixel,
        int compression,
        bool coreHeader,
        int paletteLength)
    {
        byte[] data = CreateIndexedBmp(bitsPerPixel, (BmpCompression)compression, coreHeader, new byte[paletteLength], colorsUsed: 0, indices: [0, 1, 0]);

        IImageInfo info = Image.Identify(data);
        Assert.Equal(3, info.Width);
        Assert.Equal(1, info.Height);

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(data);
        });
        Assert.Contains("palette", ex.Message);
    }

    [Theory]
    [InlineData(1, (int)BmpCompression.RGB, false, 1)]
    [InlineData(2, (int)BmpCompression.RGB, false, 1)]
    [InlineData(4, (int)BmpCompression.RGB, false, 1)]
    [InlineData(8, (int)BmpCompression.RGB, false, 1)]
    [InlineData(4, (int)BmpCompression.RLE4, false, 1)]
    [InlineData(8, (int)BmpCompression.RLE8, false, 1)]
    [InlineData(1, (int)BmpCompression.RGB, false, 0)]
    [InlineData(2, (int)BmpCompression.RGB, false, 0)]
    [InlineData(4, (int)BmpCompression.RGB, false, 0)]
    [InlineData(8, (int)BmpCompression.RGB, false, 0)]
    [InlineData(4, (int)BmpCompression.RLE4, false, 0)]
    [InlineData(8, (int)BmpCompression.RLE8, false, 0)]
    [InlineData(1, (int)BmpCompression.RGB, true, 0)]
    [InlineData(2, (int)BmpCompression.RGB, true, 0)]
    [InlineData(4, (int)BmpCompression.RGB, true, 0)]
    [InlineData(8, (int)BmpCompression.RGB, true, 0)]
    public void BmpDecoder_DecodesIndicesBeyondShortPaletteAsBlack(
        short bitsPerPixel,
        int compression,
        bool coreHeader,
        int colorsUsed)
    {
        // One red palette entry. A BITMAPCOREHEADER palette has 3 byte entries, the others have 4.
        // The two trailing bytes of the core palette are an incomplete second entry.
        byte[] palette = coreHeader ? [0x00, 0x00, 0xFF, 0x7F, 0x7F] : [0x00, 0x00, 0xFF, 0x00];
        int maxIndex = (1 << bitsPerPixel) - 1;
        byte[] data = CreateIndexedBmp(bitsPerPixel, (BmpCompression)compression, coreHeader, palette, colorsUsed, indices: [0, 1, (byte)maxIndex]);

        using Image<Rgba32> image = Image.Load<Rgba32>(data);

        Assert.Equal(new Rgba32(255, 0, 0), image[0, 0]);
        Assert.Equal(new Rgba32(0, 0, 0), image[1, 0]);
        Assert.Equal(new Rgba32(0, 0, 0), image[2, 0]);
    }

    [Theory]
    [InlineData(1, 8)] // Unused bytes between the palette and the pixel data.
    [InlineData(2, 0)] // Short palette.
    [InlineData(2, 24)]
    [InlineData(4, 0)]
    [InlineData(4, 120)]
    [InlineData(8, 0)]
    public void BmpDecoder_ReadsFourBytePaletteEntries_WhenColorsUsedIsZero(short bitsPerPixel, int unusedBytes)
    {
        // Red and green 4 byte entries in a 40 byte header bitmap, followed by unused bytes.
        byte[] palette = new byte[8 + unusedBytes];
        palette.AsSpan(8).Fill(0xAA);
        palette[2] = 0xFF;
        palette[5] = 0xFF;
        byte[] data = CreateIndexedBmp(bitsPerPixel, BmpCompression.RGB, false, palette, colorsUsed: 0, indices: [0, 1]);

        using Image<Rgba32> image = Image.Load<Rgba32>(data);

        Assert.Equal(new Rgba32(255, 0, 0), image[0, 0]);
        Assert.Equal(new Rgba32(0, 255, 0), image[1, 0]);
    }

    [Fact]
    public void BmpDecoder_ThrowsInvalidImageContentException_WhenColorsUsedOverflowsPaletteSize()
    {
        // 0x40000001 * 4 wraps to 4 bytes in 32 bit arithmetic.
        byte[] data = CreateIndexedBmp(8, BmpCompression.RGB, false, [0x00, 0x00, 0xFF, 0x00], colorsUsed: 0x40000001, indices: [0, 0]);

        Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(data);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BmpDecoder_Reads2BitFullPalette_WhenColorsUsedIsZero(bool coreHeader)
    {
        byte[][] colors = [[0x00, 0x00, 0xFF], [0x00, 0xFF, 0x00], [0xFF, 0x00, 0x00], [0xFF, 0xFF, 0xFF]];
        int entrySize = coreHeader ? 3 : 4;
        byte[] palette = new byte[colors.Length * entrySize];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i].CopyTo(palette, i * entrySize);
        }

        byte[] data = CreateIndexedBmp(2, BmpCompression.RGB, coreHeader, palette, colorsUsed: 0, indices: [0, 1, 2, 3]);

        using Image<Rgba32> image = Image.Load<Rgba32>(data);

        Assert.Equal(new Rgba32(255, 0, 0), image[0, 0]);
        Assert.Equal(new Rgba32(0, 255, 0), image[1, 0]);
        Assert.Equal(new Rgba32(0, 0, 255), image[2, 0]);
        Assert.Equal(new Rgba32(255, 255, 255), image[3, 0]);
    }

    /// <summary>
    /// Creates a one row indexed bitmap with the given raw palette bytes, placed directly after the info header.
    /// </summary>
    private static byte[] CreateIndexedBmp(
        short bitsPerPixel,
        BmpCompression compression,
        bool coreHeader,
        byte[] palette,
        int colorsUsed,
        byte[] indices)
    {
        byte[] pixelData;
        if (compression == BmpCompression.RGB)
        {
            int rowBytes = ((indices.Length * bitsPerPixel) + 31) / 32 * 4;
            pixelData = new byte[rowBytes];
            for (int i = 0; i < indices.Length; i++)
            {
                int bit = i * bitsPerPixel;
                pixelData[bit / 8] |= (byte)(indices[i] << (8 - bitsPerPixel - (bit % 8)));
            }
        }
        else
        {
            // One encoded run of length one per pixel, then end of bitmap. RLE4 uses the high nibble first.
            pixelData = new byte[(indices.Length * 2) + 2];
            for (int i = 0; i < indices.Length; i++)
            {
                pixelData[i * 2] = 1;
                pixelData[(i * 2) + 1] = compression == BmpCompression.RLE4 ? (byte)(indices[i] << 4) : indices[i];
            }

            pixelData[^1] = 1;
        }

        int headerSize = coreHeader ? BmpInfoHeader.CoreSize : BmpInfoHeader.SizeV3;
        int pixelDataOffset = BmpFileHeader.Size + headerSize + palette.Length;
        byte[] data = new byte[pixelDataOffset + pixelData.Length];
        Span<byte> span = data;

        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], pixelDataOffset);

        Span<byte> infoHeader = span[BmpFileHeader.Size..];
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader, headerSize);
        if (coreHeader)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(infoHeader[4..], (ushort)indices.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(infoHeader[6..], 1);
            BinaryPrimitives.WriteInt16LittleEndian(infoHeader[8..], 1);
            BinaryPrimitives.WriteInt16LittleEndian(infoHeader[10..], bitsPerPixel);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(infoHeader[4..], indices.Length);
            BinaryPrimitives.WriteInt32LittleEndian(infoHeader[8..], 1);
            BinaryPrimitives.WriteInt16LittleEndian(infoHeader[12..], 1);
            BinaryPrimitives.WriteInt16LittleEndian(infoHeader[14..], bitsPerPixel);
            BinaryPrimitives.WriteInt32LittleEndian(infoHeader[16..], (int)compression);
            BinaryPrimitives.WriteInt32LittleEndian(infoHeader[32..], colorsUsed);
        }

        palette.CopyTo(span[(BmpFileHeader.Size + headerSize)..]);
        pixelData.CopyTo(span[pixelDataOffset..]);

        return data;
    }

    [Theory]
    [InlineData(BmpInfoHeader.SizeV3)]
    [InlineData(BmpInfoHeader.AdobeV3WithAlphaSize)]
    [InlineData(BmpInfoHeader.SizeV4)]
    [InlineData(BmpInfoHeader.SizeV5)]
    public void BmpDecoder_ScalesAlphaMaskWiderThan8Bits(int infoHeaderSize)
    {
        // A 10 bit alpha mask in bits 22 to 31, 8 bit red and green masks and no blue mask.
        // Raw alpha values 1023, 512, 768 and 0 scale to 255, 128, 191 and 0.
        byte[] data = CreateBitfieldsBmp(
            infoHeaderSize,
            bitsPerPixel: 32,
            redMask: 0x0000FF00,
            greenMask: 0x000000FF,
            blueMask: 0,
            alphaMask: unchecked((int)0xFFC00000),
            0xFFC01234,
            0x80005678,
            0xC0009ABC,
            0x0000DEF0);

        using Image<Rgba32> image = Image.Load<Rgba32>(data);

        Assert.Equal(new Rgba32(0x12, 0x34, 0, 255), image[0, 0]);
        Assert.Equal(new Rgba32(0x56, 0x78, 0, 128), image[1, 0]);
        Assert.Equal(new Rgba32(0x9A, 0xBC, 0, 191), image[2, 0]);
        Assert.Equal(new Rgba32(0xDE, 0xF0, 0, 0), image[3, 0]);
    }

    [Theory]
    [InlineData(BmpInfoHeader.SizeV3)]
    [InlineData(BmpInfoHeader.AdobeV3WithAlphaSize)]
    [InlineData(BmpInfoHeader.SizeV4)]
    [InlineData(BmpInfoHeader.SizeV5)]
    public void BmpDecoder_ScalesMasksNarrowerThan8Bits(int infoHeaderSize)
    {
        // 4 bit alpha, 5 bit red, 6 bit green and 3 bit blue masks.
        // Raw values 8, 16, 32 and 4 scale to 136, 132, 130 and 146.
        byte[] data = CreateBitfieldsBmp(
            infoHeaderSize,
            bitsPerPixel: 32,
            redMask: 0x00F80000,
            greenMask: 0x0000FC00,
            blueMask: 0x00000007,
            alphaMask: unchecked((int)0xF0000000),
            0xF0F8FC07,
            0x80808004,
            0x00000000);

        using Image<Rgba32> image = Image.Load<Rgba32>(data);

        Assert.Equal(new Rgba32(255, 255, 255, 255), image[0, 0]);
        Assert.Equal(new Rgba32(132, 130, 146, 136), image[1, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), image[2, 0]);
    }

    [Theory]
    [InlineData(BmpInfoHeader.SizeV3, 0x00FF0000, 0x0000FF00, 0x000000FF)]
    [InlineData(BmpInfoHeader.SizeV3, 0x00007C00, 0x000003E0, 0x0000001F)]
    [InlineData(BmpInfoHeader.SizeV4, 0x3FF00000, 0x000FFC00, 0x000003FF)]
    public void BmpDecoder_DecodesZeroAlphaMaskAsOpaque(int infoHeaderSize, int redMask, int greenMask, int blueMask)
    {
        byte[] data = CreateBitfieldsBmp(
            infoHeaderSize,
            bitsPerPixel: 32,
            redMask,
            greenMask,
            blueMask,
            alphaMask: 0,
            0xFFFFFFFF,
            0x00000000);

        using Image<Rgba32> image = Image.Load<Rgba32>(data);

        Assert.Equal(new Rgba32(255, 255, 255, 255), image[0, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 255), image[1, 0]);
    }

    [Theory]
    [InlineData(BmpInfoHeader.SizeV3)]
    [InlineData(BmpInfoHeader.SizeV4)]
    public void BmpDecoder_Scales16BitMasksOfAnyWidth(int infoHeaderSize)
    {
        // 3 bit red, 7 bit green and 6 bit blue masks.
        // Raw values 4, 32 and 32 scale to 146, 64 and 130.
        byte[] data = CreateBitfieldsBmp(
            infoHeaderSize,
            bitsPerPixel: 16,
            redMask: 0xE000,
            greenMask: 0x1FC0,
            blueMask: 0x003F,
            alphaMask: 0,
            0xFFFF,
            0x8820,
            0x0000);

        using Image<Rgba32> image = Image.Load<Rgba32>(data);

        Assert.Equal(new Rgba32(255, 255, 255, 255), image[0, 0]);
        Assert.Equal(new Rgba32(146, 64, 130, 255), image[1, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 255), image[2, 0]);
    }

    /// <summary>
    /// Creates a single row BITFIELDS bitmap with the given masks and pixel values.
    /// A 40 byte header stores the masks after the header, using BI_ALPHABITFIELDS when there is an alpha mask.
    /// Larger headers store the masks inside the header.
    /// </summary>
    private static byte[] CreateBitfieldsBmp(
        int infoHeaderSize,
        short bitsPerPixel,
        int redMask,
        int greenMask,
        int blueMask,
        int alphaMask,
        params uint[] pixels)
    {
        bool masksAfterHeader = infoHeaderSize == BmpInfoHeader.SizeV3;
        BmpCompression compression = masksAfterHeader && alphaMask != 0 ? BmpCompression.BI_ALPHABITFIELDS : BmpCompression.BitFields;
        int masksLength = !masksAfterHeader ? 0 : compression == BmpCompression.BI_ALPHABITFIELDS ? 16 : 12;
        int bytesPerPixel = bitsPerPixel / 8;
        int stride = ((pixels.Length * bytesPerPixel) + 3) & ~3;

        int pixelDataOffset = BmpFileHeader.Size + infoHeaderSize + masksLength;
        byte[] data = new byte[pixelDataOffset + stride];
        Span<byte> span = data;

        // File header.
        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], pixelDataOffset);

        // Info header. Image size and resolution stay zero.
        Span<byte> infoHeader = span[BmpFileHeader.Size..];
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader, infoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader[4..], pixels.Length);
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader[8..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(infoHeader[12..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(infoHeader[14..], bitsPerPixel);
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader[16..], (int)compression);

        // The masks follow the first 40 bytes of the header in both layouts.
        Span<byte> masks = infoHeader[BmpInfoHeader.SizeV3..];
        BinaryPrimitives.WriteInt32LittleEndian(masks, redMask);
        BinaryPrimitives.WriteInt32LittleEndian(masks[4..], greenMask);
        BinaryPrimitives.WriteInt32LittleEndian(masks[8..], blueMask);
        if (masksLength != 12)
        {
            BinaryPrimitives.WriteInt32LittleEndian(masks[12..], alphaMask);
        }

        Span<byte> pixelData = span[pixelDataOffset..];
        for (int i = 0; i < pixels.Length; i++)
        {
            if (bytesPerPixel == 4)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(pixelData[(i * 4)..], pixels[i]);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(pixelData[(i * 2)..], (ushort)pixels[i]);
            }
        }

        return data;
    }

    /// <summary>
    /// Creates a bitmap with a 40 byte BITMAPINFOHEADER, an optional black color palette and zeroed pixel data.
    /// </summary>
    private static byte[] CreateBmp(
        int width,
        int height,
        short bitsPerPixel,
        int pixelDataLength,
        int paletteLength = 0,
        BmpCompression compression = BmpCompression.RGB)
    {
        const int infoHeaderStart = BmpFileHeader.Size;
        int pixelDataOffset = BmpFileHeader.Size + BmpInfoHeader.SizeV3 + (paletteLength * 4);
        byte[] data = new byte[pixelDataOffset + pixelDataLength];
        Span<byte> span = data;

        // File header.
        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], pixelDataOffset);

        // Info header. Image size and resolution stay zero.
        Span<byte> infoHeader = span[infoHeaderStart..];
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader, BmpInfoHeader.SizeV3);
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader[8..], height);
        BinaryPrimitives.WriteInt16LittleEndian(infoHeader[12..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(infoHeader[14..], bitsPerPixel);
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader[16..], (int)compression);
        BinaryPrimitives.WriteInt32LittleEndian(infoHeader[32..], paletteLength);

        return data;
    }

    // On Linux and macOS the reference decoder is Magick.NET. Magick.NET 14+ decodes 16-bit bitfield
    // bitmaps differently and rejects truncated RLE4 data. There, compare with reference output captured
    // from ImageSharp, which matches System.Drawing and Magick.NET 11.1.2.
    private static void CompareToOriginalOrReferenceOutput<TPixel>(Image<TPixel> image, TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (TestEnvironment.IsWindows)
        {
            image.CompareToOriginal(provider);
        }
        else
        {
            image.CompareToReferenceOutput(ImageComparer.Tolerant(), provider);
        }
    }
}
