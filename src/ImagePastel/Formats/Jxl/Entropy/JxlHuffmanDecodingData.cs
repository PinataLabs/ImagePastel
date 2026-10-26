// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_huffman.h, lib/jxl/dec_huffman.cc, lib/jxl/huffman_table.h and lib/jxl/huffman_table.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Runtime.CompilerServices;
using PinataLabs.ImagePastel.Formats.Jxl.Base;

namespace PinataLabs.ImagePastel.Formats.Jxl.Entropy;

/// <summary>
/// A prefix code (Brotli-style canonical Huffman code), decoded through a two-level lookup table.
/// </summary>
internal sealed class JxlHuffmanDecodingData
{
    /// <summary>The number of bits of the root lookup table.</summary>
    public const int HuffmanTableBits = 8;

    private const int CodeLengthCodes = 18;

    private const byte DefaultCodeLength = 8;

    private const byte CodeLengthRepeatCode = 16;

    // Static Huffman code for the code length code lengths.
    private static readonly JxlHuffmanCode[] CodeLengthCodeLengthsHuff =
    {
        new(2, 0), new(2, 4), new(2, 3), new(3, 2), new(2, 0), new(2, 4), new(2, 3), new(4, 1),
        new(2, 0), new(2, 4), new(2, 3), new(3, 2), new(2, 0), new(2, 4), new(2, 3), new(4, 5),
    };

