// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.Formats.Webp.Lossy;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.WebP;

[Trait("Format", "Webp")]
public class Vp8ResidualBufferTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4000)]
    public void Reader_ReturnsBlocksInAppendOrder_WhenSomeAreSkipped(int macroblockCount)
    {
        int blockCount = macroblockCount * Vp8ResidualBuffer.BlocksPerI16Macroblock;
        using var buffer = new Vp8ResidualBuffer(Configuration.Default.MemoryAllocator, macroblockCount);

        Append(buffer, blockCount, 0);

        AssertReadsBack(buffer, blockCount, 0);
    }

    [Fact]
    public void Reader_ReturnsOnlyNewBlocks_AfterClear()
    {
        const int macroblockCount = 4000;
        int blockCount = macroblockCount * Vp8ResidualBuffer.BlocksPerI16Macroblock;
        using var buffer = new Vp8ResidualBuffer(Configuration.Default.MemoryAllocator, macroblockCount);
        Append(buffer, blockCount, 0);

        buffer.Clear();
        Append(buffer, blockCount, 1);

        AssertReadsBack(buffer, blockCount, 1);
    }

    private static void Append(Vp8ResidualBuffer buffer, int blockCount, int seed)
    {
        for (int i = 0; i < blockCount; i++)
        {
            buffer.AppendBlock(Ctx(i, seed), Levels(i, seed));
        }
    }

    private static void AssertReadsBack(Vp8ResidualBuffer buffer, int blockCount, int seed)
    {
        Vp8ResidualBuffer.Reader reader = buffer.GetReader();
        for (int i = 0; i < blockCount; i++)
        {
            if (i % 7 == 3)
            {
                int skipped = Math.Min(5, blockCount - i);
                reader.SkipBlocks(skipped);
                i += skipped - 1;
                continue;
            }

            reader.ReadBlock(out int ctx, out ReadOnlySpan<short> levels);
            Assert.Equal(Ctx(i, seed), ctx);
            Assert.True(levels.SequenceEqual(Levels(i, seed)), $"Block {i} differs.");
        }
    }

    private static int Ctx(int i, int seed) => (i + seed) % 3;

    private static short[] Levels(int i, int seed)
    {
        short[] levels = new short[(i + (5 * seed)) % 17];
        for (int n = 0; n < levels.Length; n++)
        {
            levels[n] = (short)((i * 31) + n - 2048);
        }

        return levels;
    }
}
