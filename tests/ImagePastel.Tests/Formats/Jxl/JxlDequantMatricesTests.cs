// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Linq;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.VarDct;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The DC quantization factors of a frame, which <see cref="JxlDequantMatrices.DecodeDc"/> reads from
/// the DC global section and keeps for the Modular XYB conversion and the VarDCT DC; and the library AC
/// tables that no generated file uses.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlDequantMatricesTests
{
    [Fact]
    public void New_HasLibjxlsDefaultDcQuant()
    {
        JxlDequantMatrices matrices = new();

        AssertDcQuant(matrices, new[] { 1f / 4096, 1f / 512, 1f / 256 }, new[] { 4096f, 512f, 256f });
    }

    [Fact]
    public void DecodeDc_AllDefault_KeepsTheDefaults()
    {
        JxlDequantMatrices matrices = new();

        matrices.DecodeDc(new JxlTestBitWriter().Bit(true).ZeroPadToByte().ToReader());

        AssertDcQuant(matrices, new[] { 1f / 4096, 1f / 512, 1f / 256 }, new[] { 4096f, 512f, 256f });
    }

    // Each factor is an F16 divided by 128: 1, 2 and 4 give 1/128, 1/64 and 1/32.
    [Fact]
    public void DecodeDc_Custom_KeepsEachFactorOver128()
    {
        JxlDequantMatrices matrices = new();

        matrices.DecodeDc(new JxlTestBitWriter().Bit(false).Write(16, 0x3C00).Write(16, 0x4000).Write(16, 0x4400).ZeroPadToByte().ToReader());

        AssertDcQuant(matrices, new[] { 1f / 128, 1f / 64, 1f / 32 }, new[] { 128f, 64f, 32f });
    }

    [Fact]
    public void DecodeDc_ZeroFactor_Throws()
    {
        JxlDequantMatrices matrices = new();

        Assert.Throws<InvalidImageContentException>(() => matrices.DecodeDc(new JxlTestBitWriter().Bit(false).Write(16, 0x3C00).Write(16, 0).Write(16, 0x3C00).ZeroPadToByte().ToReader()));
    }

    // The library tables that no djxl comparison reaches: cjxl v0.12.0 never writes DCT32X8, DCT8X32 or
    // the 128 and 256 sizes, and none of the generated files has a DCT4X4 block. Each table is checked
    // against its definition, computed here in double precision from libjxl's parameters
    // (DequantMatricesLibraryDef in lib/jxl/quant_weights.cc at ef67fde2). The decoder's FastPowf
    // approximates pow; measured on x64, the largest relative error is 2.3e-6.
    [Theory]
    [InlineData((int)JxlAcStrategyType.Dct4X4)]
    [InlineData((int)JxlAcStrategyType.Dct32X8)]
    [InlineData((int)JxlAcStrategyType.Dct8X32)]
    [InlineData((int)JxlAcStrategyType.Dct128X128)]
    [InlineData((int)JxlAcStrategyType.Dct128X64)]
    [InlineData((int)JxlAcStrategyType.Dct64X128)]
    [InlineData((int)JxlAcStrategyType.Dct256X256)]
    [InlineData((int)JxlAcStrategyType.Dct256X128)]
    [InlineData((int)JxlAcStrategyType.Dct128X256)]
    public void Matrix_LibraryTable_MatchesItsDefinition(int rawStrategy)
    {
        JxlAcStrategyType strategy = (JxlAcStrategyType)rawStrategy;
        JxlDequantMatrices matrices = new();
        matrices.EnsureComputed(1u << rawStrategy);

        double worst = 0;
        for (int c = 0; c < 3; c++)
        {
            double[] expected = LibraryWeights(strategy, c);
            ReadOnlySpan<float> actual = matrices.Matrix(strategy, c);
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                // The matrix holds the reciprocals of the weights.
                double dequant = 1 / expected[i];
                worst = Math.Max(worst, Math.Abs(actual[i] - dequant) / dequant);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Largest relative error {worst:G3}");
        Assert.True(worst < 1e-5, $"Largest relative error {worst:G4}");
    }

    // The weights of channel c of a library table, as min(N, M) rows of max(N, M) coefficients.
    private static double[] LibraryWeights(JxlAcStrategyType strategy, int c)
    {
        double[][] large1 =
        {
            new[] { 26629.073922049845, -1.025, -0.78, -0.65012, -0.19041574084286472, -0.20819395464, -0.421064, -0.32733845535848671 },
            new[] { 9311.3238710010046, -0.3041958212306401, -0.3633036457487539, -0.35660379990111464, -0.3443074455424403, -0.33699592683512467, -0.30180866526242109, -0.27321683125358037 },
            new[] { 4992.2486445538634, -1.2, -1.2, -0.8, -0.7, -0.7, -0.4, -0.5 },
        };
        double[][] large2 =
        {
            new[] { 23629.073922049845, -1.025, -0.78, -0.65012, -0.19041574084286472, -0.20819395464, -0.421064, -0.32733845535848671 },
            new[] { 8611.3238710010046, -0.3041958212306401, -0.3633036457487539, -0.35660379990111464, -0.3443074455424403, -0.33699592683512467, -0.30180866526242109, -0.27321683125358037 },
            new[] { 4492.2486445538634, -1.2, -1.2, -0.8, -0.7, -0.7, -0.4, -0.5 },
        };

        // The first band of the large tables is a scale times the seed above.
        static double[] Scaled(double[] bands, double scale) => bands.Select((v, i) => i == 0 ? scale * v : v).ToArray();

        switch (strategy)
        {
            case JxlAcStrategyType.Dct4X4:
            {
                // Four interleaved 4x4 DCTs: coefficient (x, y) is frequency (x / 2, y / 2) of a 4x4 table.
                // The multipliers of (1, 0), (0, 1) and (1, 1) are all 1.
                double[][] bands =
                {
                    new[] { 2200.0, 0.0, 0.0, 0.0 },
                    new[] { 392.0, 0.0, 0.0, 0.0 },
                    new[] { 112.0, -0.25, -0.25, -0.5 },
                };
                double[] weights4X4 = DctWeights(bands[c], 4, 4);
                double[] weights = new double[64];
                for (int i = 0; i < 64; i++)
                {
                    int x = i % 8;
                    int y = i / 8;
                    weights[i] = weights4X4[((y / 2) * 4) + (x / 2)];
                }

                return weights;
            }

            case JxlAcStrategyType.Dct32X8:
            case JxlAcStrategyType.Dct8X32:
            {
                double[][] bands =
                {
                    new[] { 16283.2494710648897, -1.7812845336559429, -1.6309059012653515, -1.0382179034313539, -0.85, -0.7, -0.9, -1.2360638576849587 },
                    new[] { 5089.15750884921511936, -0.320049391452786891, -0.35362849922161446, -0.30340000000000003, -0.61, -0.5, -0.5, -0.6 },
                    new[] { 3397.77603275308720128, -0.321327362693153371, -0.34507619223117997, -0.70340000000000003, -0.9, -1.0, -1.0, -1.1754605576265209 },
                };
                return DctWeights(bands[c], 8, 32);
            }

            case JxlAcStrategyType.Dct128X128:
                return DctWeights(Scaled(large1[c], 1.8), 128, 128);

            case JxlAcStrategyType.Dct128X64:
            case JxlAcStrategyType.Dct64X128:
                return DctWeights(Scaled(large2[c], 1.3), 64, 128);

            case JxlAcStrategyType.Dct256X256:
                return DctWeights(Scaled(large1[c], 3.6), 256, 256);

            default:
                Assert.True(strategy is JxlAcStrategyType.Dct256X128 or JxlAcStrategyType.Dct128X256);
                return DctWeights(Scaled(large2[c], 2.6), 128, 256);
        }
    }

    // The weights of a parametric DCT table: band 0 is the first parameter, and each next band is the one
    // before times 1 + p for a positive parameter p, or divided by 1 - p otherwise. A coefficient's weight
    // interpolates the bands geometrically at its distance from the DC, scaled so that the far corner
    // falls on the last band: sqrt(2) + 1e-6 is that distance in libjxl, a little past the corner.
    private static double[] DctWeights(double[] parameters, int rows, int cols)
    {
        double[] bands = new double[parameters.Length];
        bands[0] = parameters[0];
        for (int i = 1; i < bands.Length; i++)
        {
            bands[i] = bands[i - 1] * (parameters[i] > 0 ? 1 + parameters[i] : 1 / (1 - parameters[i]));
        }

        double scale = (bands.Length - 1) / (Math.Sqrt(2) + 1e-6);
        double[] weights = new double[rows * cols];
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                double dx = x * scale / (cols - 1);
                double dy = y * scale / (rows - 1);
                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                int band = (int)distance;
                weights[(y * cols) + x] = bands[band] * Math.Pow(bands[band + 1] / bands[band], distance - band);
            }
        }

        return weights;
    }

    private static void AssertDcQuant(JxlDequantMatrices matrices, float[] dcQuant, float[] invDcQuant)
    {
        for (int c = 0; c < 3; c++)
        {
            Assert.Equal(dcQuant[c], matrices.DcQuant(c));
            Assert.Equal(invDcQuant[c], matrices.InvDcQuant(c));
        }
    }
}
