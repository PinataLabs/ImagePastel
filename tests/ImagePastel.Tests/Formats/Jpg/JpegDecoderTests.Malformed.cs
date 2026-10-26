// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jpeg;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.TestUtilities.ImageComparison;
using Xunit;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests.Formats.Jpg;

/// <summary>
/// Tests for streams written by imperfect producers, which libjpeg-based viewers still decode.
/// </summary>
[Trait("Format", "Jpg")]
public partial class JpegDecoderTests
{
    public static TheoryData<string, byte[]> ExtraneousBytesBeforeRestartData { get; } = new()
    {
        { TestImages.Jpeg.Issues.ExifGetString750Load, new byte[] { 0x00, 0x00 } },
        { TestImages.Jpeg.Issues.ExifGetString750Load, new byte[] { 0xFF, 0x00 } },
        { TestImages.Jpeg.Issues.ExifGetString750Load, new byte[] { 0x12 } },
        { TestImages.Jpeg.Issues.ExifGetString750Load, new byte[] { 0x12, 0x34 } },

        // FF xx pairs with a reserved marker code (below SOF0) look like a marker but cannot end the scan.
        { TestImages.Jpeg.Issues.ExifGetString750Load, new byte[] { 0xFF, 0x12 } },
        { TestImages.Jpeg.Issues.ExifGetString750Load, new byte[] { 0xFF, 0x01 } },
        { TestImages.Jpeg.Issues.ExifGetString750Load, new byte[] { 0xFF, 0xBF } },
        { TestImages.Jpeg.Issues.ExifGetString750Load, new byte[] { 0xFF, 0x12, 0xFF, 0x34 } },
        { TestImages.Jpeg.Baseline.Jpeg400, new byte[] { 0x00, 0x00 } },
        { TestImages.Jpeg.Baseline.Jpeg400, new byte[] { 0xFF, 0x12 } },

        // Progressive scans count down the same restart interval.
        { TestImages.Jpeg.Issues.BadRstProgressive518, new byte[] { 0x00, 0x00 } },
        { TestImages.Jpeg.Issues.BadRstProgressive518, new byte[] { 0xFF, 0x12 } },
    };

