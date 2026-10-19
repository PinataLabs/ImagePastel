// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/compressed_dc.cc (AdaptiveDCSmoothing, DequantDC).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Modular;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>The DC of a VarDCT frame: its dequantisation and its adaptive smoothing.</summary>
/// <remarks>
/// libjxl's <c>MulAdd</c> is fused on AVX2; here each multiply and add is separate (ADR 0004).
/// </remarks>
internal static class JxlCompressedDc
{
    private const float W1 = 0.20345139757231578f;
    private const float W2 = 0.0334829185968739f;

    // Computed at run time, so each step rounds to float as in libjxl.
    private static readonly float W0 = ComputeW0(W1, W2);

    /// <summary>
    /// Smooths the DC where it is flat, which hides the steps between blocks: each sample moves toward
    /// a weighted mean of its 3x3 neighbours, the less the further its channels are from that mean in
    /// DC steps. Runs between the DC groups and the AC groups.
    /// </summary>
    /// <param name="dcFactors">The DC step of each channel.</param>
    /// <param name="dc">The X, Y and B DC planes, <paramref name="xsize"/> wide.</param>
    /// <param name="xsize">The width, in blocks.</param>
    /// <param name="ysize">The height, in blocks.</param>
    public static void AdaptiveDcSmoothing(float[] dcFactors, float[][] dc, int xsize, int ysize)
    {
        if (ysize <= 2 || xsize <= 2)
        {
            return;
        }

        float[][] smoothed = { new float[xsize * ysize], new float[xsize * ysize], new float[xsize * ysize] };

        // Fill in borders that the loop below will not. First and last are unused.
        for (int c = 0; c < 3; c++)
        {
            dc[c].AsSpan(0, xsize).CopyTo(smoothed[c]);
            dc[c].AsSpan((ysize - 1) * xsize, xsize).CopyTo(smoothed[c].AsSpan((ysize - 1) * xsize));
        }

        Span<float> mc = stackalloc float[3];
        Span<float> sm = stackalloc float[3];
        for (int y = 1; y < ysize - 1; y++)
        {
            int top = (y - 1) * xsize;
            int mid = y * xsize;
            int bottom = (y + 1) * xsize;
            for (int c = 0; c < 3; c++)
            {
                smoothed[c][mid] = dc[c][mid];
                smoothed[c][mid + xsize - 1] = dc[c][mid + xsize - 1];
            }

            for (int x = 1; x < xsize - 1; x++)
            {
                float gap = 0.5f;
                for (int c = 0; c < 3; c++)
                {
                    float[] p = dc[c];
                    float corner = (p[top + x - 1] + p[top + x + 1]) + (p[bottom + x - 1] + p[bottom + x + 1]);
                    float side = (p[mid + x - 1] + p[mid + x + 1]) + (p[top + x] + p[bottom + x]);
                    mc[c] = p[mid + x];
                    sm[c] = (corner * W2) + ((side * W1) + (mc[c] * W0));
                    gap = MathF.Max(gap, MathF.Abs((mc[c] - sm[c]) / dcFactors[c]));
                }

                float factor = (-4.0f * gap) + 3.0f;
                factor = factor < 0 ? 0 : factor;
                for (int c = 0; c < 3; c++)
                {
                    smoothed[c][mid + x] = ((sm[c] - mc[c]) * factor) + mc[c];
                }
            }
        }

        for (int c = 0; c < 3; c++)
        {
            smoothed[c].CopyTo(dc[c], 0);
        }
    }

