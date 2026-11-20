// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using PinataLabs.ImagePastel.Compression.Brotli;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Compression.Brotli;

/// <summary>
/// Checks the lookup tables of prefix codes: symbols written with the canonical code of RFC 7932,
/// section 3.2, are read back through the table.
/// </summary>
public class BrotliHuffmanTests
{
    private const int RootBits = 8;

    private const int TableOffset = 37;

    public static TheoryData<int, int, int> Codes()
    {
        TheoryData<int, int, int> data = new();
        foreach (int alphabetSize in new[] { 18, 26, 256, 258, 272, 520, 704 })
        {
            foreach (int usedSymbols in new[] { 2, 3, 5, 17, 100, 256, 704 })
            {
                if (usedSymbols <= alphabetSize)
                {
                    data.Add(alphabetSize, usedSymbols, alphabetSize + usedSymbols);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void BuildHuffmanTable_ReadsBackTheSymbolsOfACompleteCode(int alphabetSize, int usedSymbols, int seed)
    {
        Random random = new(seed);
        byte[] lengths = RandomCompleteCode(alphabetSize, usedSymbols, random);
        BrotliHuffmanCode[] table = new BrotliHuffmanCode[TableOffset + alphabetSize + 376];

        int size = BrotliHuffman.BuildHuffmanTable(table, TableOffset, RootBits, SymbolLists(lengths, out ushort[] count), count);

        Assert.InRange(size, 1 << RootBits, alphabetSize + 376);
        AssertReadsBack(table, lengths, random);
    }

    [Fact]
    public void BuildHuffmanTable_OfTheLongestCodes_ReadsBackTheSymbols()
    {
        byte[] lengths = Enumerable.Range(1, 15).Select(i => (byte)i).Append((byte)15).ToArray();
        BrotliHuffmanCode[] table = new BrotliHuffmanCode[TableOffset + lengths.Length + 376];

        BrotliHuffman.BuildHuffmanTable(table, TableOffset, RootBits, SymbolLists(lengths, out ushort[] count), count);

        AssertReadsBack(table, lengths, new Random(1));
    }

    [Theory]
    [InlineData(0, new[] { 7 }, new[] { 0 })]
    [InlineData(1, new[] { 9, 2 }, new[] { 1, 1 })]
    [InlineData(2, new[] { 9, 2, 5 }, new[] { 1, 2, 2 })]
    [InlineData(3, new[] { 9, 2, 5, 1 }, new[] { 2, 2, 2, 2 })]
    [InlineData(4, new[] { 9, 2, 5, 1 }, new[] { 1, 2, 3, 3 })]
    [InlineData(4, new[] { 9, 2, 1, 5 }, new[] { 1, 2, 3, 3 })]
    public void BuildSimpleHuffmanTable_ReadsBackTheSymbols(int numSymbols, int[] symbols, int[] symbolLengths)
    {
        // RFC 7932, section 3.4: the lengths belong to the symbols in the order they are listed, and
        // symbols of the same length are sorted.
        byte[] lengths = new byte[16];
        for (int i = 0; i < symbols.Length; i++)
        {
            lengths[symbols[i]] = (byte)symbolLengths[i];
        }

        BrotliHuffmanCode[] table = new BrotliHuffmanCode[TableOffset + (1 << RootBits)];
        ushort[] val = symbols.Select(s => (ushort)s).ToArray();

        int size = BrotliHuffman.BuildSimpleHuffmanTable(table, TableOffset, RootBits, val, numSymbols);

        Assert.Equal(1 << RootBits, size);
        if (numSymbols == 0)
        {
            // One symbol takes no bits.
            BrotliBitReader reader = new(ReadOnlySpan<byte>.Empty, 0, 0);
            Assert.Equal(symbols[0], reader.ReadSymbol(table, TableOffset));
            return;
        }

        AssertReadsBack(table, lengths, new Random(numSymbols));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 2, 2, 2, 3, 4, 5, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 5, 5, 5, 5 })]
    public void BuildCodeLengthsHuffmanTable_ReadsBackTheSymbols(byte[] lengths)
    {
        ushort[] count = new ushort[6];
        foreach (byte length in lengths)
        {
            count[length]++;
        }

        BrotliHuffmanCode[] table = new BrotliHuffmanCode[32];
        BrotliHuffman.BuildCodeLengthsHuffmanTable(table, lengths, count);

        if (lengths.Count(l => l != 0) == 1)
        {
            // One symbol takes no bits.
            Assert.All(table, code => Assert.Equal((0, Array.FindIndex(lengths, l => l != 0)), (code.Bits, code.Value)));
            return;
        }

        (int Code, int Length)[] codes = CanonicalCodes(lengths);
        for (int symbol = 0; symbol < lengths.Length; symbol++)
        {
            if (lengths[symbol] == 0)
            {
                continue;
            }

            int first = ReverseBits(codes[symbol].Code, codes[symbol].Length);
            for (int rest = 0; rest < 32 >> lengths[symbol]; rest++)
            {
                BrotliHuffmanCode entry = table[first | (rest << lengths[symbol])];
                Assert.Equal((lengths[symbol], symbol), (entry.Bits, entry.Value));
            }
        }
    }

    private static ushort[] SymbolLists(byte[] lengths, out ushort[] count)
    {
        ushort[] lists = new ushort[BrotliHuffman.SymbolListsBase + lengths.Length];
        int[] next = new int[BrotliHuffman.MaxCodeLength + 1];
        count = new ushort[BrotliHuffman.MaxCodeLength + 1];
        for (int length = 0; length <= BrotliHuffman.MaxCodeLength; length++)
        {
            next[length] = length - (BrotliHuffman.MaxCodeLength + 1);
            lists[BrotliHuffman.SymbolListsBase + next[length]] = 0xFFFF;
        }

        for (int symbol = 0; symbol < lengths.Length; symbol++)
        {
            int length = lengths[symbol];
            if (length != 0)
            {
                lists[BrotliHuffman.SymbolListsBase + next[length]] = (ushort)symbol;
                next[length] = symbol;
                count[length]++;
            }
        }

        return lists;
    }

    private static byte[] RandomCompleteCode(int alphabetSize, int usedSymbols, Random random)
    {
        List<byte> leaves = new() { 1, 1 };
        while (leaves.Count < usedSymbols)
        {
            int index = random.Next(leaves.Count);
            if (leaves[index] < BrotliHuffman.MaxCodeLength)
            {
                leaves[index]++;
                leaves.Add(leaves[index]);
            }
        }

        int[] symbols = Enumerable.Range(0, alphabetSize).OrderBy(_ => random.Next()).Take(usedSymbols).ToArray();
        byte[] lengths = new byte[alphabetSize];
        for (int i = 0; i < usedSymbols; i++)
        {
            lengths[symbols[i]] = leaves[i];
        }

        return lengths;
    }

    // RFC 7932, section 3.2: codes of one length are consecutive, in symbol order, and shorter codes
    // come first.
    private static (int Code, int Length)[] CanonicalCodes(byte[] lengths)
    {
        (int Code, int Length)[] codes = new (int, int)[lengths.Length];
        int code = 0;
        for (int length = 1; length <= BrotliHuffman.MaxCodeLength; length++)
        {
            for (int symbol = 0; symbol < lengths.Length; symbol++)
            {
                if (lengths[symbol] == length)
                {
                    codes[symbol] = (code++, length);
                }
            }

            code <<= 1;
        }

        return codes;
    }

    private static int ReverseBits(int value, int count)
    {
        int reversed = 0;
        for (int i = 0; i < count; i++)
        {
            reversed |= ((value >> i) & 1) << (count - 1 - i);
        }

        return reversed;
    }

    private static void AssertReadsBack(BrotliHuffmanCode[] table, byte[] lengths, Random random)
    {
        (int Code, int Length)[] codes = CanonicalCodes(lengths);
        int[] used = Enumerable.Range(0, lengths.Length).Where(s => lengths[s] != 0).ToArray();

        int[] symbols = used.Concat(Enumerable.Range(0, 2000).Select(_ => used[random.Next(used.Length)])).ToArray();
        List<byte> stream = new();
        int bitCount = 0;
        foreach (int symbol in symbols)
        {
            // A code is packed from its most significant bit.
            for (int bit = codes[symbol].Length - 1; bit >= 0; bit--)
            {
                if ((bitCount & 7) == 0)
                {
                    stream.Add(0);
                }

                stream[^1] |= (byte)(((codes[symbol].Code >> bit) & 1) << (bitCount & 7));
                bitCount++;
            }
        }

        BrotliBitReader reader = new(stream.ToArray(), 0, 0);
        foreach (int symbol in symbols)
        {
            Assert.Equal(symbol, reader.ReadSymbol(table, TableOffset));
        }
    }
}