    [Theory]
    [MemberData(nameof(ExtraneousBytesBeforeRestartData))]
    public void Decode_ExtraneousBytesBeforeRestartMarkers_AreSkipped(string file, byte[] junk)
    {
        byte[] original = TestFile.Create(file).Bytes;
        byte[] corrupt = InsertBeforeRestartMarkers(original, junk, entropyCodedDataOnly: true);

        using Image<Rgba32> expected = Image.Load<Rgba32>(original);
        using Image<Rgba32> actual = Image.Load<Rgba32>(corrupt);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decode_AnyExtraneousBytesBeforeRestartMarkers_OnlyThrowsInvalidImageContent(bool entropyCodedDataOnly)
    {
        // This image also has RST-like byte pairs inside its APP segments. Inserting junk there corrupts the
        // segment lengths, so the marker parser can miss SOF and SOS, which must be reported as invalid content.
        byte[] original = TestFile.Create(TestImages.Jpeg.Baseline.App13WithEmptyIptc).Bytes;

        for (int b = 0; b <= 0xFF; b++)
        {
            foreach (byte[] junk in new[] { new[] { (byte)b }, new[] { (byte)b, (byte)0 }, new[] { (byte)0xFF, (byte)b } })
            {
                byte[] corrupt = InsertBeforeRestartMarkers(original, junk, entropyCodedDataOnly);
                try
                {
                    using Image<Rgba32> image = Image.Load<Rgba32>(corrupt);
                }
                catch (InvalidImageContentException)
                {
                    // Unrecoverable streams may be rejected, but only with the documented exception.
                }
            }
        }
    }

    public static TheoryData<string, byte[]> ReservedMarkerBeforeEndOfImageData { get; } = new()
    {
        { TestImages.Jpeg.Issues.ExifGetString750Load, new byte[] { 0xFF, 0x12 } },
        { TestImages.Jpeg.Issues.ExifGetString750Load, new byte[] { 0xFF, 0x01 } },
        { TestImages.Jpeg.Issues.ExifGetString750Load, new byte[] { 0xFF, 0xBF } },
        { TestImages.Jpeg.Issues.ExifGetString750Load, new byte[] { 0xFF, 0x12, 0xFF, 0x34 } },
        { TestImages.Jpeg.Baseline.Jpeg400, new byte[] { 0xFF, 0x12 } },
        { TestImages.Jpeg.Baseline.Calliphora, new byte[] { 0xFF, 0x12 } },
        { TestImages.Jpeg.Progressive.Progress, new byte[] { 0xFF, 0x12 } },
        { TestImages.Jpeg.Issues.BadRstProgressive518, new byte[] { 0xFF, 0x12 } },
    };

    [Theory]
    [MemberData(nameof(ReservedMarkerBeforeEndOfImageData))]
    public void Decode_ReservedMarkerBeforeEndOfImage_IsSkipped(string file, byte[] junk)
    {
        byte[] original = TestFile.Create(file).Bytes;
        Assert.Equal(JpegConstants.Markers.EOI, original[^1]);
        byte[] corrupt = Insert(original, original.Length - 2, junk);

        using Image<Rgba32> expected = Image.Load<Rgba32>(original);
        using Image<Rgba32> actual = Image.Load<Rgba32>(corrupt);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    // Inserts the junk bytes before every RSTn marker, either in the entropy-coded data following the first SOS only,
    // or before every RST-like byte pair in the file.
    private static byte[] InsertBeforeRestartMarkers(byte[] data, byte[] junk, bool entropyCodedDataOnly)
    {
        int start = entropyCodedDataOnly ? SegmentEnd(data, FindSegment(data, JpegConstants.Markers.SOS)) : 2;
        using var ms = new MemoryStream();
        ms.Write(data, 0, start);
        int count = 0;
        for (int i = start; i < data.Length; i++)
        {
            if (data[i] == JpegConstants.Markers.XFF && i + 1 < data.Length && data[i + 1] >= JpegConstants.Markers.RST0 && data[i + 1] <= JpegConstants.Markers.RST7)
            {
                ms.Write(junk, 0, junk.Length);
                count++;
            }

            ms.WriteByte(data[i]);
        }

        Assert.True(count > 0, "The test image has no restart markers.");
        return ms.ToArray();
    }

    [Theory]
    [InlineData(JpegConstants.Markers.RST0)]
    [InlineData(JpegConstants.Markers.RST7)]
    [InlineData(JpegConstants.Markers.TEM)]
    public void Decode_StandaloneMarkerBetweenHeaderSegments_IsSkipped(byte marker)
    {
        byte[] original = EncodeTestJpeg(JpegColorType.YCbCrRatio420);

        // RSTn and TEM have no length field (ITU T.81 B.1.1.4), insert one straight after APP0.
        int app0End = SegmentEnd(original, 2);
        byte[] corrupt = Insert(original, app0End, JpegConstants.Markers.XFF, marker);

        IImageInfo info = Image.Identify(corrupt);
        Assert.Equal(new Size(32, 24), new Size(info.Width, info.Height));

        using Image<Rgba32> expected = Image.Load<Rgba32>(original);
        using Image<Rgba32> actual = Image.Load<Rgba32>(corrupt);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    [Theory]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0x00, 0x00 })]
    [InlineData(new byte[] { 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x12, 0xFF, 0x00 })]
    public void Decode_JunkBytesBetweenHeaderSegments_AreSkipped(byte[] junk)
    {
        byte[] original = EncodeTestJpeg(JpegColorType.YCbCrRatio420);

        // Insert the junk after SOI, after APP0 and before SOS, so it is skipped both on an even and an odd offset.
        int app0End = SegmentEnd(original, 2);
        int sos = FindSegment(original, JpegConstants.Markers.SOS);
        byte[] corrupt = Insert(original, sos, junk);
        corrupt = Insert(corrupt, app0End, junk);
        corrupt = Insert(corrupt, 2, junk);

        IImageInfo info = Image.Identify(corrupt);
        Assert.Equal(new Size(32, 24), new Size(info.Width, info.Height));

        using Image<Rgba32> expected = Image.Load<Rgba32>(original);
        using Image<Rgba32> actual = Image.Load<Rgba32>(corrupt);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    [Fact]
    public void Identify_CancelledWhileSkippingJunkBytes_StopsReading()
    {
        const int junkLength = 16 * 1024 * 1024;
        const int cancelAfter = 1024 * 1024;
        byte[] original = EncodeTestJpeg(JpegColorType.YCbCrRatio420);
        byte[] corrupt = Insert(original, SegmentEnd(original, 2), new byte[junkLength]);

        using var cts = new CancellationTokenSource();
        using var stream = new CancellingStream(corrupt, cancelAfter, cts);

        Assert.ThrowsAny<OperationCanceledException>(() => JpegDecoder.Identify(Configuration.Default, stream, cts.Token));

        // The search for the next marker must notice the cancellation instead of reading the rest of the junk.
        Assert.True(stream.Position < cancelAfter * 2, $"Read {stream.Position} bytes after cancelling at {cancelAfter}.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Decode_FrameWithoutStartOfScan_ThrowsInvalidImageContent(bool progressive, bool appendEoi)
    {
        byte[] source = progressive
            ? TestFile.Create(TestImages.Jpeg.Progressive.Progress).Bytes
            : EncodeTestJpeg(JpegColorType.YCbCrRatio420);

        // Keep SOI, the tables and SOF, drop everything from the first SOS on.
        int sos = FindSegment(source, JpegConstants.Markers.SOS);
        byte[] truncated = source.AsSpan(0, sos).ToArray();
        if (appendEoi)
        {
            truncated = Insert(truncated, truncated.Length, JpegConstants.Markers.XFF, JpegConstants.Markers.EOI);
        }

        Assert.Throws<InvalidImageContentException>(() =>
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(truncated);
        });
    }

    [Fact]
    public void Decode_MarkerlessThreeComponentsWithUnknownIds_AssumesYCbCr()
    {
        // No JFIF or Adobe marker and component ids 0,1,2: libjpeg assumes YCbCr.
        byte[] data = EncodeTwoToneJpeg(JpegColorType.YCbCrRatio444);
        data = RemoveSegment(data, JpegConstants.Markers.APP0);
        data = RenumberComponents(data, 0, 1, 2);

        using Image<Rgba32> image = Image.Load<Rgba32>(data);
        AssertTwoTone(image);
    }

    [Fact]
    public void Decode_MarkerlessThreeComponentsWithRgbIds_DecodesAsRgb()
    {
        // The encoder marks RGB with both the 'R','G','B' ids and an Adobe marker. Keep only the ids.
        byte[] data = EncodeTwoToneJpeg(JpegColorType.Rgb);
        data = RemoveSegment(data, JpegConstants.Markers.APP14);

        using Image<Rgba32> image = Image.Load<Rgba32>(data);
        AssertTwoTone(image);
    }

    [Fact]
    public void Decode_AdobeTransformZeroWithUnknownIds_DecodesAsRgb()
    {
        // Keep only the Adobe marker (transform 0, RGB).
        byte[] data = EncodeTwoToneJpeg(JpegColorType.Rgb);
        data = RenumberComponents(data, 0, 1, 2);

        using Image<Rgba32> image = Image.Load<Rgba32>(data);
        AssertTwoTone(image);
    }

    private static byte[] EncodeTwoToneJpeg(JpegColorType colorType)
    {
        using var image = new Image<Rgba32>(16, 16, Color.White.ToPixel<Rgba32>());
        for (int y = 8; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                image[x, y] = new Rgba32(200, 30, 30);
            }
        }

        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { ColorType = colorType, Quality = 100 });
        return ms.ToArray();
    }

