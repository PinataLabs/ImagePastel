// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using PinataLabs.ImagePastel.Formats.Gif;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Memory;
using PinataLabs.ImagePastel.Tests.TestUtilities;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;

using Xunit;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests.Formats.Gif;

[Trait("Format", "Gif")]
[ValidateDisposedMemoryAllocations]
public class GifDecoderTests
{
    private const PixelTypes TestPixelTypes = PixelTypes.Rgba32 | PixelTypes.RgbaVector | PixelTypes.Argb32;

    private static GifDecoder GifDecoder => new();

    public static readonly string[] MultiFrameTestFiles =
    {
        TestImages.Gif.Giphy, TestImages.Gif.Kumin
    };

    private static readonly Rgb24[] GlobalColorTable =
    {
        new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(255, 255, 255)
    };

    [Theory]
    [WithFileCollection(nameof(MultiFrameTestFiles), PixelTypes.Rgba32)]
    public void Decode_VerifyAllFrames<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        {
            image.DebugSaveMultiFrame(provider);
            image.CompareToReferenceOutputMultiFrame(provider, ImageComparer.Exact);
        }
    }

    [Fact]
    public unsafe void Decode_NonTerminatedFinalFrame()
    {
        var testFile = TestFile.Create(TestImages.Gif.Rings);

        int length = testFile.Bytes.Length - 2;

        fixed (byte* data = testFile.Bytes.AsSpan(0, length))
        {
            using (var stream = new UnmanagedMemoryStream(data, length))
            {
                using (Image<Rgba32> image = GifDecoder.Decode<Rgba32>(Configuration.Default, stream, TestContext.Current.CancellationToken))
                {
                    Assert.Equal((200, 200), (image.Width, image.Height));
                }
            }
        }
    }

    [Theory]
    [WithFile(TestImages.Gif.Trans, TestPixelTypes)]
    public void GifDecoder_IsNotBoundToSinglePixelType<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        {
            image.DebugSave(provider);
            image.CompareFirstFrameToReferenceOutput(ImageComparer.Exact, provider);
        }
    }

    [Theory]
    [WithFile(TestImages.Gif.Cheers, PixelTypes.Rgba32, 93)]
    [WithFile(TestImages.Gif.Rings, PixelTypes.Rgba32, 1)]
    [WithFile(TestImages.Gif.Issues.BadDescriptorWidth, PixelTypes.Rgba32, 36)]
    public void Decode_VerifyRootFrameAndFrameCount<TPixel>(TestImageProvider<TPixel> provider, int expectedFrameCount)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        {
            Assert.Equal(expectedFrameCount, image.Frames.Count);
            image.DebugSave(provider);
            image.CompareFirstFrameToReferenceOutput(ImageComparer.Exact, provider);
        }
    }

    [Theory]
    [WithFile(TestImages.Gif.Giphy, PixelTypes.Rgba32)]
    public void CanDecodeJustOneFrame<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(new GifDecoder { DecodingMode = FrameDecodingMode.First }))
        {
            Assert.Equal(1, image.Frames.Count);
        }
    }

    [Theory]
    [WithFile(TestImages.Gif.Giphy, PixelTypes.Rgba32)]
    public void CanDecodeAllFrames<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(new GifDecoder { DecodingMode = FrameDecodingMode.All }))
        {
            Assert.True(image.Frames.Count > 1);
        }
    }

    [Theory]
    [InlineData(TestImages.Gif.Cheers, 8)]
    [InlineData(TestImages.Gif.Giphy, 8)]
    [InlineData(TestImages.Gif.Rings, 8)]
    [InlineData(TestImages.Gif.Trans, 8)]
    public void DetectPixelSize(string imagePath, int expectedPixelSize)
    {
        var testFile = TestFile.Create(imagePath);
        using (var stream = new MemoryStream(testFile.Bytes, false))
        {
            Assert.Equal(expectedPixelSize, Image.Identify(stream)?.PixelType?.BitsPerPixel);
        }
    }

    [Theory]
    [WithFile(TestImages.Gif.ZeroSize, PixelTypes.Rgba32)]
    [WithFile(TestImages.Gif.ZeroWidth, PixelTypes.Rgba32)]
    [WithFile(TestImages.Gif.ZeroHeight, PixelTypes.Rgba32)]
    public void Decode_WithInvalidDimensions_DoesThrowException<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        System.Exception ex = Record.Exception(
            () =>
            {
                using Image<TPixel> image = provider.GetImage(GifDecoder);
            });
        Assert.NotNull(ex);
        Assert.Contains("Width or height should not be 0", ex.Message);
    }

    [Theory]
    [WithFile(TestImages.Gif.MaxWidth, PixelTypes.Rgba32, 65535, 1)]
    [WithFile(TestImages.Gif.MaxHeight, PixelTypes.Rgba32, 1, 65535)]
    public void Decode_WithMaxDimensions_Works<TPixel>(TestImageProvider<TPixel> provider, int expectedWidth, int expectedHeight)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage(GifDecoder))
        {
            Assert.Equal(expectedWidth, image.Width);
            Assert.Equal(expectedHeight, image.Height);
        }
    }

    [Fact]
    public void CanDecodeIntermingledImages()
    {
        using (var kumin1 = Image.Load<Rgba32>(TestFile.Create(TestImages.Gif.Kumin).Bytes))
        using (Image.Load(TestFile.Create(TestImages.Png.Icon).Bytes))
        using (var kumin2 = Image.Load<Rgba32>(TestFile.Create(TestImages.Gif.Kumin).Bytes))
        {
            for (int i = 0; i < kumin1.Frames.Count; i++)
            {
                ImageFrame<Rgba32> first = kumin1.Frames[i];
                ImageFrame<Rgba32> second = kumin2.Frames[i];

                Assert.True(second.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> secondMemory));

                first.ComparePixelBufferTo(secondMemory.Span);
            }
        }
    }

    // https://github.com/SixLabors/ImageSharp/issues/1503
    [Theory]
    [WithFile(TestImages.Gif.Issues.Issue1530, PixelTypes.Rgba32)]
    public void Issue1530_BadDescriptorDimensions<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        image.DebugSaveMultiFrame(provider);
        image.CompareToReferenceOutputMultiFrame(provider, ImageComparer.Exact);
    }

    // https://github.com/SixLabors/ImageSharp/issues/2758
    [Theory]
    [WithFile(TestImages.Gif.Issues.Issue2758, PixelTypes.Rgba32)]
    public void Issue2758_BadDescriptorDimensions<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        image.DebugSaveMultiFrame(provider);
        image.CompareToReferenceOutputMultiFrame(provider, ImageComparer.Exact);
    }

    // https://github.com/SixLabors/ImageSharp/issues/405
    [Theory]
    [WithFile(TestImages.Gif.Issues.BadAppExtLength, PixelTypes.Rgba32)]
    [WithFile(TestImages.Gif.Issues.BadAppExtLength_2, PixelTypes.Rgba32)]
    public void Issue405_BadApplicationExtensionBlockLength<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        {
            image.DebugSave(provider);

            image.CompareFirstFrameToReferenceOutput(ImageComparer.Exact, provider);
        }
    }

    // https://github.com/SixLabors/ImageSharp/issues/1668
    [Theory]
    [WithFile(TestImages.Gif.Issues.InvalidColorIndex, PixelTypes.Rgba32)]
    public void Issue1668_InvalidColorIndex<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        {
            image.DebugSave(provider);

            image.CompareFirstFrameToReferenceOutput(ImageComparer.Exact, provider);
        }
    }

    [Theory]
    [WithFile(TestImages.Gif.Giphy, PixelTypes.Rgba32)]
    [WithFile(TestImages.Gif.Kumin, PixelTypes.Rgba32)]
    public void GifDecoder_DegenerateMemoryRequest_ShouldTranslateTo_ImageFormatException<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        provider.LimitAllocatorBufferCapacity().InPixelsSqrt(10);
        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => provider.GetImage(GifDecoder));
        Assert.IsType<InvalidMemoryOperationException>(ex.InnerException);
    }

    [Theory]
    [WithFile(TestImages.Gif.Giphy, PixelTypes.Rgba32)]
    [WithFile(TestImages.Gif.Kumin, PixelTypes.Rgba32)]
    public void GifDecoder_CanDecode_WithLimitedAllocatorBufferCapacity(
        TestImageProvider<Rgba32> provider)
    {
        static void RunTest(string providerDump, string nonContiguousBuffersStr)
        {
            TestImageProvider<Rgba32> provider
                = BasicSerializer.Deserialize<TestImageProvider<Rgba32>>(providerDump);

            provider.LimitAllocatorBufferCapacity().InPixelsSqrt(100);

            using Image<Rgba32> image = provider.GetImage(GifDecoder);
            image.DebugSave(provider, nonContiguousBuffersStr);
            image.CompareToOriginal(provider);
        }

        string providerDump = BasicSerializer.Serialize(provider);
        RemoteExecutorRunner.Invoke(
                RunTest,
                providerDump,
                "Disco")
            .Dispose();
    }

    // https://github.com/SixLabors/ImageSharp/issues/1962
    [Theory]
    [WithFile(TestImages.Gif.Issues.Issue1962NoColorTable, PixelTypes.Rgba32)]
    public void Issue1962<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);

        image.CompareFirstFrameToReferenceOutput(ImageComparer.Exact, provider);
    }

    // https://github.com/SixLabors/ImageSharp/issues/2012
    [Theory]
    [WithFile(TestImages.Gif.Issues.Issue2012EmptyXmp, PixelTypes.Rgba32)]
    public void Issue2012EmptyXmp<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();

        image.DebugSave(provider);
        image.CompareFirstFrameToReferenceOutput(ImageComparer.Exact, provider);
    }

    // https://github.com/SixLabors/ImageSharp/issues/2012
    [Theory]
    [WithFile(TestImages.Gif.Issues.Issue2012BadMinCode, PixelTypes.Rgba32)]
    public void Issue2012BadMinCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        image.DebugSave(provider);
        image.CompareToReferenceOutput(provider);
    }

    // https://bugzilla.mozilla.org/show_bug.cgi?id=55918
    [Theory]
    [WithFile(TestImages.Gif.Issues.DeferredClearCode, PixelTypes.Rgba32)]
    public void IssueDeferredClearCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();

        image.DebugSave(provider);
        image.CompareFirstFrameToReferenceOutput(ImageComparer.Exact, provider);
    }

    // https://github.com/SixLabors/ImageSharp/issues/2743
    [Theory]
    [WithFile(TestImages.Gif.Issues.BadMaxLzwBits, PixelTypes.Rgba32)]
    public void IssueTooLargeLzwBits<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        image.DebugSaveMultiFrame(provider);
        image.CompareToReferenceOutputMultiFrame(provider, ImageComparer.Exact);
    }

    // https://github.com/SixLabors/ImageSharp/issues/2859
    [Theory]
    [WithFile(TestImages.Gif.Issues.Issue2859_A, PixelTypes.Rgba32)]
    [WithFile(TestImages.Gif.Issues.Issue2859_B, PixelTypes.Rgba32)]
    public void Issue2859_LZWPixelStackOverflow<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        image.DebugSaveMultiFrame(provider);
        image.CompareToReferenceOutputMultiFrame(provider, ImageComparer.Exact);
    }

    // https://github.com/SixLabors/ImageSharp/issues/2953
    [Theory]
    [WithFile(TestImages.Gif.Issues.Issue2953, PixelTypes.Rgba32)]
    public void Issue2953<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // We should throw a InvalidImageContentException when trying to identify or load an invalid GIF file.
        var testFile = TestFile.Create(provider.SourceFileOrDescription);

        Assert.Throws<InvalidImageContentException>(() => Image.Identify(testFile.FullPath));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(testFile.FullPath));
    }

    [Fact]
    public void Decode_LzwCodePastCodeTables_StopsDecodingFrame()
    {
        byte[] data = CreateLzwCodePastCodeTablesGif();

        using Image<Rgba32> image = Image.Load<Rgba32>(data);

        // No pixels are decoded, so the frame keeps the cleared indices.
        Assert.Equal((4, 4), (image.Width, image.Height));
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Assert.Equal(GlobalColorTable[0], image[x, y].Rgb);
            }
        }
    }

    [Fact]
    public void Identify_LzwCodePastCodeTables_ReturnsImageInfo()
    {
        byte[] data = CreateLzwCodePastCodeTablesGif();

        IImageInfo info = Image.Identify(data);

        Assert.Equal((4, 4), (info.Width, info.Height));
    }

    [Theory]
    [InlineData(20, 20, 0, true)]
    [InlineData(10, 10, 5, true)]
    [InlineData(20, 20, 0, false)]
    [InlineData(10, 10, 5, false)]
    [InlineData(10, 3, 7, true)]
    [InlineData(10, 1, 0, true)]
    [InlineData(10, 2, 0, true)]
    [InlineData(10, 4, 0, true)]
    [InlineData(10, 10, 10, true)]
    [InlineData(10, 10, 10, false)]
    [InlineData(10, 10, 9, true)]
    [InlineData(10, 20, 9, true)]
    [InlineData(10, 20, 8, true)]
    public void Decode_FrameGeometry_WritesOnlyRowsInsideCanvas(int width, int height, int top, bool interlaced)
    {
        // Each frame row is filled with the color index of its row number modulo 4.
        int[] rowOrder = interlaced ? GetInterlacedRowOrder(height) : Enumerable.Range(0, height).ToArray();
        byte[] indices = new byte[width * height];
        for (int i = 0; i < rowOrder.Length; i++)
        {
            indices.AsSpan(i * width, width).Fill((byte)(rowOrder[i] % 4));
        }

        byte[] data = CreateGif(10, 10, 0, top, width, height, interlaced, 2, EncodeLzw(indices));

        using Image<Rgba32> image = Image.Load<Rgba32>(data);

        Assert.Equal((10, 10), (image.Width, image.Height));
        for (int y = 0; y < image.Height; y++)
        {
            Rgb24 expected = y < top || y >= top + height ? Color.Black.ToPixel<Rgb24>() : GlobalColorTable[(y - top) % 4];
            for (int x = 0; x < image.Width; x++)
            {
                Assert.Equal(expected, image[x, y].Rgb);
            }
        }
    }

    [Fact]
    public void Decode_InterlacedFrameWithOneVisibleRow_StopsAfterThatRow()
    {
        // The canvas shows only frame row 0, which no later interlace pass revisits. The frame data
        // ends after that row, so skipping the hidden rows would read past it into the next frame.
        byte[] first = CreateGif(4, 1, 0, 0, 4, 16, interlaced: true, 2, EncodeLzw(new byte[] { 1, 1, 1, 1 }));
        byte[] second = CreateGif(4, 1, 0, 0, 4, 1, interlaced: false, 2, EncodeLzw(new byte[] { 2, 2, 2, 2 }));

        // Replace the first file's trailer with the image descriptor and data of the second.
        int frameStart = 6 + 7 + (GlobalColorTable.Length * 3);
        byte[] data = [.. first.AsSpan(0, first.Length - 1), .. second.AsSpan(frameStart)];

        using Image<Rgba32> image = Image.Load<Rgba32>(data);

        Assert.Equal(2, image.Frames.Count);
        for (int x = 0; x < image.Width; x++)
        {
            Assert.Equal(GlobalColorTable[1], image.Frames[0][x, 0].Rgb);
            Assert.Equal(GlobalColorTable[2], image.Frames[1][x, 0].Rgb);
        }
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(0, 0)]
    public void Decode_ZeroSizedLogicalScreen_ThrowsInvalidImageContentException(int screenWidth, int screenHeight)
    {
        byte[] data = CreateGif(screenWidth, screenHeight, 0, 0, 1, 1, false, 2, EncodeLzw(new byte[] { 1 }));

        Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgba32>(data));
    }

    [Fact]
    public void Decode_FrameDataEndsBeforeFrameIsFull_DoesNotReadNextFrame()
    {
        // The first frame's only sub-block holds a clear code, index 1 and two bits of a third code.
        // Its block terminator follows, so the rest of the frame has no data.
        byte[] first = CreateGif(4, 4, 0, 0, 4, 4, interlaced: false, 2, new byte[] { 0x4C });
        byte[] second = CreateGif(4, 4, 0, 0, 4, 4, interlaced: false, 2, EncodeLzw(Enumerable.Repeat((byte)2, 16).ToArray()));

        using Image<Rgba32> image = Image.Load<Rgba32>(AppendFrame(first, second));

        Assert.Equal(2, image.Frames.Count);
        AssertFramePixels(image.Frames[0], (x, y) => x == 0 && y == 0 ? GlobalColorTable[1] : GlobalColorTable[0]);
        AssertFramePixels(image.Frames[1], (_, _) => GlobalColorTable[2]);
    }

    [Fact]
    public void Decode_FrameDataEndCodeBeforeFrameIsFull_IgnoresCodesAfterEndCode()
    {
        // Codes after the end code are not frame data, even inside the same sub-block.
        byte[] lzwData = PackThreeBitCodes(4, 1, 5, 4, 2, 2, 4, 2, 2, 4, 2, 2, 5);
        byte[] first = CreateGif(4, 4, 0, 0, 4, 4, interlaced: false, 2, lzwData);
        byte[] second = CreateGif(4, 4, 0, 0, 4, 4, interlaced: false, 2, EncodeLzw(Enumerable.Repeat((byte)3, 16).ToArray()));

        using Image<Rgba32> image = Image.Load<Rgba32>(AppendFrame(first, second));

        Assert.Equal(2, image.Frames.Count);
        AssertFramePixels(image.Frames[0], (x, y) => x == 0 && y == 0 ? GlobalColorTable[1] : GlobalColorTable[0]);
        AssertFramePixels(image.Frames[1], (_, _) => GlobalColorTable[3]);
    }

    [Fact]
    public void Decode_FrameWithoutColorTable_SkipsItsDataAndDecodesNextFrame()
    {
        // The first frame has no color table to draw with. The second frame has a local one.
        using MemoryStream stream = new();
        stream.Write("GIF89a"u8);
        WriteUInt16(stream, 4);
        WriteUInt16(stream, 4);
        stream.Write(new byte[] { 0, 0, 0 });
        WriteImageDescriptor(stream, 4, 4, 0);
        WriteImageData(stream, EncodeLzw(Enumerable.Repeat((byte)1, 16).ToArray()));
        WriteImageDescriptor(stream, 4, 4, 0x81);
        foreach (Rgb24 color in GlobalColorTable)
        {
            stream.Write(new[] { color.R, color.G, color.B });
        }

        WriteImageData(stream, EncodeLzw(Enumerable.Repeat((byte)2, 16).ToArray()));
        stream.WriteByte(0x3B);

        using Image<Rgba32> image = Image.Load<Rgba32>(stream.ToArray());

        Assert.Equal(2, image.Frames.Count);
        AssertFramePixels(image.Frames[1], (_, _) => GlobalColorTable[2]);
    }

    [Fact]
    public void Decode_TruncatedGraphicControlExtensionAfterFirstFrame_ThrowsAndReleasesImage()
    {
        // The class level ValidateDisposedMemoryAllocations attribute checks that the
        // first frame's pixel buffer is returned when decoding fails.
        byte[] first = CreateGif(4, 4, 0, 0, 4, 4, interlaced: false, 2, EncodeLzw(new byte[16]));
        byte[] data = [.. first.AsSpan(0, first.Length - 1), 0x21, 0xF9, 0x04, 0x00];

        Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgba32>(data));
    }

    [Fact]
    public void Decode_OutOfMemoryForLaterFrame_ThrowsInvalidImageContentExceptionAndReleasesImage()
    {
        // The allocator runs out of memory when the second frame's canvas is allocated.
        byte[] first = CreateGif(100, 100, 0, 0, 1, 1, interlaced: false, 2, EncodeLzw(new byte[] { 1 }));
        byte[] second = CreateGif(100, 100, 0, 0, 1, 1, interlaced: false, 2, EncodeLzw(new byte[] { 2 }));
        OutOfMemoryAllocator allocator = new(largeLength: 100 * 100, allowedLargeAllocations: 1);
        allocator.EnableNonThreadSafeLogging();
        Configuration configuration = new(new GifConfigurationModule()) { MemoryAllocator = allocator };

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(
            () => Image.Load<Rgba32>(configuration, AppendFrame(first, second)));

        Assert.IsType<OutOfMemoryException>(ex.InnerException);
        Assert.Equal(allocator.AllocationLog.Count, allocator.ReturnLog.Count);
    }

    [Fact]
    public void Decode_LogicalScreenOverAllocatorLimit_ThrowsInvalidImageContentException()
    {
        byte[] first = CreateGif(65535, 65535, 0, 0, 1, 1, interlaced: false, 2, EncodeLzw(new byte[] { 1 }));
        byte[] second = CreateGif(65535, 65535, 0, 0, 1, 1, interlaced: false, 2, EncodeLzw(new byte[] { 2 }));

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(
            () => Image.Load<Rgba32>(AppendFrame(first, second)));

        Assert.IsType<InvalidMemoryOperationException>(ex.InnerException);
    }

    // Appends the image descriptor and data of a second file made by CreateGif to the first.
    private static byte[] AppendFrame(byte[] first, byte[] second)
    {
        int frameStart = 6 + 7 + (GlobalColorTable.Length * 3);
        return [.. first.AsSpan(0, first.Length - 1), .. second.AsSpan(frameStart)];
    }

    private static void AssertFramePixels(ImageFrame<Rgba32> frame, Func<int, int, Rgb24> expected)
    {
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                Assert.Equal(expected(x, y), frame[x, y].Rgb);
            }
        }
    }

    private static void WriteImageDescriptor(Stream stream, int width, int height, byte flags)
    {
        stream.WriteByte(0x2C);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, width);
        WriteUInt16(stream, height);
        stream.WriteByte(flags);
    }

    // Writes a minimum code size of 2 and the data in a single sub-block.
    private static void WriteImageData(Stream stream, byte[] lzwData)
    {
        stream.WriteByte(2);
        stream.WriteByte((byte)lzwData.Length);
        stream.Write(lzwData);
        stream.WriteByte(0);
    }

    // Packs 3-bit LZW codes, least significant bit first.
    private static byte[] PackThreeBitCodes(params int[] codes)
    {
        List<byte> output = new();
        int buffer = 0;
        int bits = 0;
        foreach (int code in codes)
        {
            buffer |= code << bits;
            bits += 3;
            while (bits >= 8)
            {
                output.Add((byte)buffer);
                buffer >>= 8;
                bits -= 8;
            }
        }

        if (bits > 0)
        {
            output.Add((byte)buffer);
        }

        return output.ToArray();
    }

    // Creates a 4x4 GIF whose frame has a minimum code size of 12 and a single 13-bit code of 4098.
    // The clear code is 4096, so 4098 cannot be looked up in the 4096-entry code tables.
    private static byte[] CreateLzwCodePastCodeTablesGif()
        => CreateGif(4, 4, 0, 0, 4, 4, interlaced: false, minCodeSize: 12, lzwData: new byte[] { 0x02, 0x10 });

    // Creates a GIF89a with a 4-entry global color table and a single image descriptor.
    private static byte[] CreateGif(
        int screenWidth,
        int screenHeight,
        int left,
        int top,
        int width,
        int height,
        bool interlaced,
        int minCodeSize,
        byte[] lzwData)
    {
        using MemoryStream stream = new();
        stream.Write("GIF89a"u8);

        // Logical screen descriptor with a 4-entry global color table.
        WriteUInt16(stream, screenWidth);
        WriteUInt16(stream, screenHeight);
        stream.WriteByte(0x81);
        stream.WriteByte(0);
        stream.WriteByte(0);
        foreach (Rgb24 color in GlobalColorTable)
        {
            stream.WriteByte(color.R);
            stream.WriteByte(color.G);
            stream.WriteByte(color.B);
        }

        // Image descriptor with no local color table.
        stream.WriteByte(0x2C);
        WriteUInt16(stream, left);
        WriteUInt16(stream, top);
        WriteUInt16(stream, width);
        WriteUInt16(stream, height);
        stream.WriteByte((byte)(interlaced ? 0x40 : 0));

        // The image data split into sub-blocks.
        stream.WriteByte((byte)minCodeSize);
        for (int i = 0; i < lzwData.Length; i += 255)
        {
            int length = Math.Min(255, lzwData.Length - i);
            stream.WriteByte((byte)length);
            stream.Write(lzwData, i, length);
        }

        stream.WriteByte(0);
        stream.WriteByte(0x3B);
        return stream.ToArray();
    }

    // Returns the frame rows in the order an interlaced GIF stores them.
    private static int[] GetInterlacedRowOrder(int height)
    {
        List<int> rows = new();
        foreach ((int start, int step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
        {
            for (int y = start; y < height; y += step)
            {
                rows.Add(y);
            }
        }

        return rows.ToArray();
    }

    // Encodes the indices with a minimum code size of 2. A clear code is sent before every
    // pair of literal codes so the code table never grows and every code stays 3 bits wide.
    private static byte[] EncodeLzw(byte[] indices)
    {
        const int clearCode = 4;
        const int endCode = 5;
        List<byte> output = new();
        int buffer = 0;
        int bits = 0;

        void WriteCode(int code)
        {
            buffer |= code << bits;
            bits += 3;
            while (bits >= 8)
            {
                output.Add((byte)buffer);
                buffer >>= 8;
                bits -= 8;
            }
        }

        for (int i = 0; i < indices.Length; i++)
        {
            if (i % 2 == 0)
            {
                WriteCode(clearCode);
            }

            WriteCode(indices[i]);
        }

        WriteCode(endCode);
        if (bits > 0)
        {
            output.Add((byte)buffer);
        }

        return output.ToArray();
    }

    private static void WriteUInt16(Stream stream, int value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
    }

    // Throws OutOfMemoryException for allocations of at least largeLength elements once
    // allowedLargeAllocations of them have been made.
    private sealed class OutOfMemoryAllocator : TestMemoryAllocator
    {
        private readonly int largeLength;
        private int allowedLargeAllocations;

        public OutOfMemoryAllocator(int largeLength, int allowedLargeAllocations)
        {
            this.largeLength = largeLength;
            this.allowedLargeAllocations = allowedLargeAllocations;
        }

        public override IMemoryOwner<T> Allocate<T>(int length, AllocationOptions options = AllocationOptions.None)
        {
            if (length >= this.largeLength && this.allowedLargeAllocations-- <= 0)
            {
                throw new OutOfMemoryException();
            }

            return base.Allocate<T>(length, options);
        }
    }
}
