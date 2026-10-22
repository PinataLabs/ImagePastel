// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The Default (Hare) encoder's per-block AC quant adjustment, libjxl's <c>AdjustQuantBlockAC</c>: the quant
/// and the four dead-zone thresholds it gives for fixed coefficient blocks, bit for bit.
/// </summary>
/// <remarks>
/// The values come from a harness that compiles the function's own lines of lib/jxl/enc_group.cc at
/// ef67fde2 on Highway's scalar target with -ffp-contract=off, against libjxl's default Quantizer and
/// DequantMatrices (<c>D:\Data\_CodeOS\e4-notes\e4c-scratch\adjust_quant.cc</c>).
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlAdjustQuantBlockAcTests
{
    // The channel multipliers the harness passes: x_qm_multiplier, 1 for Y, b_qm_multiplier.
    private static readonly float[] QmMultipliers = { 1.25f, 1.0f, 0.8f };

    private static readonly int[] Quants = { 5, 40, 256 };

    private static readonly float[] Amplitudes = { 0.0005f, 0.002f, 0.006f, 0.02f, 0.06f, 0.25f, 1.0f, 4.0f };

    // For each strategy and channel, 24 cases (quant-major, then amplitude): the output quants, and the
    // SHA-256 of the output thresholds as four little-endian float bit patterns per case. IDENTITY and
    // DCT4X4 return at once (kPartialBlockKinds); DCT16X8 and DCT32X16 take the step branch's ix 3 and 1.
    [Theory]
    [InlineData(0, 0, "6,6,6,6,4,4,4,4,41,40,36,26,27,27,27,27,256,249,240,240,240,240,240,240", "09DFB82F5A5F090F13436D9221DA56009AF0C3A9AAA30991E54163B320A0AB20")]
    [InlineData(0, 1, "6,6,6,6,6,4,4,4,41,41,41,40,29,27,27,27,255,255,251,241,241,241,241,241", "738D1A8AF6071CCB4398E1FC8F792D7B8B4EEC0DCDA091855D2C9E69DE6FA119")]
    [InlineData(0, 2, "6,6,6,6,6,6,4,4,41,41,41,41,41,37,25,26,255,255,255,255,248,241,241,241", "09DFB82F5A5F090F13436D9221DA56009AF0C3A9AAA30991E54163B320A0AB20")]
    [InlineData(1, 0, "5,5,5,5,5,5,5,5,40,40,40,40,40,40,40,40,256,256,256,256,256,256,256,256", "09DFB82F5A5F090F13436D9221DA56009AF0C3A9AAA30991E54163B320A0AB20")]
    [InlineData(1, 1, "5,5,5,5,5,5,5,5,40,40,40,40,40,40,40,40,256,256,256,256,256,256,256,256", "09DFB82F5A5F090F13436D9221DA56009AF0C3A9AAA30991E54163B320A0AB20")]
    [InlineData(1, 2, "5,5,5,5,5,5,5,5,40,40,40,40,40,40,40,40,256,256,256,256,256,256,256,256", "09DFB82F5A5F090F13436D9221DA56009AF0C3A9AAA30991E54163B320A0AB20")]
    [InlineData(3, 0, "5,5,5,5,5,5,5,5,40,40,40,40,40,40,40,40,256,256,256,256,256,256,256,256", "09DFB82F5A5F090F13436D9221DA56009AF0C3A9AAA30991E54163B320A0AB20")]
    [InlineData(3, 1, "5,5,5,5,5,5,5,5,40,40,40,40,40,40,40,40,256,256,256,256,256,256,256,256", "09DFB82F5A5F090F13436D9221DA56009AF0C3A9AAA30991E54163B320A0AB20")]
    [InlineData(3, 2, "5,5,5,5,5,5,5,5,40,40,40,40,40,40,40,40,256,256,256,256,256,256,256,256", "09DFB82F5A5F090F13436D9221DA56009AF0C3A9AAA30991E54163B320A0AB20")]
    [InlineData(6, 0, "5,5,5,6,4,4,4,4,40,40,33,27,27,27,27,27,255,240,240,240,240,240,240,240", "618154A1A91685E337BBB9B407B84530CA5AD8935C23A57B647A69700D778DD6")]
    [InlineData(6, 1, "5,5,5,5,5,4,4,4,40,40,40,40,30,25,25,25,256,256,247,241,241,241,241,241", "8B98E8A250C06EE96A5564CD0FBC3C52C40975F5B844CB167501ECE1E5322CBA")]
    [InlineData(6, 2, "5,5,5,5,6,6,5,4,40,40,40,41,40,34,25,25,256,255,256,254,248,241,241,241", "618154A1A91685E337BBB9B407B84530CA5AD8935C23A57B647A69700D778DD6")]
    [InlineData(4, 0, "5,5,6,6,4,4,4,4,40,41,38,29,25,25,25,25,255,249,241,241,241,241,241,241", "D951687201662C886CCDD4AF59DAE7683DFC3A1332C2C5557F6CD81AF2F62FDD")]
    [InlineData(4, 1, "5,5,5,5,5,4,4,4,40,40,40,37,25,25,25,25,256,256,248,241,241,241,241,241", "861D045041CE30F43BC6740F265394190293904EFB1B3FB703C14584837B39F6")]
    [InlineData(4, 2, "5,5,5,5,5,5,4,4,40,40,40,40,40,37,25,25,256,256,256,255,248,241,241,241", "D951687201662C886CCDD4AF59DAE7683DFC3A1332C2C5557F6CD81AF2F62FDD")]
    [InlineData(10, 0, "5,5,5,5,4,4,4,4,40,40,33,25,25,25,25,25,254,240,240,240,240,240,240,240", "851CAA12E0467BC4457FAB551D63A71F03893937459234FDFA4D52E956638537")]
    [InlineData(10, 1, "5,5,5,6,6,4,4,4,40,41,41,37,25,25,25,25,255,255,244,241,241,241,241,241", "105381A364073231833B1F68BA227065DF2099507A999F3E0018E2983EE4BD2A")]
    [InlineData(10, 2, "5,5,5,5,5,5,4,4,40,40,40,41,40,32,25,25,256,255,255,254,243,241,241,241", "851CAA12E0467BC4457FAB551D63A71F03893937459234FDFA4D52E956638537")]
    [InlineData(5, 0, "5,5,6,6,4,4,4,4,41,41,38,25,25,25,25,25,255,248,241,241,241,241,241,241", "0E3DEE9B14B670B578FDA150B58516872400EA7EFB105AD47FB4647CB955208D")]
    [InlineData(5, 1, "5,5,5,5,5,4,4,4,40,40,40,33,25,25,25,25,256,253,241,241,241,241,241,241", "4341E069B3A6E00F010393B6661380EBB34232D76C5F99E43570CEA8B332869D")]
    [InlineData(5, 2, "5,5,5,5,5,5,5,4,40,40,40,40,40,37,25,25,256,256,256,256,250,241,241,241", "0E3DEE9B14B670B578FDA150B58516872400EA7EFB105AD47FB4647CB955208D")]
    [InlineData(18, 0, "5,5,5,4,4,4,4,4,40,39,28,25,25,25,25,25,251,241,241,241,241,241,241,241", "4928FFF9E50DDED19E046AE427DFE1611C34A1E582F5C89FB1F4D14D0081F3E4")]
    [InlineData(18, 1, "5,5,5,5,4,4,4,4,40,40,34,25,25,25,25,25,255,241,241,241,241,241,241,241", "032DA1301A9E94C3BC47EE188FC57C4E75B63B3E2F24E11818AF0F4F845BAE08")]
    [InlineData(18, 2, "5,5,5,6,5,5,4,4,40,40,40,40,37,25,25,25,255,256,255,245,241,241,241,241", "4928FFF9E50DDED19E046AE427DFE1611C34A1E582F5C89FB1F4D14D0081F3E4")]
    public void AdjustQuantBlockAc_MatchesLibjxlBitForBit(int rawStrategy, int c, string quants, string thresholdsSha256)
    {
        JxlQuantizer quantizer = DefaultQuantizer();
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(rawStrategy);
        int xsize = acs.CoveredBlocksX;
        int ysize = acs.CoveredBlocksY;
        JxlAcStrategy.CoefficientLayout(ref ysize, ref xsize);
        float[] block = new float[xsize * ysize * 64];
        float[] thresholds = new float[4];
        byte[] thresholdBytes = new byte[Quants.Length * Amplitudes.Length * 16];
        string[] actualQuants = new string[Quants.Length * Amplitudes.Length];
        int n = 0;
        foreach (int q in Quants)
        {
            foreach (float a in Amplitudes)
            {
                FillBlock(block, xsize, ysize, ((uint)rawStrategy * 1000u) + ((uint)c * 100u) + (uint)q, a);
                thresholds[0] = 0.58f;
                thresholds[1] = 0.64f;
                thresholds[2] = 0.64f;
                thresholds[3] = 0.64f;
                int quant = q;
                JxlEncGroup.AdjustQuantBlockAc(quantizer, c, QmMultipliers[c], acs.Strategy, xsize, ysize, thresholds, block, ref quant);
                actualQuants[n] = quant.ToString(System.Globalization.CultureInfo.InvariantCulture);
                for (int i = 0; i < 4; i++)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(thresholdBytes.AsSpan((n * 16) + (i * 4)), thresholds[i]);
                }

                n++;
            }
        }

        Assert.Equal(quants, string.Join(",", actualQuants));
        Assert.Equal(thresholdsSha256, Convert.ToHexString(SHA256.HashData(thresholdBytes)));
    }

    // The uncapped orig + 1 edge: at quant 256 a Y block whose only coefficient (val = 0.5, in the
    // top-right quarter) is quantised to zero raises the quant by one. DCT8's flat-block bump then caps it
    // at 255, but nothing caps a DCT16X16's 257. The input and the outputs are libjxl's bits.
    [Theory]
    [InlineData((int)JxlAcStrategyType.Dct, 21, 0x39B7C415u, 255, 0x3F147AE1u, 0x3F007CB6u, 0x3F007CB6u, 0x3F23D70Au)]
    [InlineData((int)JxlAcStrategyType.Dct16X16, 45, 0x39609AABu, 257, 0x3F116872u, 0x3F007CB6u, 0x3F007CB6u, 0x3F20C49Bu)]
    public void AdjustQuantBlockAc_RaisesAQuantOf256ByOneWithoutACap(int rawStrategy, int pos, uint inBits, int expectedQuant, uint t0, uint t1, uint t2, uint t3)
    {
        JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy(rawStrategy);
        int xsize = acs.CoveredBlocksX;
        int ysize = acs.CoveredBlocksY;
        float[] block = new float[xsize * ysize * 64];
        block[pos] = BitConverter.UInt32BitsToSingle(inBits);
        float[] thresholds = { 0.58f, 0.64f, 0.64f, 0.64f };
        int quant = 256;

        JxlEncGroup.AdjustQuantBlockAc(DefaultQuantizer(), 1, 1.0f, acs.Strategy, xsize, ysize, thresholds, block, ref quant);

        Assert.Equal(expectedQuant, quant);
        Assert.Equal(new[] { t0, t1, t2, t3 }, Array.ConvertAll(thresholds, BitConverter.SingleToUInt32Bits));
    }

    private static JxlQuantizer DefaultQuantizer()
    {
        JxlDequantMatrices matrices = new();
        matrices.EnsureComputed(uint.MaxValue);
        return new JxlQuantizer(matrices);
    }

    // The harness's block: an LCG in [-0.5, 0.5) with 16 fraction bits, times the amplitude, over a
    // falloff of 1 + (x / xsize + y / ysize) / 4, in rows 8 * xsize apart.
    private static void FillBlock(float[] block, int xsize, int ysize, uint seed, float amplitude)
    {
        int cols = xsize * 8;
        int rows = ysize * 8;
        uint state = seed;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                state = (state * 1664525u) + 1013904223u;
                float r = (((state >> 8) & 0xFFFF) / 65536.0f) - 0.5f;
                float fall = 1.0f + ((float)((x / xsize) + (y / ysize)) * 0.25f);
                block[(y * cols) + x] = r * amplitude / fall;
            }
        }
    }
}