    private static void AssertTwoTone(Image<Rgba32> image)
    {
        static void AssertClose(Rgba32 expected, Rgba32 actual)
        {
            const int tolerance = 8;
            Assert.True(
                Math.Abs(expected.R - actual.R) <= tolerance
                && Math.Abs(expected.G - actual.G) <= tolerance
                && Math.Abs(expected.B - actual.B) <= tolerance,
                $"Expected {expected}, got {actual}.");
        }

        AssertClose(new Rgba32(255, 255, 255), image[0, 0]);
        AssertClose(new Rgba32(255, 255, 255), image[15, 7]);
        AssertClose(new Rgba32(200, 30, 30), image[0, 8]);
        AssertClose(new Rgba32(200, 30, 30), image[15, 15]);
    }

    // Walks the marker segments up to the first SOS. Returns the offset of the first segment with the given marker, or -1.
    private static int FindSegment(byte[] data, byte marker)
    {
        int offset = 2;
        while (data[offset + 1] != marker)
        {
            if (data[offset + 1] == JpegConstants.Markers.SOS)
            {
                return -1;
            }

            offset = SegmentEnd(data, offset);
        }

        return offset;
    }

    private static byte[] RemoveSegment(byte[] data, byte marker)
    {
        int start = FindSegment(data, marker);
        Assert.NotEqual(-1, start);
        int end = SegmentEnd(data, start);
        byte[] result = new byte[data.Length - (end - start)];
        Buffer.BlockCopy(data, 0, result, 0, start);
        Buffer.BlockCopy(data, end, result, start, data.Length - end);
        return result;
    }

