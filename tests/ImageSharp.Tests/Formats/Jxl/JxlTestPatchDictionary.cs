// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Writes the patch dictionary that libjxl (https://github.com/libjxl/libjxl, commit ef67fde2) reads in
// PatchDictionary::Decode (lib/jxl/dec_patch_dictionary.cc), in the same order of values and contexts.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Linq;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// A patch dictionary for a <see cref="JxlTestFrame"/>: reference patches, each a rectangle of a
/// reference slot placed at one or more positions. The values are entropy coded with prefix codes:
/// eight clusters (the count, alpha channel and clamp contexts share the last), each a single symbol
/// that costs no bits or a flat code of 2^L symbols, and hybrid integers with split exponent 0, so a
/// value v above 0 is the token FloorLog2(v) + 1 followed by FloorLog2(v) raw bits.
/// </summary>
internal sealed class JxlTestPatchDictionary
{
    private const int NumContexts = 10;
    private const int NumRefPatchContext = 0;
    private const int ReferenceFrameContext = 1;
    private const int PatchSizeContext = 2;
    private const int PatchReferencePositionContext = 3;
    private const int PatchPositionContext = 4;
    private const int PatchBlendModeContext = 5;
    private const int PatchOffsetContext = 6;
    private const int PatchCountContext = 7;
    private const int PatchAlphaChannelContext = 8;
    private const int PatchClampContext = 9;

    // Simple context map entries of 3 bits: every cluster from 0 to 7 is used.
    private static readonly int[] ContextMap = { 0, 1, 2, 3, 4, 5, 6, 7, 7, 7 };

