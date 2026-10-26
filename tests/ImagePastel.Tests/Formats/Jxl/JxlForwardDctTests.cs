// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using PinataLabs.ImagePastel.Formats.Jxl.VarDct;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The encoder's forward transforms of the AC strategies that the Default (Hare) and Thorough (Squirrel)
/// searches choose, and the DC each block gives the DC image: each inverts the decoder's transform, and
/// each gives libjxl's values.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlForwardDctTests
{
    // The strategies of libjxl's Hare search (enc_ac_strategy.cc): the four 8x8 kinds and the aligned merges.
    public static TheoryData<int> HareStrategies => new()
    {
        (int)JxlAcStrategyType.Dct,
        (int)JxlAcStrategyType.Identity,
        (int)JxlAcStrategyType.Dct2X2,
        (int)JxlAcStrategyType.Dct4X4,
        (int)JxlAcStrategyType.Dct16X16,
        (int)JxlAcStrategyType.Dct32X32,
        (int)JxlAcStrategyType.Dct16X8,
        (int)JxlAcStrategyType.Dct8X16,
        (int)JxlAcStrategyType.Dct32X16,
        (int)JxlAcStrategyType.Dct16X32,
        (int)JxlAcStrategyType.Dct64X64,
        (int)JxlAcStrategyType.Dct64X32,
        (int)JxlAcStrategyType.Dct32X64,
    };

    // The 8x8 kinds that libjxl's search tries only below Hare (enc_ac_strategy.cc kTransforms8x8, tier
    // limit 4) and whose forward transforms are ported: DCT4X8 and DCT8X4.
    public static TheoryData<int> SquirrelOnly8x8Strategies => new()
    {
        (int)JxlAcStrategyType.Dct4X8,
        (int)JxlAcStrategyType.Dct8X4,
    };

    // The decoder's inverse of the forward transform gives the block back. Values are in [-0.5, 0.5).
    [Theory]
    [MemberData(nameof(HareStrategies))]
    [MemberData(nameof(SquirrelOnly8x8Strategies))]
    public void TransformToPixels_OfTransformFromPixels_GivesTheBlockBack(int rawStrategy)
    {
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(rawStrategy);
        int rows = acs.CoveredBlocksY * 8;
        int cols = acs.CoveredBlocksX * 8;
        float[] pixels = LcgBlock(rows * cols, (uint)rawStrategy + 1);
        float[] coefficients = new float[rows * cols];
        float[] scratch = new float[JxlDct.ScratchSize];

        JxlDct.TransformFromPixels(acs.Strategy, pixels, cols, coefficients, scratch);
        float[] back = new float[rows * cols];
        JxlDct.TransformToPixels(acs, coefficients, back, cols, scratch);

        Assert.True(MaxAbsDifference(pixels, back) <= 1e-5, $"max abs error {MaxAbsDifference(pixels, back)}");
    }

    // libjxl's TransformFromPixels of the LCG block, bit for bit: the SHA-256 of the coefficients as
    // little-endian floats. From a harness of lib/jxl/enc_transforms-inl.h at ef67fde2 on Highway's scalar
    // target with -ffp-contract=off (the DCT has no lane sums). Coefficient 1 is noted as a value.
    [Theory]
    [InlineData((int)JxlAcStrategyType.Dct, "F799629716D20DB896185D747E51CD15AB9A6488E065664D13AB9FD66E2A0E8F", -0.05878055840730667f)]
    [InlineData((int)JxlAcStrategyType.Identity, "C1F7E13572F50077334C7FEEB3A7F2B3FD99C89D4F451AA3E8520600D5FDE8F4", -0.03187894821166992f)]
    [InlineData((int)JxlAcStrategyType.Dct2X2, "9510B83459E6165C6E790E8D9371A1B6BDCEA2B3EBDD6E90DD57FA7EE65706D1", 0.017136335372924805f)]
    [InlineData((int)JxlAcStrategyType.Dct4X4, "C840BC2EF3C4558BF1F331E41F1AFC81CB48FC3C419FB6832F84189EBF6652F1", 0.003652334213256836f)]
    [InlineData((int)JxlAcStrategyType.Dct16X16, "02294B330EE82AB0A27ABD3F0E9BF386397F4C611669173640F1A498D100E788", -0.009991928935050964f)]
    [InlineData((int)JxlAcStrategyType.Dct32X32, "0EBF9242A6E55A5861E6F355D987E3ABCA6E16F6335232C1068B1E75B9B8C0E7", 0.009861333295702934f)]
    [InlineData((int)JxlAcStrategyType.Dct16X8, "CEA4BC189C1C787258D3532F8CB5D75E2E7E81F59F50835C88DB623F9E9CFA79", 0.007569536566734314f)]
    [InlineData((int)JxlAcStrategyType.Dct8X16, "D66A2FF6BC64BEDA50825A92DD903E7AB78F62619B428AFBFC7687D86FDB67C9", -0.019463010132312775f)]
    [InlineData((int)JxlAcStrategyType.Dct32X16, "D03830C00CE50218BF640F7485CCFA9A00AA83D252654BBF5CB9E5D1B486F117", 0.001264701597392559f)]
    [InlineData((int)JxlAcStrategyType.Dct16X32, "48A3257A69138AA2D293AB2CFC101D2DB8CEEFFBC4C93B5F33804126A550D0DC", -0.003828343003988266f)]
    [InlineData((int)JxlAcStrategyType.Dct64X64, "9F851DFE6D81A344665177F90C16BDFA31C3B6DC443DADEEB0A6D5CD425CA3D4", 0.0017419431824237108f)]
    [InlineData((int)JxlAcStrategyType.Dct64X32, "59CA516FC8F13A6B920C9F1AE20AF48B494E69F993047B8EB962D0428FE1A63D", -0.004918401129543781f)]
    [InlineData((int)JxlAcStrategyType.Dct32X64, "61931CD8CDBD06625CC9BF525BFABFE6BD189AC803C770A6B8F58BA2044515D1", -0.006262142211198807f)]
    [InlineData((int)JxlAcStrategyType.Dct4X8, "27985D2030D8DA9E9C48D0C5BF501769A90F108FBCF0C5C55E35CF87343475F9", -0.00014410167932510376f)]
    [InlineData((int)JxlAcStrategyType.Dct8X4, "EB351496BBE96D2C5F358C55BD30EA8244157EFE9F0E1D4355173C2851708B08", -0.04865458607673645f)]
    public void TransformFromPixels_MatchesLibjxlBitForBit(int rawStrategy, string sha256, float coefficient1)
    {
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(rawStrategy);
        int rows = acs.CoveredBlocksY * 8;
        int cols = acs.CoveredBlocksX * 8;
        float[] pixels = LcgBlock(rows * cols, (uint)rawStrategy + 1);
        float[] coefficients = new float[rows * cols];

        JxlDct.TransformFromPixels(acs.Strategy, pixels, cols, coefficients, new float[JxlDct.ScratchSize]);

        Assert.Equal(coefficient1, coefficients[1]);
        Assert.Equal(sha256, Sha256(coefficients));
    }

    // The decoder's LowestFrequenciesFromDc of the DC values gives the lowest frequencies back. The
    // coefficients are random, in [-0.5, 0.5).
    [Theory]
    [MemberData(nameof(HareStrategies))]
    public void LowestFrequenciesFromDc_OfDcFromLowestFrequencies_GivesTheLowestFrequenciesBack(int rawStrategy)
    {
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(rawStrategy);
        int cx = acs.CoveredBlocksX;
        int cy = acs.CoveredBlocksY;
        float[] block = LcgBlock(cx * cy * 64, (uint)rawStrategy + 100);
        float[] dc = new float[cx * cy];
        float[] scratch = new float[JxlDct.ScratchSize];

        JxlDct.DcFromLowestFrequencies(acs.Strategy, block, dc, cx, scratch);
        float[] llf = new float[cx * cy * 64];
        JxlDct.LowestFrequenciesFromDc(acs, dc, cx, llf, scratch);

        // The lowest frequencies are min(cx, cy) rows of max(cx, cy), in rows of the block's larger side.
        int stride = Math.Max(cx, cy) * 8;
        double worst = 0;
        for (int y = 0; y < Math.Min(cx, cy); y++)
        {
            for (int x = 0; x < Math.Max(cx, cy); x++)
            {
                worst = Math.Max(worst, Math.Abs((double)block[(y * stride) + x] - llf[(y * stride) + x]));
            }
        }

        Assert.True(worst <= 1e-5, $"max abs error {worst}");
    }

    // libjxl's DCFromLowestFrequencies of the coefficients of TransformFromPixels_MatchesLibjxlBitForBit,
    // bit for bit: the SHA-256 of the DC values (rows of the covered blocks across) as little-endian floats.
    // From the same harness.
    [Theory]
    [InlineData((int)JxlAcStrategyType.Dct, "A7B4CBF23CD5164A7556993458FDA37D202210DC27AC1EB9157AB622E3A1DA74")]
    [InlineData((int)JxlAcStrategyType.Identity, "CCB10FC76B6D6B7AA1F8EAF0DC65C45B16253223F336CB454811D59565E294A5")]
    [InlineData((int)JxlAcStrategyType.Dct2X2, "DBF3570EC4D598D7B129291F0E4BE58266B918B1687F61B958549DFC918462D4")]
    [InlineData((int)JxlAcStrategyType.Dct4X4, "F18DBAAB0EDEB047F1A308ED1AA24C094F508777EB00E05829EA934E60F75C14")]
    [InlineData((int)JxlAcStrategyType.Dct16X16, "C6D150E7AE96C2F4B0392B5E43608EDF9815CF73792514C3881556B9A366FC17")]
    [InlineData((int)JxlAcStrategyType.Dct32X32, "08D43F8701986A6A89003CC854361A6B08BEAAF094ED8D45055E222C6D49C95C")]
    [InlineData((int)JxlAcStrategyType.Dct16X8, "C74B5ED9419666430987A79A69C8B23C54DF0A9878890FD13210BC4BD0487A22")]
    [InlineData((int)JxlAcStrategyType.Dct8X16, "72296C9773D52D73E843F052D686B0F71FD601D9276A5421CAF2FAA16603DD98")]
    [InlineData((int)JxlAcStrategyType.Dct32X16, "85DA06C0291395B697DFD4D497F8FB6FE9298DEA090EFFA81BFC14794D1D3F24")]
    [InlineData((int)JxlAcStrategyType.Dct16X32, "BE248878991C9387827BE7EC0F9544B9E6443D8C8FAC9EADD34885D6363E1D35")]
    [InlineData((int)JxlAcStrategyType.Dct64X64, "F43C230F39C1BA924380AEDD5C93B3AC7C4FC0B830332987660315163EF209BE")]
    [InlineData((int)JxlAcStrategyType.Dct64X32, "0B8CCB33CD8DD30B2A7CC30225073A3A6744D179870D23B338921CE95B882385")]
    [InlineData((int)JxlAcStrategyType.Dct32X64, "8381AA5DE67E536EFDFAE8EEE650D880F1A0575BA498D915A3E3D524646DC36B")]
    [InlineData((int)JxlAcStrategyType.Dct4X8, "D4915D9A4D90C0F9A31E50CE101CB5E8E769A98C181F8BC7FABE7D1051557C3B")]
    [InlineData((int)JxlAcStrategyType.Dct8X4, "0720EB36DE2A0496C36C6F6B96B3B70BB6295CB1C5DDCFD168D4FBA9A2C7C943")]
    public void DcFromLowestFrequencies_MatchesLibjxlBitForBit(int rawStrategy, string sha256)
    {
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(rawStrategy);
        int cx = acs.CoveredBlocksX;
        int cy = acs.CoveredBlocksY;
        float[] pixels = LcgBlock(cx * cy * 64, (uint)rawStrategy + 1);
        float[] coefficients = new float[cx * cy * 64];
        float[] scratch = new float[JxlDct.ScratchSize];
        JxlDct.TransformFromPixels(acs.Strategy, pixels, cx * 8, coefficients, scratch);
        float[] dc = new float[cx * cy];

        JxlDct.DcFromLowestFrequencies(acs.Strategy, coefficients, dc, cx, scratch);

        Assert.Equal(sha256, Sha256(dc));
    }

    // AFV0 to AFV3, which libjxl's search tries from Wombat down (Thorough, slice E5c): the decoder's
    // inverse gives the block back. Each puts the AFV corner in another quadrant.
    [Theory]
    [InlineData((int)JxlAcStrategyType.Afv0)]
    [InlineData((int)JxlAcStrategyType.Afv1)]
    [InlineData((int)JxlAcStrategyType.Afv2)]
    [InlineData((int)JxlAcStrategyType.Afv3)]
    public void TransformToPixels_OfAfvTransformFromPixels_GivesTheBlockBack(int rawStrategy)
    {
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(rawStrategy);
        float[] pixels = LcgBlock(64, (uint)rawStrategy + 1);
        float[] coefficients = new float[64];
        float[] scratch = new float[JxlDct.ScratchSize];

        JxlDct.TransformFromPixels(acs.Strategy, pixels, 8, coefficients, scratch);
        float[] back = new float[64];
        JxlDct.TransformToPixels(acs, coefficients, back, 8, scratch);

        Assert.True(MaxAbsDifference(pixels, back) <= 1e-5, $"max abs error {MaxAbsDifference(pixels, back)}");
    }

    // libjxl's AFVTransformFromPixels of the LCG block, bit for bit, as the test above it. From a harness of
    // lib/jxl/enc_transforms-inl.h at ef67fde2 (e5-notes/e5c-scratch/fwd_afv.cc) on Highway's scalar target
    // with -ffp-contract=off: the AFV corner's 16 sums are mul then add, in the order of the pixels.
    [Theory]
    [InlineData((int)JxlAcStrategyType.Afv0, "015EA69E341A5DDFD54A80570782FC19FE6067692FDA78F246FD2A0C0F5DCD0F", -0.02538013458251953f)]
    [InlineData((int)JxlAcStrategyType.Afv1, "CADCDD8FDC5757E45421733322F5A7CED69FDF1FE6CD5A0659389498B7C14DDF", 0.02108287811279297f)]
    [InlineData((int)JxlAcStrategyType.Afv2, "2AE95F1F3BC84B9A5E2C60CB03342B7D59C1834AE436B9A71BCF598678126CDC", -0.010546207427978516f)]
    [InlineData((int)JxlAcStrategyType.Afv3, "DB2F854CA38D36DAACD2998AE47F751089E805DAAA91C5E747005A435C16E95E", 0.06282997131347656f)]
    public void AfvTransformFromPixels_MatchesLibjxlBitForBit(int rawStrategy, string sha256, float coefficient1)
    {
        float[] pixels = LcgBlock(64, (uint)rawStrategy + 1);
        float[] coefficients = new float[64];

        JxlDct.TransformFromPixels((JxlAcStrategyType)rawStrategy, pixels, 8, coefficients, new float[JxlDct.ScratchSize]);

        Assert.Equal(coefficient1, coefficients[1]);
        Assert.Equal(sha256, Sha256(coefficients));
    }

    // The committed k4x4AFVBasisTranspose is libjxl's, value for value: the SHA-256 of its 256 floats, row
    // by row, little-endian. The harness reads libjxl's table back through AFVDCT4x4 of each unit pixel.
    [Fact]
    public void Afv4X4BasisTranspose_IsLibjxlsTable()
        => Assert.Equal("CF20660F8B563DD6A427D565961B8DD598C55394A8C19F05BC561099E577A43E", Sha256(JxlDct.Afv4X4BasisTranspose.ToArray()));

    // The basis is orthonormal: the forward table is the inverse of the decoder's.
    [Fact]
    public void Afv4X4BasisTranspose_IsOrthonormal()
    {
        ReadOnlySpan<float> basis = JxlDct.Afv4X4BasisTranspose;
        double worst = 0;
        for (int a = 0; a < 16; a++)
        {
            for (int b = 0; b < 16; b++)
            {
                // Columns a and b of the transpose are patterns a and b.
                double dot = 0;
                for (int j = 0; j < 16; j++)
                {
                    dot += (double)basis[(j * 16) + a] * basis[(j * 16) + b];
                }

                worst = Math.Max(worst, Math.Abs(dot - (a == b ? 1.0 : 0.0)));
            }
        }

        Assert.True(worst <= 1e-6, $"max deviation {worst}");
    }

    // The block of the libjxl harness: an LCG's values with 16 fraction bits, exact in float.
    private static float[] LcgBlock(int count, uint seed)
    {
        float[] values = new float[count];
        uint state = seed;
        for (int i = 0; i < count; i++)
        {
            state = unchecked((state * 1664525u) + 1013904223u);
            values[i] = (((state >> 8) & 0xFFFF) / 65536.0f) - 0.5f;
        }

        return values;
    }

    private static string Sha256(float[] values)
    {
        byte[] bytes = new byte[values.Length * sizeof(float)];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), values[i]);
        }

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static double MaxAbsDifference(float[] a, float[] b)
    {
        double worst = 0;
        for (int i = 0; i < a.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs((double)a[i] - b[i]));
        }

        return worst;
    }
}
