// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Tiff.Compression.Compressors;
using SixLabors.ImageSharp.Formats.Tiff.Constants;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Tiff.Utils;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Tiff;

/// <summary>
/// Decodes hand-built TIFF files that use tag combinations which the encoder does not write.
/// </summary>
[Trait("Format", "Tiff")]
public class TiffDecoderCorrectnessTests
{
    private const ushort UnusedPrivateTag = 65000;

    private static readonly byte[] PaletteIndices = { 0, 128, 200, 255 };

    [Theory]
    [InlineData(TiffCompression.None)]
    [InlineData(TiffCompression.Lzw)]
    public void Decode_PaletteWithEightBitColorMap_ScalesEntriesAsEightBit(TiffCompression compression)
    {
        ushort[] colorMap = CreateGrayColorMap();

        using Image<Rgb24> image = Image.Load<Rgb24>(CreatePaletteTiff(colorMap, compression));

        Assert.Equal(new Rgb24(0, 0, 0), image[0, 0]);
        Assert.Equal(new Rgb24(128, 128, 128), image[1, 0]);
        Assert.Equal(new Rgb24(200, 200, 200), image[2, 0]);
        Assert.Equal(new Rgb24(255, 255, 255), image[3, 0]);
    }

    [Theory]
    [InlineData(TiffCompression.None)]
    [InlineData(TiffCompression.Lzw)]
    public void Decode_PaletteWithSixteenBitColorMap_ScalesEntriesAsSixteenBit(TiffCompression compression)
    {
        // A single entry above 255 means the colormap uses the full 16-bit range.
        ushort[] colorMap = CreateGrayColorMap();
        colorMap[1] = 256;

        using Image<Rgb24> image = Image.Load<Rgb24>(CreatePaletteTiff(colorMap, compression));

        Assert.Equal(new Rgb24(0, 0, 0), image[0, 0]);
        Assert.Equal(new Rgb24(0, 0, 0), image[1, 0]);
        Assert.Equal(new Rgb24(1, 1, 1), image[2, 0]);
        Assert.Equal(new Rgb24(1, 1, 1), image[3, 0]);
    }

