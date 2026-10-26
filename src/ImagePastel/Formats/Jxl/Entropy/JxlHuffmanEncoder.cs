// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_huffman.h and lib/jxl/enc_huffman.cc (StoreHuffmanTreeOfHuffmanTreeToBitMask,
// StoreHuffmanTreeToBitMask, StoreSimpleHuffmanTree, StoreHuffmanTree, BuildAndStoreHuffmanTree).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;

namespace PinataLabs.ImagePastel.Formats.Jxl.Entropy;

/// <summary>
/// Writes a prefix code, as <see cref="JxlHuffmanDecodingData.ReadFromBitStream"/> reads it: a simple
/// code for at most four symbols, otherwise the code lengths, themselves prefix coded.
/// </summary>
internal static class JxlHuffmanEncoder
{
    private const int CodeLengthCodes = 18;

    /// <summary>
    /// Builds a Huffman code for <paramref name="histogram"/> and writes it, in a format that
    /// <see cref="JxlHuffmanDecodingData.ReadFromBitStream"/> reads.
    /// </summary>
    /// <typeparam name="TWriter">The writer type.</typeparam>
    /// <param name="histogram">The population count of each symbol.</param>
    /// <param name="length">The alphabet size.</param>
    /// <param name="depth">Receives the code length of each symbol; must be zero on entry.</param>
    /// <param name="bits">Receives the code of each symbol; must be zero on entry.</param>
    /// <param name="writer">The writer.</param>
    public static void BuildAndStoreHuffmanTree<TWriter>(ReadOnlySpan<uint> histogram, int length, Span<byte> depth, Span<ushort> bits, TWriter writer)
        where TWriter : IJxlBitSink
    {
        int count = 0;
        Span<int> s4 = stackalloc int[4];
        s4.Clear();
        for (int i = 0; i < length; i++)
        {
            if (histogram[i] != 0)
            {
                if (count < 4)
                {
                    s4[count] = i;
                }
                else if (count > 4)
                {
                    break;
                }

                count++;
            }
        }

        int maxBitsCounter = length - 1;
        int maxBits = 0;
        while (maxBitsCounter != 0)
        {
            maxBitsCounter >>= 1;
            ++maxBits;
        }

        if (count <= 1)
        {
            // Output symbol bits and depths are initialized with 0, nothing to do.
            writer.Write(4, 1);
            writer.Write(maxBits, (ulong)s4[0]);
            return;
        }

        JxlHuffmanTree.CreateHuffmanTree(histogram, length, 15, depth);
        JxlHuffmanTree.ConvertBitDepthsToSymbols(depth, length, bits);

        if (count <= 4)
        {
            StoreSimpleHuffmanTree(depth, s4, count, maxBits, writer);
        }
        else
        {
            StoreHuffmanTree(depth, length, writer);
        }
    }

