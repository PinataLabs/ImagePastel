// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from Brotli (https://github.com/google/brotli) at tag v1.2.0 (commit 028fb5a2):
// c/dec/huffman.h and c/dec/huffman.c.
// Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors. MIT License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace PinataLabs.ImagePastel.Compression.Brotli;

internal static class BrotliHuffman
{
    public const int MaxCodeLength = 15;

    public const int MaxSize26 = 396;

    public const int MaxSize258 = 632;

    public const int MaxSize272 = 646;

    public const int MaxCodeLengthCodeLength = 5;

    public const int CodeLengthCodes = 18;

    /// <summary>
    /// The number of entries before the symbols in a list of symbols: the heads of the chains, one for
    /// each code length. The C code indexes them with -16 to -1.
    /// </summary>
    public const int SymbolListsBase = MaxCodeLength + 1;

    private const int ReverseBitsLowest = 1 << 7;

    public static void BuildCodeLengthsHuffmanTable(Span<BrotliHuffmanCode> table, ReadOnlySpan<byte> codeLengths, ReadOnlySpan<ushort> count)
    {
        Span<int> sorted = stackalloc int[CodeLengthCodes];

        Span<int> offset = stackalloc int[MaxCodeLengthCodeLength + 1];

        int symbol = -1;
        for (int length = 1; length <= MaxCodeLengthCodeLength; length++)
        {
            symbol += count[length];
            offset[length] = symbol;
        }

        offset[0] = CodeLengthCodes - 1;

        symbol = CodeLengthCodes;
        do
        {
            symbol--;
            sorted[offset[codeLengths[symbol]]--] = symbol;
        }
        while (symbol != 0);

        const int tableSize = 1 << MaxCodeLengthCodeLength;

        if (offset[0] == 0)
        {
            table.Slice(0, tableSize).Fill(new BrotliHuffmanCode(0, sorted[0]));
            return;
        }

        int key = 0;
        int keyStep = ReverseBitsLowest;
        symbol = 0;
        int bits = 1;
        int step = 2;
        do
        {
            for (int bitsCount = count[bits]; bitsCount != 0; --bitsCount)
            {
                BrotliHuffmanCode code = new(bits, sorted[symbol++]);
                ReplicateValue(table.Slice(ReverseBits(key)), step, tableSize, code);
                key += keyStep;
            }

            step <<= 1;
            keyStep >>= 1;
        }
        while (++bits <= MaxCodeLengthCodeLength);
    }

    public static int BuildHuffmanTable(BrotliHuffmanCode[] rootTable, int rootOffset, int rootBits, ReadOnlySpan<ushort> symbolLists, Span<ushort> count)
    {
        int maxLength = -1;
        while (symbolLists[SymbolListsBase + maxLength] == 0xFFFF)
        {
            maxLength--;
        }

        maxLength += MaxCodeLength + 1;

        int table = rootOffset;
        int tableBits = rootBits;
        int tableSize = 1 << tableBits;
        int totalSize = tableSize;

        if (tableBits > maxLength)
        {
            tableBits = maxLength;
            tableSize = 1 << tableBits;
        }

        int key = 0;
        int keyStep = ReverseBitsLowest;
        int bits = 1;
        int step = 2;
        int symbol;
        do
        {
            symbol = bits - (MaxCodeLength + 1);
            for (int bitsCount = count[bits]; bitsCount != 0; --bitsCount)
            {
                symbol = symbolLists[SymbolListsBase + symbol];
                BrotliHuffmanCode code = new(bits, symbol);
                ReplicateValue(rootTable.AsSpan(table + ReverseBits(key)), step, tableSize, code);
                key += keyStep;
            }

            step <<= 1;
            keyStep >>= 1;
        }
        while (++bits <= tableBits);

        while (totalSize != tableSize)
        {
            rootTable.AsSpan(table, tableSize).CopyTo(rootTable.AsSpan(table + tableSize));
            tableSize <<= 1;
        }

        keyStep = ReverseBitsLowest >> (rootBits - 1);
        int subKey = ReverseBitsLowest << 1;
        int subKeyStep = ReverseBitsLowest;
        step = 2;
        for (int len = rootBits + 1; len <= maxLength; ++len)
        {
            symbol = len - (MaxCodeLength + 1);
            for (; count[len] != 0; --count[len])
            {
                if (subKey == ReverseBitsLowest << 1)
                {
                    table += tableSize;
                    tableBits = NextTableBitSize(count, len, rootBits);
                    tableSize = 1 << tableBits;
                    totalSize += tableSize;
                    subKey = ReverseBits(key);
                    key += keyStep;
                    rootTable[rootOffset + subKey] = new BrotliHuffmanCode(tableBits + rootBits, table - rootOffset - subKey);
                    subKey = 0;
                }

                symbol = symbolLists[SymbolListsBase + symbol];
                BrotliHuffmanCode code = new(len - rootBits, symbol);
                ReplicateValue(rootTable.AsSpan(table + ReverseBits(subKey)), step, tableSize, code);
                subKey += subKeyStep;
            }

            step <<= 1;
            subKeyStep >>= 1;
        }

        return totalSize;
    }