    // Rewrites the component ids in SOF0 and the single SOS of a baseline interleaved jpeg.
    private static byte[] RenumberComponents(byte[] data, params byte[] ids)
    {
        byte[] result = (byte[])data.Clone();
        int sof = FindSegment(result, JpegConstants.Markers.SOF0);
        int sos = FindSegment(result, JpegConstants.Markers.SOS);
        Assert.Equal(ids.Length, result[sof + 9]);
        Assert.Equal(ids.Length, result[sos + 4]);
        for (int i = 0; i < ids.Length; i++)
        {
            result[sof + 10 + (3 * i)] = ids[i];
            result[sos + 5 + (2 * i)] = ids[i];
        }

        return result;
    }

    private static byte[] EncodeTestJpeg(JpegColorType colorType)
    {
        using var image = new Image<Rgba32>(32, 24);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                image[x, y] = new Rgba32((byte)(x * 8), (byte)(y * 10), (byte)((x + y) * 4));
            }
        }

        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { ColorType = colorType, Quality = 90 });
        return ms.ToArray();
    }

    // Returns the offset just past the marker segment (FF xx + length) starting at the given offset.
    private static int SegmentEnd(byte[] data, int offset)
    {
        Assert.Equal(JpegConstants.Markers.XFF, data[offset]);
        return offset + 2 + ((data[offset + 2] << 8) | data[offset + 3]);
    }

    private static byte[] Insert(byte[] data, int offset, params byte[] bytes)
    {
        byte[] result = new byte[data.Length + bytes.Length];
        Buffer.BlockCopy(data, 0, result, 0, offset);
        Buffer.BlockCopy(bytes, 0, result, offset, bytes.Length);
        Buffer.BlockCopy(data, offset, result, offset + bytes.Length, data.Length - offset);
        return result;
    }

    // Cancels the token once the given number of bytes has been read.
    private sealed class CancellingStream : MemoryStream
    {
        private readonly long cancelAfter;
        private readonly CancellationTokenSource cts;

        public CancellingStream(byte[] data, long cancelAfter, CancellationTokenSource cts)
            : base(data)
        {
            this.cancelAfter = cancelAfter;
            this.cts = cts;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = base.Read(buffer, offset, count);
            this.CancelIfPast();
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            int read = base.Read(buffer);
            this.CancelIfPast();
            return read;
        }

        public override int ReadByte()
        {
            int read = base.ReadByte();
            this.CancelIfPast();
            return read;
        }

        private void CancelIfPast()
        {
            if (this.Position >= this.cancelAfter)
            {
                this.cts.Cancel();
            }
        }
    }
}
