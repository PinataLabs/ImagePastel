// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dct-inl.h (CoeffBundle, DCT1DImpl, IDCT1DImpl, DCT1D, IDCT1D, ComputeScaledDCT,
// ComputeScaledIDCT), lib/jxl/dct_scales.h (kSqrt2, DCTTotalResampleScale), lib/jxl/transpose-inl.h
// (the generic transpose) and lib/jxl/dec_transforms-inl.h (ReinterpretingDCT, IDCT2TopBlock, and the
// cases of TransformToPixels and LowestFrequenciesFromDC; the AFV transform is in JxlDct.Afv.cs), and
// lib/jxl/enc_transforms-inl.h (ReinterpretingIDCT, DCT2TopBlock, and the cases of TransformFromPixels
// and DCFromLowestFrequencies; the AFV transform is in JxlDct.Afv.cs).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace PinataLabs.ImagePastel.Formats.Jxl.VarDct;

/// <summary>
/// The inverse transforms of the AC strategies: for the DCTs, libjxl's "Lowest Complexity Self Recursive
/// Radix-2 DCT II/III" (Perera and Liu), unscaled, after the transposed coefficient layout of JPEG XL, for
/// every size from 2x2 to 256x256; Identity; and AFV0 to AFV3. Also the lowest frequencies a block takes
/// from the DC image.
/// </summary>
/// <remarks>
/// libjxl runs the 1D transform on a vector of columns at once with Highway. Each lane does the same
/// arithmetic, so this scalar port runs it one column at a time. libjxl's <c>MulAdd</c> and
/// <c>NegMulAdd</c> are fused on AVX2; here the multiply and the add are separate (ADR 0004).
/// </remarks>
internal static partial class JxlDct
{
    /// <summary>
    /// The number of floats of scratch space the transforms need: a 256x256 block, and the columns of
    /// the 1D transforms.
    /// </summary>
    public const int ScratchSize = (MaxDim * MaxDim) + (4 * MaxDim);

    /// <summary>The most 8x8 blocks a strategy covers along either side: a 256x256 block covers 32.</summary>
    public const int MaxCoveredBlocks = MaxDim / BlockDim;

    private const int MaxDim = 256;
    private const int BlockDim = 8;
    private const int DctBlockSize = BlockDim * BlockDim;
    private const float Sqrt2 = 1.41421356237f;

