// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Jpeg;
using PinataLabs.ImagePastel.IO;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Formats.Jpg.Utils;
using PinataLabs.ImagePastel.Tests.TestUtilities;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;

using Xunit;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests.Formats.Jpg;

// TODO: Scatter test cases into multiple test classes
[Trait("Format", "Jpg")]
[ValidateDisposedMemoryAllocations]
public partial class JpegDecoderTests
{
    public const PixelTypes CommonNonDefaultPixelTypes = PixelTypes.Rgba32 | PixelTypes.Argb32 | PixelTypes.Bgr24 | PixelTypes.RgbaVector;

    private const float BaselineTolerance = 0.001F / 100;

    private const float ProgressiveTolerance = 0.2F / 100;

    private static ImageComparer GetImageComparer<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        string file = provider.SourceFileOrDescription;

        if (!CustomToleranceValues.TryGetValue(file, out float tolerance))
        {
            bool baseline = file.IndexOf("baseline", StringComparison.OrdinalIgnoreCase) >= 0;
            tolerance = baseline ? BaselineTolerance : ProgressiveTolerance;
        }

        return ImageComparer.Tolerant(tolerance);
    }

    private static bool SkipTest(ITestImageProvider provider)
    {
        string[] largeImagesToSkipOn32Bit =
            {
                TestImages.Jpeg.Baseline.Jpeg420Exif,
                TestImages.Jpeg.Issues.MissingFF00ProgressiveBedroom159,
                TestImages.Jpeg.Issues.BadZigZagProgressive385,
                TestImages.Jpeg.Issues.NoEoiProgressive517,
                TestImages.Jpeg.Issues.BadRstProgressive518,
                TestImages.Jpeg.Issues.InvalidEOI695,
                TestImages.Jpeg.Issues.ExifResizeOutOfRange696,
                TestImages.Jpeg.Issues.ExifGetString750Transform
            };

        return !TestEnvironment.Is64BitProcess && largeImagesToSkipOn32Bit.Contains(provider.SourceFileOrDescription);
    }

    public JpegDecoderTests(ITestOutputHelper output) => this.Output = output;

    private ITestOutputHelper Output { get; }

    private static JpegDecoder JpegDecoder => new();

    [Fact]
    public void ParseStream_BasicPropertiesAreCorrect()
    {
        byte[] bytes = TestFile.Create(TestImages.Jpeg.Progressive.Progress).Bytes;
        using var ms = new MemoryStream(bytes);
        using var bufferedStream = new BufferedReadStream(Configuration.Default, ms);
        using var decoder = new JpegDecoderCore(Configuration.Default, new JpegDecoder());
        using Image<Rgba32> image = decoder.Decode<Rgba32>(bufferedStream, cancellationToken: TestContext.Current.CancellationToken);

        // I don't know why these numbers are different. All I know is that the decoder works
        // and spectral data is exactly correct also.
        // VerifyJpeg.VerifyComponentSizes3(decoder.Frame.Components, 43, 61, 22, 31, 22, 31);
        VerifyJpeg.VerifyComponentSizes3(decoder.Frame.Components, 44, 62, 22, 31, 22, 31);
    }

    public const string DecodeBaselineJpegOutputName = "DecodeBaselineJpeg";

    [Fact]
    public void Decode_NonGeneric_CreatesRgb24Image()
    {
        string file = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, TestImages.Jpeg.Baseline.Jpeg420Small);
        using var image = Image.Load(file);
        Assert.IsType<Image<Rgb24>>(image);
    }

    [Fact]
    public async Task DecodeAsync_NonGeneric_CreatesRgb24Image()
    {
        string file = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, TestImages.Jpeg.Baseline.Jpeg420Small);
        using Image image = await Image.LoadAsync(file, TestContext.Current.CancellationToken);
        Assert.IsType<Image<Rgb24>>(image);
    }

    [Theory]
    [WithFile(TestImages.Jpeg.Baseline.Calliphora, CommonNonDefaultPixelTypes)]
    public void JpegDecoder_IsNotBoundToSinglePixelType<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage(JpegDecoder);
        image.DebugSave(provider);

        provider.Utility.TestName = DecodeBaselineJpegOutputName;
        image.CompareToReferenceOutput(
            ImageComparer.Tolerant(BaselineTolerance),
            provider,
            appendPixelTypeToFileName: false);
    }

    [Theory]
    [WithFile(TestImages.Jpeg.Baseline.Floorplan, PixelTypes.Rgba32)]
    [WithFile(TestImages.Jpeg.Progressive.Festzug, PixelTypes.Rgba32)]
    public void Decode_DegenerateMemoryRequest_ShouldTranslateTo_ImageFormatException<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        provider.LimitAllocatorBufferCapacity().InBytesSqrt(10);
        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => provider.GetImage(JpegDecoder));
        this.Output.WriteLine(ex.Message);
        Assert.IsType<InvalidMemoryOperationException>(ex.InnerException);
    }

    [Theory]
    [WithFile(TestImages.Jpeg.Baseline.Floorplan, PixelTypes.Rgba32)]
    [WithFile(TestImages.Jpeg.Progressive.Festzug, PixelTypes.Rgba32)]
    public async Task DecodeAsync_DegenerateMemoryRequest_ShouldTranslateTo_ImageFormatException<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        provider.LimitAllocatorBufferCapacity().InBytesSqrt(10);
        InvalidImageContentException ex = await Assert.ThrowsAsync<InvalidImageContentException>(() => provider.GetImageAsync(JpegDecoder));
        this.Output.WriteLine(ex.Message);
        Assert.IsType<InvalidMemoryOperationException>(ex.InnerException);
    }

    [Fact]
    public async Task DecodeAsync_IsCancellable()
    {
        var cts = new CancellationTokenSource();
        string file = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, TestImages.Jpeg.Baseline.Jpeg420Small);
        using var pausedStream = new PausedStream(file);
        pausedStream.OnWaiting(s =>
        {
            cts.Cancel();
            pausedStream.Release();
        });

        var config = Configuration.CreateDefaultInstance();
        config.FileSystem = new SingleStreamFileSystem(pausedStream);
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            using Image image = await Image.LoadAsync(config, "someFakeFile", cts.Token);
        });
    }

    [Fact]
    public async Task Identify_IsCancellable()
    {
        var cts = new CancellationTokenSource();

        string file = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, TestImages.Jpeg.Baseline.Jpeg420Small);
        using var pausedStream = new PausedStream(file);
        pausedStream.OnWaiting(s =>
        {
            cts.Cancel();
            pausedStream.Release();
        });

        var config = Configuration.CreateDefaultInstance();
        config.FileSystem = new SingleStreamFileSystem(pausedStream);

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await Image.IdentifyAsync(config, "someFakeFile", cts.Token));
    }

    [Theory]
    [WithFileCollection(nameof(UnsupportedTestJpegs), PixelTypes.Rgba32)]
    public void ThrowsNotSupported_WithUnsupportedJpegs<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Assert.Throws<NotSupportedException>(() =>
        {
            using Image<TPixel> image = provider.GetImage(JpegDecoder);
        });
    }

    // https://github.com/SixLabors/ImageSharp/pull/1732
    [Theory]
    [WithFile(TestImages.Jpeg.Issues.WrongColorSpace, PixelTypes.Rgba32)]
    public void Issue1732_DecodesWithRgbColorSpace<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(JpegDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    // https://github.com/SixLabors/ImageSharp/issues/2057
    [Theory]
    [WithFile(TestImages.Jpeg.Issues.Issue2057App1Parsing, PixelTypes.Rgba32)]
    public void Issue2057_DecodeWorks<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(JpegDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    // https://github.com/SixLabors/ImageSharp/issues/2133
    [Theory]
    [WithFile(TestImages.Jpeg.Issues.Issue2133DeduceColorSpace, PixelTypes.Rgba32)]
    public void Issue2133_DeduceColorSpace<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(JpegDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    // https://github.com/SixLabors/ImageSharp/issues/2174
    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(2, 3, 4)]
    public void Issue2174_ThreeComponentWithoutColorSpaceMarkers_DecodesAsYCbCr(byte id0, byte id1, byte id2)
    {
        using Image<Rgba32> source = new(16, 16, Color.White.ToPixel<Rgba32>());
        using MemoryStream encoded = new();
        source.SaveAsJpeg(encoded, new JpegEncoder { ColorType = JpegColorType.YCbCrRatio444, Quality = 100 });

        byte[] data = RewriteWithoutApp0(encoded.ToArray(), id0, id1, id2);

        using Image<Rgba32> image = Image.Load<Rgba32>(data);
        Rgba32 actual = image[0, 0];

        Assert.InRange(actual.R, 250, 255);
        Assert.InRange(actual.G, 250, 255);
        Assert.InRange(actual.B, 250, 255);
        Assert.Equal(JpegColorType.YCbCrRatio444, image.Metadata.GetJpegMetadata().ColorType);

        // Copies a baseline jpeg, dropping the APP0 (JFIF) segment and replacing the frame and scan component ids.
        static byte[] RewriteWithoutApp0(byte[] jpeg, byte id0, byte id1, byte id2)
        {
            byte[] ids = [id0, id1, id2];
            using MemoryStream output = new();
            output.Write(jpeg, 0, 2);

            int offset = 2;
            while (offset < jpeg.Length)
            {
                Assert.Equal(0xFF, jpeg[offset]);
                byte marker = jpeg[offset + 1];
                int length = (jpeg[offset + 2] << 8) | jpeg[offset + 3];
                byte[] segment = jpeg.AsSpan(offset, length + 2).ToArray();

                if (marker == JpegConstants.Markers.SOF0)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        segment[10 + (i * 3)] = ids[i];
                    }
                }
                else if (marker == JpegConstants.Markers.SOS)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        segment[5 + (i * 2)] = ids[i];
                    }

                    output.Write(segment);
                    output.Write(jpeg, offset + segment.Length, jpeg.Length - offset - segment.Length);
                    break;
                }

                if (marker != JpegConstants.Markers.APP0)
                {
                    output.Write(segment);
                }

                offset += segment.Length;
            }

            return output.ToArray();
        }
    }

    // https://github.com/SixLabors/ImageSharp/issues/2638
    [Theory]
    [WithFile(TestImages.Jpeg.Issues.Issue2638, PixelTypes.Rgba32)]
    public void Issue2638_DecodeWorks<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(JpegDecoder))
        {
            image.DebugSave(provider);
            image.CompareToOriginal(provider);
        }
    }

    // https://github.com/SixLabors/ImageSharp/issues/2136
    [Theory]
    [InlineData(new byte[] { 0x12 })]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0xFF, 0x00 })]
    [InlineData(new byte[] { 0x12, 0xFF, 0x00 })]
    [InlineData(new byte[] { 0xFF, 0xD0 })]
    [InlineData(new byte[] { 0x12, 0xFF, 0xD3 })]
    public void Issue2136_DecodeWorks_WithExtraneousBytesAfterScan(byte[] extraneousPattern)
    {
        using var source = new Image<Rgb24>(32, 32);
        source.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgb24((byte)(x * 8), (byte)(y * 8), (byte)((x + y) * 4));
                }
            }
        });

        using var encoded = new MemoryStream();
        source.SaveAsJpeg(encoded);
        byte[] clean = encoded.ToArray();

        // Insert junk bytes between the end of the entropy coded data and the EOI marker,
        // like the scanner output reported in the issue.
        int eoiPosition = clean.Length - 2;
        Assert.Equal(JpegConstants.Markers.XFF, clean[eoiPosition]);
        Assert.Equal(JpegConstants.Markers.EOI, clean[eoiPosition + 1]);

        using var corrupted = new MemoryStream();
        corrupted.Write(clean, 0, eoiPosition);
        for (int i = 0; i < 32; i++)
        {
            corrupted.Write(extraneousPattern, 0, extraneousPattern.Length);
        }

        corrupted.Write(clean, eoiPosition, 2);

        using Image<Rgb24> expected = Image.Load<Rgb24>(clean);
        using Image<Rgb24> actual = Image.Load<Rgb24>(corrupted.ToArray());

        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    // DEBUG ONLY!
    // The PDF.js output should be saved by "tests\ImagePastel.Tests\Formats\Jpg\pdfjs\jpeg-converter.htm"
    // into "\tests\Images\ActualOutput\JpegDecoderTests\"
    // [Theory]
    // [WithFile(TestImages.Jpeg.Progressive.Progress, PixelTypes.Rgba32, "PdfJsOriginal_progress.png")]
    public void ValidateProgressivePdfJsOutput<TPixel>(
        TestImageProvider<TPixel> provider,
        string pdfJsOriginalResultImage)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // tests\ImagePastel.Tests\Formats\Jpg\pdfjs\jpeg-converter.htm
        string pdfJsOriginalResultPath = Path.Combine(
            provider.Utility.GetTestOutputDir(),
            pdfJsOriginalResultImage);

        byte[] sourceBytes = TestFile.Create(TestImages.Jpeg.Progressive.Progress).Bytes;

        provider.Utility.TestName = nameof(DecodeProgressiveJpegOutputName);

        var comparer = ImageComparer.Tolerant(0, 0);

        using (Image<TPixel> expectedImage = provider.GetReferenceOutputImage<TPixel>(appendPixelTypeToFileName: false))
        using (var pdfJsOriginalResult = Image.Load<Rgba32>(pdfJsOriginalResultPath))
        using (var pdfJsPortResult = Image.Load<Rgba32>(sourceBytes, JpegDecoder))
        {
            ImageSimilarityReport originalReport = comparer.CompareImagesOrFrames(expectedImage, pdfJsOriginalResult);
            ImageSimilarityReport portReport = comparer.CompareImagesOrFrames(expectedImage, pdfJsPortResult);

            this.Output.WriteLine($"Difference for PDF.js ORIGINAL: {originalReport.DifferencePercentageString}");
            this.Output.WriteLine($"Difference for PORT: {portReport.DifferencePercentageString}");
        }
    }

    [Theory]
    [WithFile(TestImages.Jpeg.Issues.HangBadScan, PixelTypes.L8)]
    public void DecodeHang<TPixel>(TestImageProvider<TPixel> provider)
    where TPixel : unmanaged, IPixel<TPixel>
    {
        if (TestEnvironment.IsWindows &&
            TestEnvironment.RunsOnCI)
        {
            // Windows CI runs consistently fail with OOM.
            return;
        }

        using Image<TPixel> image = provider.GetImage(JpegDecoder);
        Assert.Equal(65503, image.Width);
        Assert.Equal(65503, image.Height);
    }
}
