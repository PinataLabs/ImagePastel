// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.CompilerServices;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats.Jpeg.Components.Encoder;

/// <summary>
/// Converts Rgb24 pixels to YCbCr without SIMD. The results are identical to <see cref="RgbToYCbCrConverterVectorized"/>,
/// so the encoder writes the same bytes with and without AVX2.
/// </summary>
internal static class RgbToYCbCrConverterScalar
{
    /// <summary>
    /// Converts Rgb24 pixels into YCbCr color space with 4:4:4 sampling of luminance and chroma.
    /// </summary>
    /// <param name="rgbSpan">Span of 64 Rgb24 pixels</param>
    /// <param name="yBlock">Resulting Y values block</param>
    /// <param name="cbBlock">Resulting Cb values block</param>
    /// <param name="crBlock">Resulting Cr values block</param>
    public static void Convert444(ReadOnlySpan<Rgb24> rgbSpan, ref Block8x8F yBlock, ref Block8x8F cbBlock, ref Block8x8F crBlock)
    {
        for (int i = 0; i < Block8x8F.Size; i++)
        {
            Rgb24 c = rgbSpan[i];

            yBlock[i] = ToY(c.R, c.G, c.B);
            cbBlock[i] = ToCb(c.R, c.G, c.B);
            crBlock[i] = ToCr(c.R, c.G, c.B);
        }
    }

    /// <summary>
    /// Converts a 16x8 strip of Rgb24 pixels into YCbCr color space with 4:2:0 subsampling of luminance and chroma.
    /// </summary>
    /// <remarks>Calculates 2 out of 4 luminance blocks and half of chroma blocks. This method must be called twice per 4x 8x8 DCT blocks with different row param.</remarks>
    /// <param name="rgbSpan">Span of 128 Rgb24 pixels, 16 per row</param>
    /// <param name="yBlockLeft">First or "left" resulting Y block</param>
    /// <param name="yBlockRight">Second or "right" resulting Y block</param>
    /// <param name="cbBlock">Resulting Cb values block</param>
    /// <param name="crBlock">Resulting Cr values block</param>
    /// <param name="row">Row index of the 16x16 block, 0 or 1</param>
    public static void Convert420(ReadOnlySpan<Rgb24> rgbSpan, ref Block8x8F yBlockLeft, ref Block8x8F yBlockRight, ref Block8x8F cbBlock, ref Block8x8F crBlock, int row)
    {
        DebugGuard.MustBeBetweenOrEqualTo(row, 0, 1, nameof(row));

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                Rgb24 left = rgbSpan[(y * 16) + x];
                Rgb24 right = rgbSpan[(y * 16) + x + 8];
                yBlockLeft[(y * 8) + x] = ToY(left.R, left.G, left.B);
                yBlockRight[(y * 8) + x] = ToY(right.R, right.G, right.B);
            }
        }

        // Chroma is converted from the 2x2 average of RGB, which is exact in float.
        int chromaOffset = row * (Block8x8F.Size / 2);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int top = (y * 32) + (x * 2);
                Rgb24 p0 = rgbSpan[top];
                Rgb24 p1 = rgbSpan[top + 1];
                Rgb24 p2 = rgbSpan[top + 16];
                Rgb24 p3 = rgbSpan[top + 17];

                float r = (p0.R + p1.R + p2.R + p3.R) * 0.25F;
                float g = (p0.G + p1.G + p2.G + p3.G) * 0.25F;
                float b = (p0.B + p1.B + p2.B + p3.B) * 0.25F;

                cbBlock[chromaOffset + (y * 8) + x] = ToCb(r, g, b);
                crBlock[chromaOffset + (y * 8) + x] = ToCr(r, g, b);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ToY(float r, float g, float b)
        => (0.299F * r) + (0.587F * g) + (0.114F * b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ToCb(float r, float g, float b)
        => 128F + ((-0.168736F * r) - (0.331264F * g) + (0.5F * b));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ToCr(float r, float g, float b)
        => 128F + ((0.5F * r) - (0.418688F * g) - (0.081312F * b));
}
