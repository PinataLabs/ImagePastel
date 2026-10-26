// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.VarDct;
using PinataLabs.ImagePastel.Memory;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The search of libjxl's <c>ComputeARHeuristics</c> (dynamic EPF sharpness, E5h) on fixed inputs: the
/// per-block error (<c>ComputeBlockL2Distance</c>) and the choice of each block's sharpness from fixed
/// error images (both passes and the multipliers). The expected values come from a libjxl harness at
/// ef67fde2 (<c>e5h-scratch/ar.cc</c>: TDM g++, Highway's scalar target, <c>-ffp-contract=off</c>), which
/// runs the libjxl lines verbatim on the same inputs.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncArHeuristicsTests
{
    private uint state;

    // The harness's a (orig_opsin) is 21x13, so the right column of blocks is 5 pixels wide and the bottom
    // row 5 pixels high. b is the decoder's padded 24x16, with values in its padding that must not be read.
    [Fact]
    public void ComputeBlockL2Distance_OnFixedPlanes_MatchesLibjxl()
    {
        uint[] expected = { 0x3D9D6AE8, 0x3D89566E, 0x3D503E48, 0x3D467414, 0x3D5B9BF7, 0x3D5183AC };
        MemoryAllocator allocator = Configuration.Default.MemoryAllocator;
        Buffer2D<float>[] a = new Buffer2D<float>[3];
        Buffer2D<float>[] b = new Buffer2D<float>[3];
        try
        {
            this.state = 7;
            for (int c = 0; c < 3; c++)
            {
                a[c] = allocator.Allocate2D<float>(21, 13);
                b[c] = allocator.Allocate2D<float>(24, 16);
                b[c].DangerousGetSingleSpan().Fill(1000.0f);
                for (int y = 0; y < 13; y++)
                {
                    for (int x = 0; x < 21; x++)
                    {
                        a[c][x, y] = (this.U() * 0.6f) - 0.1f;
                    }
                }
            }

            for (int c = 0; c < 3; c++)
            {
                for (int y = 0; y < 13; y++)
                {
                    for (int x = 0; x < 21; x++)
                    {
                        b[c][x, y] = a[c][x, y] + ((this.U() - 0.5f) * 0.02f);
                    }
                }
            }

            using Buffer2D<float> mask = allocator.Allocate2D<float>(24, 16);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 24; x++)
                {
                    mask[x, y] = this.U() * 3.0f;
                }
            }

            uint[] actual = new uint[6];
            for (int by = 0; by < 2; by++)
            {
                for (int bx = 0; bx < 3; bx++)
                {
                    actual[(by * 3) + bx] = BitConverter.SingleToUInt32Bits(JxlEncHeuristics.ComputeBlockL2Distance(a, b, mask, by, bx));
                }
            }

            Assert.Equal(expected, actual);
        }
        finally
        {
            foreach (Buffer2D<float> plane in a)
            {
                plane?.Dispose();
            }

            foreach (Buffer2D<float> plane in b)
            {
                plane?.Dispose();
            }
        }
    }

    // The steps by distance: {0, 4} above 4.5, else {0, 2, 7}.
    [Theory]
    [InlineData(0.6f, new byte[] { 0, 2, 7 })]
    [InlineData(4.5f, new byte[] { 0, 2, 7 })]
    [InlineData(4.51f, new byte[] { 0, 4 })]
    [InlineData(25.0f, new byte[] { 0, 4 })]
    public void EpfSteps_ByDistance_AreLibjxls(float distance, byte[] expected)
        => Assert.Equal(expected, JxlEncHeuristics.EpfSteps(distance).Values);

    // Fine errors (U() * 100) seldom tie; coarse ones (eighths from 0 to 7/8) tie often, so they exercise
    // the kFavorNoSmoothing scale, the strict < of the step loop (ties keep the first step) and the
    // top/left rule's ties. d7 clamps to 5. The multipliers are 1, times c3 for step 0: the context
    // histogram's ratio is an integer division, always 0.
    [Theory]
    [InlineData("fine1", 1.0f, 13, 9, false, 11u, 0x3F7AEC8Du, "007022000070202702020700700072777007000007277277700720700207770702022207000002702777070720277072772022077207277220222", "007022000070202702020700700072777007000007277277700720700207770702022207000002702777070720277072772022077207277220222")]
    [InlineData("fine2", 2.0f, 13, 9, false, 12u, 0x3F75F2DEu, "700002700222707022222070772202077027702200720022270072270020770700027070022202220077270707007020722022722272007202707", "700002700222707020222070772202077027702200720022270072270020770700027070022202220077270707007020722022722272007202707")]
    [InlineData("fine45", 4.5f, 13, 9, false, 13u, 0x3F69F00Eu, "027202207772707007002200707270702777220270202000027072222007700222777270727022222207020007200702070020700700202027702", "027202207772707007002200707270702777220200202000027072222007700222770270727022222207020007200700070020700700202007702")]
    [InlineData("fine5", 5.0f, 13, 9, false, 14u, 0x3F679B5Au, "044040440404000000404044400044000444040000444004404440044004444004000404400444004440004440440400404000044040000444440", "044040440404000000404044400044000444040000444004404440044004444004000404000444004040004440440400404000044040000444440")]
    [InlineData("fine7", 7.0f, 13, 9, false, 15u, 0x3F679B5Au, "444004440000000404044440444000000040040404400000440040440404440040004444404044044404444444400040040004440440000000000", "440004440000000404044440444000000040040404400000440040440004440040004444400044044404044444000040040004440400000000000")]
    [InlineData("coarse2", 2.0f, 13, 9, true, 16u, 0x3F75F2DEu, "270702000772722000707002727070222002002007222000222072272027707002702007220202000002200207200702077020220072222720220", "270702000722722000707002727070220002002007222000222072272027707002702007220202000002200207200702077020220022222720220")]
    [InlineData("coarse5", 5.0f, 13, 9, true, 17u, 0x3F679B5Au, "000404400004044044400000444404444044404000000400004000044440000044004440404004444404400004404044004000040444400000404", "000404400004044044400000444404444044404000000400004000044040000044004440404004444404400004404044004000040444400000404")]
    [InlineData("coarse06", 0.6f, 7, 5, true, 18u, 0x3F7CF136u, "07770770720770700200272222220007072", "07770770720720700200272222220007072")]
    public void ChooseEpfSharpness_OnFixedErrorImages_MatchesLibjxl(string name, float distance, int xs, int ys, bool coarse, uint seed, uint expectedC3, string expectedFirstPass, string expectedFinal)
    {
        this.state = seed;
        float[][] errorImages = new float[8][];
        for (int v = 0; v < 8; v++)
        {
            errorImages[v] = new float[xs * ys];
            for (int i = 0; i < xs * ys; i++)
            {
                errorImages[v][i] = coarse ? (this.Next() % 8) * 0.125f : this.U() * 100.0f;
            }
        }

        JxlEncHeuristics.EpfStepSet steps = JxlEncHeuristics.EpfSteps(distance);
        byte[] sharpness = new byte[xs * ys];
        Array.Fill(sharpness, steps.Values[^1]);

        JxlEncHeuristics.EpfContextHistogram histogram = JxlEncHeuristics.ChooseEpfSharpnessFirstPass(steps, errorImages, sharpness, xs, ys);
        Assert.Equal(expectedFirstPass, Digits(sharpness));

        float[] mul = JxlEncHeuristics.EpfMultipliers(steps, MathF.Min(5.0f, distance), histogram);
        Assert.Equal(ExpectedMultipliers(steps.Values, expectedC3), mul);

        JxlEncHeuristics.ChooseEpfSharpnessSecondPass(steps, errorImages, mul, sharpness, xs, ys);
        Assert.Equal(expectedFinal, Digits(sharpness));

        // The whole search from the same errors, as ComputeArHeuristics runs it.
        byte[] again = new byte[xs * ys];
        JxlEncHeuristics.ChooseEpfSharpness(distance, steps, errorImages, again, xs, ys);
        Assert.True(expectedFinal == Digits(again), name);
    }

    private static string Digits(byte[] plane)
    {
        char[] chars = new char[plane.Length];
        for (int i = 0; i < plane.Length; i++)
        {
            chars[i] = (char)('0' + plane[i]);
        }

        return new string(chars);
    }

    // The harness prints mul[27] in libjxl's layout, lut[val] + 3 * (lut[top] * 3 + lut[left]): c3 for
    // step 0 and 1 for the others in every context the steps reach, 0 elsewhere.
    private static float[] ExpectedMultipliers(byte[] steps, uint c3Bits)
    {
        float[] mul = new float[27];
        for (int top = 0; top < steps.Length; top++)
        {
            for (int left = 0; left < steps.Length; left++)
            {
                for (int val = 0; val < steps.Length; val++)
                {
                    mul[val + (3 * ((top * 3) + left))] = val == 0 ? BitConverter.UInt32BitsToSingle(c3Bits) : 1.0f;
                }
            }
        }

        return mul;
    }

    private uint Next()
    {
        this.state = (this.state * 1664525u) + 1013904223u;
        return this.state >> 8;
    }

    private float U() => this.Next() * (1.0f / 16777216.0f);
}
