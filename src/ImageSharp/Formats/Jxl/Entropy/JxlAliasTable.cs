// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/ans_params.h, lib/jxl/ans_common.h and lib/jxl/ans_common.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Formats.Jxl.Entropy;

/// <summary>Builds and reads alias tables.</summary>
internal static class JxlAliasTable
{
    /// <summary>Looks up the symbol for <paramref name="value"/> in the table that starts at <paramref name="tableStart"/>.</summary>
    /// <param name="table">The tables.</param>
    /// <param name="tableStart">The first entry of this histogram's table.</param>
    /// <param name="value">The ANS state, masked to the table size.</param>
    /// <param name="logEntrySize">Log2 of the entry size.</param>
    /// <param name="entrySizeMinus1">The entry size minus 1.</param>
    /// <returns>The symbol.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static JxlAliasSymbol Lookup(JxlAliasEntry[] table, int tableStart, uint value, int logEntrySize, uint entrySizeMinus1)
    {
        uint i = value >> logEntrySize;
        uint pos = value & entrySizeMinus1;
        ref JxlAliasEntry entry = ref table[tableStart + (int)i];
        bool greater = pos >= entry.Cutoff;

        JxlAliasSymbol s;
        s.Value = greater ? entry.RightValue : (int)i;
        s.Offset = (greater ? entry.Offsets1 : 0u) + pos;

        // greater ? freq1 : freq0
        s.Freq = (uint)(entry.Freq0 ^ (greater ? entry.Freq1XorFreq0 : 0));
        return s;
    }

    /// <summary>
    /// Builds the alias table of a distribution that sums to 2^<paramref name="logRange"/>.
    /// Throws <see cref="InvalidImageContentException"/> for an invalid distribution.
    /// </summary>
    /// <param name="distribution">The distribution; trailing zeros are ignored.</param>
    /// <param name="logRange">Log2 of the distribution's sum.</param>
    /// <param name="logAlphaSize">Log2 of the table size.</param>
    /// <param name="a">The tables.</param>
    /// <param name="start">The first entry to fill.</param>
    public static void InitAliasTable(List<int> distribution, int logRange, int logAlphaSize, JxlAliasEntry[] a, int start)
    {
        uint range = 1u << logRange;
        int tableSize = 1 << logAlphaSize;
        Ensure(tableSize <= range);
        while (distribution.Count > 0 && distribution[^1] == 0)
        {
            distribution.RemoveAt(distribution.Count - 1);
        }

        // Ensure that a valid table is always returned, even for an empty distribution.
        if (distribution.Count == 0)
        {
            distribution.Add((int)range);
        }

        Ensure(distribution.Count <= tableSize);
        uint entrySize = range >> logAlphaSize; // this is exact
        int singleSymbol = -1;
        long sum = 0;
        for (int sym = 0; sym < distribution.Count; sym++)
        {
            int v = distribution[sym];
            sum += v;
            if (v == JxlAnsParams.AnsTabSize)
            {
                Ensure(singleSymbol == -1);
                singleSymbol = sym;
            }
        }

        Ensure(sum == range);
        if (singleSymbol != -1)
        {
            byte symbol = (byte)singleSymbol;
            Ensure(singleSymbol == symbol);
            for (int i = 0; i < tableSize; i++)
            {
                ref JxlAliasEntry e = ref a[start + i];
                e.RightValue = symbol;
                e.Cutoff = 0;
                e.Offsets1 = (ushort)(entrySize * i);
                e.Freq0 = 0;
                e.Freq1XorFreq0 = JxlAnsParams.AnsTabSize;
            }

            return;
        }

        Stack<int> underfullPosn = new();
        Stack<int> overfullPosn = new();
        uint[] cutoffs = new uint[tableSize];

        // Initialize entries.
        for (int i = 0; i < distribution.Count; i++)
        {
            cutoffs[i] = (uint)distribution[i];
            if (cutoffs[i] > entrySize)
            {
                overfullPosn.Push(i);
            }
            else if (cutoffs[i] < entrySize)
            {
                underfullPosn.Push(i);
            }
        }

        for (int i = distribution.Count; i < tableSize; i++)
        {
            cutoffs[i] = 0;
            underfullPosn.Push(i);
        }

        // Reassign overflow/underflow values.
        while (overfullPosn.Count > 0)
        {
            int overfullI = overfullPosn.Pop();
            Ensure(underfullPosn.Count > 0);
            int underfullI = underfullPosn.Pop();
            uint underfullBy = entrySize - cutoffs[underfullI];
            cutoffs[overfullI] -= underfullBy;

            // overfull positions have their original symbols
            a[start + underfullI].RightValue = (byte)overfullI;
            a[start + underfullI].Offsets1 = (ushort)cutoffs[overfullI];

            // Slot overfull_i now has a cutoff smaller than entry_size.
            if (cutoffs[overfullI] < entrySize)
            {
                underfullPosn.Push(overfullI);
            }
            else if (cutoffs[overfullI] > entrySize)
            {
                overfullPosn.Push(overfullI);
            }
        }

        for (int i = 0; i < tableSize; i++)
        {
            ref JxlAliasEntry e = ref a[start + i];

            // cutoffs[i] is properly initialized but the clang-analyzer doesn't infer it, so libjxl
            // suppresses a warning here.
            if (cutoffs[i] == entrySize)
            {
                e.RightValue = (byte)i;
                e.Offsets1 = 0;
                e.Cutoff = 0;
            }
            else
            {
                // Note that right_value is not initialized.
                e.Offsets1 = unchecked((ushort)(e.Offsets1 - cutoffs[i]));
                e.Cutoff = (byte)cutoffs[i];
            }

            int freq0 = i < distribution.Count ? distribution[i] : 0;
            int i1 = e.RightValue;
            int freq1 = i1 < distribution.Count ? distribution[i1] : 0;
            e.Freq0 = (ushort)freq0;
            e.Freq1XorFreq0 = (ushort)(freq1 ^ freq0);
        }
    }

    private static void Ensure(bool condition)
    {
        if (!condition)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid ANS distribution");
        }
    }
}
