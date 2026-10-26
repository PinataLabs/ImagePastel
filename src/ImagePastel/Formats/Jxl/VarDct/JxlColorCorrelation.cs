// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/chroma_from_luma.h and lib/jxl/chroma_from_luma.cc (ColorCorrelation, ColorCorrelationMap),
// and kYToBRatio from lib/jxl/cms/opsin_params.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using static PinataLabs.ImagePastel.Formats.Jxl.Fields.JxlU32Distr;

namespace PinataLabs.ImagePastel.Formats.Jxl.VarDct;

/// <summary>
/// Chroma from luma: the linear model that predicts the X and B channels from the Y channel, as a
/// frame-wide base and a factor per 64x64 colour tile.
/// </summary>
/// <remarks>
/// The decoder's base B correlation defaults to <c>kYToBRatio</c> (1) whatever the colour transform:
/// libjxl's <c>InitializePassesSharedState</c> calls <c>ColorCorrelationMap::Create</c> with its default
/// <c>XYB = true</c>. An encoder of a frame without XYB writes a base of 0 explicitly.
/// </remarks>
internal sealed class JxlColorCorrelation
{
    /// <summary>The side of a colour tile, in pixels.</summary>
    public const int ColorTileDim = 64;

    /// <summary>The side of a colour tile, in 8x8 blocks.</summary>
    public const int ColorTileDimInBlocks = ColorTileDim / 8;

    /// <summary>The default denominator of the per-tile factors.</summary>
    public const int DefaultColorFactor = 84;

    // kYToBRatio in lib/jxl/cms/opsin_params.h.
    private const float YToBRatio = 1.0f;

    private static readonly JxlU32Enc ColorFactorDist = new(Val(DefaultColorFactor), Val(256), BitsOffset(8, 2), BitsOffset(16, 258));

    private readonly float[] dcFactors = new float[4];
    private uint colorFactor = DefaultColorFactor;
    private float colorScale = 1.0f / DefaultColorFactor;
    private float baseCorrelationX;
    private float baseCorrelationB = YToBRatio;
    private int ytoxDc;
    private int ytobDc;

    /// <summary>Initializes a new instance of the <see cref="JxlColorCorrelation"/> class.</summary>
    /// <param name="xsize">The frame width, in pixels.</param>
    /// <param name="ysize">The frame height, in pixels.</param>
    public JxlColorCorrelation(long xsize, long ysize)
    {
        this.XSizeTiles = (int)JxlBits.DivCeil(xsize, ColorTileDim);
        this.YSizeTiles = (int)JxlBits.DivCeil(ysize, ColorTileDim);
        this.YtoXMap = new sbyte[this.XSizeTiles * this.YSizeTiles];
        this.YtoBMap = new sbyte[this.XSizeTiles * this.YSizeTiles];
        this.RecomputeDcFactors();
    }

    /// <summary>Gets the width of the tile maps.</summary>
    public int XSizeTiles { get; }

    /// <summary>Gets the height of the tile maps.</summary>
    public int YSizeTiles { get; }

    /// <summary>Gets the X factor of each colour tile, row by row.</summary>
    public sbyte[] YtoXMap { get; }

    /// <summary>Gets the B factor of each colour tile, row by row.</summary>
    public sbyte[] YtoBMap { get; }

    /// <summary>Gets the DC factors: X in entry 0 and B in entry 2.</summary>
    public ReadOnlySpan<float> DcFactors => this.dcFactors;

    /// <summary>Returns the ratio that predicts X from Y for a tile factor.</summary>
    /// <param name="xFactor">The tile factor.</param>
    /// <returns>The ratio.</returns>
    public float YtoXRatio(int xFactor) => this.baseCorrelationX + (xFactor * this.colorScale);

    /// <summary>Returns the ratio that predicts B from Y for a tile factor.</summary>
    /// <param name="bFactor">The tile factor.</param>
    /// <returns>The ratio.</returns>
    public float YtoBRatio(int bFactor) => this.baseCorrelationB + (bFactor * this.colorScale);

    /// <summary>Reads the frame-wide parameters from the DC global section.</summary>
    /// <param name="br">The reader.</param>
    public void DecodeDc(JxlBitReader br)
    {
        if (br.ReadBits(1) == 1)
        {
            // All default.
            return;
        }

        this.SetColorFactor(JxlFieldCoders.ReadU32(ColorFactorDist, br));
        this.baseCorrelationX = JxlFieldCoders.ReadF16OrThrow(br);
        if (MathF.Abs(this.baseCorrelationX) > 4.0f)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Base X correlation is out of range");
        }

        this.baseCorrelationB = JxlFieldCoders.ReadF16OrThrow(br);
        if (MathF.Abs(this.baseCorrelationB) > 4.0f)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Base B correlation is out of range");
        }

        this.ytoxDc = (int)br.ReadBits(8) + sbyte.MinValue;
        this.ytobDc = (int)br.ReadBits(8) + sbyte.MinValue;
        this.RecomputeDcFactors();
    }

    private void SetColorFactor(uint factor)
    {
        this.colorFactor = factor;
        this.colorScale = 1.0f / this.colorFactor;
        this.RecomputeDcFactors();
    }

    private void RecomputeDcFactors()
    {
        this.dcFactors[0] = this.YtoXRatio(this.ytoxDc);
        this.dcFactors[2] = this.YtoBRatio(this.ytobDc);
    }
}
