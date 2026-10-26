// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/compressed_dc.cc (AdaptiveDCSmoothing, DequantDC).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.Modular;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.VarDct;

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
    /// DC steps. Runs between the DC groups and the AC groups. The smoothed planes replace the planes of
    /// <paramref name="dc"/>, which are disposed, as libjxl swaps the images.
    /// </summary>
    /// <param name="allocator">The allocator of the smoothed planes.</param>
    /// <param name="dcFactors">The DC step of each channel.</param>
    /// <param name="dc">The X, Y and B DC planes, <paramref name="xsize"/> by <paramref name="ysize"/>.</param>
    /// <param name="xsize">The width, in blocks.</param>
    /// <param name="ysize">The height, in blocks.</param>
    public static void AdaptiveDcSmoothing(MemoryAllocator allocator, float[] dcFactors, Buffer2D<float>[] dc, int xsize, int ysize)
    {
        if (ysize <= 2 || xsize <= 2)
        {
            return;
        }

        Buffer2D<float>[] smoothed = new Buffer2D<float>[3];
        try
        {
            for (int c = 0; c < 3; c++)
            {
                smoothed[c] = allocator.Allocate2D<float>(xsize, ysize);

                // Fill in borders that the loop below will not. First and last are unused.
                dc[c].DangerousGetRowSpan(0).Slice(0, xsize).CopyTo(smoothed[c].DangerousGetRowSpan(0));
                dc[c].DangerousGetRowSpan(ysize - 1).Slice(0, xsize).CopyTo(smoothed[c].DangerousGetRowSpan(ysize - 1));
            }

            Smooth(dcFactors, dc, smoothed, xsize, ysize);
        }
        catch
        {
            JxlPlanes.DisposeAll(smoothed);
            throw;
        }

        for (int c = 0; c < 3; c++)
        {
            dc[c].Dispose();
            dc[c] = smoothed[c];
        }
    }

    private static void Smooth(float[] dcFactors, Buffer2D<float>[] dc, Buffer2D<float>[] smoothed, int xsize, int ysize)
    {
        Span<float> mc = stackalloc float[3];
        Span<float> sm = stackalloc float[3];
        for (int y = 1; y < ysize - 1; y++)
        {
            ReadOnlySpan<float> top0 = dc[0].DangerousGetRowSpan(y - 1);
            ReadOnlySpan<float> top1 = dc[1].DangerousGetRowSpan(y - 1);
            ReadOnlySpan<float> top2 = dc[2].DangerousGetRowSpan(y - 1);
            ReadOnlySpan<float> mid0 = dc[0].DangerousGetRowSpan(y);
            ReadOnlySpan<float> mid1 = dc[1].DangerousGetRowSpan(y);
            ReadOnlySpan<float> mid2 = dc[2].DangerousGetRowSpan(y);
            ReadOnlySpan<float> bottom0 = dc[0].DangerousGetRowSpan(y + 1);
            ReadOnlySpan<float> bottom1 = dc[1].DangerousGetRowSpan(y + 1);
            ReadOnlySpan<float> bottom2 = dc[2].DangerousGetRowSpan(y + 1);
            Span<float> out0 = smoothed[0].DangerousGetRowSpan(y);
            Span<float> out1 = smoothed[1].DangerousGetRowSpan(y);
            Span<float> out2 = smoothed[2].DangerousGetRowSpan(y);
            out0[0] = mid0[0];
            out1[0] = mid1[0];
            out2[0] = mid2[0];
            out0[xsize - 1] = mid0[xsize - 1];
            out1[xsize - 1] = mid1[xsize - 1];
            out2[xsize - 1] = mid2[xsize - 1];

            for (int x = 1; x < xsize - 1; x++)
            {
                float gap = 0.5f;
                gap = SmoothSample(top0, mid0, bottom0, x, dcFactors[0], gap, out mc[0], out sm[0]);
                gap = SmoothSample(top1, mid1, bottom1, x, dcFactors[1], gap, out mc[1], out sm[1]);
                gap = SmoothSample(top2, mid2, bottom2, x, dcFactors[2], gap, out mc[2], out sm[2]);

                float factor = (-4.0f * gap) + 3.0f;
                factor = factor < 0 ? 0 : factor;
                out0[x] = ((sm[0] - mc[0]) * factor) + mc[0];
                out1[x] = ((sm[1] - mc[1]) * factor) + mc[1];
                out2[x] = ((sm[2] - mc[2]) * factor) + mc[2];
            }
        }
    }

    // One channel of one sample: its value, the weighted mean of its 3x3 neighbourhood, and the gap so far.
    private static float SmoothSample(ReadOnlySpan<float> top, ReadOnlySpan<float> mid, ReadOnlySpan<float> bottom, int x, float dcFactor, float gap, out float mc, out float sm)
    {
        float corner = (top[x - 1] + top[x + 1]) + (bottom[x - 1] + bottom[x + 1]);
        float side = (mid[x - 1] + mid[x + 1]) + (top[x] + bottom[x]);
        mc = mid[x];
        sm = (corner * W2) + ((side * W1) + (mc * W0));
        return MathF.Max(gap, MathF.Abs((mc - sm) / dcFactor));
    }

    /// <summary>
    /// Dequantises the DC of one DC group into <paramref name="dc"/>, adding the chroma-from-luma
    /// prediction, and sets the quantised-DC bucket of each block for the block context map.
    /// </summary>
    /// <param name="r">The DC group, in blocks.</param>
    /// <param name="dc">The X, Y and B DC planes of the frame.</param>
    /// <param name="quantDc">The bucket of each block of the frame.</param>
    /// <param name="input">The decoded DC: the Y, X and B channels.</param>
    /// <param name="dcFactors">The DC step of each channel.</param>
    /// <param name="mul">The extra-precision multiplier.</param>
    /// <param name="cflFactors">The chroma-from-luma DC factors: X in entry 0 and B in entry 2.</param>
    /// <param name="chromaSubsampling">The chroma subsampling.</param>
    /// <param name="bctx">The block context map, for the DC thresholds.</param>
    public static void DequantDc(JxlRect r, Buffer2D<float>[] dc, Buffer2D<byte> quantDc, JxlModularImage input, float[] dcFactors, float mul, ReadOnlySpan<float> cflFactors, JxlYCbCrChromaSubsampling chromaSubsampling, JxlBlockCtxMap bctx)
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
                Span<float> decRowX = dc[0].DangerousGetRowSpan(y0 + y).Slice(x0, xsize);
                Span<float> decRowY = dc[1].DangerousGetRowSpan(y0 + y).Slice(x0, xsize);
                Span<float> decRowB = dc[2].DangerousGetRowSpan(y0 + y).Slice(x0, xsize);
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
                    Span<float> row = dc[c].DangerousGetRowSpan(ry0 + y).Slice(rx0, rxsize);
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
                quantDc.DangerousGetRowSpan(y0 + y).Slice(x0, xsize).Clear();
            }
        }
        else
        {
            int[][] thresholds = bctx.DcThresholds;
            for (int y = 0; y < ysize; y++)
            {
                Span<byte> qdcRowVal = quantDc.DangerousGetRowSpan(y0 + y).Slice(x0, xsize);
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