    /// <summary>
    /// Dequantises the DC of one DC group into <paramref name="dc"/>, adding the chroma-from-luma
    /// prediction, and sets the quantised-DC bucket of each block for the block context map.
    /// </summary>
    /// <param name="r">The DC group, in blocks.</param>
    /// <param name="dc">The X, Y and B DC planes, <paramref name="stride"/> wide.</param>
    /// <param name="quantDc">The bucket of each block, <paramref name="stride"/> wide.</param>
    /// <param name="stride">The width of the planes.</param>
    /// <param name="input">The decoded DC: the Y, X and B channels.</param>
    /// <param name="dcFactors">The DC step of each channel.</param>
    /// <param name="mul">The extra-precision multiplier.</param>
    /// <param name="cflFactors">The chroma-from-luma DC factors: X in entry 0 and B in entry 2.</param>
    /// <param name="chromaSubsampling">The chroma subsampling.</param>
    /// <param name="bctx">The block context map, for the DC thresholds.</param>
    public static void DequantDc(JxlRect r, float[][] dc, byte[] quantDc, int stride, JxlModularImage input, float[] dcFactors, float mul, ReadOnlySpan<float> cflFactors, JxlYCbCrChromaSubsampling chromaSubsampling, JxlBlockCtxMap bctx)
    {
        int x0 = (int)r.X0;
        int y0 = (int)r.Y0;
        int xsize = (int)r.XSize;
        int ysize = (int)r.YSize;
        if (Is444(chromaSubsampling))
        {
            float facX = dcFactors[0] * mul;
            float facY = dcFactors[1] * mul;
            float facB = dcFactors[2] * mul;
            float cflFacX = cflFactors[0];
            float cflFacB = cflFactors[2];
            for (int y = 0; y < ysize; y++)
            {
                Span<float> decRowX = dc[0].AsSpan(((y0 + y) * stride) + x0, xsize);
                Span<float> decRowY = dc[1].AsSpan(((y0 + y) * stride) + x0, xsize);
                Span<float> decRowB = dc[2].AsSpan(((y0 + y) * stride) + x0, xsize);
                ReadOnlySpan<int> quantRowX = input.Channel[1].Row(y);
                ReadOnlySpan<int> quantRowY = input.Channel[0].Row(y);
                ReadOnlySpan<int> quantRowB = input.Channel[2].Row(y);
                for (int x = 0; x < xsize; x++)
                {
                    float inX = quantRowX[x] * facX;
                    float inY = quantRowY[x] * facY;
                    float inB = quantRowB[x] * facB;
                    decRowY[x] = inY;
                    decRowX[x] = (inY * cflFacX) + inX;
                    decRowB[x] = (inY * cflFacB) + inB;
                }
            }
        }
        else
        {
            foreach (int c in stackalloc int[] { 1, 0, 2 })
            {
                int hs = chromaSubsampling.HShift(c);
                int vs = chromaSubsampling.VShift(c);
                int rx0 = x0 >> hs;
                int ry0 = y0 >> vs;
                int rxsize = xsize >> hs;
                int rysize = ysize >> vs;
                float fac = dcFactors[c] * mul;
                JxlModularChannel ch = input.Channel[c < 2 ? c ^ 1 : c];
                for (int y = 0; y < rysize; y++)
                {
                    ReadOnlySpan<int> quantRow = ch.Row(y);
                    Span<float> row = dc[c].AsSpan(((ry0 + y) * stride) + rx0, rxsize);
                    for (int x = 0; x < rxsize; x++)
                    {
                        row[x] = quantRow[x] * fac;
                    }
                }
            }
        }

        if (bctx.NumDcCtxs <= 1)
        {
            for (int y = 0; y < ysize; y++)
            {
                quantDc.AsSpan(((y0 + y) * stride) + x0, xsize).Clear();
            }
        }
        else
        {
            int[][] thresholds = bctx.DcThresholds;
            for (int y = 0; y < ysize; y++)
            {
                Span<byte> qdcRowVal = quantDc.AsSpan(((y0 + y) * stride) + x0, xsize);
                ReadOnlySpan<int> quantRowX = input.Channel[1].Row(y >> chromaSubsampling.VShift(0));
                ReadOnlySpan<int> quantRowY = input.Channel[0].Row(y >> chromaSubsampling.VShift(1));
                ReadOnlySpan<int> quantRowB = input.Channel[2].Row(y >> chromaSubsampling.VShift(2));
                for (int x = 0; x < xsize; x++)
                {
                    int bucketX = 0;
                    int bucketY = 0;
                    int bucketB = 0;
                    foreach (int t in thresholds[0])
                    {
                        if (quantRowX[x >> chromaSubsampling.HShift(0)] > t)
                        {
                            bucketX++;
                        }
                    }

                    foreach (int t in thresholds[1])
                    {
                        if (quantRowY[x >> chromaSubsampling.HShift(1)] > t)
                        {
                            bucketY++;
                        }
                    }

                    foreach (int t in thresholds[2])
                    {
                        if (quantRowB[x >> chromaSubsampling.HShift(2)] > t)
                        {
                            bucketB++;
                        }
                    }

                    int bucket = bucketX;
                    bucket *= thresholds[2].Length + 1;
                    bucket += bucketB;
                    bucket *= thresholds[1].Length + 1;
                    bucket += bucketY;
                    qdcRowVal[x] = (byte)bucket;
                }
            }
        }
    }

    private static float ComputeW0(float w1, float w2)
    {
        float sum = w1 + w2;
        float times4 = 4.0f * sum;
        return 1.0f - times4;
    }

    /// <summary>Returns whether no channel is subsampled.</summary>
    /// <param name="cs">The chroma subsampling.</param>
    /// <returns><see langword="true"/> for 4:4:4.</returns>
    public static bool Is444(JxlYCbCrChromaSubsampling cs)
        => cs.HShift(0) == 0 && cs.VShift(0) == 0 && cs.HShift(2) == 0 && cs.VShift(2) == 0 && cs.HShift(1) == 0 && cs.VShift(1) == 0;
}
