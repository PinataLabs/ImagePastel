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
    // libjxl's forward AFV basis, which cjxl's AFVDCT4x4 codes the corner with: row j is pixel j's share of
    // each of the 16 coefficients. From libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2,
    // k4x4AFVBasisTranspose in lib/jxl/enc_transforms-inl.h. Copyright (c) the JPEG XL Project Authors.
    // BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.
    private static readonly double[,] ForwardAfvBasis =
    {
        { 0.2500000000000000, 0.8769029297991420, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, -0.4105377591765233, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000 },
        { 0.2500000000000000, 0.2206518106944235, 0.0000000000000000, 0.0000000000000000, -0.7071067811865474, 0.6235485373547691, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000 },
        { 0.2500000000000000, -0.1014005039375376, 0.4067007583026075, -0.2125574805828875, 0.0000000000000000, -0.0643507165794627, -0.4517556589999482, -0.3046847507248690, 0.3017929516615495, 0.4082482904638627, 0.1747866975480809, -0.2110560104933578, -0.1426608480880726, -0.1381354035075859, -0.1743760259965107, 0.1135498731499434 },
        { 0.2500000000000000, -0.1014005039375375, 0.4444481661973445, 0.3085497062849767, 0.0000000000000000, -0.0643507165794627, 0.1585450355184006, 0.5112616136591823, 0.2579236279634118, 0.0000000000000000, 0.0812611176717539, 0.1856718091610980, -0.3416446842253372, 0.3302282550303788, 0.0702790691196284, -0.0741750459581035 },
        { 0.2500000000000000, 0.2206518106944236, 0.0000000000000000, 0.0000000000000000, 0.7071067811865476, 0.6235485373547694, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000 },
        { 0.2500000000000000, -0.1014005039375378, 0.0000000000000000, 0.4706702258572536, 0.0000000000000000, -0.0643507165794628, -0.0403851516082220, 0.0000000000000000, 0.1627234014286620, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.7367497537172237, 0.0875511500058708, -0.2921026642334881, 0.1940289303259434 },
        { 0.2500000000000000, -0.1014005039375377, 0.1957439937204294, -0.1621205195722993, 0.0000000000000000, -0.0643507165794628, 0.0074182263792424, -0.2904801297289980, 0.0952002265347504, 0.0000000000000000, -0.3675398009862027, 0.4921585901373873, 0.2462710772207515, -0.0794670660590957, 0.3623817333531167, -0.4351904965232280 },
        { 0.2500000000000000, -0.1014005039375376, 0.2929100136981264, 0.0000000000000000, 0.0000000000000000, -0.0643507165794627, 0.3935103426921017, -0.0657870154914280, 0.0000000000000000, -0.4082482904638628, -0.3078822139579090, -0.3852501370925192, -0.0857401903551931, -0.4613374887461511, 0.0000000000000000, 0.2191868483885747 },
        { 0.2500000000000000, -0.1014005039375376, -0.4067007583026072, -0.2125574805828705, 0.0000000000000000, -0.0643507165794627, -0.4517556589999464, 0.3046847507248840, 0.3017929516615503, -0.4082482904638635, -0.1747866975480813, 0.2110560104933581, -0.1426608480880734, -0.1381354035075829, -0.1743760259965108, 0.1135498731499426 },
        { 0.2500000000000000, -0.1014005039375377, -0.1957439937204287, -0.1621205195722833, 0.0000000000000000, -0.0643507165794628, 0.0074182263792444, 0.2904801297290076, 0.0952002265347505, 0.0000000000000000, 0.3675398009862011, -0.4921585901373891, 0.2462710772207514, -0.0794670660591026, 0.3623817333531165, -0.4351904965232251 },
        { 0.2500000000000000, -0.1014005039375375, 0.0000000000000000, -0.4706702258572528, 0.0000000000000000, -0.0643507165794627, 0.1107416575309343, 0.0000000000000000, -0.1627234014286617, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, 0.1488339922711357, 0.4972464710953509, 0.2921026642334879, 0.5550443808910661 },
        { 0.2500000000000000, -0.1014005039375377, 0.1137907446044809, -0.1464291867126764, 0.0000000000000000, -0.0643507165794628, 0.0829816309488205, -0.2388977352334460, -0.3531238544981630, -0.4082482904638630, 0.4826689115059883, 0.1741941265991622, -0.0476868035022925, 0.1253805944856366, -0.4326608024727445, -0.2546827712406646 },
        { 0.2500000000000000, -0.1014005039375377, -0.4444481661973438, 0.3085497062849487, 0.0000000000000000, -0.0643507165794628, 0.1585450355183970, -0.5112616136592012, 0.2579236279634129, 0.0000000000000000, -0.0812611176717504, -0.1856718091610990, -0.3416446842253373, 0.3302282550303805, 0.0702790691196282, -0.0741750459581023 },
        { 0.2500000000000000, -0.1014005039375376, -0.2929100136981264, 0.0000000000000000, 0.0000000000000000, -0.0643507165794627, 0.3935103426921022, 0.0657870154914254, 0.0000000000000000, 0.4082482904638634, 0.3078822139579031, 0.3852501370925211, -0.0857401903551927, -0.4613374887461554, 0.0000000000000000, 0.2191868483885728 },
        { 0.2500000000000000, -0.1014005039375376, -0.1137907446044814, -0.1464291867126654, 0.0000000000000000, -0.0643507165794627, 0.0829816309488214, 0.2388977352334547, -0.3531238544981624, 0.4082482904638630, -0.4826689115059858, -0.1741941265991621, -0.0476868035022928, 0.1253805944856431, -0.4326608024727457, -0.2546827712406641 },
        { 0.2500000000000000, -0.1014005039375374, 0.0000000000000000, 0.4251149611657548, 0.0000000000000000, -0.0643507165794626, -0.4517556589999480, 0.0000000000000000, -0.6035859033230976, 0.0000000000000000, 0.0000000000000000, 0.0000000000000000, -0.1426608480880724, -0.1381354035075845, 0.3487520519930227, 0.1135498731499429 },
    };

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

    // Identity holds the pixels of four 4x4 squares, interleaved like the coefficients of DCT4X4: square
    // (qx, qy) takes slot (i, j) from coefficient row qy + 2i and column qx + 2j. Its mean is a 2x2
    // Hadamard of coefficients 0, 1, 8 and 9, and its other slots hold residuals about the pixel at (1, 1),
    // which is the mean less a sixteenth of the residuals. Each pixel is that pixel plus its own residual,
    // but slot (1, 1)'s residual belongs to pixel (0, 0), whose slot holds the mean. Measured on x64: the
    // largest error relative to the largest pixel is 1.1e-7.
    [Fact]
    public void TransformToPixels_Identity_MatchesTheDoublePrecisionDefinition()
    {
        double[] coefficients = RandomCoefficients(64, 1);
        double[] expected = new double[64];
        for (int qy = 0; qy < 2; qy++)
        {
            for (int qx = 0; qx < 2; qx++)
            {
                double residuals = 0;
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        residuals += i == 0 && j == 0 ? 0 : IdentitySlot(coefficients, qx, qy, i, j);
                    }
                }

                double centre = IdentityMean(coefficients, qx, qy) - (residuals / 16);
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        double residual = (i, j) switch
                        {
                            (0, 0) => IdentitySlot(coefficients, qx, qy, 1, 1),
                            (1, 1) => 0,
                            _ => IdentitySlot(coefficients, qx, qy, i, j),
                        };
                        expected[((((qy * 4) + i) * 8) + (qx * 4)) + j] = centre + residual;
                    }
                }
            }
        }

        AssertClose(expected, Transform(JxlAcStrategyType.Identity, coefficients, 8, 8), 1e-6);
    }

    // Each square of an Identity block has the mean that its Hadamard input gives it.
    [Fact]
    public void TransformToPixels_Identity_EachSquareHasItsMean()
    {
        double[] coefficients = RandomCoefficients(64, 11);
        float[] pixels = Transform(JxlAcStrategyType.Identity, coefficients, 8, 8);
        for (int qy = 0; qy < 2; qy++)
        {
            for (int qx = 0; qx < 2; qx++)
            {
                double sum = 0;
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        sum += pixels[((((qy * 4) + y) * 8) + (qx * 4)) + x];
                    }
                }

                Assert.Equal(IdentityMean(coefficients, qx, qy), sum / 16, 1e-6);
            }
        }
    }

    // The 4x4 corner of an AFV block has a basis of its own: 16 patterns that must be orthonormal, the
    // first a constant 1/4. Pattern j is the corner of an AFV0 block whose only input is coefficient
    // (2 (j / 4), 2 (j % 4)); pattern 0 comes from coefficients 0, 1 and 8, mixed to give the corner a
    // DC of 1 and the two DCT parts none. Measured on x64: the largest error of a dot product is 8.2e-8.
    [Fact]
    public void TransformToPixels_AfvCorner_IsAnOrthonormalBasisWithAConstantFirstPattern()
    {
        double[][] basis = AfvCornerBasis();

        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(0.25, basis[0][i], 1e-7);
        }

        double worst = 0;
        for (int j = 0; j < 16; j++)
        {
            for (int k = 0; k < 16; k++)
            {
                double dot = 0;
                for (int i = 0; i < 16; i++)
                {
                    dot += basis[j][i] * basis[k][i];
                }

                worst = Math.Max(worst, Math.Abs(dot - (j == k ? 1 : 0)));
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Largest error {worst:G3}");
        Assert.True(worst < 1e-6, $"Largest error {worst:G4}");
    }

    // The decoder's corner basis must be the transpose of the encoder's, so that it inverts what cjxl
    // codes: pattern i, read from the decoder, has at pixel j the share of coefficient i that pixel j
    // has in libjxl's forward basis. A permuted, mirrored or sign-flipped pattern fails. The decoder's
    // table and the encoder's are separate in libjxl and differ in the last digits of a few entries.
    // Measured on x64: the largest error is 3.0e-8.
    [Fact]
    public void TransformToPixels_AfvCorner_IsTheTransposeOfLibjxlsForwardBasis()
    {
        double[][] basis = AfvCornerBasis();

        double worst = 0;
        for (int i = 0; i < 16; i++)
        {
            for (int j = 0; j < 16; j++)
            {
                worst = Math.Max(worst, Math.Abs(basis[i][j] - ForwardAfvBasis[j, i]));
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Largest error {worst:G3}");
        Assert.True(worst < 1e-6, $"Largest error {worst:G4}");
    }

    // An AFV block is three parts. The 4x4 corner at (4 (kind % 2), 4 (kind / 2)) takes the even rows and
    // even columns of the coefficients in a basis of its own, mirrored in x for kinds 1 and 3 and in y for
    // kinds 2 and 3. The 4x4 square beside it takes the even rows and odd columns as a DCT. The other four
    // rows take the odd rows as an 8-wide, 4-tall DCT. Their DCs are 4 (c0 + c1 + c8), c0 - c1 + c8 and
    // c0 - c8. Measured on x64: the largest error relative to the largest pixel is 2.0e-7.
    [Theory]
    [InlineData((int)JxlAcStrategyType.Afv0)]
    [InlineData((int)JxlAcStrategyType.Afv1)]
    [InlineData((int)JxlAcStrategyType.Afv2)]
    [InlineData((int)JxlAcStrategyType.Afv3)]
    public void TransformToPixels_Afv_MatchesItsThreeDoublePrecisionParts(int rawStrategy)
    {
        int kind = rawStrategy - (int)JxlAcStrategyType.Afv0;
        int afvX = kind & 1;
        int afvY = kind / 2;
        double[] coefficients = RandomCoefficients(64, rawStrategy);
        double c0 = coefficients[0];
        double c1 = coefficients[1];
        double c8 = coefficients[8];
        double[] expected = new double[64];

        // The corner, in the transpose of libjxl's forward basis: pattern j has at pixel i the share of
        // coefficient j that pixel i has.
        double[] corner = new double[16];
        for (int j = 0; j < 16; j++)
        {
            double c = j == 0 ? 4 * (c0 + c1 + c8) : coefficients[(2 * (j / 4) * 8) + (2 * (j % 4))];
            for (int i = 0; i < 16; i++)
            {
                corner[i] += c * ForwardAfvBasis[i, j];
            }
        }

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int cy = afvY == 1 ? 3 - y : y;
                int cx = afvX == 1 ? 3 - x : x;
                expected[(((afvY * 4) + y) * 8) + (afvX * 4) + x] = corner[(cy * 4) + cx];
            }
        }

        // The square beside it.
        double[] square = new double[16];
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                square[(i * 4) + j] = coefficients[(2 * i * 8) + (2 * j) + 1];
            }
        }

        square[0] = c0 - c1 + c8;
        double[] squarePixels = InverseDct(square, 4, 4);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                expected[(((afvY * 4) + y) * 8) + (afvX == 1 ? 0 : 4) + x] = squarePixels[(y * 4) + x];
            }
        }

        // The other four rows.
        double[] rows = new double[32];
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                rows[(i * 8) + j] = coefficients[(((2 * i) + 1) * 8) + j];
            }
        }

        rows[0] = c0 - c8;
        Array.Copy(InverseDct(rows, 8, 4), 0, expected, (afvY == 1 ? 0 : 4) * 8, 32);

        AssertClose(expected, Transform((JxlAcStrategyType)rawStrategy, coefficients, 8, 8), 1e-6);
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

        JxlDct.LowestFrequenciesFromDc(acs, dc, cx, llf, new float[JxlDct.ScratchSize]);

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

    // Slot (i, j) of square (qx, qy) of an Identity block.
    private static double IdentitySlot(double[] coefficients, int qx, int qy, int i, int j)
        => coefficients[((qy + (2 * i)) * 8) + qx + (2 * j)];

    // The mean of square (qx, qy) of an Identity block: a 2x2 Hadamard of coefficients 0, 1, 8 and 9, in
    // which coefficient 1 changes sign along y and coefficient 8 along x.
    private static double IdentityMean(double[] coefficients, int qx, int qy)
        => coefficients[0] + ((qy == 0 ? 1 : -1) * coefficients[1]) + ((qx == 0 ? 1 : -1) * coefficients[8]) + ((qx == qy ? 1 : -1) * coefficients[9]);

    // The 16 patterns of the AFV corner, each as 16 pixels, read from AFV0 blocks with one input each.
    private static double[][] AfvCornerBasis()
    {
        double[][] basis = new double[16][];
        for (int j = 0; j < 16; j++)
        {
            double[] coefficients = new double[64];
            if (j == 0)
            {
                coefficients[0] = 1.0 / 16;
                coefficients[1] = 1.0 / 8;
                coefficients[8] = 1.0 / 16;
            }
            else
            {
                coefficients[(2 * (j / 4) * 8) + (2 * (j % 4))] = 1;
            }

            float[] pixels = Transform(JxlAcStrategyType.Afv0, coefficients, 8, 8);
            basis[j] = new double[16];
            for (int i = 0; i < 16; i++)
            {
                basis[j][i] = pixels[((i / 4) * 8) + (i % 4)];
            }

            // Only the corner is set.
            for (int i = 0; i < 64; i++)
            {
                if (i / 8 >= 4 || i % 8 >= 4)
                {
                    Assert.Equal(0, pixels[i], 1e-7);
                }
            }
        }

        return basis;
    }

    private static float[] Transform(JxlAcStrategyType type, double[] coefficients, int width, int height)
    {
        float[] input = Array.ConvertAll(coefficients, v => (float)v);

        // A stride wider than the block, to show the transform keeps to its columns.
        int stride = width + 3;
        float[] pixels = new float[stride * height];
        JxlDct.TransformToPixels(JxlAcStrategy.FromRawStrategy((int)type), input, pixels, stride, new float[JxlDct.ScratchSize]);
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