    // The order of the code length code lengths: kCodeLengthCodeOrder in lib/jxl/dec_huffman.cc.
    private static readonly int[] CodeLengthCodeOrder = { 1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

    public JxlTestPatchDictionary(params JxlTestRefPatch[] refPatches) => this.RefPatches = refPatches;

    public IReadOnlyList<JxlTestRefPatch> RefPatches { get; }

    /// <summary>Gets the number of reference patches to write, when it is not the number given.</summary>
    public long? NumRefPatches { get; init; }

    /// <summary>
    /// Gets the initial ANS state. When set, the dictionary is coded with one ANS histogram of the single
    /// symbol 0, which never changes the state, so every value must be 0 and the final-state check
    /// passes only for the state 0x130000.
    /// </summary>
    public uint? AnsState { get; init; }

    /// <summary>Writes the dictionary for an image of <paramref name="numExtraChannels"/> extra channels.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="numExtraChannels">The number of extra channels of the image.</param>
    public void Write(JxlTestBitWriter w, int numExtraChannels)
    {
        List<(int Context, long Value)> values = this.Values(numExtraChannels);
        if (this.AnsState is uint state)
        {
            if (values.Any(v => v.Value != 0))
            {
                throw new InvalidOperationException("An ANS dictionary can only hold zeros.");
            }

            w.Bit(false); // no LZ77
            w.Bit(true).Write(2, 0); // simple context map: every context on histogram 0
            w.Bit(false).Write(2, 0); // ANS, log alpha size 5
            w.Write(3, 0); // split exponent 0
            w.Bit(true).Bit(false).Bit(false); // simple histogram of the one symbol 0
            w.Write(32, state);
            return;
        }

        // The tokens of each cluster.
        int numClusters = ContextMap.Max() + 1;
        SortedSet<int>[] tokens = new SortedSet<int>[numClusters];
        for (int i = 0; i < numClusters; i++)
        {
            tokens[i] = new SortedSet<int>();
        }

        foreach ((int context, long value) in values)
        {
            tokens[ContextMap[context]].Add(Token(value, out _, out _));
        }

        w.Bit(false); // no LZ77
        w.Bit(true).Write(2, 3); // simple context map, 3 bits per entry
        foreach (int cluster in ContextMap)
        {
            w.Write(3, (ulong)cluster);
        }

        w.Bit(true); // prefix codes
        for (int i = 0; i < numClusters; i++)
        {
            w.Write(4, 0); // split exponent 0
        }

        int[] codeBits = new int[numClusters];
        for (int i = 0; i < numClusters; i++)
        {
            int alphabetSize;
            if (tokens[i].Count == 0 || (tokens[i].Count == 1 && tokens[i].Min == 0))
            {
                alphabetSize = 1;
            }
            else if (tokens[i].Count == 1)
            {
                alphabetSize = tokens[i].Min + 1;
            }
            else
            {
                codeBits[i] = Math.Max(1, JxlBits.CeilLog2Nonzero((uint)tokens[i].Max + 1));
                alphabetSize = 1 << codeBits[i];
            }

            WriteVarLenUint16(w, alphabetSize - 1);
        }

        for (int i = 0; i < numClusters; i++)
        {
            if (codeBits[i] > 0)
            {
                // A complex code whose only code length code is L, so every symbol has length L.
                w.Write(2, 0);
                foreach (int codeLength in CodeLengthCodeOrder)
                {
                    w.Write(2, codeLength == codeBits[i] ? 1UL : 0UL); // code length code length 4, or 0
                }
            }
            else if (tokens[i].Count == 1 && tokens[i].Min != 0)
            {
                // A simple code of one symbol, which costs no bits.
                int alphabetSize = tokens[i].Min + 1;
                int maxBits = JxlBits.FloorLog2Nonzero((uint)(alphabetSize - 1)) + 1;
                w.Write(2, 1).Write(2, 0).Write(maxBits, (ulong)tokens[i].Min);
            }
        }

        foreach ((int context, long value) in values)
        {
            int cluster = ContextMap[context];
            int token = Token(value, out int rawBits, out ulong raw);
            if (codeBits[cluster] > 0)
            {
                w.Write(codeBits[cluster], Reverse((uint)token, codeBits[cluster]));
            }

            w.Write(rawBits, raw);
        }
    }

    private static bool UsesAlpha(JxlPatchBlendMode mode)
        => mode is JxlPatchBlendMode.BlendAbove or JxlPatchBlendMode.BlendBelow or JxlPatchBlendMode.AlphaWeightedAddAbove or JxlPatchBlendMode.AlphaWeightedAddBelow;

    private static int Token(long value, out int rawBits, out ulong raw)
    {
        if (value == 0)
        {
            rawBits = 0;
            raw = 0;
            return 0;
        }

        rawBits = JxlBits.FloorLog2Nonzero((ulong)value);
        raw = (ulong)value - (1UL << rawBits);
        return rawBits + 1;
    }

    // A canonical code is read most significant bit first.
    private static ulong Reverse(uint value, int bits)
    {
        ulong result = 0;
        for (int i = 0; i < bits; i++)
        {
            result |= ((value >> i) & 1) << (bits - 1 - i);
        }

        return result;
    }

    private static void WriteVarLenUint16(JxlTestBitWriter w, int value)
    {
        if (value == 0)
        {
            w.Bit(false);
            return;
        }

        int nbits = JxlBits.FloorLog2Nonzero((uint)value);
        w.Bit(true).Write(4, (ulong)nbits);
        w.Write(nbits, (ulong)(value - (1 << nbits)));
    }

    // The values in the order PatchDictionary::Decode reads them.
    private List<(int Context, long Value)> Values(int numExtraChannels)
    {
        List<(int Context, long Value)> values = new() { (NumRefPatchContext, this.NumRefPatches ?? this.RefPatches.Count) };
        foreach (JxlTestRefPatch refPatch in this.RefPatches)
        {
            values.Add((ReferenceFrameContext, refPatch.Ref));
            values.Add((PatchReferencePositionContext, refPatch.X0));
            values.Add((PatchReferencePositionContext, refPatch.Y0));
            values.Add((PatchSizeContext, refPatch.XSize - 1));
            values.Add((PatchSizeContext, refPatch.YSize - 1));
            values.Add((PatchCountContext, refPatch.Patches.Count - 1));
            for (int i = 0; i < refPatch.Patches.Count; i++)
            {
                JxlTestPatch patch = refPatch.Patches[i];
                if (i == 0)
                {
                    values.Add((PatchPositionContext, patch.X));
                    values.Add((PatchPositionContext, patch.Y));
                }
                else
                {
                    JxlTestPatch prev = refPatch.Patches[i - 1];
                    values.Add((PatchOffsetContext, JxlBits.PackSigned(patch.X - prev.X)));
                    values.Add((PatchOffsetContext, JxlBits.PackSigned(patch.Y - prev.Y)));
                }

                foreach (JxlTestPatchBlending blending in patch.Blendings)
                {
                    values.Add((PatchBlendModeContext, (long)blending.Mode));
                    if (UsesAlpha(blending.Mode) && numExtraChannels > 1)
                    {
                        values.Add((PatchAlphaChannelContext, blending.AlphaChannel));
                    }

                    if (UsesAlpha(blending.Mode) || blending.Mode == JxlPatchBlendMode.Mul)
                    {
                        values.Add((PatchClampContext, blending.Clamp ? 1 : 0));
                    }
                }
            }
        }

        return values;
    }
}

/// <summary>A rectangle of reference slot <see cref="Ref"/>, and the positions where it goes in the frame.</summary>
internal sealed class JxlTestRefPatch
{
    public JxlTestRefPatch(uint @ref, uint x0, uint y0, uint xsize, uint ysize, params JxlTestPatch[] patches)
    {
        this.Ref = @ref;
        this.X0 = x0;
        this.Y0 = y0;
        this.XSize = xsize;
        this.YSize = ysize;
        this.Patches = patches;
    }

    public uint Ref { get; }

    public uint X0 { get; }

    public uint Y0 { get; }

    public uint XSize { get; }

    public uint YSize { get; }

    public IReadOnlyList<JxlTestPatch> Patches { get; }
}

/// <summary>
/// One position of a reference patch, which may be negative to make a delta go below zero, and its
/// blendings: the colour first, then one for each extra channel.
/// </summary>
internal sealed class JxlTestPatch
{
    public JxlTestPatch(int x, int y, params JxlTestPatchBlending[] blendings)
    {
        this.X = x;
        this.Y = y;
        this.Blendings = blendings;
    }

    public int X { get; }

    public int Y { get; }

    public IReadOnlyList<JxlTestPatchBlending> Blendings { get; }
}

/// <summary>The blending of a patch's colour or of one extra channel.</summary>
internal readonly record struct JxlTestPatchBlending(JxlPatchBlendMode Mode, uint AlphaChannel = 0, bool Clamp = false);