    private static void StoreHuffmanTreeOfHuffmanTreeToBitMask<TWriter>(int numCodes, ReadOnlySpan<byte> codeLengthBitdepth, TWriter writer)
        where TWriter : IJxlBitSink
    {
        ReadOnlySpan<byte> storageOrder = new byte[] { 1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

        // The bit lengths of the Huffman code over the code length alphabet
        // are compressed with the following static Huffman code:
        //   Symbol   Code
        //   ------   ----
        //   0          00
        //   1        1110
        //   2         110
        //   3          01
        //   4          10
        //   5        1111
        ReadOnlySpan<byte> huffmanBitLengthHuffmanCodeSymbols = new byte[] { 0, 7, 3, 2, 1, 15 };
        ReadOnlySpan<byte> huffmanBitLengthHuffmanCodeBitLengths = new byte[] { 2, 4, 3, 2, 2, 4 };

        // Throw away trailing zeros:
        int codesToStore = CodeLengthCodes;
        if (numCodes > 1)
        {
            for (; codesToStore > 0; --codesToStore)
            {
                if (codeLengthBitdepth[storageOrder[codesToStore - 1]] != 0)
                {
                    break;
                }
            }
        }

        int skipSome = 0; // skips none.
        if (codeLengthBitdepth[storageOrder[0]] == 0 && codeLengthBitdepth[storageOrder[1]] == 0)
        {
            skipSome = 2; // skips two.
            if (codeLengthBitdepth[storageOrder[2]] == 0)
            {
                skipSome = 3; // skips three.
            }
        }

        writer.Write(2, (ulong)skipSome);
        for (int i = skipSome; i < codesToStore; ++i)
        {
            int l = codeLengthBitdepth[storageOrder[i]];
            writer.Write(huffmanBitLengthHuffmanCodeBitLengths[l], huffmanBitLengthHuffmanCodeSymbols[l]);
        }
    }

    private static void StoreHuffmanTreeToBitMask<TWriter>(int huffmanTreeSize, ReadOnlySpan<byte> huffmanTree, ReadOnlySpan<byte> huffmanTreeExtraBits, ReadOnlySpan<byte> codeLengthBitdepth, ReadOnlySpan<ushort> codeLengthBitdepthSymbols, TWriter writer)
        where TWriter : IJxlBitSink
    {
        for (int i = 0; i < huffmanTreeSize; ++i)
        {
            int ix = huffmanTree[i];
            writer.Write(codeLengthBitdepth[ix], codeLengthBitdepthSymbols[ix]);
            DebugGuard.IsTrue(ix <= 17, "Code length symbol out of range");

            // Extra bits
            switch (ix)
            {
                case 16:
                    writer.Write(2, huffmanTreeExtraBits[i]);
                    break;
                case 17:
                    writer.Write(3, huffmanTreeExtraBits[i]);
                    break;
                default:
                    // no-op
                    break;
            }
        }
    }

    private static void StoreSimpleHuffmanTree<TWriter>(ReadOnlySpan<byte> depths, Span<int> symbols, int numSymbols, int maxBits, TWriter writer)
        where TWriter : IJxlBitSink
    {
        // value of 1 indicates a simple Huffman code
        writer.Write(2, 1);
        writer.Write(2, (ulong)(numSymbols - 1)); // NSYM - 1

        // Sort
        for (int i = 0; i < numSymbols; i++)
        {
            for (int j = i + 1; j < numSymbols; j++)
            {
                if (depths[symbols[j]] < depths[symbols[i]])
                {
                    (symbols[j], symbols[i]) = (symbols[i], symbols[j]);
                }
            }
        }

        // libjxl spells out the two, three and four symbol cases; they write the same symbols.
        for (int i = 0; i < numSymbols; i++)
        {
            writer.Write(maxBits, (ulong)symbols[i]);
        }

        if (numSymbols == 4)
        {
            // tree-select
            writer.Write(1, depths[symbols[0]] == 1 ? 1UL : 0UL);
        }
    }

    // num = alphabet size
    // depths = symbol depths
    private static void StoreHuffmanTree<TWriter>(ReadOnlySpan<byte> depths, int num, TWriter writer)
        where TWriter : IJxlBitSink
    {
        // Write the Huffman tree into the compact representation.
        byte[] arena = new byte[2 * num];
        Span<byte> huffmanTree = arena.AsSpan(0, num);
        Span<byte> huffmanTreeExtraBits = arena.AsSpan(num, num);
        int huffmanTreeSize = 0;
        JxlHuffmanTree.WriteHuffmanTree(depths, num, ref huffmanTreeSize, huffmanTree, huffmanTreeExtraBits);

        // Calculate the statistics of the Huffman tree in the compact representation.
        Span<uint> huffmanTreeHistogram = stackalloc uint[CodeLengthCodes];
        huffmanTreeHistogram.Clear();
        for (int i = 0; i < huffmanTreeSize; ++i)
        {
            ++huffmanTreeHistogram[huffmanTree[i]];
        }

        int numCodes = 0;
        int code = 0;
        for (int i = 0; i < CodeLengthCodes; ++i)
        {
            if (huffmanTreeHistogram[i] != 0)
            {
                if (numCodes == 0)
                {
                    code = i;
                    numCodes = 1;
                }
                else if (numCodes == 1)
                {
                    numCodes = 2;
                    break;
                }
            }
        }

        // Calculate another Huffman tree to use for compressing both the
        // earlier Huffman tree with.
        Span<byte> codeLengthBitdepth = stackalloc byte[CodeLengthCodes];
        codeLengthBitdepth.Clear();
        Span<ushort> codeLengthBitdepthSymbols = stackalloc ushort[CodeLengthCodes];
        codeLengthBitdepthSymbols.Clear();
        JxlHuffmanTree.CreateHuffmanTree(huffmanTreeHistogram, CodeLengthCodes, 5, codeLengthBitdepth);
        JxlHuffmanTree.ConvertBitDepthsToSymbols(codeLengthBitdepth, CodeLengthCodes, codeLengthBitdepthSymbols);

        // Now, we have all the data, let's start storing it
        StoreHuffmanTreeOfHuffmanTreeToBitMask(numCodes, codeLengthBitdepth, writer);

        if (numCodes == 1)
        {
            codeLengthBitdepth[code] = 0;
        }

        // Store the real huffman tree now.
        StoreHuffmanTreeToBitMask(huffmanTreeSize, huffmanTree, huffmanTreeExtraBits, codeLengthBitdepth, codeLengthBitdepthSymbols, writer);
    }
}
