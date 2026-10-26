// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The prefix-code writer and the decoder's prefix-code reader are a mirror pair: every code the
/// writer stores must read back, and every symbol written with it must decode to itself.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlHuffmanEncoderTests
{
    public static TheoryData<string> Shapes { get; } = new()
    {
        "one-symbol-at-0",
        "one-symbol-at-5",
        "two-symbols",
        "three-symbols",
        "four-symbols-balanced",
        "four-symbols-skewed",
        "forty-symbols",
        "long-with-runs",
        "deeper-than-15",
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void BuildAndStoreHuffmanTree_ReadsBackAndDecodesEverySymbol(string shape)
    {
        uint[] histogram = CreateHistogram(shape);
        int length = histogram.Length;
        byte[] depth = new byte[length];
        ushort[] bits = new ushort[length];
        JxlBitWriter writer = new();

        JxlHuffmanEncoder.BuildAndStoreHuffmanTree(histogram, length, depth, bits, writer);
        List<int> symbols = new();
        for (int i = 0; i < length; i++)
        {
            for (uint n = 0; n < Math.Min(histogram[i], 3u); n++)
            {
                symbols.Add(i);
                writer.Write(depth[i], bits[i]);
            }
        }

        writer.ZeroPadToByte();
        JxlBitReader reader = new(writer.ToArray());
        JxlHuffmanDecodingData code = new();
        Assert.True(code.ReadFromBitStream(length, reader));
        foreach (int expected in symbols)
        {
            reader.Refill();
            Assert.Equal(expected, code.ReadSymbol(reader));
        }

        Assert.True(reader.AllReadsWithinBounds());
        reader.Close();
        Assert.All(depth, d => Assert.InRange(d, 0, 15));
    }

    private static uint[] CreateHistogram(string shape)
    {
        switch (shape)
        {
            case "one-symbol-at-0":
                return new uint[] { 7 };
            case "one-symbol-at-5":
                return new uint[] { 0, 0, 0, 0, 0, 9 };
            case "two-symbols":
                return new uint[] { 0, 3, 0, 1 };
            case "three-symbols":
                return new uint[] { 5, 1, 0, 0, 1 };
            case "four-symbols-balanced":
                return new uint[] { 4, 4, 0, 4, 4 };
            case "four-symbols-skewed":
                return new uint[] { 10, 3, 2, 0, 1 };
            case "forty-symbols":
            {
                uint[] h = new uint[40];
                for (int i = 0; i < h.Length; i++)
                {
                    h[i] = (uint)(((i * 37) % 11) + (i % 3 == 0 ? 0 : 1));
                }

                return h;
            }

            case "long-with-runs":
            {
                // More than 50 symbols, so the code lengths are run-length coded: long zero runs and
                // runs of equal non-zero lengths.
                uint[] h = new uint[300];
                for (int i = 0; i < h.Length; i++)
                {
                    h[i] = i < 40 ? 8u : i < 120 ? 0u : i < 200 ? 2u : i % 17 == 0 ? 5u : 0u;
                }

                return h;
            }

            case "deeper-than-15":
            {
                // Fibonacci counts give an unbounded Huffman tree one level deeper per symbol.
                uint[] h = new uint[25];
                uint a = 1, b = 1;
                for (int i = 0; i < h.Length; i++)
                {
                    h[i] = a;
                    (a, b) = (b, a + b);
                }

                return h;
            }

            default:
                throw new ArgumentException(shape, nameof(shape));
        }
    }
}