    /// <summary>
    /// Transforms the coefficients of a DCT block into its pixels (libjxl's <c>TransformToPixels</c>).
    /// The coefficients are overwritten.
    /// </summary>
    /// <param name="acs">The strategy.</param>
    /// <param name="coefficients">The coefficients, 64 per 8x8 block the strategy covers; used as scratch space.</param>
    /// <param name="pixels">The first pixel of the block.</param>
    /// <param name="pixelsStride">The distance between pixel rows.</param>
    /// <param name="scratch">Scratch space, <see cref="ScratchSize"/> floats.</param>
    public static void TransformToPixels(JxlAcStrategy acs, Span<float> coefficients, Span<float> pixels, int pixelsStride, Span<float> scratch)
    {
        JxlAcStrategyType strategy = acs.Strategy;
        switch (strategy)
        {
            case JxlAcStrategyType.Dct8X4:
            {
                Span<float> block = stackalloc float[4 * 8];
                float block0 = coefficients[0];
                float block1 = coefficients[8];
                float dc0 = block0 + block1;
                float dc1 = block0 - block1;
                for (int x = 0; x < 2; x++)
                {
                    block[0] = x == 0 ? dc0 : dc1;
                    GatherAc(coefficients, x * 8, 16, 1, 4, 8, block);

                    ComputeScaledIdct(8, 4, block, pixels.Slice(x * 4), pixelsStride, scratch);
                }

                break;
            }

            case JxlAcStrategyType.Dct4X8:
            {
                Span<float> block = stackalloc float[4 * 8];
                float block0 = coefficients[0];
                float block1 = coefficients[8];
                float dc0 = block0 + block1;
                float dc1 = block0 - block1;
                for (int y = 0; y < 2; y++)
                {
                    block[0] = y == 0 ? dc0 : dc1;
                    GatherAc(coefficients, y * 8, 16, 1, 4, 8, block);

                    ComputeScaledIdct(4, 8, block, pixels.Slice(y * 4 * pixelsStride), pixelsStride, scratch);
                }

                break;
            }

            case JxlAcStrategyType.Dct4X4:
            {
                Span<float> dcs = stackalloc float[4];
                SubBlockDcs(coefficients, dcs);
                Span<float> block = stackalloc float[4 * 4];
                for (int y = 0; y < 2; y++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        block[0] = dcs[(y * 2) + x];
                        GatherAc(coefficients, (y * 8) + x, 16, 2, 4, 4, block);

                        ComputeScaledIdct(4, 4, block, pixels.Slice((y * 4 * pixelsStride) + (x * 4)), pixelsStride, scratch);
                    }
                }

                break;
            }

            case JxlAcStrategyType.Dct2X2:
            {
                Span<float> coeffs = stackalloc float[DctBlockSize];
                coefficients.Slice(0, DctBlockSize).CopyTo(coeffs);
                Idct2TopBlock(2, coeffs);
                Idct2TopBlock(4, coeffs);
                Idct2TopBlock(8, coeffs);
                for (int y = 0; y < BlockDim; y++)
                {
                    coeffs.Slice(y * BlockDim, BlockDim).CopyTo(pixels.Slice(y * pixelsStride));
                }

                break;
            }

            case JxlAcStrategyType.Identity:
            {
                // Four 4x4 squares of residuals about the pixel at (1, 1) of each, whose DCs are a 2x2
                // Hadamard of the top-left coefficients. Slot (1, 1)'s residual belongs to pixel (0, 0).
                Span<float> dcs = stackalloc float[4];
                SubBlockDcs(coefficients, dcs);
                for (int y = 0; y < 2; y++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        float blockDc = dcs[(y * 2) + x];
                        float residualSum = 0;
                        for (int iy = 0; iy < 4; iy++)
                        {
                            for (int ix = 0; ix < 4; ix++)
                            {
                                if (ix == 0 && iy == 0)
                                {
                                    continue;
                                }

                                residualSum += coefficients[((y + (iy * 2)) * 8) + x + (ix * 2)];
                            }
                        }

                        int centre = (((4 * y) + 1) * pixelsStride) + (4 * x) + 1;
                        pixels[centre] = blockDc - (residualSum * (1.0f / 16));
                        for (int iy = 0; iy < 4; iy++)
                        {
                            for (int ix = 0; ix < 4; ix++)
                            {
                                if (ix == 1 && iy == 1)
                                {
                                    continue;
                                }

                                pixels[(((y * 4) + iy) * pixelsStride) + (x * 4) + ix] =
                                    coefficients[((y + (iy * 2)) * 8) + x + (ix * 2)] + pixels[centre];
                            }
                        }

                        pixels[(y * 4 * pixelsStride) + (x * 4)] = coefficients[((y + 2) * 8) + x + 2] + pixels[centre];
                    }
                }

                break;
            }

            case JxlAcStrategyType.Afv0:
            case JxlAcStrategyType.Afv1:
            case JxlAcStrategyType.Afv2:
            case JxlAcStrategyType.Afv3:
            {
                AfvTransformToPixels(strategy - JxlAcStrategyType.Afv0, coefficients, pixels, pixelsStride, scratch);
                break;
            }