    private static ReadOnlySpan<byte> CodeLengthCodeOrder => new byte[] { 1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

    /// <summary>Gets the lookup table.</summary>
    public JxlHuffmanCode[] Table { get; private set; } = Array.Empty<JxlHuffmanCode>();

    /// <summary>
    /// Makes a code with one symbol, 0, that consumes no bits, for an alphabet of size 1.
    /// </summary>
    public void SetSingleZeroSymbol() => this.Table = new JxlHuffmanCode[1 << HuffmanTableBits];

    /// <summary>Reads the code for an alphabet of <paramref name="alphabetSize"/> symbols.</summary>
    /// <param name="alphabetSize">The alphabet size.</param>
    /// <param name="br">The reader.</param>
    /// <returns><see langword="false"/> if the code is invalid.</returns>
    public bool ReadFromBitStream(int alphabetSize, JxlBitReader br)
    {
        if (alphabetSize > 1 << JxlAnsParams.PrefixMaxBits)
        {
            return false;
        }

        // 1 for a simple code; 0 for no skipping, 2 skips 2 code lengths, 3 skips 3 code lengths.
        int simpleCodeOrSkip = (int)br.ReadBits(2);
        if (simpleCodeOrSkip == 1)
        {
            this.Table = new JxlHuffmanCode[1 << HuffmanTableBits];
            return ReadSimpleCode(alphabetSize, br, this.Table);
        }

        byte[] codeLengths = new byte[alphabetSize];
        Span<byte> codeLengthCodeLengths = stackalloc byte[CodeLengthCodes];
        codeLengthCodeLengths.Clear();
        int space = 32;
        int numCodes = 0;
        for (int i = simpleCodeOrSkip; i < CodeLengthCodes && space > 0; ++i)
        {
            int codeLenIdx = CodeLengthCodeOrder[i];
            br.Refill();
            JxlHuffmanCode p = CodeLengthCodeLengthsHuff[br.PeekBits(4)];
            br.Consume(p.Bits);
            byte v = (byte)p.Value;
            codeLengthCodeLengths[codeLenIdx] = v;
            if (v != 0)
            {
                space -= 32 >> v;
                ++numCodes;
            }
        }

        bool ok = (numCodes == 1 || space == 0) && ReadHuffmanCodeLengths(codeLengthCodeLengths, alphabetSize, codeLengths, br);
        if (!ok)
        {
            return false;
        }

        Span<ushort> counts = stackalloc ushort[16];
        counts.Clear();
        for (int i = 0; i < alphabetSize; ++i)
        {
            ++counts[codeLengths[i]];
        }

        JxlHuffmanCode[] table = new JxlHuffmanCode[alphabetSize + 376];
        int tableSize = BuildHuffmanTable(table, HuffmanTableBits, codeLengths, counts);
        Array.Resize(ref table, tableSize);
        this.Table = table;
        return tableSize > 0;
    }

    /// <summary>Reads one symbol. The caller has refilled the reader.</summary>
    /// <param name="br">The reader.</param>
    /// <returns>The symbol.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public int ReadSymbol(JxlBitReader br)
    {
        JxlHuffmanCode[] table = this.Table;
        int index = (int)br.PeekBits(HuffmanTableBits);
        int nBits = table[index].Bits;
        if (nBits > HuffmanTableBits)
        {
            br.Consume(HuffmanTableBits);
            nBits -= HuffmanTableBits;
            index += table[index].Value;
            index += (int)br.PeekBits(nBits);
        }

        br.Consume(table[index].Bits);
        return table[index].Value;
    }

    /// <summary>
    /// Builds a lookup table from code lengths in symbol order. <paramref name="count"/> is the histogram
    /// of code lengths and is consumed.
    /// </summary>
    /// <param name="rootTable">The table to fill.</param>
    /// <param name="rootBits">The number of bits of the root table.</param>
    /// <param name="codeLengths">The code length of each symbol.</param>
    /// <param name="count">The number of symbols of each code length.</param>
    /// <returns>The populated table size, or 0 for an invalid tree.</returns>
    internal static int BuildHuffmanTable(JxlHuffmanCode[] rootTable, int rootBits, ReadOnlySpan<byte> codeLengths, Span<ushort> count)
    {
        const int maxBits = JxlAnsParams.PrefixMaxBits;

        // Offsets in the sorted table for each length.
        Span<ushort> offset = stackalloc ushort[maxBits + 1];
        int maxLength = 1;

        if (codeLengths.Length > 1 << maxBits)
        {
            return 0;
        }

        // Symbols sorted by code length.
        ushort[] sorted = new ushort[codeLengths.Length];

        // Generate offsets into the sorted symbol table by code length.
        {
            ushort sum = 0;
            for (int len = 1; len <= maxBits; len++)
            {
                offset[len] = sum;
                if (count[len] != 0)
                {
                    sum = (ushort)(sum + count[len]);
                    maxLength = len;
                }
            }
        }

        // Sort symbols by length, by symbol order within each length.
        for (int symbol = 0; symbol < codeLengths.Length; symbol++)
        {
            if (codeLengths[symbol] != 0)
            {
                sorted[offset[codeLengths[symbol]]++] = (ushort)symbol;
            }
        }

        int table = 0;
        int tableBits = rootBits;
        int tableSize = 1 << tableBits;
        int totalSize = tableSize;
        JxlHuffmanCode code;

        // Special case: a code with only one value.
        if (offset[maxBits] == 1)
        {
            code.Bits = 0;
            code.Value = sorted[0];
            for (int k = 0; k < totalSize; ++k)
            {
                rootTable[table + k] = code;
            }

            return totalSize;
        }

        // Fill in the root table. Reduce the table size if possible, and create the repetitions
        // by copying.
        if (tableBits > maxLength)
        {
            tableBits = maxLength;
            tableSize = 1 << tableBits;
        }

        int key = 0;
        int sym = 0;
        code.Bits = 1;
        int step = 2;
        do
        {
            for (; count[code.Bits] != 0; --count[code.Bits])
            {
                code.Value = sorted[sym++];
                ReplicateValue(rootTable, table + key, step, tableSize, code);
                key = GetNextKey(key, code.Bits);
            }

            step <<= 1;
        }
        while (++code.Bits <= tableBits);

        // If rootBits != tableBits, only a fraction of the table was created: replicate it.
        while (totalSize != tableSize)
        {
            Array.Copy(rootTable, table, rootTable, table + tableSize, tableSize);
            tableSize <<= 1;
        }

        // Fill in 2nd level tables and add pointers to the root table.
        int mask = totalSize - 1;
        int low = -1;
        step = 2;
        for (int len = rootBits + 1; len <= maxLength; ++len, step <<= 1)
        {
            for (; count[len] != 0; --count[len])
            {
                if ((key & mask) != low)
                {
                    table += tableSize;
                    tableBits = NextTableBitSize(count, len, rootBits);
                    tableSize = 1 << tableBits;
                    totalSize += tableSize;
                    low = key & mask;
                    rootTable[low].Bits = (byte)(tableBits + rootBits);
                    rootTable[low].Value = (ushort)(table - low);
                }

                code.Bits = (byte)(len - rootBits);
                code.Value = sorted[sym++];
                ReplicateValue(rootTable, table + (key >> rootBits), step, tableSize, code);
                key = GetNextKey(key, len);
            }
        }

        return totalSize;
    }

    // Returns reverse(reverse(key, len) + 1, len), where reverse(key, len) is the bit-wise reversal
    // of the len least significant bits of key.
    private static int GetNextKey(int key, int len)
    {
        int step = 1 << (len - 1);
        while ((key & step) != 0)
        {
            step >>= 1;
        }

        return (key & (step - 1)) + step;
    }

    // Stores code in table[0], table[step], table[2*step], ..., table[end - step].
    // Assumes that end is an integer multiple of step.
    private static void ReplicateValue(JxlHuffmanCode[] table, int start, int step, int end, JxlHuffmanCode code)
    {
        do
        {
            end -= step;
            table[start + end] = code;
        }
        while (end > 0);
    }

    // Returns the table width of the next 2nd level table. count is the histogram of bit lengths
    // for the remaining symbols, len is the code length of the next processed symbol.
    private static int NextTableBitSize(ReadOnlySpan<ushort> count, int len, int rootBits)
    {
        int left = 1 << (len - rootBits);
        while (len < JxlAnsParams.PrefixMaxBits)
        {
            if (left <= count[len])
            {
                break;
            }

            left -= count[len];
            ++len;
            left <<= 1;
        }

        return len - rootBits;
    }

    private static bool ReadHuffmanCodeLengths(ReadOnlySpan<byte> codeLengthCodeLengths, int numSymbols, byte[] codeLengths, JxlBitReader br)
    {
        int symbol = 0;
        byte prevCodeLen = DefaultCodeLength;
        int repeat = 0;
        byte repeatCodeLen = 0;
        int space = 32768;
        JxlHuffmanCode[] table = new JxlHuffmanCode[32];

        Span<ushort> counts = stackalloc ushort[16];
        counts.Clear();
        for (int i = 0; i < CodeLengthCodes; ++i)
        {
            ++counts[codeLengthCodeLengths[i]];
        }

        if (BuildHuffmanTable(table, 5, codeLengthCodeLengths, counts) == 0)
        {
            return false;
        }

        while (symbol < numSymbols && space > 0)
        {
            br.Refill();
            JxlHuffmanCode p = table[br.PeekBits(5)];
            br.Consume(p.Bits);
            byte codeLen = (byte)p.Value;
            if (codeLen < CodeLengthRepeatCode)
            {
                repeat = 0;
                codeLengths[symbol++] = codeLen;
                if (codeLen != 0)
                {
                    prevCodeLen = codeLen;
                    space -= 32768 >> codeLen;
                }
            }
            else
            {
                int extraBits = codeLen - 14;
                byte newLen = 0;
                if (codeLen == CodeLengthRepeatCode)
                {
                    newLen = prevCodeLen;
                }

                if (repeatCodeLen != newLen)
                {
                    repeat = 0;
                    repeatCodeLen = newLen;
                }

                int oldRepeat = repeat;
                if (repeat > 0)
                {
                    repeat -= 2;
                    repeat <<= extraBits;
                }

                repeat += (int)br.ReadBits(extraBits) + 3;
                int repeatDelta = repeat - oldRepeat;
                if (symbol + repeatDelta > numSymbols)
                {
                    return false;
                }

                codeLengths.AsSpan(symbol, repeatDelta).Fill(repeatCodeLen);
                symbol += repeatDelta;
                if (repeatCodeLen != 0)
                {
                    space -= repeatDelta << (15 - repeatCodeLen);
                }
            }
        }

        if (space != 0)
        {
            return false;
        }

        codeLengths.AsSpan(symbol, numSymbols - symbol).Clear();
        return true;
    }

    private static bool ReadSimpleCode(int alphabetSize, JxlBitReader br, JxlHuffmanCode[] table)
    {
        int maxBits = alphabetSize > 1 ? JxlBits.FloorLog2Nonzero((uint)(alphabetSize - 1)) + 1 : 0;

        int numSymbols = (int)br.ReadBits(2) + 1;

        Span<ushort> symbols = stackalloc ushort[4];
        symbols.Clear();
        for (int i = 0; i < numSymbols; ++i)
        {
            ushort symbol = (ushort)br.ReadBits(maxBits);
            if (symbol >= alphabetSize)
            {
                return false;
            }

            symbols[i] = symbol;
        }

        for (int i = 0; i < numSymbols - 1; ++i)
        {
            for (int j = i + 1; j < numSymbols; ++j)
            {
                if (symbols[i] == symbols[j])
                {
                    return false;
                }
            }
        }

        if (numSymbols == 4)
        {
            numSymbols += (int)br.ReadBits(1);
        }

        int tableSize = 1;
        switch (numSymbols)
        {
            case 1:
                table[0] = new JxlHuffmanCode(0, symbols[0]);
                break;
            case 2:
                if (symbols[0] > symbols[1])
                {
                    Swap(symbols, 0, 1);
                }

                table[0] = new JxlHuffmanCode(1, symbols[0]);
                table[1] = new JxlHuffmanCode(1, symbols[1]);
                tableSize = 2;
                break;
            case 3:
                if (symbols[1] > symbols[2])
                {
                    Swap(symbols, 1, 2);
                }

                table[0] = new JxlHuffmanCode(1, symbols[0]);
                table[2] = new JxlHuffmanCode(1, symbols[0]);
                table[1] = new JxlHuffmanCode(2, symbols[1]);
                table[3] = new JxlHuffmanCode(2, symbols[2]);
                tableSize = 4;
                break;
            case 4:
                for (int i = 0; i < 3; ++i)
                {
                    for (int j = i + 1; j < 4; ++j)
                    {
                        if (symbols[i] > symbols[j])
                        {
                            Swap(symbols, i, j);
                        }
                    }
                }

                table[0] = new JxlHuffmanCode(2, symbols[0]);
                table[2] = new JxlHuffmanCode(2, symbols[1]);
                table[1] = new JxlHuffmanCode(2, symbols[2]);
                table[3] = new JxlHuffmanCode(2, symbols[3]);
                tableSize = 4;
                break;
            case 5:
                if (symbols[2] > symbols[3])
                {
                    Swap(symbols, 2, 3);
                }

                table[0] = new JxlHuffmanCode(1, symbols[0]);
                table[1] = new JxlHuffmanCode(2, symbols[1]);
                table[2] = new JxlHuffmanCode(1, symbols[0]);
                table[3] = new JxlHuffmanCode(3, symbols[2]);
                table[4] = new JxlHuffmanCode(1, symbols[0]);
                table[5] = new JxlHuffmanCode(2, symbols[1]);
                table[6] = new JxlHuffmanCode(1, symbols[0]);
                table[7] = new JxlHuffmanCode(3, symbols[3]);
                tableSize = 8;
                break;
            default:
                return false;
        }

        const int goalSize = 1 << HuffmanTableBits;
        while (tableSize != goalSize)
        {
            Array.Copy(table, 0, table, tableSize, tableSize);
            tableSize <<= 1;
        }

        return true;
    }

    private static void Swap(Span<ushort> symbols, int i, int j) => (symbols[i], symbols[j]) = (symbols[j], symbols[i]);
}
