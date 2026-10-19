// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dct-inl.h (CoeffBundle, IDCT1DImpl, IDCT1D, ComputeScaledIDCT), lib/jxl/dct_scales.h
// (kSqrt2, WcMultipliers<4> and <8>), lib/jxl/transpose-inl.h (the generic transpose) and the DCT
// cases of TransformToPixels and LowestFrequenciesFromDC in lib/jxl/dec_transforms-inl.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The inverse DCT of an 8x8 block: libjxl's "Lowest Complexity Self Recursive Radix-2 DCT II/III"
/// (Perera and Liu), unscaled, after the transposed coefficient layout of JPEG XL.
/// </summary>
/// <remarks>
/// libjxl runs the 1D transform on a vector of columns at once with Highway. Each lane does the same
/// arithmetic, so this scalar port runs it one column at a time. libjxl's <c>MulAdd</c> and
/// <c>NegMulAdd</c> are fused on AVX2; here the multiply and the add are separate (ADR 0004).
/// </remarks>
internal static class JxlDct
{
    private const float Sqrt2 = 1.41421356237f;

    // 1 / (2 * cos((i + 0.5) * pi / N)) for i < N / 2.
    private static ReadOnlySpan<float> WcMultipliers4 => new[] { 0.541196100146197f, 1.3065629648763764f };

    private static ReadOnlySpan<float> WcMultipliers8 => new[] { 0.5097955791041592f, 0.6013448869350453f, 0.8999762231364156f, 2.5629154477415055f };

    /// <summary>
    /// Transforms the 64 coefficients of a DCT8 block into 8x8 pixels (libjxl's
    /// <c>ComputeScaledIDCT&lt;8, 8&gt;</c>). The coefficients are overwritten.
    /// </summary>
    /// <param name="coefficients">The coefficients, 64 entries; used as scratch space.</param>
    /// <param name="pixels">The first pixel of the block.</param>
    /// <param name="pixelsStride">The distance between pixel rows.</param>
    public static void TransformToPixelsDct8(Span<float> coefficients, Span<float> pixels, int pixelsStride)
    {
        Span<float> block = stackalloc float[64];
        Span<float> tmp = stackalloc float[16];

        // ROWS == COLS: IDCT1D<8, 8> from the coefficients into block, transpose block into the
        // coefficients, then IDCT1D<8, 8> into the pixels.
        Idct1D(coefficients, 8, block, 8, tmp);
        Transpose8X8(block, coefficients);
        Idct1D(coefficients, 8, pixels, pixelsStride, tmp);
    }

    /// <summary>
    /// Sets the lowest frequencies of a DCT8 block from its DC (libjxl's <c>LowestFrequenciesFromDC</c>):
    /// the one coefficient is the DC itself.
    /// </summary>
    /// <param name="dc">The DC of the block.</param>
    /// <param name="llf">The coefficients of the block.</param>
    public static void LowestFrequenciesFromDcDct8(float dc, Span<float> llf) => llf[0] = dc;

    // IDCT1D<8, 8>: the 1D IDCT down each of the 8 columns of from, into to.
    private static void Idct1D(ReadOnlySpan<float> from, int fromStride, Span<float> to, int toStride, Span<float> tmp)
    {
        Span<float> column = stackalloc float[8];
        Span<float> output = stackalloc float[8];
        for (int i = 0; i < 8; i++)
        {
            for (int k = 0; k < 8; k++)
            {
                column[k] = from[(k * fromStride) + i];
            }

            Idct1DImpl8(column, output, tmp);
            for (int k = 0; k < 8; k++)
            {
                to[(k * toStride) + i] = output[k];
            }
        }
    }

    // IDCT1DImpl<8>.
    private static void Idct1DImpl8(ReadOnlySpan<float> from, Span<float> to, Span<float> scratch)
    {
        Span<float> tmp = scratch.Slice(0, 8);
        ForwardEvenOdd(from, tmp, 8);
        Idct1DImpl4(tmp.Slice(0, 4), tmp.Slice(0, 4), scratch.Slice(8));
        BTranspose(tmp.Slice(4, 4));
        Idct1DImpl4(tmp.Slice(4, 4), tmp.Slice(4, 4), scratch.Slice(8));
        MultiplyAndAdd(tmp, to, WcMultipliers8);
    }

    // IDCT1DImpl<4>. from and to may be the same: from is read before to is written.
    private static void Idct1DImpl4(ReadOnlySpan<float> from, Span<float> to, Span<float> scratch)
    {
        Span<float> tmp = scratch.Slice(0, 4);
        ForwardEvenOdd(from, tmp, 4);
        Idct1DImpl2(tmp.Slice(0, 2), tmp.Slice(0, 2));
        BTranspose(tmp.Slice(2, 2));
        Idct1DImpl2(tmp.Slice(2, 2), tmp.Slice(2, 2));
        MultiplyAndAdd(tmp, to, WcMultipliers4);
    }

    // IDCT1DImpl<2>.
    private static void Idct1DImpl2(ReadOnlySpan<float> from, Span<float> to)
    {
        float in1 = from[0];
        float in2 = from[1];
        to[0] = in1 + in2;
        to[1] = in1 - in2;
    }

    // CoeffBundle<N>::ForwardEvenOdd: the even entries, then the odd ones.
    private static void ForwardEvenOdd(ReadOnlySpan<float> from, Span<float> to, int n)
    {
        for (int i = 0; i < n / 2; i++)
        {
            to[i] = from[2 * i];
        }

        for (int i = n / 2; i < n; i++)
        {
            to[i] = from[(2 * (i - (n / 2))) + 1];
        }
    }

    // CoeffBundle<N>::BTranspose.
    private static void BTranspose(Span<float> coeff)
    {
        for (int i = coeff.Length - 1; i > 0; i--)
        {
            coeff[i] += coeff[i - 1];
        }

        coeff[0] *= Sqrt2;
    }

    // CoeffBundle<N>::MultiplyAndAdd.
    private static void MultiplyAndAdd(ReadOnlySpan<float> coeff, Span<float> output, ReadOnlySpan<float> multipliers)
    {
        int n = coeff.Length;
        for (int i = 0; i < n / 2; i++)
        {
            float mul = multipliers[i];
            float in1 = coeff[i];
            float in2 = coeff[(n / 2) + i];
            float product = mul * in2;
            output[i] = product + in1;
            output[n - i - 1] = in1 - product;
        }
    }

    // Transpose<8, 8>: to(m, n) = from(n, m).
    private static void Transpose8X8(ReadOnlySpan<float> from, Span<float> to)
    {
        for (int n = 0; n < 8; n++)
        {
            for (int m = 0; m < 8; m++)
            {
                to[(m * 8) + n] = from[(n * 8) + m];
            }
        }
    }
}
