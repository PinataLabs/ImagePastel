// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using PinataLabs.ImagePastel.Compression.Zlib;
using PinataLabs.ImagePastel.Formats.Png;

namespace PinataLabs.ImagePastel.Tests.Formats.Png;

/// <summary>
/// Builds PNG files chunk by chunk, with valid CRCs, so tests can control the exact chunk layout.
/// </summary>
internal static class PngTestFileBuilder
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // The Adam7 pass layout from the PNG specification, section 8.2.
    private static readonly int[] PassFirstColumn = { 0, 4, 0, 2, 0, 1, 0 };
    private static readonly int[] PassFirstRow = { 0, 0, 4, 0, 2, 0, 1 };
    private static readonly int[] PassColumnIncrement = { 8, 8, 4, 4, 2, 2, 1 };
    private static readonly int[] PassRowIncrement = { 8, 8, 8, 4, 4, 2, 2 };

    /// <summary>
    /// Concatenates the signature and the given chunks into a PNG file.
    /// </summary>
    public static byte[] Build(params byte[][] chunks)
    {
        using var stream = new MemoryStream();
        stream.Write(Signature);
        foreach (byte[] chunk in chunks)
        {
            stream.Write(chunk);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Encodes a chunk: big-endian length, type, data and the CRC over type and data.
    /// </summary>
    public static byte[] Chunk(string type, ReadOnlySpan<byte> data)
    {
        byte[] chunk = new byte[data.Length + 12];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        Encoding.ASCII.GetBytes(type, chunk.AsSpan(4, 4));
        data.CopyTo(chunk.AsSpan(8));

        uint crc = Crc32.Calculate(chunk.AsSpan(4, data.Length + 4));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(data.Length + 8), crc);
        return chunk;
    }

    /// <summary>
    /// Encodes an IHDR chunk with compression method 0 and filter method 0.
    /// </summary>
    public static byte[] Header(int width, int height, byte bitDepth, PngColorType colorType, PngInterlaceMode interlaceMode = PngInterlaceMode.None)
    {
        byte[] data = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(data, width);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), height);
        data[8] = bitDepth;
        data[9] = (byte)colorType;
        data[12] = (byte)interlaceMode;
        return Chunk("IHDR", data);
    }

    /// <summary>
    /// Encodes an IEND chunk.
    /// </summary>
    public static byte[] End() => Chunk("IEND", default);

    /// <summary>
    /// Encodes a gAMA chunk: the gamma times 100000.
    /// </summary>
    public static byte[] Gamma(uint value)
    {
        byte[] data = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(data, value);
        return Chunk("gAMA", data);
    }

    /// <summary>
    /// Encodes a cHRM chunk: white x, y, then red, green and blue x, y, each times 100000.
    /// </summary>
    public static byte[] Chromaticities(uint whiteX, uint whiteY, uint redX, uint redY, uint greenX, uint greenY, uint blueX, uint blueY)
    {
        uint[] values = { whiteX, whiteY, redX, redY, greenX, greenY, blueX, blueY };
        byte[] data = new byte[32];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(i * 4), values[i]);
        }

        return Chunk("cHRM", data);
    }

    /// <summary>
    /// Encodes an sRGB chunk with the perceptual rendering intent.
    /// </summary>
    public static byte[] StandardRgb() => Chunk("sRGB", new byte[] { 0 });

    /// <summary>
    /// Encodes a cICP chunk with the given ITU-T H.273 code points.
    /// </summary>
    public static byte[] Cicp(byte primaries, byte transfer, byte matrix = 0, byte fullRange = 1)
        => Chunk("cICP", new[] { primaries, transfer, matrix, fullRange });

    /// <summary>
    /// Encodes an iCCP chunk holding the given profile bytes.
    /// </summary>
    public static byte[] EmbeddedProfile(byte[] profile)
    {
        byte[] compressed = Zlib(profile);
        byte[] data = new byte[compressed.Length + 3];
        data[0] = (byte)'x'; // Profile name "x", null separator, compression method 0.
        compressed.CopyTo(data, 3);
        return Chunk("iCCP", data);
    }

    /// <summary>
    /// Inserts chunks into a PNG file straight after its IHDR chunk.
    /// </summary>
    public static byte[] InsertAfterHeader(byte[] png, params byte[][] chunks)
    {
        int headerEnd = Signature.Length + 12 + BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(Signature.Length));
        using var stream = new MemoryStream();
        stream.Write(png, 0, headerEnd);
        foreach (byte[] chunk in chunks)
        {
            stream.Write(chunk);
        }

        stream.Write(png, headerEnd, png.Length - headerEnd);
        return stream.ToArray();
    }

    /// <summary>
    /// Compresses filtered scanline data into a single zlib stream.
    /// </summary>
    public static byte[] Zlib(byte[] scanlines)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
        {
            zlib.Write(scanlines);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Builds the unfiltered scanlines of an image with one sample per pixel, as used by palette and grayscale images.
    /// </summary>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <param name="interlaced">Whether to lay the scanlines out in the seven Adam7 passes.</param>
    /// <param name="pixel">Returns the sample for the pixel at x, y.</param>
    /// <param name="bitDepth">The bits per sample: 1, 2, 4 or 8. Samples under 8 bits are packed from the high bits down.</param>
    public static byte[] Scanlines(int width, int height, bool interlaced, Func<int, int, byte> pixel, int bitDepth = 8)
    {
        var data = new List<byte>();
        if (!interlaced)
        {
            AddPass(data, width, height, 0, 0, 1, 1, pixel, bitDepth);
            return data.ToArray();
        }

        for (int pass = 0; pass < 7; pass++)
        {
            AddPass(data, width, height, PassFirstColumn[pass], PassFirstRow[pass], PassColumnIncrement[pass], PassRowIncrement[pass], pixel, bitDepth);
        }

        return data.ToArray();
    }

    /// <summary>
    /// Reads the chunks of a PNG file, in order, without checking their CRCs.
    /// </summary>
    public static List<(string Type, byte[] Data)> ReadChunks(byte[] png)
    {
        var chunks = new List<(string Type, byte[] Data)>();
        int offset = Signature.Length;
        while (offset + 12 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            chunks.Add((type, png.AsSpan(offset + 8, length).ToArray()));
            offset += length + 12;
        }

        return chunks;
    }

    private static void AddPass(List<byte> data, int width, int height, int firstColumn, int firstRow, int columnIncrement, int rowIncrement, Func<int, int, byte> pixel, int bitDepth)
    {
        if (firstColumn >= width || firstRow >= height)
        {
            // Passes with no pixels contribute no scanlines.
            return;
        }

        for (int y = firstRow; y < height; y += rowIncrement)
        {
            data.Add(0); // Filter type None.
            int packed = 0;
            int packedBits = 0;
            for (int x = firstColumn; x < width; x += columnIncrement)
            {
                packed = (packed << bitDepth) | (pixel(x, y) & ((1 << bitDepth) - 1));
                packedBits += bitDepth;
                if (packedBits == 8)
                {
                    data.Add((byte)packed);
                    packed = 0;
                    packedBits = 0;
                }
            }

            if (packedBits > 0)
            {
                // Pad the last byte of the row with zero bits.
                data.Add((byte)(packed << (8 - packedBits)));
            }
        }
    }
}
