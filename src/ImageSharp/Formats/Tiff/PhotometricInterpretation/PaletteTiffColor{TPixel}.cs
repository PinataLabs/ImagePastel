// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using SixLabors.ImageSharp.Formats.Tiff.Utils;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Tiff.PhotometricInterpretation;

/// <summary>
/// Implements the 'PaletteTiffColor' photometric interpretation (for all bit depths).
/// </summary>
internal class PaletteTiffColor<TPixel> : TiffBaseColorDecoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly ushort bitsPerSample0;

    private readonly TPixel[] palette;

    /// <param name="bitsPerSample">The number of bits per sample for each pixel.</param>
    /// <param name="colorMap">The RGB color lookup table to use for decoding the image.</param>
    public PaletteTiffColor(TiffBitsPerSample bitsPerSample, ushort[] colorMap)
    {
        this.bitsPerSample0 = bitsPerSample.Channel0;
        int colorCount = 1 << this.bitsPerSample0;
        this.palette = GeneratePalette(colorMap, colorCount);
    }

    /// <inheritdoc/>
    public override void Decode(ReadOnlySpan<byte> data, Buffer2D<TPixel> pixels, int left, int top, int width, int height)
    {
        var bitReader = new BitReader(data);

        for (int y = top; y < top + height; y++)
        {
            Span<TPixel> pixelRow = pixels.DangerousGetRowSpan(y).Slice(left, width);
            for (int x = 0; x < pixelRow.Length; x++)
            {
                int index = bitReader.ReadBits(this.bitsPerSample0);
                pixelRow[x] = this.palette[index];
            }

            bitReader.NextRow();
        }
    }

    private static TPixel[] GeneratePalette(ushort[] colorMap, int colorCount)
    {
        var palette = new TPixel[colorCount];

        const int rOffset = 0;
        int gOffset = colorCount;
        int bOffset = colorCount * 2;

        // ColorMap entries are 16-bit values, but many writers store 8-bit values instead.
        // For compatibility, a colormap where every entry fits in 8 bits is scaled as 8-bit.
        float maxValue = IsEightBitColorMap(colorMap) ? 255F : 65535F;

        for (int i = 0; i < palette.Length; i++)
        {
            float r = colorMap[rOffset + i] / maxValue;
            float g = colorMap[gOffset + i] / maxValue;
            float b = colorMap[bOffset + i] / maxValue;
            palette[i].FromScaledVector4(new Vector4(r, g, b, 1.0f));
        }

        return palette;
    }

    private static bool IsEightBitColorMap(ReadOnlySpan<ushort> colorMap)
    {
        foreach (ushort entry in colorMap)
        {
            if (entry > byte.MaxValue)
            {
                return false;
            }
        }

        return true;
    }
}