            default:
            {
                // Every other strategy is a DCT of its size: ComputeScaledIDCT<ROWS, COLS>.
                ComputeScaledIdct(acs.CoveredBlocksY * BlockDim, acs.CoveredBlocksX * BlockDim, coefficients, pixels, pixelsStride, scratch);
                break;
            }
        }
    }

    /// <summary>
    /// Transforms the pixels of a block into its coefficients, as the encoder's <c>TransformFromPixels</c>
    /// does: the inverse of <see cref="TransformToPixels"/>, for every strategy.
    /// </summary>
    /// <param name="strategy">The strategy.</param>
    /// <param name="pixels">The first pixel of the block.</param>
    /// <param name="pixelsStride">The distance between pixel rows.</param>
    /// <param name="coefficients">The coefficients, 64 per 8x8 block the strategy covers, in its layout.</param>
    /// <param name="scratch">Scratch space, <see cref="ScratchSize"/> floats.</param>
    public static void TransformFromPixels(JxlAcStrategyType strategy, ReadOnlySpan<float> pixels, int pixelsStride, Span<float> coefficients, Span<float> scratch)
    {
        switch (strategy)
        {
            case JxlAcStrategyType.Identity:
                IdentityFromPixels(pixels, pixelsStride, coefficients);
                break;

            case JxlAcStrategyType.Dct4X4:
            {
                Span<float> block = stackalloc float[4 * 4];
                for (int y = 0; y < 2; y++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        ComputeScaledDct(4, 4, pixels.Slice((y * 4 * pixelsStride) + (x * 4)), pixelsStride, block, scratch);
                        for (int iy = 0; iy < 4; iy++)
                        {
                            for (int ix = 0; ix < 4; ix++)
                            {
                                coefficients[((y + (iy * 2)) * 8) + x + (ix * 2)] = block[(iy * 4) + ix];
                            }
                        }
                    }
                }

                SubBlockDcsFromPixels(coefficients);
                break;
            }

            case JxlAcStrategyType.Dct2X2:
                Dct2TopBlock(8, pixels, pixelsStride, coefficients);
                Dct2TopBlock(4, coefficients, BlockDim, coefficients);
                Dct2TopBlock(2, coefficients, BlockDim, coefficients);
                break;

            case JxlAcStrategyType.Dct8X4:
            {
                // Two 8-row, 4-column DCTs side by side; ComputeScaledDCT<8, 4> gives each transposed, 4
                // rows of 8, stored in alternate rows of the block.
                Span<float> block = stackalloc float[4 * 8];
                for (int x = 0; x < 2; x++)
                {
                    ComputeScaledDct(8, 4, pixels.Slice(x * 4), pixelsStride, block, scratch);
                    for (int iy = 0; iy < 4; iy++)
                    {
                        for (int ix = 0; ix < 8; ix++)
                        {
                            // Store transposed.
                            coefficients[((x + (iy * 2)) * 8) + ix] = block[(iy * 8) + ix];
                        }
                    }
                }

                HalfBlockDcsFromPixels(coefficients);
                break;
            }

            case JxlAcStrategyType.Dct4X8:
            {
                // Two 4-row, 8-column DCTs one above the other, stored in alternate rows of the block.
                Span<float> block = stackalloc float[4 * 8];
                for (int y = 0; y < 2; y++)
                {
                    ComputeScaledDct(4, 8, pixels.Slice(y * 4 * pixelsStride), pixelsStride, block, scratch);
                    for (int iy = 0; iy < 4; iy++)
                    {
                        for (int ix = 0; ix < 8; ix++)
                        {
                            coefficients[((y + (iy * 2)) * 8) + ix] = block[(iy * 8) + ix];
                        }
                    }
                }

                HalfBlockDcsFromPixels(coefficients);
                break;
            }

            case JxlAcStrategyType.Afv0:
            case JxlAcStrategyType.Afv1:
            case JxlAcStrategyType.Afv2:
            case JxlAcStrategyType.Afv3:
                AfvTransformFromPixels(strategy - JxlAcStrategyType.Afv0, pixels, pixelsStride, coefficients, scratch);
                break;

            default:
            {
                // Every other strategy is a DCT of its size: ComputeScaledDCT<ROWS, COLS>.
                JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy((int)strategy);
                ComputeScaledDct(acs.CoveredBlocksY * BlockDim, acs.CoveredBlocksX * BlockDim, pixels, pixelsStride, coefficients, scratch);
                break;
            }
        }
    }

    /// <summary>
    /// Sets the lowest frequencies of a block from its DC values (libjxl's <c>LowestFrequenciesFromDC</c>):
    /// the DC itself for a block of one 8x8 block, else a DCT of the DC values, rescaled to the block's size.
    /// </summary>
    /// <param name="acs">The strategy.</param>
    /// <param name="dc">The DC value of the first 8x8 block of the block.</param>
    /// <param name="dcStride">The distance between rows of DC values.</param>
    /// <param name="llf">The coefficients of the block, whose lowest frequencies are set.</param>
    /// <param name="scratch">Scratch space, <see cref="ScratchSize"/> floats.</param>
    public static void LowestFrequenciesFromDc(JxlAcStrategy acs, ReadOnlySpan<float> dc, int dcStride, Span<float> llf, Span<float> scratch)
    {
        int rows = acs.CoveredBlocksY;
        int cols = acs.CoveredBlocksX;
        if (rows * cols == 1)
        {
            llf[0] = dc[0];
            return;
        }

        // ReinterpretingDCT<DCT_ROWS = 8 * ROWS, DCT_COLS = 8 * COLS, LF_ROWS = ROWS, LF_COLS = COLS,
        // ROWS, COLS>, into llf with a stride of the block's larger side.
        int outputStride = Math.Max(rows, cols) * BlockDim;
        Span<float> block = scratch.Slice(0, rows * cols);
        ComputeScaledDct(rows, cols, dc, dcStride, block, scratch.Slice(MaxDim * MaxDim / 4));
        ReadOnlySpan<float> rowScales = ResampleScales(rows);
        ReadOnlySpan<float> colScales = ResampleScales(cols);
        if (rows < cols)
        {
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    llf[(y * outputStride) + x] = block[(y * cols) + x] * rowScales[y] * colScales[x];
                }
            }
        }
        else
        {
            for (int y = 0; y < cols; y++)
            {
                for (int x = 0; x < rows; x++)
                {
                    llf[(y * outputStride) + x] = block[(y * rows) + x] * colScales[y] * rowScales[x];
                }
            }
        }
    }

    /// <summary>
    /// Gives the DC values of the 8x8 blocks a block covers from its coefficients (libjxl's encoder
    /// <c>DCFromLowestFrequencies</c>): the first coefficient for a block of one 8x8 block, else an inverse
    /// DCT of the lowest frequencies, rescaled to the DC image's size. The inverse of
    /// <see cref="LowestFrequenciesFromDc"/>.
    /// </summary>
    /// <param name="strategy">The strategy.</param>
    /// <param name="block">The coefficients of the block, in its layout.</param>
    /// <param name="dc">The DC value of the first 8x8 block of the block.</param>
    /// <param name="dcStride">The distance between rows of DC values.</param>
    /// <param name="scratch">Scratch space, <see cref="ScratchSize"/> floats.</param>
    public static void DcFromLowestFrequencies(JxlAcStrategyType strategy, ReadOnlySpan<float> block, Span<float> dc, int dcStride, Span<float> scratch)
    {
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy((int)strategy);
        int rows = acs.CoveredBlocksY;
        int cols = acs.CoveredBlocksX;
        if (rows * cols == 1)
        {
            dc[0] = block[0];
            return;
        }

        // ReinterpretingIDCT<DCT_ROWS = 8 * ROWS, DCT_COLS = 8 * COLS, LF_ROWS = ROWS, LF_COLS = COLS,
        // ROWS, COLS>, from block with a stride of the block's larger side. libjxl puts the scaled lowest
        // frequencies first in its scratch space and ComputeScaledIDCT's own space after them; here they go
        // after the rows * cols floats that ComputeScaledIdct takes for its block.
        int inputStride = Math.Max(rows, cols) * BlockDim;
        Span<float> lowest = scratch.Slice(MaxCoveredBlocks * MaxCoveredBlocks, rows * cols);
        ReadOnlySpan<float> rowScales = InverseResampleScales(rows);
        ReadOnlySpan<float> colScales = InverseResampleScales(cols);
        if (rows < cols)
        {
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    lowest[(y * cols) + x] = block[(y * inputStride) + x] * rowScales[y] * colScales[x];
                }
            }
        }
        else
        {
            for (int y = 0; y < cols; y++)
            {
                for (int x = 0; x < rows; x++)
                {
                    lowest[(y * rows) + x] = block[(y * inputStride) + x] * colScales[y] * rowScales[x];
                }
            }
        }

        ComputeScaledIdct(rows, cols, lowest, dc, dcStride, scratch);
    }

    // DCTTotalResampleScale<8 * N, N>, for the block counts of the strategies the encoder chooses (up to
    // 64x64).
    private static ReadOnlySpan<float> InverseResampleScales(int n) => n switch
    {
        1 => ResampleScales8To1,
        2 => ResampleScales16To2,
        4 => ResampleScales32To4,
        8 => ResampleScales64To8,
        _ => throw new ArgumentOutOfRangeException(nameof(n), n, "Not a block count of a strategy the encoder chooses"),
    };

    // DCTTotalResampleScale<N, 8 * N>.
    private static ReadOnlySpan<float> ResampleScales(int n) => n switch
    {
        1 => ResampleScales1To8,
        2 => ResampleScales2To16,
        4 => ResampleScales4To32,
        8 => ResampleScales8To64,
        16 => ResampleScales16To128,
        32 => ResampleScales32To256,
        _ => throw new ArgumentOutOfRangeException(nameof(n), n, "Not a block count of a strategy"),
    };

    private static ReadOnlySpan<float> WcMultipliers(int n) => n switch
    {
        4 => WcMultipliers4,
        8 => WcMultipliers8,
        16 => WcMultipliers16,
        32 => WcMultipliers32,
        64 => WcMultipliers64,
        128 => WcMultipliers128,
        256 => WcMultipliers256,
        _ => throw new ArgumentOutOfRangeException(nameof(n), n, "Not a DCT size"),
    };

    // The DCs of the four 4x4 sub-blocks of DCT4X4 and Identity: a 2x2 Hadamard of coefficients 0, 1,
    // 8 and 9.
    private static void SubBlockDcs(ReadOnlySpan<float> coefficients, Span<float> dcs)
    {
        float block00 = coefficients[0];
        float block01 = coefficients[1];
        float block10 = coefficients[8];
        float block11 = coefficients[9];
        dcs[0] = block00 + block01 + block10 + block11;
        dcs[1] = block00 + block01 - block10 - block11;
        dcs[2] = block00 - block01 + block10 - block11;
        dcs[3] = block00 - block01 - block10 + block11;
    }

    // The encoder's DC of DCT4X8 and DCT8X4: coefficients 0 and 8 hold the two halves' DCs, and become
    // their sum and difference, halved.
    private static void HalfBlockDcsFromPixels(Span<float> coefficients)
    {
        float block0 = coefficients[0];
        float block1 = coefficients[8];
        coefficients[0] = (block0 + block1) * 0.5f;
        coefficients[8] = (block0 - block1) * 0.5f;
    }

    // The encoder's mirror of SubBlockDcs: coefficients 0, 1, 8 and 9 hold the four sub-blocks' DCs, and
    // become their 2x2 Hadamard, scaled by 1 / 4.
    private static void SubBlockDcsFromPixels(Span<float> coefficients)
    {
        float block00 = coefficients[0];
        float block01 = coefficients[1];
        float block10 = coefficients[8];
        float block11 = coefficients[9];
        coefficients[0] = (block00 + block01 + block10 + block11) * 0.25f;
        coefficients[1] = (block00 + block01 - block10 - block11) * 0.25f;
        coefficients[8] = (block00 - block01 + block10 - block11) * 0.25f;
        coefficients[9] = (block00 - block01 - block10 + block11) * 0.25f;
    }

    // The IDENTITY case of the encoder's TransformFromPixels: four 4x4 squares, each as its mean and the
    // residuals about its pixel at (1, 1).
    private static void IdentityFromPixels(ReadOnlySpan<float> pixels, int pixelsStride, Span<float> coefficients)
    {
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                float blockDc = 0;
                for (int iy = 0; iy < 4; iy++)
                {
                    for (int ix = 0; ix < 4; ix++)
                    {
                        blockDc += pixels[(((y * 4) + iy) * pixelsStride) + (x * 4) + ix];
                    }
                }

                blockDc *= 1.0f / 16;
                float centre = pixels[(((y * 4) + 1) * pixelsStride) + (x * 4) + 1];
                for (int iy = 0; iy < 4; iy++)
                {
                    for (int ix = 0; ix < 4; ix++)
                    {
                        if (ix == 1 && iy == 1)
                        {
                            continue;
                        }

                        coefficients[((y + (iy * 2)) * 8) + x + (ix * 2)] =
                            pixels[(((y * 4) + iy) * pixelsStride) + (x * 4) + ix] - centre;
                    }
                }

                coefficients[((y + 2) * 8) + x + 2] = coefficients[(y * 8) + x];
                coefficients[(y * 8) + x] = blockDc;
            }
        }

        SubBlockDcsFromPixels(coefficients);
    }

    // DCT2TopBlock<S>: the S x S top-left corner of "from" (rows fromStride apart) as a pyramid level of
    // 2x2 Haar sums into "to", an 8x8 block. "from" and "to" may be the same.
    private static void Dct2TopBlock(int s, ReadOnlySpan<float> from, int fromStride, Span<float> to)
    {
        Span<float> temp = stackalloc float[DctBlockSize];
        int num2X2 = s / 2;
        for (int y = 0; y < num2X2; y++)
        {
            for (int x = 0; x < num2X2; x++)
            {
                float c00 = from[(y * 2 * fromStride) + (x * 2)];
                float c01 = from[(y * 2 * fromStride) + (x * 2) + 1];
                float c10 = from[(((y * 2) + 1) * fromStride) + (x * 2)];
                float c11 = from[(((y * 2) + 1) * fromStride) + (x * 2) + 1];
                float r00 = c00 + c01 + c10 + c11;
                float r01 = c00 + c01 - c10 - c11;
                float r10 = c00 - c01 + c10 - c11;
                float r11 = c00 - c01 - c10 + c11;
                r00 *= 0.25f;
                r01 *= 0.25f;
                r10 *= 0.25f;
                r11 *= 0.25f;
                temp[(y * BlockDim) + x] = r00;
                temp[(y * BlockDim) + num2X2 + x] = r01;
                temp[((y + num2X2) * BlockDim) + x] = r10;
                temp[((y + num2X2) * BlockDim) + num2X2 + x] = r11;
            }
        }

        for (int y = 0; y < s; y++)
        {
            temp.Slice(y * BlockDim, s).CopyTo(to.Slice(y * BlockDim));
        }
    }

    // The loop that starts each sub-block transform in libjxl: the rows x cols block takes coefficient
    // start + iy * rowStride + ix * colStep at (ix, iy), except at (0, 0), which holds the sub-block's DC.
    private static void GatherAc(ReadOnlySpan<float> coefficients, int start, int rowStride, int colStep, int rows, int cols, Span<float> block)
    {
        for (int iy = 0; iy < rows; iy++)
        {
            for (int ix = 0; ix < cols; ix++)
            {
                if (ix == 0 && iy == 0)
                {
                    continue;
                }

                block[(iy * cols) + ix] = coefficients[start + (iy * rowStride) + (ix * colStep)];
            }
        }
    }

    // IDCT2TopBlock<S>, in place in an 8x8 block.
    private static void Idct2TopBlock(int s, Span<float> block)
    {
        Span<float> temp = stackalloc float[DctBlockSize];
        int num2X2 = s / 2;
        for (int y = 0; y < num2X2; y++)
        {
            for (int x = 0; x < num2X2; x++)
            {
                float c00 = block[(y * BlockDim) + x];
                float c01 = block[(y * BlockDim) + num2X2 + x];
                float c10 = block[((y + num2X2) * BlockDim) + x];
                float c11 = block[((y + num2X2) * BlockDim) + num2X2 + x];
                float r00 = c00 + c01 + c10 + c11;
                float r01 = c00 + c01 - c10 - c11;
                float r10 = c00 - c01 + c10 - c11;
                float r11 = c00 - c01 - c10 + c11;
                temp[(y * 2 * BlockDim) + (x * 2)] = r00;
                temp[(y * 2 * BlockDim) + (x * 2) + 1] = r01;
                temp[(((y * 2) + 1) * BlockDim) + (x * 2)] = r10;
                temp[(((y * 2) + 1) * BlockDim) + (x * 2) + 1] = r11;
            }
        }

        for (int y = 0; y < s; y++)
        {
            temp.Slice(y * BlockDim, s).CopyTo(block.Slice(y * BlockDim));
        }
    }

    // ComputeScaledIDCT<ROWS, COLS>: from holds the coefficients, min(ROWS, COLS) rows of
    // max(ROWS, COLS); it is overwritten. The ROWS x COLS pixels go to "to".
    private static void ComputeScaledIdct(int rows, int cols, Span<float> from, Span<float> to, int toStride, Span<float> scratch)
    {
        Span<float> block = scratch.Slice(0, rows * cols);
        Span<float> tmp = scratch.Slice(MaxDim * MaxDim);

        // Reverse the steps done in ComputeScaledDCT.
        if (rows < cols)
        {
            Transpose(rows, cols, from, cols, block, rows);
            Idct1D(cols, rows, block, rows, from, rows, tmp);
            Transpose(cols, rows, from, rows, block, cols);
            Idct1D(rows, cols, block, cols, to, toStride, tmp);
        }
        else
        {
            Idct1D(cols, rows, from, rows, block, rows, tmp);
            Transpose(cols, rows, block, rows, from, cols);
            Idct1D(rows, cols, from, cols, to, toStride, tmp);
        }
    }

    // ComputeScaledDCT<ROWS, COLS>: the ROWS x COLS values of "from" (whose rows are fromStride apart)
    // into "to", min(ROWS, COLS) rows of max(ROWS, COLS).
    private static void ComputeScaledDct(int rows, int cols, ReadOnlySpan<float> from, int fromStride, Span<float> to, Span<float> scratch)
    {
        Span<float> block = scratch.Slice(0, rows * cols);
        Span<float> tmp = scratch.Slice(rows * cols);
        if (rows < cols)
        {
            Dct1D(rows, cols, from, fromStride, block, cols, tmp);
            Transpose(rows, cols, block, cols, to, rows);
            Dct1D(cols, rows, to, rows, block, rows, tmp);
            Transpose(cols, rows, block, rows, to, cols);
        }
        else
        {
            Dct1D(rows, cols, from, fromStride, to, cols, tmp);
            Transpose(rows, cols, to, cols, block, rows);
            Dct1D(cols, rows, block, rows, to, rows, tmp);
        }
    }

    // IDCT1D<N, M>: the N-point 1D IDCT down each of the M columns of from, into to.
    private static void Idct1D(int n, int m, ReadOnlySpan<float> from, int fromStride, Span<float> to, int toStride, Span<float> scratch)
    {
        Span<float> column = scratch.Slice(0, n);
        Span<float> output = scratch.Slice(n, n);
        Span<float> tmp = scratch.Slice(2 * n, 2 * n);
        for (int i = 0; i < m; i++)
        {
            for (int k = 0; k < n; k++)
            {
                column[k] = from[(k * fromStride) + i];
            }

            Idct1DImpl(n, column, output, tmp);
            for (int k = 0; k < n; k++)
            {
                to[(k * toStride) + i] = output[k];
            }
        }
    }

    // IDCT1DImpl<N>. from and to may be the same: from is read before to is written.
    private static void Idct1DImpl(int n, ReadOnlySpan<float> from, Span<float> to, Span<float> scratch)
    {
        if (n == 1)
        {
            to[0] = from[0];
            return;
        }

        if (n == 2)
        {
            float in1 = from[0];
            float in2 = from[1];
            to[0] = in1 + in2;
            to[1] = in1 - in2;
            return;
        }

        int half = n / 2;
        Span<float> tmp = scratch.Slice(0, n);
        ForwardEvenOdd(from, tmp, n);
        Idct1DImpl(half, tmp.Slice(0, half), tmp.Slice(0, half), scratch.Slice(n));
        BTranspose(tmp.Slice(half, half));
        Idct1DImpl(half, tmp.Slice(half, half), tmp.Slice(half, half), scratch.Slice(n));
        MultiplyAndAdd(tmp, to, WcMultipliers(n));
    }

    // DCT1D<N, M>: the N-point 1D DCT down each of the M columns of from, scaled by 1 / N, into to.
    private static void Dct1D(int n, int m, ReadOnlySpan<float> from, int fromStride, Span<float> to, int toStride, Span<float> scratch)
    {
        Span<float> mem = scratch.Slice(0, n);
        Span<float> tmp = scratch.Slice(n);
        float mul = 1.0f / n;
        for (int i = 0; i < m; i++)
        {
            // CoeffBundle::LoadFromBlock, DCT1DImpl, CoeffBundle::StoreToBlockAndScale.
            for (int k = 0; k < n; k++)
            {
                mem[k] = from[(k * fromStride) + i];
            }

            Dct1DImpl(n, mem, tmp);
            for (int k = 0; k < n; k++)
            {
                to[(k * toStride) + i] = mul * mem[k];
            }
        }
    }

    // DCT1DImpl<N>, in place in mem.
    private static void Dct1DImpl(int n, Span<float> mem, Span<float> scratch)
    {
        if (n == 1)
        {
            return;
        }

        if (n == 2)
        {
            float in1 = mem[0];
            float in2 = mem[1];
            mem[0] = in1 + in2;
            mem[1] = in1 - in2;
            return;
        }

        int half = n / 2;
        Span<float> tmp = scratch.Slice(0, n);

        // CoeffBundle<N / 2>::AddReverse and SubReverse.
        for (int i = 0; i < half; i++)
        {
            tmp[i] = mem[i] + mem[n - i - 1];
        }

        Dct1DImpl(half, tmp.Slice(0, half), scratch.Slice(n));
        for (int i = 0; i < half; i++)
        {
            tmp[half + i] = mem[i] - mem[n - i - 1];
        }

        // CoeffBundle<N>::Multiply.
        ReadOnlySpan<float> multipliers = WcMultipliers(n);
        for (int i = 0; i < half; i++)
        {
            tmp[half + i] *= multipliers[i];
        }

        Dct1DImpl(half, tmp.Slice(half, half), scratch.Slice(n));
        B(tmp.Slice(half, half));

        // CoeffBundle<N>::InverseEvenOdd.
        for (int i = 0; i < half; i++)
        {
            mem[2 * i] = tmp[i];
        }

        for (int i = half; i < n; i++)
        {
            mem[(2 * (i - half)) + 1] = tmp[i];
        }
    }

    // CoeffBundle<N>::B. libjxl's MulAdd(in1_0, sqrt2, in2_0) is not fused here (ADR 0004).
    private static void B(Span<float> coeff)
    {
        float product = coeff[0] * Sqrt2;
        coeff[0] = product + coeff[1];
        for (int i = 1; i + 1 < coeff.Length; i++)
        {
            coeff[i] += coeff[i + 1];
        }
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

    // CoeffBundle<N>::MultiplyAndAdd. libjxl's MulAdd and NegMulAdd are not fused here (ADR 0004).
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

    // Transpose<ROWS, COLS>: to(m, n) = from(n, m), for the ROWS x COLS values of from.
    private static void Transpose(int rows, int cols, ReadOnlySpan<float> from, int fromStride, Span<float> to, int toStride)
    {
        for (int n = 0; n < rows; n++)
        {
            for (int m = 0; m < cols; m++)
            {
                to[(m * toStride) + n] = from[(n * fromStride) + m];
            }
        }
    }
}
