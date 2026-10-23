// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The encoder's block context map at Default (libjxl's <c>FindBestBlockEntropyModel</c> and
/// <c>EncodeBlockCtxMap</c>), slice E4e.
/// </summary>
/// <remarks>
/// The expected maps come from a libjxl harness (<c>e4e-scratch\bcm.cc</c>: libjxl's function at
/// <c>ef67fde2</c>, built with g++ and libstdc++), on fixed raw quant field and strategy images that both
/// sides make with the same generator (see <see cref="CreateImages"/>). The random cases have no ties in
/// the merge loop's sort, so they do not depend on the STL's sort order; the constant case has many zero
/// counts, but sorts 13 clusters, which libstdc++ (like MSVC's STL) sorts by insertion, stably.
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlEncBlockCtxMapTests
{
    [Theory]
    [InlineData(73, 57, 1.0f, 0u, 5, "", 3, "0,1,1,1,1,1,1,1,1,1,1,1,1,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2")]
    [InlineData(64, 40, 1.0f, 1u, 0, "", 3, "0,1,0,1,1,1,1,1,0,0,0,0,0,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2")]
    [InlineData(128, 64, 0.5f, 9u, 0, "128", 13, "0,0,1,2,3,3,4,5,4,6,5,3,5,5,4,4,6,4,4,7,7,7,7,7,7,5,8,8,9,10,11,11,12,12,12,12,12,11,12,12,12,12,12,12,12,12,12,12,12,12,12,12,8,8,9,10,11,11,12,12,12,12,12,11,12,12,12,12,12,12,12,12,12,12,12,12,12,12")]
    [InlineData(64, 48, 2.5f, 2u, 0, "", 3, "0,1,0,0,1,1,1,0,1,0,0,0,0,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2")]
    [InlineData(96, 64, 0.3f, 1u, 0, "127", 14, "0,1,2,3,4,5,6,7,8,8,7,7,7,6,4,6,4,5,5,4,4,5,5,5,7,4,9,10,11,12,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,9,10,11,12,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13")]
    [InlineData(96, 64, 0.3f, 22u, 0, "126", 14, "0,1,2,3,4,5,4,5,6,7,8,7,5,8,8,8,5,8,4,5,6,6,4,4,4,5,9,10,11,12,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,9,10,11,12,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13,13")]
    public void FindBestBlockEntropyModel_GivesLibjxlsMap(int xsize, int ysize, float distance, uint seed, int constantQf, string expectedQft, int expectedNumCtxs, string expectedCtxMap)
    {
        (int[] rawQuantField, JxlAcStrategyImage acStrategy) = CreateImages(xsize, ysize, seed, constantQf);
        JxlBlockCtxMap blockCtxMap = new();

        JxlEncBlockCtxMap.FindBestBlockEntropyModel(JxlSpeedTier.Hare, distance, rawQuantField, acStrategy, blockCtxMap);

        Assert.Equal(ParseList(expectedQft, uint.Parse), blockCtxMap.QfThresholds);
        Assert.Equal(expectedNumCtxs, blockCtxMap.NumCtxs);
        Assert.Equal(ParseList(expectedCtxMap, byte.Parse), blockCtxMap.CtxMap);
        Assert.Empty(blockCtxMap.DcThresholds[0]);
        Assert.Empty(blockCtxMap.DcThresholds[1]);
        Assert.Empty(blockCtxMap.DcThresholds[2]);
        Assert.Equal(1, blockCtxMap.NumDcCtxs);
    }

    // Below 1024·d blocks (31x32 = 992 at d1), and at Falcon, the map keeps its default.
    [Theory]
    [InlineData(31, 32, 1.0f, (int)JxlSpeedTier.Hare)]
    [InlineData(73, 57, 1.0f, (int)JxlSpeedTier.Falcon)]
    [InlineData(73, 57, 8.0f, (int)JxlSpeedTier.Hare)]
    public void FindBestBlockEntropyModel_SmallFrameOrFalcon_KeepsTheDefault(int xsize, int ysize, float distance, int speedTier)
    {
        (int[] rawQuantField, JxlAcStrategyImage acStrategy) = CreateImages(xsize, ysize, 1, 0);
        JxlBlockCtxMap blockCtxMap = new();

        JxlEncBlockCtxMap.FindBestBlockEntropyModel((JxlSpeedTier)speedTier, distance, rawQuantField, acStrategy, blockCtxMap);

        AssertDefault(blockCtxMap);
    }

    // The written map reads back through the decoder's JxlBlockCtxMap.Decode to the same thresholds, map
    // and number of contexts.
    [Theory]
    [InlineData(73, 57, 1.0f, 0u, 5)]
    [InlineData(128, 64, 0.5f, 9u, 0)]
    [InlineData(96, 64, 0.3f, 1u, 0)]
    public void EncodeBlockCtxMap_DecodesToTheSameMap(int xsize, int ysize, float distance, uint seed, int constantQf)
    {
        (int[] rawQuantField, JxlAcStrategyImage acStrategy) = CreateImages(xsize, ysize, seed, constantQf);
        JxlBlockCtxMap written = new();
        JxlEncBlockCtxMap.FindBestBlockEntropyModel(JxlSpeedTier.Hare, distance, rawQuantField, acStrategy, written);

        JxlBlockCtxMap read = WriteAndRead(written, out int bits);

        Assert.True(bits > 1, $"{bits} bits");
        Assert.Equal(written.QfThresholds, read.QfThresholds);
        Assert.Equal(written.CtxMap, read.CtxMap);
        Assert.Equal(written.NumCtxs, read.NumCtxs);
        Assert.Equal(1, read.NumDcCtxs);
    }

    [Fact]
    public void EncodeBlockCtxMap_Default_IsOneBit()
    {
        JxlBlockCtxMap read = WriteAndRead(new JxlBlockCtxMap(), out int bits);

        Assert.Equal(1, bits);
        AssertDefault(read);
    }

    internal static void AssertDefault(JxlBlockCtxMap blockCtxMap)
    {
        JxlBlockCtxMap defaultMap = new();
        Assert.Empty(blockCtxMap.QfThresholds);
        Assert.Equal(defaultMap.NumCtxs, blockCtxMap.NumCtxs);
        Assert.Equal(defaultMap.CtxMap, blockCtxMap.CtxMap);
    }

    private static JxlBlockCtxMap WriteAndRead(JxlBlockCtxMap map, out int bits)
    {
        JxlBitWriter writer = new();
        JxlEncBlockCtxMap.EncodeBlockCtxMap(map, writer);
        bits = (int)writer.BitsWritten;
        writer.ZeroPadToByte();
        JxlBlockCtxMap read = new();
        read.Decode(new JxlBitReader(writer.ToArray()));
        return read;
    }

    // The harness's images: an LCG s = s·1664525 + 1013904223, Next() = s >> 8. With a constant qf, every
    // block is DCT8. Otherwise each block in row-major order draws a raw strategy (DCT when it does not fit
    // there) and is Set over what earlier blocks covered, so every block keeps its own draw; then each block
    // draws its qf, 1 + Next() % 255.
    private static (int[] RawQuantField, JxlAcStrategyImage AcStrategy) CreateImages(int xsize, int ysize, uint seed, int constantQf)
    {
        uint s = seed;
        uint Next()
        {
            s = unchecked((s * 1664525u) + 1013904223u);
            return s >> 8;
        }

        JxlAcStrategyImage acStrategy = new(xsize, ysize);
        acStrategy.FillDct8();
        int[] rawQuantField = new int[xsize * ysize];
        if (constantQf > 0)
        {
            Array.Fill(rawQuantField, constantQf);
            return (rawQuantField, acStrategy);
        }

        for (int y = 0; y < ysize; y++)
        {
            for (int x = 0; x < xsize; x++)
            {
                JxlAcStrategyType type = (JxlAcStrategyType)(Next() % 27);
                JxlAcStrategy acs = JxlAcStrategy.FromRawStrategy((int)type);
                if (x + acs.CoveredBlocksX > xsize || y + acs.CoveredBlocksY > ysize)
                {
                    type = JxlAcStrategyType.Dct;
                }

                acStrategy.Set(x, y, type);
            }
        }

        for (int i = 0; i < rawQuantField.Length; i++)
        {
            rawQuantField[i] = 1 + (int)(Next() % 255);
        }

        return (rawQuantField, acStrategy);
    }

    private static T[] ParseList<T>(string list, Func<string, T> parse)
        => list.Length == 0 ? Array.Empty<T>() : Array.ConvertAll(list.Split(','), s => parse(s));
}
