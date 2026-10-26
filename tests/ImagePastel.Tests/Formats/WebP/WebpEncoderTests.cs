// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Formats.Webp.Lossless;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Memory;
using SixLabors.ImageSharp.Tests.TestUtilities;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;
using static SixLabors.ImageSharp.Tests.TestImages.Webp;

namespace SixLabors.ImageSharp.Tests.Formats.Webp;

[Trait("Format", "Webp")]
public class WebpEncoderTests
{
    private static string TestImageLossyFullPath => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, Lossy.NoFilter06);

    [Theory]
    [WithFile(Flag, PixelTypes.Rgba32, WebpFileFormatType.Lossy)] // If its not a webp input image, it should default to lossy.
    [WithFile(Lossless.NoTransform1, PixelTypes.Rgba32, WebpFileFormatType.Lossless)]
    [WithFile(Lossy.Bike, PixelTypes.Rgba32, WebpFileFormatType.Lossy)]
    public void Encode_PreserveRatio<TPixel>(TestImageProvider<TPixel> provider, WebpFileFormatType expectedFormat)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var options = new WebpEncoder();
        using Image<TPixel> input = provider.GetImage();
        using var memoryStream = new MemoryStream();
        input.Save(memoryStream, options);

        memoryStream.Position = 0;
        using var output = Image.Load<Rgba32>(memoryStream);

        ImageMetadata meta = output.Metadata;
        WebpMetadata webpMetaData = meta.GetWebpMetadata();
        Assert.Equal(expectedFormat, webpMetaData.FileFormat);
    }

    [Theory]
    [WithFile(Flag, PixelTypes.Rgba32)]
    [WithFile(TestImages.Png.PalettedTwoColor, PixelTypes.Rgba32)]
    [WithFile(TestImages.Png.Paletted256Colors, PixelTypes.Rgba32)]
    public void Encode_Lossless_WithPalette_Works<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var encoder = new WebpEncoder()
        {
            FileFormat = WebpFileFormatType.Lossless,
            Quality = 100,
            Method = WebpEncodingMethod.BestQuality
        };

        using Image<TPixel> image = provider.GetImage();
        image.VerifyEncoder(provider, "webp", string.Empty, encoder);
    }

    [Theory]
    [WithFile(TestImages.Bmp.Car, PixelTypes.Rgba32, 100)]
    [WithFile(TestImages.Bmp.Car, PixelTypes.Rgba32, 80)]
    [WithFile(TestImages.Bmp.Car, PixelTypes.Rgba32, 20)]
    public void Encode_Lossless_WithDifferentQuality_Works<TPixel>(TestImageProvider<TPixel> provider, int quality)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var encoder = new WebpEncoder()
        {
            FileFormat = WebpFileFormatType.Lossless,
            Quality = quality
        };

        using Image<TPixel> image = provider.GetImage();
        string testOutputDetails = string.Concat("lossless", "_q", quality);
        image.VerifyEncoder(provider, "webp", testOutputDetails, encoder);
    }

    [Theory]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 0, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 1, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 2, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 3, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 4, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 5, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 6, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 0, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 1, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 2, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 3, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 4, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 5, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 6, 100)]
    public void Encode_Lossless_WithDifferentMethodAndQuality_Works<TPixel>(TestImageProvider<TPixel> provider, WebpEncodingMethod method, int quality)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var encoder = new WebpEncoder()
        {
            FileFormat = WebpFileFormatType.Lossless,
            Method = method,
            Quality = quality
        };

        using Image<TPixel> image = provider.GetImage();
        string testOutputDetails = string.Concat("lossless", "_m", method, "_q", quality);
        image.VerifyEncoder(provider, "webp", testOutputDetails, encoder);
    }

    [Theory]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 15114)]
    public void Encode_Lossless_WithBestQuality_HasExpectedSize<TPixel>(TestImageProvider<TPixel> provider, int expectedBytes)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var encoder = new WebpEncoder()
        {
            FileFormat = WebpFileFormatType.Lossless,
            Method = WebpEncodingMethod.BestQuality
        };

        using Image<TPixel> image = provider.GetImage();
        using var memoryStream = new MemoryStream();
        image.Save(memoryStream, encoder);

        Assert.Equal(memoryStream.Length, expectedBytes);
    }

    [Theory]
    [WithFile(RgbTestPattern100x100, PixelTypes.Rgba32, 85)]
    [WithFile(RgbTestPattern100x100, PixelTypes.Rgba32, 60)]
    [WithFile(RgbTestPattern80x80, PixelTypes.Rgba32, 40)]
    [WithFile(RgbTestPattern80x80, PixelTypes.Rgba32, 20)]
    [WithFile(RgbTestPattern80x80, PixelTypes.Rgba32, 10)]
    [WithFile(RgbTestPattern63x63, PixelTypes.Rgba32, 40)]
    public void Encode_Lossless_WithNearLosslessFlag_Works<TPixel>(TestImageProvider<TPixel> provider, int nearLosslessQuality)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var encoder = new WebpEncoder()
        {
            FileFormat = WebpFileFormatType.Lossless,
            NearLossless = true,
            NearLosslessQuality = nearLosslessQuality
        };

        using Image<TPixel> image = provider.GetImage();
        string testOutputDetails = string.Concat("nearlossless", "_q", nearLosslessQuality);
        image.VerifyEncoder(provider, "webp", testOutputDetails, encoder, customComparer: GetComparer(nearLosslessQuality));
    }

    [Theory]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 0)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 1)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 2)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 3)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 4)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 5)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 6)]
    [WithFile(Lossy.Alpha1, PixelTypes.Rgba32, 4)]
    public void Encode_Lossless_WithPreserveTransparentColor_Works<TPixel>(TestImageProvider<TPixel> provider, WebpEncodingMethod method)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var encoder = new WebpEncoder()
        {
            FileFormat = WebpFileFormatType.Lossless,
            Method = method,
            TransparentColorMode = WebpTransparentColorMode.Preserve
        };

        using Image<TPixel> image = provider.GetImage();
        string testOutputDetails = string.Concat("lossless", "_m", method);
        image.VerifyEncoder(provider, "webp", testOutputDetails, encoder);
    }

    [Fact]
    public void Encode_Lossless_OneByOnePixel_Works()
    {
        // Just make sure, encoding 1 pixel by 1 pixel does not throw an exception.
        using var image = new Image<Rgba32>(1, 1);
        var encoder = new WebpEncoder() { FileFormat = WebpFileFormatType.Lossless };
        using (var memStream = new MemoryStream())
        {
            image.SaveAsWebp(memStream, encoder);
        }
    }

    [Theory]
    [InlineData(16383, 4)]
    [InlineData(4, 16383)]
    [InlineData(16384, 4)]
    [InlineData(4, 16384)]
    public void Encode_Lossless_WithMaximumDimension_Works(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(255, 0, 0));
        var encoder = new WebpEncoder() { FileFormat = WebpFileFormatType.Lossless, Method = WebpEncodingMethod.Fastest };
        using var memStream = new MemoryStream();

        image.SaveAsWebp(memStream, encoder);
        memStream.Position = 0;
        using var decoded = Image.Load<Rgba32>(memStream);

        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        Assert.Equal(new Rgba32(255, 0, 0), decoded[width - 1, height - 1]);
    }

    [Theory]
    [InlineData(16385, 4)]
    [InlineData(4, 16385)]
    public void Encode_Lossless_WithDimensionAboveMaximum_Throws(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(255, 0, 0));
        var encoder = new WebpEncoder() { FileFormat = WebpFileFormatType.Lossless, Method = WebpEncodingMethod.Fastest };
        using var memStream = new MemoryStream();

        Assert.Throws<ArgumentOutOfRangeException>(() => image.SaveAsWebp(memStream, encoder));
        Assert.Equal(0, memStream.Length);
    }

    [Theory]
    [InlineData(16384, 4)]
    [InlineData(4, 16384)]
    [InlineData(20000, 4)]
    public void Encode_Lossy_WithDimensionAboveMaximum_Throws(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(255, 0, 0));
        var encoder = new WebpEncoder() { FileFormat = WebpFileFormatType.Lossy, Method = WebpEncodingMethod.Fastest };
        using var memStream = new MemoryStream();

        Assert.Throws<ArgumentOutOfRangeException>(() => image.SaveAsWebp(memStream, encoder));
        Assert.Equal(0, memStream.Length);
    }

    [Theory]
    [InlineData(16383, 4)]
    [InlineData(4, 16383)]
    public void Encode_Lossy_WithMaximumDimension_Works(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(255, 0, 0));
        var encoder = new WebpEncoder() { FileFormat = WebpFileFormatType.Lossy, Method = WebpEncodingMethod.Fastest };
        using var memStream = new MemoryStream();

        image.SaveAsWebp(memStream, encoder);
        memStream.Position = 0;
        using var decoded = Image.Load<Rgba32>(memStream);

        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
    }

    [Theory]
    [InlineData(16, 4)]
    [InlineData(16, 5)]
    [InlineData(15, 14)]
    public void Encode_Lossy_WithOneTransparentPixel_KeepsAlpha(int height, int transparentRow)
    {
        using var image = new Image<Rgba32>(16, height, new Rgba32(255, 0, 0));
        image[5, transparentRow] = new Rgba32(255, 0, 0, 0);
        var encoder = new WebpEncoder() { FileFormat = WebpFileFormatType.Lossy };
        using var memStream = new MemoryStream();

        image.SaveAsWebp(memStream, encoder);
        Assert.True(memStream.ToArray().AsSpan().IndexOf("ALPH"u8) >= 0, "The ALPH chunk is missing.");
        memStream.Position = 0;
        using var decoded = Image.Load<Rgba32>(memStream);

        Assert.Equal(0, decoded[5, transparentRow].A);
        Assert.Equal(255, decoded[5, transparentRow == 4 ? 5 : 4].A);
    }

    [Fact]
    public void Encode_Lossy_WithOpaqueImage_WritesNoAlpha()
    {
        using var image = new Image<Rgba32>(16, 15, new Rgba32(255, 0, 0));
        var encoder = new WebpEncoder() { FileFormat = WebpFileFormatType.Lossy };
        using var memStream = new MemoryStream();

        image.SaveAsWebp(memStream, encoder);

        Assert.True(memStream.ToArray().AsSpan().IndexOf("ALPH"u8) < 0, "An opaque image has an ALPH chunk.");
    }

    [Fact]
    public void Encode_Lossy_WithNoisyImage_AllocatesInProportionToImageSize()
    {
        const int width = 1000;
        const int height = 750;

        // Run on a single thread so that all allocations are made on the current thread.
        Configuration configuration = Configuration.Default.Clone();
        configuration.MaxDegreeOfParallelism = 1;
        using var image = new Image<Rgb24>(configuration, width, height);
        var random = new Random(42);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                random.NextBytes(MemoryMarshal.AsBytes(accessor.GetRowSpan(y)));
            }
        });

        var encoder = new WebpEncoder() { FileFormat = WebpFileFormatType.Lossy, Quality = 90 };
        using var memStream = new MemoryStream();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        image.SaveAsWebp(memStream, encoder);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        // A bit writer that copies its buffer each time the output grows by 1 KiB allocates over 200 MB here,
        // and mode selection that creates scratch objects for each macroblock allocates about 14 times the pixel and output size.
        long pixelBytes = width * height * 3;
        Assert.True(allocated < 4 * (pixelBytes + memStream.Length), $"Allocated {allocated} bytes for {memStream.Length} bytes of output.");
    }

    [Fact]
    public void Encode_Lossy_WithRandomAlpha_AllocatesInProportionToImageSize()
    {
        const int width = 1000;
        const int height = 750;

        // Run on a single thread so that all allocations are made on the current thread.
        Configuration configuration = Configuration.Default.Clone();
        configuration.MaxDegreeOfParallelism = 1;
        using var image = new Image<Rgba32>(configuration, width, height);
        var random = new Random(42);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                random.NextBytes(MemoryMarshal.AsBytes(accessor.GetRowSpan(y)));
            }
        });

        var encoder = new WebpEncoder() { FileFormat = WebpFileFormatType.Lossy, UseAlphaCompression = true };
        using var memStream = new MemoryStream();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        image.SaveAsWebp(memStream, encoder);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        // Lossless alpha compression that creates an object for each backward reference allocates about 21 times
        // the pixel and output size here. The bound leaves room for the histograms of the alpha encoder, whose
        // number is capped and so does not shrink with the image.
        long pixelBytes = width * height * 4;
        Assert.True(allocated < 8 * (pixelBytes + memStream.Length), $"Allocated {allocated} bytes for {memStream.Length} bytes of output.");
    }

    [Fact]
    public void Encode_Lossless_WithBackwardReferencesOverAllocationLimit_Works()
    {
        // The image buffers fit in 1 MB, but its backward references (8 bytes per pixel) do not.
        Configuration configuration = Configuration.Default.Clone();
        configuration.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions { AllocationLimitMegabytes = 1 });
        using var image = new Image<Rgba32>(configuration, 500, 400);
        var random = new Random(42);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                }
            }
        });

        var encoder = new WebpEncoder() { FileFormat = WebpFileFormatType.Lossless };
        using var memStream = new MemoryStream();
        image.SaveAsWebp(memStream, encoder);

        memStream.Position = 0;
        using var decoded = Image.Load<Rgba32>(memStream);
        ImageComparer.Exact.VerifySimilarity(image, decoded);
    }

    [Theory]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 20)]
    public void Encode_Lossy_WithDifferentQuality_Works<TPixel>(TestImageProvider<TPixel> provider, int quality)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var encoder = new WebpEncoder()
        {
            FileFormat = WebpFileFormatType.Lossy,
            Quality = quality
        };

        using Image<TPixel> image = provider.GetImage();
        string testOutputDetails = string.Concat("lossy", "_q", quality);
        image.VerifyEncoder(provider, "webp", testOutputDetails, encoder, customComparer: GetComparer(quality));
    }

    [Theory]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 80)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 50)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 30)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 10)]
    public void Encode_Lossy_WithDifferentFilterStrength_Works<TPixel>(TestImageProvider<TPixel> provider, int filterStrength)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var encoder = new WebpEncoder()
        {
            FileFormat = WebpFileFormatType.Lossy,
            FilterStrength = filterStrength
        };

        using Image<TPixel> image = provider.GetImage();
        string testOutputDetails = string.Concat("lossy", "_f", filterStrength);
        image.VerifyEncoder(provider, "webp", testOutputDetails, encoder, customComparer: GetComparer(75));
    }

    [Theory]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 80)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 50)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 30)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 10)]
    public void Encode_Lossy_WithDifferentSpatialNoiseShapingStrength_Works<TPixel>(TestImageProvider<TPixel> provider, int snsStrength)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var encoder = new WebpEncoder()
        {
            FileFormat = WebpFileFormatType.Lossy,
            SpatialNoiseShaping = snsStrength
        };

        using Image<TPixel> image = provider.GetImage();
        string testOutputDetails = string.Concat("lossy", "_sns", snsStrength);
        image.VerifyEncoder(provider, "webp", testOutputDetails, encoder, customComparer: GetComparer(75));
    }

    [Theory]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 0, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 1, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 2, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 3, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 4, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 5, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 6, 75)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 0, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 1, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 2, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 3, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 4, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 5, 100)]
    [WithFile(Lossy.NoFilter06, PixelTypes.Rgba32, 6, 100)]
    public void Encode_Lossy_WithDifferentMethodsAndQuality_Works<TPixel>(TestImageProvider<TPixel> provider, WebpEncodingMethod method, int quality)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var encoder = new WebpEncoder()
        {
            FileFormat = WebpFileFormatType.Lossy,
            Method = method,
            Quality = quality
        };

        using Image<TPixel> image = provider.GetImage();
        string testOutputDetails = string.Concat("lossy", "_m", method, "_q", quality);
        image.VerifyEncoder(provider, "webp", testOutputDetails, encoder, customComparer: GetComparer(quality));
    }

    [Theory]
    [WithFile(TestImages.Png.Transparency, PixelTypes.Rgba32, false)]
    [WithFile(TestImages.Png.Transparency, PixelTypes.Rgba32, true)]
    public void Encode_Lossy_WithAlpha_Works<TPixel>(TestImageProvider<TPixel> provider, bool compressed)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var encoder = new WebpEncoder()
        {
            FileFormat = WebpFileFormatType.Lossy,
            UseAlphaCompression = compressed
        };

        using Image<TPixel> image = provider.GetImage();
        image.VerifyEncoder(provider, "webp", $"with_alpha_compressed_{compressed}", encoder, ImageComparer.Tolerant(0.04f));
    }

    [Theory]
    [WithFile(TestPatternOpaque, PixelTypes.Rgba32)]
    [WithFile(TestPatternOpaqueSmall, PixelTypes.Rgba32)]
    public void Encode_Lossless_WorksWithTestPattern<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();

        var encoder = new WebpEncoder() { FileFormat = WebpFileFormatType.Lossless };
        image.VerifyEncoder(provider, "webp", string.Empty, encoder);
    }

    [Theory]
    [WithFile(TestPatternOpaque, PixelTypes.Rgba32)]
    [WithFile(TestPatternOpaqueSmall, PixelTypes.Rgba32)]
    public void Encode_Lossy_WorksWithTestPattern<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();

        var encoder = new WebpEncoder() { FileFormat = WebpFileFormatType.Lossy };
        image.VerifyEncoder(provider, "webp", string.Empty, encoder, ImageComparer.Tolerant(0.04f));
    }

    public static void RunEncodeLossy_WithPeakImage()
    {
        var provider = TestImageProvider<Rgba32>.File(TestImageLossyFullPath);
        using Image<Rgba32> image = provider.GetImage();

        var encoder = new WebpEncoder() { FileFormat = WebpFileFormatType.Lossy };
        image.VerifyEncoder(provider, "webp", string.Empty, encoder, ImageComparer.Tolerant(0.04f));
    }

    [Fact]
    public void RunEncodeLossy_WithPeakImage_WithHardwareIntrinsics_Works() => FeatureTestRunner.RunWithHwIntrinsicsFeature(RunEncodeLossy_WithPeakImage, HwIntrinsics.AllowAll);

    [Fact]
    public void RunEncodeLossy_WithPeakImage_WithoutHardwareIntrinsics_Works() => FeatureTestRunner.RunWithHwIntrinsicsFeature(RunEncodeLossy_WithPeakImage, HwIntrinsics.DisableHWIntrinsic);

    private static string GetIntrinsicsOutputPath(string intrinsics)
        => Path.Combine(TestEnvironment.CreateOutputDirectory(nameof(WebpEncoderTests)), $"Encode_Lossy_IsTheSameWithAndWithoutAvx2_{intrinsics}.webp");

    private static void RunEncodeLossy_ToFile(string intrinsics)
    {
        using var image = Image.Load<Rgba32>(TestImageLossyFullPath);
        var encoder = new WebpEncoder() { FileFormat = WebpFileFormatType.Lossy };
        image.Save(GetIntrinsicsOutputPath(intrinsics), encoder);
    }

    [Fact]
    public void Encode_Lossy_IsTheSameWithAndWithoutAvx2()
    {
        string withAvx2 = GetIntrinsicsOutputPath(nameof(HwIntrinsics.AllowAll));
        string withoutAvx2 = GetIntrinsicsOutputPath(nameof(HwIntrinsics.DisableAVX2));
        File.Delete(withAvx2);
        File.Delete(withoutAvx2);

        FeatureTestRunner.RunWithHwIntrinsicsFeature(RunEncodeLossy_ToFile, HwIntrinsics.AllowAll | HwIntrinsics.DisableAVX2);

        if (!File.Exists(withAvx2))
        {
            // Remote execution is not supported on this platform.
            return;
        }

        Assert.Equal(File.ReadAllBytes(withAvx2), File.ReadAllBytes(withoutAvx2));
    }

    [Fact]
    [ValidateDisposedMemoryAllocations]
    public void Vp8LEncoder_DisposeBeforeEncoding_ReturnsBuffers()
    {
        Configuration configuration = Configuration.Default;
        var encoder = new Vp8LEncoder(configuration.MemoryAllocator, configuration, 16, 16, 75, WebpEncodingMethod.Default, WebpTransparentColorMode.Clear, false, 100);

        encoder.Dispose();
        encoder.Dispose();
    }

    [Fact]
    public void Vp8LEncoder_OutOfMemoryBeforeTransformBuffers_ThrowsOutOfMemoryException()
    {
        using Image<Rgba32> image = CreateNoisyImage(32, 24);
        var allocator = new FailingAllocator();
        allocator.EnableNonThreadSafeLogging();
        using var memStream = new MemoryStream();

        Assert.Throws<OutOfMemoryException>(() =>
        {
            using var encoder = new Vp8LEncoder(allocator, Configuration.Default, image.Width, image.Height, 75, WebpEncodingMethod.Default, WebpTransparentColorMode.Clear, false, 100);

            // The image has too many colors for a palette, so the first allocation after the constructor is
            // the entropy histogram made while the image is analyzed, before the transform buffers.
            allocator.AllocationsBeforeFailure = 0;
            encoder.Encode(image, memStream);
        });

        Assert.NotEmpty(allocator.AllocationLog);
        AssertEachBufferReturnedOnce(allocator);
    }

    [Fact]
    public void Vp8LEncoder_OutOfMemoryAtAnyAllocation_ReturnsEachBufferOnce()
    {
        // Best quality at quality 100 tries every crunch config, so later failures also hit
        // the transform buffers that replace the ones of an earlier config.
        using Image<Rgba32> image = CreateNoisyImage(32, 24);
        bool completed = false;
        for (int allowed = 0; !completed; allowed++)
        {
            var allocator = new FailingAllocator();
            allocator.EnableNonThreadSafeLogging();
            using var memStream = new MemoryStream();
            try
            {
                using var encoder = new Vp8LEncoder(allocator, Configuration.Default, image.Width, image.Height, 100, WebpEncodingMethod.BestQuality, WebpTransparentColorMode.Clear, false, 100);
                allocator.AllocationsBeforeFailure = allowed;
                encoder.Encode(image, memStream);
                completed = true;
            }
            catch (OutOfMemoryException)
            {
            }

            AssertEachBufferReturnedOnce(allocator);
        }
    }

    [Theory]
    [ValidateDisposedMemoryAllocations]
    [InlineData(WebpFileFormatType.Lossless, WebpEncodingMethod.Default, 75, false)]
    [InlineData(WebpFileFormatType.Lossless, WebpEncodingMethod.BestQuality, 100, false)]
    [InlineData(WebpFileFormatType.Lossless, WebpEncodingMethod.Default, 75, true)]
    [InlineData(WebpFileFormatType.Lossy, WebpEncodingMethod.Default, 75, false)]
    public void Encode_WithNoisyImage_ReturnsBuffers(WebpFileFormatType fileFormat, WebpEncodingMethod method, int quality, bool nearLossless)
    {
        using Image<Rgba32> image = CreateNoisyImage(32, 24);
        var encoder = new WebpEncoder()
        {
            FileFormat = fileFormat,
            Method = method,
            Quality = quality,
            NearLossless = nearLossless,
            NearLosslessQuality = 60
        };
        using var memStream = new MemoryStream();

        image.SaveAsWebp(memStream, encoder);
    }

    private static Image<Rgba32> CreateNoisyImage(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        var random = new Random(42);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                random.NextBytes(MemoryMarshal.AsBytes(accessor.GetRowSpan(y)));
            }
        });

        return image;
    }

    private static void AssertEachBufferReturnedOnce(TestMemoryAllocator allocator)
    {
        int[] allocated = allocator.AllocationLog.Select(a => a.HashCodeOfBuffer).OrderBy(h => h).ToArray();
        int[] returned = allocator.ReturnLog.Select(r => r.HashCodeOfBuffer).OrderBy(h => h).ToArray();
        Assert.Equal(allocated, returned);
    }

    private static ImageComparer GetComparer(int quality)
    {
        float tolerance = 0.01f; // ~1.0%

        if (quality < 30)
        {
            tolerance = 0.02f; // ~2.0%
        }

        return ImageComparer.Tolerant(tolerance);
    }

    // Throws OutOfMemoryException once AllocationsBeforeFailure more allocations have been made.
    private sealed class FailingAllocator : TestMemoryAllocator
    {
        public int AllocationsBeforeFailure { get; set; } = -1;

        public override IMemoryOwner<T> Allocate<T>(int length, AllocationOptions options = AllocationOptions.None)
        {
            if (this.AllocationsBeforeFailure == 0)
            {
                throw new OutOfMemoryException();
            }

            if (this.AllocationsBeforeFailure > 0)
            {
                this.AllocationsBeforeFailure--;
            }

            return base.Allocate<T>(length, options);
        }
    }
}
