// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Tiff.Constants;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Tiff;

/// <summary>
/// Decodes hand-built TIFF files with unusual layouts, very large strips and malformed data.
/// </summary>
[Trait("Format", "Tiff")]
[ValidateDisposedMemoryAllocations]
public class TiffDecoderMalformedInputTests
{
    [Theory]
    [InlineData(false, 4000, null)]
    [InlineData(false, 4000, 100000u)]
    [InlineData(false, 12000, 100000u)]
    [InlineData(false, 12000, uint.MaxValue)]
    [InlineData(true, 4, null)]
    [InlineData(true, 4, 2u)]
    [InlineData(true, 12000, null)]
    [InlineData(true, 12000, 1u)]
    [InlineData(true, 12000, 2u)]
    [InlineData(true, 12000, 100000u)]
    [InlineData(true, 12000, uint.MaxValue)]
    public void Decode_RowsPerStripMissingOrAboveHeight_DecodesAllRows(bool planar, int width, uint? rowsPerStrip)
    {
        const int height = 2;
        int rowsInStrip = (int)Math.Min(rowsPerStrip ?? uint.MaxValue, height);
        int stripsPerPlane = height / rowsInStrip;

        List<byte[]> strips = new();
        if (planar)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                for (int strip = 0; strip < stripsPerPlane; strip++)
                {
                    byte[] data = new byte[width * rowsInStrip];
                    for (int y = 0; y < rowsInStrip; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            data[(y * width) + x] = Sample(x, (strip * rowsInStrip) + y, channel);
                        }
                    }

                    strips.Add(data);
                }
            }
        }
        else
        {
            for (int strip = 0; strip < stripsPerPlane; strip++)
            {
                byte[] data = new byte[width * rowsInStrip * 3];
                for (int y = 0; y < rowsInStrip; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        for (int channel = 0; channel < 3; channel++)
                        {
                            data[(((y * width) + x) * 3) + channel] = Sample(x, (strip * rowsInStrip) + y, channel);
                        }
                    }
                }

                strips.Add(data);
            }
        }

        TiffPage page = TiffPage.Create(width, height, new ushort[] { 8, 8, 8 }, TiffPhotometricInterpretation.Rgb, planar, rowsPerStrip, strips);

        using Image<Rgb24> image = Decode<Rgb24>(BuildTiff(page));

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.Equal(new Rgb24(Sample(x, y, 0), Sample(x, y, 1), Sample(x, y, 2)), image[x, y]);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decode_RowsPerStripZero_ThrowsImageFormatException(bool planar)
    {
        byte[][] strips = planar ? new[] { new byte[4], new byte[4], new byte[4] } : new[] { new byte[12] };
        TiffPage page = TiffPage.Create(2, 2, new ushort[] { 8, 8, 8 }, TiffPhotometricInterpretation.Rgb, planar, 0, strips);

        Assert.Throws<ImageFormatException>(() => Decode<Rgb24>(BuildTiff(page)).Dispose());
    }

    [Theory]
    [InlineData(TiffPredictor.Horizontal, false)]
    [InlineData(TiffPredictor.Horizontal, true)]
    [InlineData(TiffPredictor.FloatingPoint, false)]
    public void Decode_PredictorWithoutLzwOrDeflate_IgnoresPredictor(TiffPredictor predictor, bool packBits)
    {
        // The samples would decode as 10, 21, 33, 46 if the horizontal predictor were applied.
        byte[] samples = { 10, 11, 12, 13 };
        TiffPage page = TiffPage.Create(2, 2, new ushort[] { 8 }, TiffPhotometricInterpretation.BlackIsZero, false, null, new[] { packBits ? new byte[] { 3, 10, 11, 12, 13 } : samples });
        page.Compression = packBits ? TiffCompression.PackBits : TiffCompression.None;
        page.Predictor = predictor;

        using Image<L8> image = Decode<L8>(BuildTiff(page));

        Assert.Equal(samples[0], image[0, 0].PackedValue);
        Assert.Equal(samples[1], image[1, 0].PackedValue);
        Assert.Equal(samples[2], image[0, 1].PackedValue);
        Assert.Equal(samples[3], image[1, 1].PackedValue);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Decode_PlanarWithFewerStripOffsetsThanPlanes_ThrowsImageFormatException(int stripCount)
    {
        byte[][] strips = Enumerable.Range(0, stripCount).Select(_ => new byte[4]).ToArray();
        TiffPage page = TiffPage.Create(2, 2, new ushort[] { 8, 8, 8 }, TiffPhotometricInterpretation.Rgb, true, null, strips);

        Assert.Throws<ImageFormatException>(() => Decode<Rgb24>(BuildTiff(page)).Dispose());
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(0, 0)]
    public void Decode_ZeroWidthOrHeight_ThrowsImageFormatException(int width, int height)
    {
        TiffPage page = TiffPage.Create(width, height, new ushort[] { 8 }, TiffPhotometricInterpretation.BlackIsZero, false, null, new[] { new byte[4] });

        Assert.ThrowsAny<ImageFormatException>(() => Decode<L8>(BuildTiff(page)).Dispose());
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    public void Identify_ZeroWidthOrHeight_ThrowsImageFormatException(int width, int height)
    {
        TiffPage page = TiffPage.Create(width, height, new ushort[] { 8 }, TiffPhotometricInterpretation.BlackIsZero, false, null, new[] { new byte[4] });
        using MemoryStream stream = new(BuildTiff(page), false);

        Assert.ThrowsAny<ImageFormatException>(() => Image.Identify(stream));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decode_FewerStripByteCountsThanStripOffsets_ThrowsImageFormatException(bool planar)
    {
        byte[][] strips = planar
            ? Enumerable.Range(0, 6).Select(_ => new byte[2]).ToArray()
            : new[] { new byte[6], new byte[6] };
        TiffPage page = TiffPage.Create(2, 2, new ushort[] { 8, 8, 8 }, TiffPhotometricInterpretation.Rgb, planar, 1, strips);
        page.StripByteCounts = page.StripByteCounts.Take(page.StripByteCounts.Length - 1).ToArray();

        Assert.Throws<ImageFormatException>(() => Decode<Rgb24>(BuildTiff(page)).Dispose());
    }

    [Fact]
    public void Decode_UncompressedStripLargerThan2GB_DecodesInBands()
    {
        // 16384 x 8193 pixels with 4 x 32-bit samples is one strip of 2_147_745_792 bytes (more than 2^31),
        // while the decoded L8 image needs only 134 MB. Only the first 256 pixels are stored; they are white.
        const int width = 16384;
        const int height = 8193;
        byte[] data = new byte[256 * 16];
        data.AsSpan().Fill(0xFF);
        TiffPage page = TiffPage.Create(width, height, new ushort[] { 32, 32, 32, 32 }, TiffPhotometricInterpretation.Rgb, false, null, new[] { data });

        using Image<L8> image = Decode<L8>(BuildTiff(page));

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(255, image[0, 0].PackedValue);
        Assert.Equal(255, image[255, 0].PackedValue);
        Assert.Equal(0, image[256, 0].PackedValue);
        Assert.Equal(0, image[0, 1].PackedValue);
        Assert.Equal(0, image[width - 1, height - 1].PackedValue);
    }

    [Fact]
    public void Decode_PlanarUncompressedPlaneLargerThanBufferLimit_DecodesInBands()
    {
        // Each 4096 x 5000 plane of 32-bit samples needs 81.92 MB, more than the 64 MB buffer limit,
        // while the decoded L8 image needs only 20.48 MB. The first 1024 pixels of each plane are white.
        const int width = 4096;
        const int height = 5000;
        byte[][] strips = Enumerable.Range(0, 4).Select(_ => Enumerable.Repeat((byte)0xFF, 1024 * 4).ToArray()).ToArray();
        TiffPage page = TiffPage.Create(width, height, new ushort[] { 32, 32, 32, 32 }, TiffPhotometricInterpretation.Rgb, true, null, strips);

        Configuration configuration = CreateConfiguration(allocationLimitMegabytes: 64);

        using Image<L8> image = Decode<L8>(BuildTiff(page), configuration);

        Assert.Equal(255, image[0, 0].PackedValue);
        Assert.Equal(255, image[1023, 0].PackedValue);
        Assert.Equal(0, image[1024, 0].PackedValue);
        Assert.Equal(0, image[width - 1, height - 1].PackedValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decode_UncompressedStripAboveBufferLimit_DecodesEveryBand(bool planar)
    {
        // With a 1 MB buffer limit, the 1024 x 700 strip of 3 x 16-bit grey samples (4.3 MB chunky,
        // 1.43 MB per plane) is read in several bands, while the decoded L8 image needs 0.72 MB.
        const int width = 1024;
        const int height = 700;
        TiffPage page = TiffPage.Create(width, height, new ushort[] { 16, 16, 16 }, TiffPhotometricInterpretation.Rgb, planar, null, CreateGrey16Strips(width, height, planar));

        using Image<L8> image = Decode<L8>(BuildTiff(page), CreateConfiguration(allocationLimitMegabytes: 1));

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.Equal(Sample(x, y, 0), image[x, y].PackedValue);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decode_UncompressedStripTruncated_DecodesMissingRowsAsZero(bool planar)
    {
        // StripByteCounts claims whole strips, but the file ends 300 rows into the last strip. The missing
        // rows must decode as if they were zero, the same as a complete file that stores zeros there,
        // and must not repeat data from an earlier band.
        const int width = 1024;
        const int height = 700;
        const int storedRows = 300;
        byte[][] strips = CreateGrey16Strips(width, height, planar);
        int bytesPerRow = strips[^1].Length / height;
        strips[^1].AsSpan(storedRows * bytesPerRow).Clear();
        TiffPage complete = TiffPage.Create(width, height, new ushort[] { 16, 16, 16 }, TiffPhotometricInterpretation.Rgb, planar, null, strips);

        strips[^1] = strips[^1].AsSpan(0, storedRows * bytesPerRow).ToArray();
        TiffPage truncated = TiffPage.Create(width, height, new ushort[] { 16, 16, 16 }, TiffPhotometricInterpretation.Rgb, planar, null, strips);
        truncated.StripByteCounts = complete.StripByteCounts;

        using Image<L8> expected = Decode<L8>(BuildTiff(complete));
        using Image<L8> image = Decode<L8>(BuildTiff(truncated), CreateConfiguration(allocationLimitMegabytes: 1));

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.Equal(expected[x, y], image[x, y]);
            }
        }
    }

    [Fact]
    public void Decode_BigTiffStripOffsetPastEndOfFile_DecodesEveryBandAsZero()
    {
        // The strip starts past the end of the file, so all of its data is missing. Adding a later band's
        // position to the strip offset must not wrap around to the start of the file and decode those bytes.
        const int width = 1024;
        const int height = 700;
        byte[] tiff = BuildBigTiffWithStripOffset(width, height, ulong.MaxValue);

        using Image<L8> image = Decode<L8>(tiff, CreateConfiguration(allocationLimitMegabytes: 1));

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.Equal(0, image[x, y].PackedValue);
            }
        }
    }

    [Fact]
    public void Decode_CompressedStripLargerThan2GB_ThrowsWithStripSizeInMessage()
    {
        // A Deflate strip cannot be decoded in bands, so its 2_147_745_792 byte buffer is reported.
        TiffPage page = TiffPage.Create(16384, 8193, new ushort[] { 32, 32, 32, 32 }, TiffPhotometricInterpretation.Rgb, false, null, new[] { new byte[16] });
        page.Compression = TiffCompression.Deflate;

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => Decode<L8>(BuildTiff(page)).Dispose());

        Assert.Contains("16384x8193", ex.Message, StringComparison.Ordinal);
        Assert.Contains("2147745792", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TiffCompression.Deflate)]
    [InlineData(TiffCompression.PackBits)]
    [InlineData(TiffCompression.Lzw)]
    public void Decode_CompressedStripByteCountAboveIntMaxValue_ThrowsInvalidImageContentException(TiffCompression compression)
    {
        TiffPage page = TiffPage.Create(2, 2, new ushort[] { 8 }, TiffPhotometricInterpretation.BlackIsZero, false, null, new[] { new byte[16] });
        page.Compression = compression;
        page.StripByteCounts = new[] { 0x90000000u };

        Assert.Throws<InvalidImageContentException>(() => Decode<L8>(BuildTiff(page)).Dispose());
    }

    [Theory]
    [InlineData(TiffPhotometricInterpretation.BlackIsZero)]
    [InlineData(TiffPhotometricInterpretation.Rgb)]
    public void Decode_JpegStripHoldingAnotherFormat_ThrowsInvalidImageContentException(TiffPhotometricInterpretation photometric)
    {
        // Without JPEGTables each strip is a whole JPEG stream. A strip that holds a TIFF is not decoded by format detection,
        // which would also decode a strip that points at its own file again and again.
        ushort[] bitsPerSample = photometric == TiffPhotometricInterpretation.Rgb ? new ushort[] { 8, 8, 8 } : new ushort[] { 8 };
        byte[] nestedTiff = BuildTiff(TiffPage.Create(2, 2, bitsPerSample, photometric, false, null, new[] { new byte[4 * bitsPerSample.Length] }));
        TiffPage page = TiffPage.Create(2, 2, bitsPerSample, photometric, false, null, new[] { nestedTiff });
        page.Compression = TiffCompression.Jpeg;

        Assert.Throws<InvalidImageContentException>(() => Decode<Rgba32>(BuildTiff(page)).Dispose());
    }

    [Fact]
    public void Decode_FrameAboveAllocationLimit_ThrowsWithRealDimensions()
    {
        TiffPage page = TiffPage.Create(20000, 15000, new ushort[] { 16, 16, 16, 16 }, TiffPhotometricInterpretation.Rgb, false, null, new[] { new byte[4096] });

        Configuration configuration = CreateConfiguration(allocationLimitMegabytes: 100);

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => Decode<L8>(BuildTiff(page), configuration).Dispose());

        Assert.Contains("20000x15000", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_DirectoriesStoredInReverseFileOrder_DecodesAllFrames()
    {
        TiffPage first = TiffPage.Create(2, 2, new ushort[] { 8 }, TiffPhotometricInterpretation.BlackIsZero, false, 2, new[] { new byte[] { 10, 11, 12, 13 } });
        TiffPage second = TiffPage.Create(2, 2, new ushort[] { 8 }, TiffPhotometricInterpretation.BlackIsZero, false, 2, new[] { new byte[] { 20, 21, 22, 23 } });

        using Image<L8> image = Decode<L8>(BuildTiff(new[] { first, second }, reverseIfdOrder: true));

        Assert.Equal(2, image.Frames.Count);
        Assert.Equal(10, image.Frames[0][0, 0].PackedValue);
        Assert.Equal(13, image.Frames[0][1, 1].PackedValue);
        Assert.Equal(20, image.Frames[1][0, 0].PackedValue);
        Assert.Equal(23, image.Frames[1][1, 1].PackedValue);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void Decode_DirectoryChainRevisitsDirectory_ThrowsImageFormatException(int pageCount, bool reverseIfdOrder)
    {
        TiffPage[] pages = Enumerable.Range(0, pageCount)
            .Select(_ => TiffPage.Create(2, 2, new ushort[] { 8 }, TiffPhotometricInterpretation.BlackIsZero, false, 2, new[] { new byte[4] }))
            .ToArray();

        byte[] tiff = BuildTiff(pages, reverseIfdOrder, loopToFirstIfd: true);

        ImageFormatException ex = Assert.Throws<ImageFormatException>(() => Decode<L8>(tiff).Dispose());
        Assert.Contains("circular", ex.Message, StringComparison.Ordinal);
    }

    [Theory(Timeout = 10_000)]
    [InlineData(1UL)]
    [InlineData(5_000_000_000UL)]
    public async Task Decode_BigTiffEntryCountBeyondEndOfFile_ThrowsPromptly(ulong entryCount)
    {
        // A BigTIFF header whose first directory, at offset 16, declares entryCount entries of 20 bytes
        // each, and then ends: the file holds the count and none of the entries (GHSA-wmxv-xphr-5c9g).
        byte[] tiff = new byte[24];
        tiff[0] = (byte)'I';
        tiff[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), TiffConstants.BigTiffHeaderMagicNumber);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(4), TiffConstants.BigTiffBytesize);
        BinaryPrimitives.WriteUInt64LittleEndian(tiff.AsSpan(8), 16);
        BinaryPrimitives.WriteUInt64LittleEndian(tiff.AsSpan(16), entryCount);

        await Task.Run(() => Assert.ThrowsAny<ImageFormatException>(() => Decode<L8>(tiff).Dispose()), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Decode_LzwDataLongerThanStrip_IgnoresExcessData()
    {
        // Encode 64x64 black pixels as one LZW strip, then shrink ImageLength and RowsPerStrip to 3,
        // so the 4096 bytes of LZW data are decoded into a strip buffer of 192 bytes.
        using MemoryStream encoded = new();
        using (Image<L8> source = new(64, 64))
        {
            source.Save(encoded, new TiffEncoder { Compression = TiffCompression.Lzw, BitsPerPixel = TiffBitsPerPixel.Bit8 });
        }

        byte[] tiff = encoded.ToArray();
        Assert.Equal(1u, ReadEntryCount(tiff, ExifTagValue.StripOffsets));
        PatchEntryValue(tiff, ExifTagValue.ImageLength, 3);
        PatchEntryValue(tiff, ExifTagValue.RowsPerStrip, 3);

        using Image<L8> image = Decode<L8>(tiff);

        Assert.Equal(64, image.Width);
        Assert.Equal(3, image.Height);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Assert.Equal(0, image[x, y].PackedValue);
            }
        }
    }

    private static byte Sample(int x, int y, int channel) => (byte)((x * 7) + (y * 101) + (channel * 53));

    /// <summary>
    /// Creates the strips of a grey image stored as 3 x 16-bit RGB samples, one strip for the image
    /// (chunky) or one strip per plane (planar). Each sample is <c>Sample(x, y, 0) * 257</c>.
    /// </summary>
    private static byte[][] CreateGrey16Strips(int width, int height, bool planar)
    {
        int planes = planar ? 3 : 1;
        int samplesPerPixel = planar ? 1 : 3;
        byte[][] strips = new byte[planes][];
        for (int plane = 0; plane < planes; plane++)
        {
            byte[] data = new byte[width * height * samplesPerPixel * 2];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int sample = 0; sample < samplesPerPixel; sample++)
                    {
                        int index = ((((y * width) + x) * samplesPerPixel) + sample) * 2;
                        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(index), (ushort)(Sample(x, y, 0) * 257));
                    }
                }
            }

            strips[plane] = data;
        }

        return strips;
    }

    private static Image<TPixel> Decode<TPixel>(byte[] tiff, Configuration configuration = null)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using MemoryStream stream = new(tiff, false);
        return Image.Load<TPixel>(configuration ?? Configuration.Default, stream, new TiffDecoder());
    }

    private static Configuration CreateConfiguration(int allocationLimitMegabytes)
    {
        Configuration configuration = Configuration.Default.Clone();
        configuration.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions { AllocationLimitMegabytes = allocationLimitMegabytes });
        return configuration;
    }

    private static byte[] BuildTiff(TiffPage page) => BuildTiff(new[] { page });

    /// <summary>
    /// Writes a little-endian TIFF. The IFDs follow the header in page order, or in reverse page order
    /// when <paramref name="reverseIfdOrder"/> is set. The strip data of each page follows, so the
    /// last strip ends the file.
    /// </summary>
    private static byte[] BuildTiff(IReadOnlyList<TiffPage> pages, bool reverseIfdOrder = false, bool loopToFirstIfd = false)
    {
        List<byte> bytes = new() { 0x49, 0x49, 42, 0, 0, 0, 0, 0 };

        // Entry and value sizes do not depend on the data offsets, so the data can be placed first.
        uint[] dataOffsets = new uint[pages.Count];
        uint dataOffset = (uint)bytes.Count;
        foreach (TiffPage page in pages)
        {
            foreach ((ExifTagValue _, ExifDataType type, uint[] entryValues) in page.GetEntries(0))
            {
                int size = entryValues.Length * (type == ExifDataType.Short ? 2 : 4);
                dataOffset += 12 + (uint)(size > 4 ? size : 0);
            }

            dataOffset += 2 + 4;
        }

        for (int i = 0; i < pages.Count; i++)
        {
            dataOffsets[i] = dataOffset;
            dataOffset += (uint)pages[i].Data.Length;
        }

        uint[] ifdOffsets = new uint[pages.Count];
        int[] nextIfdPositions = new int[pages.Count];
        IEnumerable<int> order = Enumerable.Range(0, pages.Count);
        foreach (int i in reverseIfdOrder ? order.Reverse() : order)
        {
            ifdOffsets[i] = (uint)bytes.Count;
            List<(ExifTagValue Tag, ExifDataType Type, uint[] Values)> entries = pages[i].GetEntries(dataOffsets[i]);

            int valuesOffset = bytes.Count + 2 + (entries.Count * 12) + 4;
            List<byte> values = new();
            AddUInt16(bytes, (ushort)entries.Count);
            foreach ((ExifTagValue tag, ExifDataType type, uint[] entryValues) in entries)
            {
                AddUInt16(bytes, (ushort)tag);
                AddUInt16(bytes, (ushort)type);
                AddUInt32(bytes, (uint)entryValues.Length);

                List<byte> target = entryValues.Length * (type == ExifDataType.Short ? 2 : 4) <= 4 ? bytes : values;
                if (target == values)
                {
                    AddUInt32(bytes, (uint)(valuesOffset + values.Count));
                }

                int start = target.Count;
                foreach (uint value in entryValues)
                {
                    if (type == ExifDataType.Short)
                    {
                        AddUInt16(target, (ushort)value);
                    }
                    else
                    {
                        AddUInt32(target, value);
                    }
                }

                while (target == bytes && target.Count - start < 4)
                {
                    target.Add(0);
                }
            }

            nextIfdPositions[i] = bytes.Count;
            AddUInt32(bytes, 0);
            bytes.AddRange(values);
        }

        foreach (TiffPage page in pages)
        {
            bytes.AddRange(page.Data);
        }

        byte[] tiff = bytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), ifdOffsets[0]);
        for (int i = 0; i < pages.Count; i++)
        {
            uint next = i + 1 < pages.Count ? ifdOffsets[i + 1] : loopToFirstIfd ? ifdOffsets[0] : 0;
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(nextIfdPositions[i]), next);
        }

        return tiff;
    }

    /// <summary>
    /// Writes a little-endian BigTIFF with one uncompressed strip of 3 x 16-bit RGB samples at the given offset.
    /// The file is padded with 0xFF bytes to more than the strip size, so a wrapped offset lands inside it.
    /// </summary>
    private static byte[] BuildBigTiffWithStripOffset(int width, int height, ulong stripOffset)
    {
        ulong stripByteCount = (ulong)width * (ulong)height * 6;
        (ExifTagValue Tag, ExifDataType Type, ulong Count, ulong Value)[] entries =
        {
            (ExifTagValue.ImageWidth, ExifDataType.Long, 1, (ulong)width),
            (ExifTagValue.ImageLength, ExifDataType.Long, 1, (ulong)height),
            (ExifTagValue.BitsPerSample, ExifDataType.Short, 3, 16 | (16UL << 16) | (16UL << 32)),
            (ExifTagValue.Compression, ExifDataType.Short, 1, (ulong)TiffCompression.None),
            (ExifTagValue.PhotometricInterpretation, ExifDataType.Short, 1, (ulong)TiffPhotometricInterpretation.Rgb),
            (ExifTagValue.StripOffsets, ExifDataType.Long8, 1, stripOffset),
            (ExifTagValue.SamplesPerPixel, ExifDataType.Short, 1, 3),
            (ExifTagValue.RowsPerStrip, ExifDataType.Long, 1, (ulong)height),
            (ExifTagValue.StripByteCounts, ExifDataType.Long8, 1, stripByteCount),
        };

        // Header: byte order, version 43, offset size 8, reserved, first IFD offset.
        List<byte> bytes = new() { 0x49, 0x49, 43, 0, 8, 0, 0, 0 };
        AddUInt64(bytes, 16);
        AddUInt64(bytes, (ulong)entries.Length);
        foreach ((ExifTagValue tag, ExifDataType type, ulong count, ulong value) in entries)
        {
            AddUInt16(bytes, (ushort)tag);
            AddUInt16(bytes, (ushort)type);
            AddUInt64(bytes, count);
            AddUInt64(bytes, value);
        }

        AddUInt64(bytes, 0);
        bytes.AddRange(Enumerable.Repeat((byte)0xFF, (int)stripByteCount));
        return bytes.ToArray();
    }

    private static void AddUInt64(List<byte> bytes, ulong value)
    {
        AddUInt32(bytes, (uint)value);
        AddUInt32(bytes, (uint)(value >> 32));
    }

    private static void AddUInt16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
    }

    private static void AddUInt32(List<byte> bytes, uint value)
    {
        AddUInt16(bytes, (ushort)value);
        AddUInt16(bytes, (ushort)(value >> 16));
    }

    private static int FindEntry(byte[] tiff, ExifTagValue tag)
    {
        int ifdOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(tiff.AsSpan(4));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(ifdOffset));
        for (int i = 0; i < count; i++)
        {
            int entry = ifdOffset + 2 + (i * 12);
            if (BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(entry)) == (ushort)tag)
            {
                return entry;
            }
        }

        throw new InvalidOperationException($"Tag {tag} not found.");
    }

    private static uint ReadEntryCount(byte[] tiff, ExifTagValue tag)
        => BinaryPrimitives.ReadUInt32LittleEndian(tiff.AsSpan(FindEntry(tiff, tag) + 4));

    private static void PatchEntryValue(byte[] tiff, ExifTagValue tag, uint value)
    {
        int entry = FindEntry(tiff, tag);
        if (BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(entry + 2)) == (ushort)ExifDataType.Short)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(entry + 8), (ushort)value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(entry + 8), value);
        }
    }

    private sealed class TiffPage
    {
        public int Width { get; private set; }

        public int Height { get; private set; }

        public ushort[] BitsPerSample { get; private set; }

        public TiffPhotometricInterpretation Photometric { get; private set; }

        public TiffCompression Compression { get; set; } = TiffCompression.None;

        public TiffPredictor? Predictor { get; set; }

        public bool Planar { get; private set; }

        public uint? RowsPerStrip { get; private set; }

        public byte[] Data { get; private set; }

        public uint[] StripOffsets { get; private set; }

        public uint[] StripByteCounts { get; set; }

        public static TiffPage Create(int width, int height, ushort[] bitsPerSample, TiffPhotometricInterpretation photometric, bool planar, uint? rowsPerStrip, IReadOnlyList<byte[]> strips)
        {
            uint[] offsets = new uint[strips.Count];
            uint[] counts = new uint[strips.Count];
            uint position = 0;
            for (int i = 0; i < strips.Count; i++)
            {
                offsets[i] = position;
                counts[i] = (uint)strips[i].Length;
                position += counts[i];
            }

            return new TiffPage
            {
                Width = width,
                Height = height,
                BitsPerSample = bitsPerSample,
                Photometric = photometric,
                Planar = planar,
                RowsPerStrip = rowsPerStrip,
                Data = strips.SelectMany(s => s).ToArray(),
                StripOffsets = offsets,
                StripByteCounts = counts,
            };
        }

        public List<(ExifTagValue Tag, ExifDataType Type, uint[] Values)> GetEntries(uint dataOffset)
        {
            List<(ExifTagValue Tag, ExifDataType Type, uint[] Values)> entries = new()
            {
                (ExifTagValue.ImageWidth, ExifDataType.Long, new[] { (uint)this.Width }),
                (ExifTagValue.ImageLength, ExifDataType.Long, new[] { (uint)this.Height }),
                (ExifTagValue.BitsPerSample, ExifDataType.Short, this.BitsPerSample.Select(b => (uint)b).ToArray()),
                (ExifTagValue.Compression, ExifDataType.Short, new[] { (uint)this.Compression }),
                (ExifTagValue.PhotometricInterpretation, ExifDataType.Short, new[] { (uint)this.Photometric }),
                (ExifTagValue.StripOffsets, ExifDataType.Long, this.StripOffsets.Select(o => o + dataOffset).ToArray()),
                (ExifTagValue.SamplesPerPixel, ExifDataType.Short, new[] { (uint)this.BitsPerSample.Length }),
                (ExifTagValue.StripByteCounts, ExifDataType.Long, this.StripByteCounts),
            };

            if (this.RowsPerStrip.HasValue)
            {
                entries.Add((ExifTagValue.RowsPerStrip, ExifDataType.Long, new[] { this.RowsPerStrip.Value }));
            }

            if (this.Predictor.HasValue)
            {
                entries.Add((ExifTagValue.Predictor, ExifDataType.Short, new[] { (uint)this.Predictor.Value }));
            }

            if (this.Planar)
            {
                entries.Add((ExifTagValue.PlanarConfiguration, ExifDataType.Short, new[] { (uint)TiffPlanarConfiguration.Planar }));
            }

            entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));
            return entries;
        }
    }
}
