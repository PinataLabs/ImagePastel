// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Numerics;

namespace SixLabors.ImageSharp.Formats.Tiff.PhotometricInterpretation;

/// <summary>
/// Reads the samples of a 'Separated' (CMYK) TIFF row into ink amounts.
/// Each result holds cyan, magenta, yellow and black in X, Y, Z and W, from 0 (no ink) to 1 (full ink),
/// so a later stage can turn them into RGB with the formula or with a CMYK profile.
/// </summary>
internal static class TiffCmykSamples
{
    private const float Max8Bit = 1F / byte.MaxValue;

    private const float Max16Bit = 1F / ushort.MaxValue;

    /// <summary>
    /// Reads a row of chunky samples, four per pixel in the order C, M, Y, K.
    /// </summary>
    /// <param name="data">The row data.</param>
    /// <param name="bitsPerSample">The bits per sample: 8 or 16.</param>
    /// <param name="isBigEndian">Whether 16-bit samples are big endian.</param>
    /// <param name="cmyk">The ink amounts, one per pixel.</param>
    public static void ReadChunky(ReadOnlySpan<byte> data, int bitsPerSample, bool isBigEndian, Span<Vector4> cmyk)
    {
        if (bitsPerSample == 8)
        {
            for (int x = 0; x < cmyk.Length; x++)
            {
                ReadOnlySpan<byte> pixel = data.Slice(x * 4, 4);
                cmyk[x] = new Vector4(pixel[0], pixel[1], pixel[2], pixel[3]) * Max8Bit;
            }

            return;
        }

        for (int x = 0; x < cmyk.Length; x++)
        {
            ReadOnlySpan<byte> pixel = data.Slice(x * 8, 8);
            cmyk[x] = new Vector4(
                Read16(pixel, isBigEndian),
                Read16(pixel.Slice(2), isBigEndian),
                Read16(pixel.Slice(4), isBigEndian),
                Read16(pixel.Slice(6), isBigEndian)) * Max16Bit;
        }
    }

    /// <summary>
    /// Reads a row of planar samples, one plane per ink.
    /// </summary>
    /// <param name="cyan">The cyan plane's row data.</param>
    /// <param name="magenta">The magenta plane's row data.</param>
    /// <param name="yellow">The yellow plane's row data.</param>
    /// <param name="black">The black plane's row data.</param>
    /// <param name="bitsPerSample">The bits per sample: 8 or 16.</param>
    /// <param name="isBigEndian">Whether 16-bit samples are big endian.</param>
    /// <param name="cmyk">The ink amounts, one per pixel.</param>
    public static void ReadPlanar(
        ReadOnlySpan<byte> cyan,
        ReadOnlySpan<byte> magenta,
        ReadOnlySpan<byte> yellow,
        ReadOnlySpan<byte> black,
        int bitsPerSample,
        bool isBigEndian,
        Span<Vector4> cmyk)
    {
        if (bitsPerSample == 8)
        {
            for (int x = 0; x < cmyk.Length; x++)
            {
                cmyk[x] = new Vector4(cyan[x], magenta[x], yellow[x], black[x]) * Max8Bit;
            }

            return;
        }

        for (int x = 0; x < cmyk.Length; x++)
        {
            int offset = x * 2;
            cmyk[x] = new Vector4(
                Read16(cyan.Slice(offset), isBigEndian),
                Read16(magenta.Slice(offset), isBigEndian),
                Read16(yellow.Slice(offset), isBigEndian),
                Read16(black.Slice(offset), isBigEndian)) * Max16Bit;
        }
    }

    private static ushort Read16(ReadOnlySpan<byte> data, bool isBigEndian)
        => isBigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data) : BinaryPrimitives.ReadUInt16LittleEndian(data);
}
