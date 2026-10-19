// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Modular;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

[Trait("Format", "Jxl")]
public class JxlTreeLearnerTests
{
    // libjxl's EstimateBits, as cjxl runs it on AVX2: symbol i adds its bits to lane i % 8 of a vector
    // of 8 floats, and Highway's SumOfLanes adds the two 4-lane blocks, then lanes 0 and 3 and lanes 1
    // and 2 of the result, then those two sums.
    [Fact]
    public void EstimateBits_SumsInTheAvx2LaneOrder()
    {
        int[] counts = { 9000, 1, 3, 70, 1, 2, 500, 11, 13, 1, 1700, 19, 1, 23, 290, 31, 37, 1, 4100 };
        float[] lanes = new float[8];
        int total = 0;
        foreach (int count in counts)
        {
            total += count;
        }

        float inOrder = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            float term = Term(counts[i], total);
            lanes[i % 8] -= term;
            inOrder -= term;
        }

        float expected = ((lanes[0] + lanes[4]) + (lanes[3] + lanes[7])) + ((lanes[1] + lanes[5]) + (lanes[2] + lanes[6]));
        Assert.NotEqual(inOrder, expected);

        float actual = JxlTreeLearner.EstimateBits(counts, 0, counts.Length);

        Assert.Equal(expected, actual);
    }

    // The bits of one symbol: its count times the log of its probability, which is at least 1/4096.
    private static float Term(int count, int total)
    {
        float probability = Math.Max(count * (1.0f / total), 1.0f / JxlAnsParams.AnsTabSize);
        return count == total ? 0 : count * JxlFastMath.FastLog2f(probability);
    }
}
