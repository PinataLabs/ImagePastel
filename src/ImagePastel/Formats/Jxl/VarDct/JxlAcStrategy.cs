// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/ac_strategy.h and lib/jxl/ac_strategy.cc (AcStrategyType, AcStrategy, CoeffOrderAndLut),
// and lib/jxl/coeff_order_fwd.h (CoefficientLayout).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>The raw transform types of a VarDCT block.</summary>
internal enum JxlAcStrategyType : byte
{
    /// <summary>Regular block size DCT.</summary>
    Dct = 0,

    /// <summary>Encode pixels without transforming.</summary>
    Identity = 1,

    /// <summary>Use 2-by-2 DCT.</summary>
    Dct2X2 = 2,

    /// <summary>Use 4-by-4 DCT.</summary>
    Dct4X4 = 3,

    /// <summary>Use 16-by-16 DCT.</summary>
    Dct16X16 = 4,

    /// <summary>Use 32-by-32 DCT.</summary>
    Dct32X32 = 5,

    /// <summary>Use 16-by-8 DCT.</summary>
    Dct16X8 = 6,

    /// <summary>Use 8-by-16 DCT.</summary>
    Dct8X16 = 7,

    /// <summary>Use 32-by-8 DCT.</summary>
    Dct32X8 = 8,

    /// <summary>Use 8-by-32 DCT.</summary>
    Dct8X32 = 9,

    /// <summary>Use 32-by-16 DCT.</summary>
    Dct32X16 = 10,

    /// <summary>Use 16-by-32 DCT.</summary>
    Dct16X32 = 11,

    /// <summary>4x8 DCT.</summary>
    Dct4X8 = 12,

    /// <summary>8x4 DCT.</summary>
    Dct8X4 = 13,

    /// <summary>Corner-DCT.</summary>
    Afv0 = 14,

    /// <summary>Corner-DCT.</summary>
    Afv1 = 15,

    /// <summary>Corner-DCT.</summary>
    Afv2 = 16,

    /// <summary>Corner-DCT.</summary>
    Afv3 = 17,

    /// <summary>Larger DCT.</summary>
    Dct64X64 = 18,

    /// <summary>Larger DCT.</summary>
    Dct64X32 = 19,

    /// <summary>Larger DCT.</summary>
    Dct32X64 = 20,

    /// <summary>Larger DCT. No transforms smaller than 64x64 are allowed below.</summary>
    Dct128X128 = 21,

    /// <summary>Larger DCT.</summary>
    Dct128X64 = 22,

    /// <summary>Larger DCT.</summary>
    Dct64X128 = 23,

    /// <summary>Larger DCT.</summary>
    Dct256X256 = 24,

    /// <summary>Larger DCT.</summary>
    Dct256X128 = 25,

    /// <summary>Larger DCT.</summary>
    Dct128X256 = 26,
}