    public static int BuildSimpleHuffmanTable(BrotliHuffmanCode[] table, int offset, int rootBits, Span<ushort> val, int numSymbols)
    {
        int tableSize = 1;
        int goalSize = 1 << rootBits;
        Span<BrotliHuffmanCode> t = table.AsSpan(offset, goalSize);
        switch (numSymbols)
        {
            case 0:
                t[0] = new BrotliHuffmanCode(0, val[0]);
                break;
            case 1:
                if (val[1] > val[0])
                {
                    t[0] = new BrotliHuffmanCode(1, val[0]);
                    t[1] = new BrotliHuffmanCode(1, val[1]);
                }
                else
                {
                    t[0] = new BrotliHuffmanCode(1, val[1]);
                    t[1] = new BrotliHuffmanCode(1, val[0]);
                }

                tableSize = 2;
                break;
            case 2:
                t[0] = new BrotliHuffmanCode(1, val[0]);
                t[2] = new BrotliHuffmanCode(1, val[0]);
                if (val[2] > val[1])
                {
                    t[1] = new BrotliHuffmanCode(2, val[1]);
                    t[3] = new BrotliHuffmanCode(2, val[2]);
                }
                else
                {
                    t[1] = new BrotliHuffmanCode(2, val[2]);
                    t[3] = new BrotliHuffmanCode(2, val[1]);
                }

                tableSize = 4;
                break;
            case 3:
                for (int i = 0; i < 3; ++i)
                {
                    for (int k = i + 1; k < 4; ++k)
                    {
                        if (val[k] < val[i])
                        {
                            (val[k], val[i]) = (val[i], val[k]);
                        }
                    }
                }

                t[0] = new BrotliHuffmanCode(2, val[0]);
                t[2] = new BrotliHuffmanCode(2, val[1]);
                t[1] = new BrotliHuffmanCode(2, val[2]);
                t[3] = new BrotliHuffmanCode(2, val[3]);
                tableSize = 4;
                break;
            case 4:
                if (val[3] < val[2])
                {
                    (val[3], val[2]) = (val[2], val[3]);
                }

                t[0] = new BrotliHuffmanCode(1, val[0]);
                t[1] = new BrotliHuffmanCode(2, val[1]);
                t[2] = new BrotliHuffmanCode(1, val[0]);
                t[3] = new BrotliHuffmanCode(3, val[2]);
                t[4] = new BrotliHuffmanCode(1, val[0]);
                t[5] = new BrotliHuffmanCode(2, val[1]);
                t[6] = new BrotliHuffmanCode(1, val[0]);
                t[7] = new BrotliHuffmanCode(3, val[3]);
                tableSize = 8;
                break;
        }

        while (tableSize != goalSize)
        {
            t.Slice(0, tableSize).CopyTo(t.Slice(tableSize));
            tableSize <<= 1;
        }

        return goalSize;
    }

    // Reverses the low 8 bits. The C code reads kReverseBits, a table of the 256 values.
    private static int ReverseBits(int num)
    {
        num = ((num & 0xF0) >> 4) | ((num & 0x0F) << 4);
        num = ((num & 0xCC) >> 2) | ((num & 0x33) << 2);
        return ((num & 0xAA) >> 1) | ((num & 0x55) << 1);
    }

    private static void ReplicateValue(Span<BrotliHuffmanCode> table, int step, int end, BrotliHuffmanCode code)
    {
        do
        {
            end -= step;
            table[end] = code;
        }
        while (end > 0);
    }

    private static int NextTableBitSize(ReadOnlySpan<ushort> count, int len, int rootBits)
    {
        int left = 1 << (len - rootBits);
        while (len < MaxCodeLength)
        {
            left -= count[len];
            if (left <= 0)
            {
                break;
            }

            ++len;
            left <<= 1;
        }

        return len - rootBits;
    }
}
