// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_huffman_tree.h and lib/jxl/enc_huffman_tree.cc (HuffmanTree, SetDepth, CreateHuffmanTree,
// WriteHuffmanTree, ConvertBitDepthsToSymbols).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;

namespace PinataLabs.ImagePastel.Formats.Jxl.Entropy;

/// <summary>
/// Builds length-limited Huffman codes and their compact, run-length coded representation, for the
/// prefix-code writer. It is the encoder's side of <see cref="JxlHuffmanDecodingData"/>.
/// </summary>
internal static class JxlHuffmanTree
{
    /// <summary>
    /// Computes the code length of each symbol of a Huffman code for the population counts in
    /// <paramref name="data"/>, with no code longer than <paramref name="treeLimit"/> bits. A symbol
    /// with a count of 0 gets no code.
    /// </summary>
    /// <param name="data">The population counts.</param>
    /// <param name="length">The number of symbols.</param>
    /// <param name="treeLimit">The maximum code length.</param>
    /// <param name="depth">Receives the code length of each symbol.</param>
    public static void CreateHuffmanTree(ReadOnlySpan<uint> data, int length, int treeLimit, Span<byte> depth)
    {
        // For block sizes below 64 kB, we never need to do a second iteration
        // of this loop. Probably all of our block sizes will be smaller than
        // that, so this loop is mostly of academic interest. If we actually
        // would need this, we would be better off with the Katajainen algorithm.
        for (uint countLimit = 1; ; countLimit *= 2)
        {
            List<Node> tree = new((2 * length) + 1);

            for (int i = length; i != 0;)
            {
                --i;
                if (data[i] != 0)
                {
                    uint count = Math.Max(data[i], countLimit - 1);
                    tree.Add(new Node(count, -1, (short)i));
                }
            }

            int n = tree.Count;
            if (n == 1)
            {
                // Fake value; will be fixed on upper level.
                depth[tree[0].IndexRightOrValue] = 1;
                break;
            }

            // libjxl's std::sort is not stable, but Compare is a strict total order on the leaves
            // (their values differ), so any sort gives the same order.
            tree.Sort(Compare);

            // The nodes are:
            // [0, n): the sorted leaf nodes that we start with.
            // [n]: we add a sentinel here.
            // [n + 1, 2n): new parent nodes are added here, starting from
            //              (n+1). These are naturally in ascending order.
            // [2n]: we add a sentinel at the end as well.
            // There will be (2n+1) elements at the end.
            Node sentinel = new(uint.MaxValue, -1, -1);
            tree.Add(sentinel);
            tree.Add(sentinel);

            int leaf = 0; // Points to the next leaf node.
            int inner = n + 1; // Points to the next non-leaf node.
            for (int k = n - 1; k != 0; --k)
            {
                int left;
                int right;
                if (tree[leaf].TotalCount <= tree[inner].TotalCount)
                {
                    left = leaf;
                    ++leaf;
                }
                else
                {
                    left = inner;
                    ++inner;
                }

                if (tree[leaf].TotalCount <= tree[inner].TotalCount)
                {
                    right = leaf;
                    ++leaf;
                }
                else
                {
                    right = inner;
                    ++inner;
                }

                // The sentinel node becomes the parent node.
                int jEnd = tree.Count - 1;
                tree[jEnd] = new Node(tree[left].TotalCount + tree[right].TotalCount, (short)left, (short)right);

                // Add back the last sentinel node.
                tree.Add(sentinel);
            }

            DebugGuard.IsTrue(tree.Count == (2 * n) + 1, "Huffman tree has 2n + 1 nodes");
            SetDepth(tree[(2 * n) - 1], tree, depth, 0);

            // We need to pack the Huffman tree in tree_limit bits.
            // If this was not successful, add fake entities to the lowest values
            // and retry.
            byte maxDepth = 0;
            for (int i = 0; i < length; i++)
            {
                maxDepth = Math.Max(maxDepth, depth[i]);
            }

            if (maxDepth <= treeLimit)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Writes the code lengths in <paramref name="depth"/> as the code length alphabet: lengths 0 to
    /// 15, 16 to repeat the previous non-zero length, and 17 to repeat a zero length, each with its
    /// extra bits. Trailing zeros are dropped.
    /// </summary>
    /// <param name="depth">The code length of each symbol.</param>
    /// <param name="length">The number of symbols.</param>
    /// <param name="treeSize">The number of entries written to <paramref name="tree"/>.</param>
    /// <param name="tree">Receives the code length symbols.</param>
    /// <param name="extraBitsData">Receives the extra bits of each code length symbol.</param>
    public static void WriteHuffmanTree(ReadOnlySpan<byte> depth, int length, ref int treeSize, Span<byte> tree, Span<byte> extraBitsData)
    {
        byte previousValue = 8;

        // Throw away trailing zeros.
        int newLength = length;
        for (int i = 0; i < length; ++i)
        {
            if (depth[length - i - 1] == 0)
            {
                --newLength;
            }
            else
            {
                break;
            }
        }

        // First gather statistics on if it is a good idea to do rle.
        bool useRleForNonZero = false;
        bool useRleForZero = false;
        if (length > 50)
        {
            // Find rle coding for longer codes.
            // Shorter codes seem not to benefit from rle.
            DecideOverRleUse(depth, newLength, out useRleForNonZero, out useRleForZero);
        }

        // Actual rle coding.
        for (int i = 0; i < newLength;)
        {
            byte value = depth[i];
            int reps = 1;
            if ((value != 0 && useRleForNonZero) || (value == 0 && useRleForZero))
            {
                for (int k = i + 1; k < newLength && depth[k] == value; ++k)
                {
                    ++reps;
                }
            }

            if (value == 0)
            {
                WriteHuffmanTreeRepetitionsZeros(reps, ref treeSize, tree, extraBitsData);
            }
            else
            {
                WriteHuffmanTreeRepetitions(previousValue, value, reps, ref treeSize, tree, extraBitsData);
                previousValue = value;
            }

            i += reps;
        }
    }

    /// <summary>
    /// Computes the canonical code of each symbol from its code length, bit-reversed for the
    /// least-significant-bit-first writer.
    /// </summary>
    /// <param name="depth">The code length of each symbol.</param>
    /// <param name="len">The number of symbols.</param>
    /// <param name="bits">Receives the code of each symbol with a non-zero length.</param>
    public static void ConvertBitDepthsToSymbols(ReadOnlySpan<byte> depth, int len, Span<ushort> bits)
    {
        // In Brotli, all bit depths are [1..15]
        // 0 bit depth means that the symbol does not exist.
        const int maxBits = 16; // 0..15 are values for bits
        Span<ushort> blCount = stackalloc ushort[maxBits];
        blCount.Clear();
        for (int i = 0; i < len; ++i)
        {
            ++blCount[depth[i]];
        }

        blCount[0] = 0;

        Span<ushort> nextCode = stackalloc ushort[maxBits];
        nextCode[0] = 0;
        int code = 0;
        for (int i = 1; i < maxBits; ++i)
        {
            code = (code + blCount[i - 1]) << 1;
            nextCode[i] = (ushort)code;
        }

        for (int i = 0; i < len; ++i)
        {
            if (depth[i] != 0)
            {
                bits[i] = ReverseBits(depth[i], nextCode[depth[i]]++);
            }
        }
    }

    private static void SetDepth(Node p, List<Node> pool, Span<byte> depth, byte level)
    {
        if (p.IndexLeft >= 0)
        {
            ++level;
            SetDepth(pool[p.IndexLeft], pool, depth, level);
            SetDepth(pool[p.IndexRightOrValue], pool, depth, level);
        }
        else
        {
            depth[p.IndexRightOrValue] = level;
        }
    }

    // Compare the root nodes, least popular first; indices are in decreasing order
    // before sorting is applied.
    private static int Compare(Node v0, Node v1)
    {
        if (v0.TotalCount != v1.TotalCount)
        {
            return v0.TotalCount < v1.TotalCount ? -1 : 1;
        }

        return v1.IndexRightOrValue.CompareTo(v0.IndexRightOrValue);
    }

    private static void Reverse(Span<byte> v, int start, int end)
    {
        --end;
        while (start < end)
        {
            (v[start], v[end]) = (v[end], v[start]);
            ++start;
            --end;
        }
    }

    private static void WriteHuffmanTreeRepetitions(byte previousValue, byte value, int repetitions, ref int treeSize, Span<byte> tree, Span<byte> extraBitsData)
    {
        DebugGuard.IsTrue(repetitions > 0, "At least one repetition");
        if (previousValue != value)
        {
            tree[treeSize] = value;
            extraBitsData[treeSize] = 0;
            ++treeSize;
            --repetitions;
        }

        if (repetitions == 7)
        {
            tree[treeSize] = value;
            extraBitsData[treeSize] = 0;
            ++treeSize;
            --repetitions;
        }

        if (repetitions < 3)
        {
            for (int i = 0; i < repetitions; ++i)
            {
                tree[treeSize] = value;
                extraBitsData[treeSize] = 0;
                ++treeSize;
            }
        }
        else
        {
            repetitions -= 3;
            int start = treeSize;
            while (true)
            {
                tree[treeSize] = 16;
                extraBitsData[treeSize] = (byte)(repetitions & 0x3);
                ++treeSize;
                repetitions >>= 2;
                if (repetitions == 0)
                {
                    break;
                }

                --repetitions;
            }

            Reverse(tree, start, treeSize);
            Reverse(extraBitsData, start, treeSize);
        }
    }

    private static void WriteHuffmanTreeRepetitionsZeros(int repetitions, ref int treeSize, Span<byte> tree, Span<byte> extraBitsData)
    {
        if (repetitions == 11)
        {
            tree[treeSize] = 0;
            extraBitsData[treeSize] = 0;
            ++treeSize;
            --repetitions;
        }

        if (repetitions < 3)
        {
            for (int i = 0; i < repetitions; ++i)
            {
                tree[treeSize] = 0;
                extraBitsData[treeSize] = 0;
                ++treeSize;
            }
        }
        else
        {
            repetitions -= 3;
            int start = treeSize;
            while (true)
            {
                tree[treeSize] = 17;
                extraBitsData[treeSize] = (byte)(repetitions & 0x7);
                ++treeSize;
                repetitions >>= 3;
                if (repetitions == 0)
                {
                    break;
                }

                --repetitions;
            }

            Reverse(tree, start, treeSize);
            Reverse(extraBitsData, start, treeSize);
        }
    }

    private static void DecideOverRleUse(ReadOnlySpan<byte> depth, int length, out bool useRleForNonZero, out bool useRleForZero)
    {
        int totalRepsZero = 0;
        int totalRepsNonZero = 0;
        int countRepsZero = 1;
        int countRepsNonZero = 1;
        for (int i = 0; i < length;)
        {
            byte value = depth[i];
            int reps = 1;
            for (int k = i + 1; k < length && depth[k] == value; ++k)
            {
                ++reps;
            }

            if (reps >= 3 && value == 0)
            {
                totalRepsZero += reps;
                ++countRepsZero;
            }

            if (reps >= 4 && value != 0)
            {
                totalRepsNonZero += reps;
                ++countRepsNonZero;
            }

            i += reps;
        }

        useRleForNonZero = totalRepsNonZero > countRepsNonZero * 2;
        useRleForZero = totalRepsZero > countRepsZero * 2;
    }

    private static ushort ReverseBits(int numBits, ushort bits)
    {
        // Pre-reversed 4-bit values.
        ReadOnlySpan<byte> lut = new byte[] { 0x0, 0x8, 0x4, 0xc, 0x2, 0xa, 0x6, 0xe, 0x1, 0x9, 0x5, 0xd, 0x3, 0xb, 0x7, 0xf };
        int retval = lut[bits & 0xf];
        for (int i = 4; i < numBits; i += 4)
        {
            retval <<= 4;
            bits = (ushort)(bits >> 4);
            retval |= lut[bits & 0xf];
        }

        retval >>= -numBits & 0x3;
        return (ushort)retval;
    }

    // A node of the tree: a leaf has IndexLeft -1 and the symbol in IndexRightOrValue.
    private readonly struct Node
    {
        public Node(uint count, short left, short right)
        {
            this.TotalCount = count;
            this.IndexLeft = left;
            this.IndexRightOrValue = right;
        }

        public uint TotalCount { get; }

        public short IndexLeft { get; }

        public short IndexRightOrValue { get; }
    }
}