/// <summary>
/// The transform of a block, and whether this 8x8 block is the first (top-left) block of it.
/// </summary>
internal readonly struct JxlAcStrategy
{
    /// <summary>The number of strategies.</summary>
    public const int NumValidStrategies = (int)JxlAcStrategyType.Dct128X256 + 1;

    public JxlAcStrategy(JxlAcStrategyType strategy, bool isFirst)
    {
        this.Strategy = strategy;
        this.IsFirstBlock = isFirst;
    }

    /// <summary>Gets the strategy.</summary>
    public JxlAcStrategyType Strategy { get; }

    /// <summary>Gets a value indicating whether this is the top-left 8x8 block of the strategy.</summary>
    public bool IsFirstBlock { get; }

    /// <summary>Gets the raw strategy value.</summary>
    public byte RawStrategy => (byte)this.Strategy;

    /// <summary>Gets the number of 8x8 blocks the strategy covers horizontally.</summary>
    public int CoveredBlocksX => CoveredBlocksXLut[(int)this.Strategy];

    /// <summary>Gets the number of 8x8 blocks the strategy covers vertically.</summary>
    public int CoveredBlocksY => CoveredBlocksYLut[(int)this.Strategy];

    /// <summary>Gets log2 of the number of 8x8 blocks the strategy covers.</summary>
    public int Log2CoveredBlocks => Log2CoveredBlocksLut[(int)this.Strategy];

    private static ReadOnlySpan<byte> CoveredBlocksXLut => new byte[]
    {
        1, 1, 1, 1, 2, 4, 1, 2, 1, 4, 2, 4, 1, 1, 1, 1, 1, 1, 8, 4, 8, 16, 8, 16, 32, 16, 32,
    };

    private static ReadOnlySpan<byte> CoveredBlocksYLut => new byte[]
    {
        1, 1, 1, 1, 2, 4, 2, 1, 4, 1, 4, 2, 1, 1, 1, 1, 1, 1, 8, 8, 4, 16, 16, 8, 32, 32, 16,
    };

    private static ReadOnlySpan<byte> Log2CoveredBlocksLut => new byte[]
    {
        0, 0, 0, 0, 2, 4, 1, 1, 2, 2, 3, 3, 0, 0, 0, 0, 0, 0, 6, 5, 5, 8, 7, 7, 10, 9, 9,
    };

    /// <summary>Returns whether <paramref name="rawStrategy"/> names a strategy.</summary>
    /// <param name="rawStrategy">The raw strategy.</param>
    /// <returns><see langword="true"/> if it is valid.</returns>
    public static bool IsRawStrategyValid(int rawStrategy) => rawStrategy is < NumValidStrategies and >= 0;

    /// <summary>Returns the first block of a strategy.</summary>
    /// <param name="rawStrategy">The raw strategy, which must be valid.</param>
    /// <returns>The strategy.</returns>
    public static JxlAcStrategy FromRawStrategy(int rawStrategy) => new((JxlAcStrategyType)rawStrategy, true);

    /// <summary>
    /// Writes the natural coefficient order: a zig-zag order generalised to non-square blocks, by
    /// increasing "anisotropic" frequency of the continuous DCT basis.
    /// </summary>
    /// <param name="order">The order, <see cref="CoveredBlocksX"/> x <see cref="CoveredBlocksY"/> x 64 entries.</param>
    public void ComputeNaturalCoeffOrder(Span<uint> order) => this.CoeffOrderAndLut(order, isLut: false);

    /// <summary>
    /// Writes the inverse of <see cref="ComputeNaturalCoeffOrder"/>: for each coefficient, its index in
    /// the natural order.
    /// </summary>
    /// <param name="lut">The look-up table, <see cref="CoveredBlocksX"/> x <see cref="CoveredBlocksY"/> x 64 entries.</param>
    public void ComputeNaturalCoeffOrderLut(Span<uint> lut) => this.CoeffOrderAndLut(lut, isLut: true);

    /// <summary>
    /// Swaps <paramref name="rows"/> and <paramref name="columns"/> so the rows are the smaller: DCT
    /// coefficients are laid out with the fewer rows.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="columns">The columns.</param>
    public static void CoefficientLayout(ref int rows, ref int columns)
    {
        int r = rows;
        int c = columns;
        rows = Math.Min(r, c);
        columns = Math.Max(r, c);
    }

    // Tries to generalize zig-zag order to non-square blocks. Surprisingly, in square block frequency
    // along the (i + j == const) diagonals is roughly the same. For historical reasons, consecutive
    // diagonals are traversed in alternating directions - so called "zig-zag" (or "snake") order.
    // With isLut, the inverse look-up table instead (libjxl's template parameter), which only the encoder uses.
    private void CoeffOrderAndLut(Span<uint> output, bool isLut)
    {
        const int blockDim = JxlFrameDimensions.BlockDim;
        int cx = this.CoveredBlocksX;
        int cy = this.CoveredBlocksY;
        CoefficientLayout(ref cy, ref cx);

        // CoefficientLayout ensures cx >= cy. We compute the zigzag order for a cx x cx block, then
        // discard all the lines that are not multiple of the ratio between cx and cy.
        int xs = cx / cy;
        int xsm = xs - 1;
        int xss = JxlBits.CeilLog2Nonzero((uint)xs);

        // First half of the block
        int cur = cx * cy;
        for (int i = 0; i < cx * blockDim; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                int x = j;
                int y = i - j;
                if (i % 2 != 0)
                {
                    (x, y) = (y, x);
                }

                if ((y & xsm) != 0)
                {
                    continue;
                }

                y >>= xss;
                int val;
                if (x < cx && y < cy)
                {
                    val = (y * cx) + x;
                }
                else
                {
                    val = cur++;
                }

                if (isLut)
                {
                    output[(y * cx * blockDim) + x] = (uint)val;
                }
                else
                {
                    output[val] = (uint)((y * cx * blockDim) + x);
                }
            }
        }

        // Second half
        for (int ip = (cx * blockDim) - 1; ip > 0; ip--)
        {
            int i = ip - 1;
            for (int j = 0; j <= i; j++)
            {
                int x = (cx * blockDim) - 1 - (i - j);
                int y = (cx * blockDim) - 1 - j;
                if (i % 2 != 0)
                {
                    (x, y) = (y, x);
                }

                if ((y & xsm) != 0)
                {
                    continue;
                }

                y >>= xss;
                int val = cur++;
                if (isLut)
                {
                    output[(y * cx * blockDim) + x] = (uint)val;
                }
                else
                {
                    output[val] = (uint)((y * cx * blockDim) + x);
                }
            }
        }
    }
}
