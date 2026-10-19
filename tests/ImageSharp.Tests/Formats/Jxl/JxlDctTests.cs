// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The inverse transforms of every DCT strategy, against a plain double-precision inverse DCT, and the
/// lowest frequencies a block takes from the DC image.
/// </summary>
/// <remarks>
/// The reference is the unscaled DCT-III of JPEG XL: a coefficient at frequency (u, v) of an N x M block
/// adds c s(u) s(v) cos((2x + 1) u pi / 2N) cos((2y + 1) v pi / 2M) to pixel (x, y), with s(0) = 1 and
/// s(k) = sqrt(2). The coefficients of a block lie in a grid of min(N, M) rows by max(N, M) columns;
/// the row is the vertical frequency of a wide block, and the horizontal one of a tall or square block
/// ("JPEG XL is transposed, JPEG is not"). The blocks smaller than 8x8 interleave the coefficients of
/// their sub-blocks and code the sub-blocks' DCs as a 2-point DCT in the top-left corner.
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlDctTests
{
    // Each DCT strategy of one 8x8 block or more, as a raw strategy (the type is internal).
    public static TheoryData<int> DctStrategies => new()
    {
        (int)JxlAcStrategyType.Dct,
        (int)JxlAcStrategyType.Dct16X16,
        (int)JxlAcStrategyType.Dct32X32,
        (int)JxlAcStrategyType.Dct16X8,
        (int)JxlAcStrategyType.Dct8X16,
        (int)JxlAcStrategyType.Dct32X8,
        (int)JxlAcStrategyType.Dct8X32,
        (int)JxlAcStrategyType.Dct32X16,
        (int)JxlAcStrategyType.Dct16X32,
        (int)JxlAcStrategyType.Dct64X64,
        (int)JxlAcStrategyType.Dct64X32,
        (int)JxlAcStrategyType.Dct32X64,
        (int)JxlAcStrategyType.Dct128X128,
        (int)JxlAcStrategyType.Dct128X64,
        (int)JxlAcStrategyType.Dct64X128,
        (int)JxlAcStrategyType.Dct256X256,
        (int)JxlAcStrategyType.Dct256X128,
        (int)JxlAcStrategyType.Dct128X256,
    };

    public static TheoryData<int> AllDctStrategies
    {
        get
        {
            TheoryData<int> data = new()
            {
                (int)JxlAcStrategyType.Dct2X2,
                (int)JxlAcStrategyType.Dct4X4,
                (int)JxlAcStrategyType.Dct4X8,
                (int)JxlAcStrategyType.Dct8X4,
            };
            foreach (int type in DctStrategies)
            {
                data.Add(type);
            }

            return data;
        }
    }

    // Float error: the fast transform runs in float, and its error grows with the block. Measured on x64,
    // the largest error relative to the largest pixel is 9.3e-8 at 8x8, 6.8e-7 at 32x32, 1.3e-6 at 64x64,
    // 1.8e-6 at 128x128 and 5.8e-6 at 256x256. The bound is 1e-5.
    [Theory]
    [MemberData(nameof(DctStrategies))]
    public void TransformToPixels_Dct_MatchesTheDoublePrecisionInverseDct(int rawStrategy)
    {
        JxlAcStrategyType type = (JxlAcStrategyType)rawStrategy;
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(rawStrategy);
        int height = acs.CoveredBlocksY * 8;
        int width = acs.CoveredBlocksX * 8;
        double[] coefficients = RandomCoefficients(width * height, rawStrategy);

        double[] expected = InverseDct(coefficients, width, height);
        float[] actual = Transform(type, coefficients, width, height);

        AssertClose(expected, actual, 1e-5);
    }

    // DCT8X4 is two 4-wide, 8-tall DCTs side by side, and DCT4X8 two 8-wide, 4-tall DCTs one above the
    // other. Sub-block i takes coefficient rows i, i + 2, i + 4 and i + 6, and its DC is c(0) + c(8)
    // for the first and c(0) - c(8) for the second.
    [Theory]
    [InlineData((int)JxlAcStrategyType.Dct8X4)]
    [InlineData((int)JxlAcStrategyType.Dct4X8)]
    public void TransformToPixels_TwoHalves_MatchesTwoDoublePrecisionInverseDcts(int rawStrategy)
    {
        JxlAcStrategyType type = (JxlAcStrategyType)rawStrategy;
        bool sideBySide = type == JxlAcStrategyType.Dct8X4;
        double[] coefficients = RandomCoefficients(64, rawStrategy);
        double[] expected = new double[64];
        for (int half = 0; half < 2; half++)
        {
            // The 32 coefficients of the half as a 4-row, 8-column grid.
            double[] sub = new double[32];
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    sub[(r * 8) + c] = coefficients[(((2 * r) + half) * 8) + c];
                }
            }

            sub[0] = half == 0 ? coefficients[0] + coefficients[8] : coefficients[0] - coefficients[8];
            double[] pixels = sideBySide ? InverseDct(sub, 4, 8) : InverseDct(sub, 8, 4);
            int w = sideBySide ? 4 : 8;
            for (int y = 0; y < 32 / w; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int px = sideBySide ? (half * 4) + x : x;
                    int py = sideBySide ? y : (half * 4) + y;
                    expected[(py * 8) + px] = pixels[(y * w) + x];
                }
            }
        }

        AssertClose(expected, Transform(type, coefficients, 8, 8), 1e-6);
    }

    // DCT4X4 is four 4x4 DCTs. Sub-block (x, y) takes the coefficients at rows y + 2i and columns x + 2j,
    // and the four DCs are a 2x2 DCT of coefficients 0, 1, 8 and 9.
    [Fact]
    public void TransformToPixels_Dct4X4_MatchesFourDoublePrecisionInverseDcts()
    {
        double[] coefficients = RandomCoefficients(64, 3);
        double[] dcs = InverseDct(new[] { coefficients[0], coefficients[1], coefficients[8], coefficients[9] }, 2, 2);
        double[] expected = new double[64];
        for (int by = 0; by < 2; by++)
        {
            for (int bx = 0; bx < 2; bx++)
            {
                double[] sub = new double[16];
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        sub[(i * 4) + j] = coefficients[((by + (2 * i)) * 8) + bx + (2 * j)];
                    }
                }

                sub[0] = dcs[(by * 2) + bx];
                double[] pixels = InverseDct(sub, 4, 4);
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        expected[(((by * 4) + y) * 8) + (bx * 4) + x] = pixels[(y * 4) + x];
                    }
                }
            }
        }

        AssertClose(expected, Transform(JxlAcStrategyType.Dct4X4, coefficients, 8, 8), 1e-6);
    }

    // DCT2X2 is a pyramid of 2x2 DCTs: the top-left 2x2 coefficients give the DCs of four 4x4 squares,
    // those and the next coefficients give the DCs of sixteen 2x2 squares, and so on to the pixels. At
    // size s, square (x, y) takes the coefficients (x, y), (x + s, y), (x, y + s) and (x + s, y + s).
    [Fact]
    public void TransformToPixels_Dct2X2_MatchesAPyramidOfDoublePrecisionInverseDcts()
    {
        double[] coefficients = RandomCoefficients(64, 2);
        double[] current = (double[])coefficients.Clone();
        for (int s = 1; s < 8; s *= 2)
        {
            double[] next = (double[])current.Clone();
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    double[] square = InverseDct(new[] { current[(y * 8) + x], current[(y * 8) + x + s], current[((y + s) * 8) + x], current[((y + s) * 8) + x + s] }, 2, 2);
                    next[(2 * y * 8) + (2 * x)] = square[0];
                    next[(2 * y * 8) + (2 * x) + 1] = square[1];
                    next[(((2 * y) + 1) * 8) + (2 * x)] = square[2];
                    next[(((2 * y) + 1) * 8) + (2 * x) + 1] = square[3];
                }
            }

            current = next;
        }

        AssertClose(current, Transform(JxlAcStrategyType.Dct2X2, coefficients, 8, 8), 1e-6);
    }

    // The lowest frequencies that a block takes from the DC image must give each 8x8 block of its pixels
    // the mean that the DC image holds for it, when no other coefficient is set. The DC values are in
    // [-1, 1]. Measured on x64: the largest error is 0 for one 8x8 block, 1.1e-7 up to 32x32, 2.3e-7 at
    // 64x64, 5.2e-7 at 128x128 and 1.6e-6 at 256x256. The bound is 4e-6.
    [Theory]
    [MemberData(nameof(AllDctStrategies))]
    public void LowestFrequenciesFromDc_GivesEachBlockItsDc(int rawStrategy)
    {
        JxlAcStrategyType type = (JxlAcStrategyType)rawStrategy;
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(rawStrategy);
        int cx = acs.CoveredBlocksX;
        int cy = acs.CoveredBlocksY;
        double[] dcDouble = RandomCoefficients(cx * cy, 100 + rawStrategy);
        float[] dc = Array.ConvertAll(dcDouble, v => (float)v);
        float[] llf = new float[cx * cy * 64];

        JxlDct.LowestFrequenciesFromDc(type, dc, cx, llf, new float[JxlDct.ScratchSize]);

        // Every other coefficient is zero; the pixels come from the double-precision reference.
        double[] pixels;
        if (cx * cy == 1)
        {
            float[] block = new float[64];
            block[0] = llf[0];
            pixels = Array.ConvertAll(Transform(type, Array.ConvertAll(block, v => (double)v), 8, 8), v => (double)v);
        }
        else
        {
            pixels = InverseDct(Array.ConvertAll(llf, v => (double)v), cx * 8, cy * 8);
        }

        double worst = 0;
        for (int by = 0; by < cy; by++)
        {
            for (int bx = 0; bx < cx; bx++)
            {
                double sum = 0;
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        sum += pixels[((((by * 8) + y) * cx * 8) + (bx * 8)) + x];
                    }
                }

                worst = Math.Max(worst, Math.Abs((sum / 64) - dc[(by * cx) + bx]));
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Largest error {worst:G3}");
        Assert.True(worst < 4e-6, $"Largest error {worst:G4}");
    }

    private static float[] Transform(JxlAcStrategyType type, double[] coefficients, int width, int height)
    {
        float[] input = Array.ConvertAll(coefficients, v => (float)v);

        // A stride wider than the block, to show the transform keeps to its columns.
        int stride = width + 3;
        float[] pixels = new float[stride * height];
        JxlDct.TransformToPixels(type, input, pixels, stride, new float[JxlDct.ScratchSize]);
        float[] result = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            Array.Copy(pixels, y * stride, result, y * width, width);
        }

        return result;
    }

    // The coefficients are random in [-1, 1].
    private static double[] RandomCoefficients(int count, int seed)
    {
        Random random = new(seed);
        double[] result = new double[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = (float)((random.NextDouble() * 2) - 1);
        }

        return result;
    }

    // The 2D inverse DCT of a width x height block whose coefficients lie in a min x max grid, in double
    // precision, one dimension after the other.
    private static double[] InverseDct(double[] coefficients, int width, int height)
    {
        bool wide = width > height;
        int columns = Math.Max(width, height);

        // c(u, v) for horizontal frequency u and vertical frequency v.
        double Coefficient(int u, int v) => wide ? coefficients[(v * columns) + u] : coefficients[(u * columns) + v];

        // Along y first, into tmp(u, y); then along x.
        double[] tmp = new double[width * height];
        for (int u = 0; u < width; u++)
        {
            for (int y = 0; y < height; y++)
            {
                double sum = 0;
                for (int v = 0; v < height; v++)
                {
                    sum += Coefficient(u, v) * Basis(v, y, height);
                }

                tmp[(y * width) + u] = sum;
            }
        }

        double[] pixels = new double[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double sum = 0;
                for (int u = 0; u < width; u++)
                {
                    sum += tmp[(y * width) + u] * Basis(u, x, width);
                }

                pixels[(y * width) + x] = sum;
            }
        }

        return pixels;
    }

    private static double Basis(int k, int x, int n) => (k == 0 ? 1 : Math.Sqrt(2)) * Math.Cos((((2 * x) + 1) * k * Math.PI) / (2 * n));

    // Checks each pixel against the reference, relative to the largest reference pixel.
    private static void AssertClose(double[] expected, float[] actual, double relative)
    {
        double scale = 0;
        double worst = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            scale = Math.Max(scale, Math.Abs(expected[i]));
            worst = Math.Max(worst, Math.Abs(expected[i] - actual[i]));
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Largest error {worst:G3}, relative {worst / scale:G3}");
        Assert.True(worst <= relative * scale, $"Largest error {worst:G4}, relative {worst / scale:G4}");
    }
}
