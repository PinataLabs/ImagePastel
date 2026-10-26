// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.VarDct;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Smoke tests of the VarDCT building blocks that the generated files cannot single out: the DCT8
/// natural order, the Lehmer code, the 8x8 IDCT and two of libjxl's header bounds.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlVarDctComponentsTests
{
    [Fact]
    public void ComputeNaturalCoeffOrder_Dct8_IsTheJpegZigZag()
    {
        // The JPEG zig-zag order (ITU T.81, figure A.6), as row-major positions.
        uint[] zigZag =
        {
            0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
            12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
        };
        uint[] order = new uint[64];

        JxlAcStrategy.FromRawStrategy((int)JxlAcStrategyType.Dct).ComputeNaturalCoeffOrder(order);

        Assert.Equal(zigZag, order);
    }

    // A Lehmer code counts, for each entry, the unused values below it: the permutation 2, 0, 3, 1 has
    // the code 2, 0, 1, 0.
    [Fact]
    public void DecodeLehmerCode_GivesThePermutation()
    {
        uint[] permutation = new uint[4];

        JxlCoeffOrder.DecodeLehmerCode(new uint[] { 2, 0, 1, 0 }, 4, permutation);

        Assert.Equal(new uint[] { 2, 0, 3, 1 }, permutation);
    }

    [Fact]
    public void DecodeLehmerCode_EntryPastTheEnd_Throws()
        => Assert.Throws<InvalidImageContentException>(() => JxlCoeffOrder.DecodeLehmerCode(new uint[] { 0, 3, 0 }, 3, new uint[3]));

    // The unscaled DCT-III: pixel (x, y) = sum over (u, v) of c(u, v) s(u) s(v) cos((2x + 1) u pi / 16)
    // cos((2y + 1) v pi / 16), with s(0) = 1 and s(u) = sqrt(2). JPEG XL stores the coefficients
    // transposed (libjxl: "JPEG XL is transposed, JPEG is not"), so c(u, v) is coefficient u * 8 + v.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 3)]
    [InlineData(5, 7)]
    [InlineData(7, 7)]
    public void TransformToPixelsDct8_OneCoefficient_GivesItsCosineBasis(int u, int v)
    {
        float[] coefficients = new float[64];
        coefficients[(u * 8) + v] = 0.5f;
        float[] pixels = new float[64];

        JxlDct.TransformToPixels(JxlAcStrategy.FromRawStrategy((int)JxlAcStrategyType.Dct), coefficients, pixels, 8, new float[JxlDct.ScratchSize]);

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                double expected = 0.5 * Basis(u, x) * Basis(v, y);
                Assert.True(Math.Abs(expected - pixels[(y * 8) + x]) < 1e-5, $"({x}, {y}): expected {expected}, got {pixels[(y * 8) + x]}");
            }
        }
    }

    [Fact]
    public void ColorCorrelationDecodeDc_BaseOutOfRange_Throws()
    {
        // Not default, the default colour factor (selector 0), then a base X correlation of 5 (F16 0x4500).
        JxlColorCorrelation cmap = new(64, 64);

        Assert.Throws<InvalidImageContentException>(() => cmap.DecodeDc(new JxlTestBitWriter().Bit(false).Write(2, 0).Write(16, 0x4500).Write(16, 0).Write(16, 0).ZeroPadToByte().ToReader()));
    }

    [Fact]
    public void BlockCtxMapDecode_MoreThan64Buckets_Throws()
    {
        // Not default; 15 X thresholds and 15 Y thresholds, each 0 (selector 0, 4 bits): 16 x 16 DC
        // buckets. No B and no quant field thresholds.
        JxlTestBitWriter w = new JxlTestBitWriter().Bit(false);
        for (int j = 0; j < 2; j++)
        {
            w.Write(4, 15);
            for (int i = 0; i < 15; i++)
            {
                w.Write(2, 0).Write(4, 0);
            }
        }

        w.Write(4, 0).Write(4, 0);
        JxlBlockCtxMap map = new();

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => map.Decode(w.ZeroPadToByte().ToReader()));
        Assert.Contains("too big", ex.Message, StringComparison.Ordinal);
    }

    private static double Basis(int u, int x) => (u == 0 ? 1 : Math.Sqrt(2)) * Math.Cos((((2 * x) + 1) * u * Math.PI) / 16);
}
