// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/ac_strategy.h and lib/jxl/ac_strategy.cc (AcStrategyRow, AcStrategyImage, FillDCT8, Set).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;

namespace PinataLabs.ImagePastel.Formats.Jxl.VarDct;

/// <summary>
/// The strategy of each 8x8 block of a frame. Each byte holds the strategy in its high bits and, in its
/// low bit, whether the block is the first of its strategy.
/// </summary>
internal sealed class JxlAcStrategyImage
{
    // A value that does not represent a valid combined AC strategy value. Used as a sentinel.
    private const byte Invalid = 0xFF;

    private readonly byte[] layers;

    /// <summary>Initializes a new instance of the <see cref="JxlAcStrategyImage"/> class.</summary>
    /// <param name="xsize">The width, in blocks.</param>
    /// <param name="ysize">The height, in blocks.</param>
    public JxlAcStrategyImage(int xsize, int ysize)
    {
        this.XSize = xsize;
        this.YSize = ysize;
        this.layers = new byte[xsize * ysize];
    }

    /// <summary>Gets the width, in blocks.</summary>
    public int XSize { get; }

    /// <summary>Gets the height, in blocks.</summary>
    public int YSize { get; }

    /// <summary>Marks every block as not yet set.</summary>
    public void FillInvalid() => Array.Fill(this.layers, Invalid);

    /// <summary>Sets every block to DCT8, as the encoder's <c>FillDCT8</c> does for the whole image.</summary>
    public void FillDct8() => Array.Fill(this.layers, (byte)(((int)JxlAcStrategyType.Dct << 1) | 1));

    /// <summary>Sets every block of <paramref name="rect"/> to DCT8, as the encoder's <c>FillDCT8(rect)</c> does.</summary>
    /// <param name="rect">The blocks.</param>
    public void FillDct8(JxlRect rect)
    {
        for (long y = rect.Y0; y < rect.Y0 + rect.YSize; y++)
        {
            Array.Fill(this.layers, (byte)(((int)JxlAcStrategyType.Dct << 1) | 1), (int)((y * this.XSize) + rect.X0), (int)rect.XSize);
        }
    }

    /// <summary>
    /// Sets the strategy whose first block is (<paramref name="x"/>, <paramref name="y"/>) on every
    /// block it covers, and throws if any of those blocks already has a strategy (libjxl's
    /// <c>SetNoBoundsCheck</c> with <c>check = true</c>, as the decoder calls it). The caller has checked
    /// that the strategy fits in the image.
    /// </summary>
    /// <param name="x">The first block's column.</param>
    /// <param name="y">The first block's row.</param>
    /// <param name="type">The strategy.</param>
    /// <exception cref="InvalidImageContentException">A block the strategy covers already has one.</exception>
    public void SetRejectingOverlap(int x, int y, JxlAcStrategyType type) => this.Set(x, y, type, rejectOverlap: true);

    /// <summary>
    /// Sets the strategy whose first block is (<paramref name="x"/>, <paramref name="y"/>) on every
    /// block it covers, over whatever was there, as the encoder's <c>Set</c> does. The strategy must fit.
    /// </summary>
    /// <param name="x">The first block's column.</param>
    /// <param name="y">The first block's row.</param>
    /// <param name="type">The strategy.</param>
    public void Set(int x, int y, JxlAcStrategyType type)
    {
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy((int)type);
        DebugGuard.IsTrue(y + acs.CoveredBlocksY <= this.YSize && x + acs.CoveredBlocksX <= this.XSize, "The strategy must fit in the image");
        this.Set(x, y, type, rejectOverlap: false);
    }

    // libjxl's SetNoBoundsCheck(x, y, type, check): with rejectOverlap (libjxl's check), a block that
    // already has a strategy is an error; without it, the new strategy overwrites it.
    private void Set(int x, int y, JxlAcStrategyType type, bool rejectOverlap)
    {
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy((int)type);
        for (int iy = 0; iy < acs.CoveredBlocksY; iy++)
        {
            for (int ix = 0; ix < acs.CoveredBlocksX; ix++)
            {
                int pos = ((y + iy) * this.XSize) + x + ix;
                if (rejectOverlap && this.layers[pos] != Invalid)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid AC strategy: block overlap");
                }

                this.layers[pos] = (byte)(((int)type << 1) | ((iy | ix) == 0 ? 1 : 0));
            }
        }
    }

    /// <summary>Returns whether block (<paramref name="x"/>, <paramref name="y"/>) has a strategy.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns><see langword="true"/> if it is set.</returns>
    public bool IsValid(int x, int y) => this.layers[(y * this.XSize) + x] != Invalid;

    /// <summary>Returns the strategy of block (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The strategy.</returns>
    public JxlAcStrategy Get(int x, int y)
    {
        byte value = this.layers[(y * this.XSize) + x];
        return new JxlAcStrategy((JxlAcStrategyType)(value >> 1), (value & 1) != 0);
    }
}