    [Theory]
    [InlineData(TiffCompression.None)]
    [InlineData(TiffCompression.CcittGroup4Fax)]
    public void Decode_BilevelWithoutBitsPerSample_UsesDefaultOfOneBit(TiffCompression compression)
    {
        using var source = new Image<L8>(16, 8);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                source[x, y] = new L8((byte)((x + y) % 3 == 0 ? 255 : 0));
            }
        }

        using var stream = new MemoryStream();
        source.Save(stream, new TiffEncoder { BitsPerPixel = TiffBitsPerPixel.Bit1, Compression = compression });
        byte[] withBitsPerSample = stream.ToArray();
        byte[] withoutBitsPerSample = RenameTag(withBitsPerSample, (ushort)ExifTagValue.BitsPerSample, UnusedPrivateTag);

        using Image<L8> expected = Image.Load<L8>(withBitsPerSample);
        using Image<L8> actual = Image.Load<L8>(withoutBitsPerSample);
        IImageInfo info = Image.Identify(withoutBitsPerSample);

        ImageComparer.Exact.VerifySimilarity(expected, actual);
        Assert.Equal(TiffBitsPerPixel.Bit1, actual.Frames.RootFrame.Metadata.GetTiffMetadata().BitsPerPixel);
        Assert.Equal(1, info.PixelType.BitsPerPixel);
    }

    [Theory]
    [InlineData(JpegColorType.YCbCrRatio420, 2, 2)]
    [InlineData(JpegColorType.YCbCrRatio422, 2, 1)]
    [InlineData(JpegColorType.YCbCrRatio444, 1, 1)]
    public void Decode_JpegCompressedYCbCrWithSubsampling_MatchesEmbeddedJpeg(JpegColorType colorType, ushort horizontalSubsampling, ushort verticalSubsampling)
    {
        const int size = 64;
        using var source = new Image<Rgb24>(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                source[x, y] = new Rgb24((byte)(x * 4), (byte)(y * 4), (byte)(255 - (x * 2) - (y * 2)));
            }
        }

        using var jpegStream = new MemoryStream();
        source.SaveAsJpeg(jpegStream, new JpegEncoder { ColorType = colorType, Quality = 90 });
        byte[] jpeg = jpegStream.ToArray();

        byte[] tiff = CreateJpegCompressedTiff(jpeg, GetJpegTables(jpeg), size, horizontalSubsampling, verticalSubsampling);

        using Image<Rgb24> expected = Image.Load<Rgb24>(jpeg);
        using Image<Rgb24> actual = Image.Load<Rgb24>(tiff);

        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    [Theory]
    [InlineData(0xD0)]
    [InlineData(0xD7)]
    [InlineData(0x01)]
    public void Decode_JpegTablesWithStandaloneMarker_SkipsMarker(byte marker)
    {
        const int size = 16;
        using var source = new Image<Rgb24>(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                source[x, y] = new Rgb24((byte)(x * 16), (byte)(y * 16), (byte)(255 - (x * 8) - (y * 8)));
            }
        }

        using var jpegStream = new MemoryStream();
        source.SaveAsJpeg(jpegStream, new JpegEncoder { ColorType = JpegColorType.YCbCrRatio444, Quality = 90 });
        byte[] jpeg = jpegStream.ToArray();

        // RSTn and TEM have no length field (ITU T.81 B.1.1.4), insert one between the DQT and DHT segments.
        byte[] jpegTables = GetJpegTables(jpeg);
        int dht = 2;
        while (jpegTables[dht + 1] != 0xC4)
        {
            dht += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpegTables.AsSpan(dht + 2));
        }

        jpegTables = jpegTables.Take(dht).Concat(new byte[] { 0xFF, marker }).Concat(jpegTables.Skip(dht)).ToArray();

        byte[] tiff = CreateJpegCompressedTiff(jpeg, jpegTables, size, 1, 1);

        using Image<Rgb24> expected = Image.Load<Rgb24>(jpeg);
        using Image<Rgb24> actual = Image.Load<Rgb24>(tiff);

        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    /// <summary>
    /// Builds a single strip, JPEG-compressed YCbCr TIFF from a JPEG file and a JPEGTables value.
    /// </summary>
    private static byte[] CreateJpegCompressedTiff(byte[] jpeg, byte[] jpegTables, int size, ushort horizontalSubsampling, ushort verticalSubsampling)
        => new TiffTestFileBuilder()
            .AddPage(jpeg)
            .AddLong(ExifTagValue.ImageWidth, (uint)size)
            .AddLong(ExifTagValue.ImageLength, (uint)size)
            .AddShort(ExifTagValue.BitsPerSample, 8, 8, 8)
            .AddShort(ExifTagValue.Compression, (ushort)TiffCompression.Jpeg)
            .AddShort(ExifTagValue.PhotometricInterpretation, (ushort)TiffPhotometricInterpretation.YCbCr)
            .AddShort(ExifTagValue.SamplesPerPixel, 3)
            .AddLong(ExifTagValue.RowsPerStrip, (uint)size)
            .AddShort(ExifTagValue.PlanarConfiguration, (ushort)TiffPlanarConfiguration.Chunky)
            .AddEntry(ExifTagValue.JPEGTables, ExifDataType.Undefined, (uint)jpegTables.Length, jpegTables)
            .AddShort(ExifTagValue.YCbCrSubsampling, horizontalSubsampling, verticalSubsampling)
            .Build();

    [Theory]
    [InlineData(TiffCompression.None, 2, 2)]
    [InlineData(TiffCompression.None, 2, 1)]
    [InlineData(TiffCompression.Lzw, 2, 2)]
    [InlineData(TiffCompression.Lzw, 2, 1)]
    [InlineData(TiffCompression.Deflate, 2, 2)]
    [InlineData(TiffCompression.Deflate, 2, 1)]
    public void Decode_SubsampledYCbCr_ReversesSubsampling(TiffCompression compression, int horizontalSubsampling, int verticalSubsampling)
    {
        const int width = 8;
        const int height = 4;

        // Each data unit holds the luma samples of a block, left to right and top to bottom, then Cb and Cr.
        // Neutral chroma makes every pixel gray with the value of its luma sample.
        var strip = new List<byte>();
        for (int blockTop = 0; blockTop < height; blockTop += verticalSubsampling)
        {
            for (int blockLeft = 0; blockLeft < width; blockLeft += horizontalSubsampling)
            {
                for (int y = blockTop; y < blockTop + verticalSubsampling; y++)
                {
                    for (int x = blockLeft; x < blockLeft + horizontalSubsampling; x++)
                    {
                        strip.Add(Luma(x, y));
                    }
                }

                strip.Add(128);
                strip.Add(128);
            }
        }

        byte[] stripData = compression switch
        {
            TiffCompression.Lzw => CompressLzw(strip.ToArray()),
            TiffCompression.Deflate => CompressDeflate(strip.ToArray()),
            _ => strip.ToArray(),
        };

        byte[] tiff = new TiffTestFileBuilder()
            .AddPage(stripData)
            .AddLong(ExifTagValue.ImageWidth, width)
            .AddLong(ExifTagValue.ImageLength, height)
            .AddShort(ExifTagValue.BitsPerSample, 8, 8, 8)
            .AddShort(ExifTagValue.Compression, (ushort)compression)
            .AddShort(ExifTagValue.PhotometricInterpretation, (ushort)TiffPhotometricInterpretation.YCbCr)
            .AddShort(ExifTagValue.SamplesPerPixel, 3)
            .AddLong(ExifTagValue.RowsPerStrip, height)
            .AddShort(ExifTagValue.PlanarConfiguration, (ushort)TiffPlanarConfiguration.Chunky)
            .AddShort(ExifTagValue.YCbCrSubsampling, (ushort)horizontalSubsampling, (ushort)verticalSubsampling)
            .Build();

        using Image<Rgb24> image = Image.Load<Rgb24>(tiff);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte luma = Luma(x, y);
                Assert.Equal(new Rgb24(luma, luma, luma), image[x, y]);
            }
        }

        static byte Luma(int x, int y) => (byte)((y * 64) + (x * 8));
    }

    private static ushort[] CreateGrayColorMap()
    {
        // Entry i is i for each of the red, green and blue tables.
        var colorMap = new ushort[256 * 3];
        for (int i = 0; i < colorMap.Length; i++)
        {
            colorMap[i] = (ushort)(i % 256);
        }

        return colorMap;
    }

    private static byte[] CreatePaletteTiff(ushort[] colorMap, TiffCompression compression)
    {
        byte[] strip = PaletteIndices.ToArray();
        if (compression == TiffCompression.Lzw)
        {
            // Horizontal differencing (Predictor 2) stores each index as the difference to its left neighbour.
            for (int x = strip.Length - 1; x > 0; x--)
            {
                strip[x] -= strip[x - 1];
            }

            strip = CompressLzw(strip);
        }

        TiffTestFileBuilder builder = new TiffTestFileBuilder()
            .AddPage(strip)
            .AddLong(ExifTagValue.ImageWidth, (uint)PaletteIndices.Length)
            .AddLong(ExifTagValue.ImageLength, 1)
            .AddShort(ExifTagValue.BitsPerSample, 8)
            .AddShort(ExifTagValue.Compression, (ushort)compression)
            .AddShort(ExifTagValue.PhotometricInterpretation, (ushort)TiffPhotometricInterpretation.PaletteColor)
            .AddShort(ExifTagValue.SamplesPerPixel, 1)
            .AddLong(ExifTagValue.RowsPerStrip, 1)
            .AddShort(ExifTagValue.ColorMap, colorMap);

        if (compression == TiffCompression.Lzw)
        {
            builder.AddShort(ExifTagValue.Predictor, (ushort)TiffPredictor.Horizontal);
        }

        return builder.Build();
    }

    /// <summary>
    /// Builds a JPEGTables value (SOI, then the DQT and DHT segments, then EOI) from a JPEG file.
    /// </summary>
    private static byte[] GetJpegTables(byte[] jpeg)
    {
        var tables = new List<byte> { 0xFF, 0xD8 };
        int offset = 2;
        while (jpeg[offset + 1] != 0xDA)
        {
            int segmentLength = 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(offset + 2));
            if (jpeg[offset + 1] is 0xDB or 0xC4)
            {
                tables.AddRange(jpeg.AsSpan(offset, segmentLength).ToArray());
            }

            offset += segmentLength;
        }

        tables.Add(0xFF);
        tables.Add(0xD9);
        return tables.ToArray();
    }

    /// <summary>
    /// Changes the tag number of an entry in the first IFD of a little-endian TIFF, which hides the entry from the decoder.
    /// </summary>
    private static byte[] RenameTag(byte[] tiff, ushort tag, ushort newTag)
    {
        byte[] result = tiff.ToArray();
        int ifdOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(4));
        int entryCount = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(ifdOffset));
        for (int i = 0; i < entryCount; i++)
        {
            Span<byte> entry = result.AsSpan(ifdOffset + 2 + (i * 12), 12);
            if (BinaryPrimitives.ReadUInt16LittleEndian(entry) == tag)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(entry, newTag);
                return result;
            }
        }

        throw new InvalidOperationException($"Tag {tag} not found.");
    }

    private static byte[] CompressLzw(byte[] data)
    {
        using var stream = new MemoryStream();
        using (var encoder = new TiffLzwEncoder(Configuration.Default.MemoryAllocator))
        {
            encoder.Encode(data, stream);
        }

        return stream.ToArray();
    }

    private static byte[] CompressDeflate(byte[] data)
    {
        using var stream = new MemoryStream();
        using (var zlib = new ZLibStream(stream, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return stream.ToArray();
    }
}
