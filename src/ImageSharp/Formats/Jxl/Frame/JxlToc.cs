// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/toc.h and lib/jxl/toc.cc (NumTocEntries, ReadToc).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <summary>Reads the table of contents: the byte size of each section of a frame.</summary>
internal static class JxlToc
{
    /// <summary>The largest number of TOC entries.</summary>
    public const long MaxTocEntries = 65536;

    /// <summary>The encoding of each section size.</summary>
    public static readonly JxlU32Enc TocDist = new(Bits(10), BitsOffset(14, 1024), BitsOffset(22, 17408), BitsOffset(30, 4211712));

    /// <summary>Returns the index of the section of AC group <paramref name="group"/> in pass <paramref name="pass"/>.</summary>
    /// <param name="pass">The pass.</param>
    /// <param name="group">The group.</param>
    /// <param name="numGroups">The number of groups.</param>
    /// <param name="numDcGroups">The number of DC groups.</param>
    /// <returns>The section index.</returns>
    public static long AcGroupIndex(long pass, long group, long numGroups, long numDcGroups)
        => 2 + numDcGroups + (pass * numGroups) + group;

    /// <summary>Returns the number of sections: one if there is one group and one pass.</summary>
    /// <param name="numGroups">The number of groups.</param>
    /// <param name="numDcGroups">The number of DC groups.</param>
    /// <param name="numPasses">The number of passes.</param>
    /// <returns>The number of TOC entries.</returns>
    public static long NumTocEntries(long numGroups, long numDcGroups, long numPasses)
    {
        if (numGroups == 1 && numPasses == 1)
        {
            return 1;
        }

        return AcGroupIndex(0, 0, numGroups, numDcGroups) + (numGroups * numPasses);
    }

    /// <summary>
    /// Reads <paramref name="tocEntries"/> section sizes. Throws <see cref="InvalidImageContentException"/>
    /// for too many entries or too few bytes.
    /// </summary>
    /// <param name="tocEntries">The number of entries.</param>
    /// <param name="reader">The reader.</param>
    /// <returns>The size of each section, in bytes.</returns>
    public static uint[] ReadToc(long tocEntries, JxlBitReader reader)
    {
        if (tocEntries > MaxTocEntries)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("too many toc entries");
        }

        uint[] sizes = new uint[tocEntries];
        if (reader.TotalBitsConsumed >= reader.TotalBytes * 8)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Not enough bytes for TOC");
        }

        if (reader.ReadBits(1) == 1)
        {
            CheckBitBudget(reader, tocEntries);

            // libjxl decodes the Lehmer-coded permutation here (DecodePermutation in coeff_order.cc).
            JxlThrowHelper.ThrowNotSupportedException("A permuted TOC");
        }

        reader.JumpToByteBoundary();
        CheckBitBudget(reader, tocEntries);
        for (long i = 0; i < tocEntries; ++i)
        {
            sizes[i] = JxlFieldCoders.ReadU32(TocDist, reader);
        }

        reader.JumpToByteBoundary();
        CheckBitBudget(reader, 0);
        return sizes;
    }

    private static void CheckBitBudget(JxlBitReader reader, long numEntries)
    {
        // U32Coder reads 2 bits to recognize variant and kTocDist cheapest variant is Bits(10),
        // this way at least 12 bits are required per toc-entry.
        long minimalBitCost = numEntries * (2 + 10);
        long bitBudget = reader.TotalBytes * 8;
        long expenses = reader.TotalBitsConsumed;
        if (expenses <= bitBudget && minimalBitCost <= bitBudget - expenses)
        {
            return;
        }

        JxlThrowHelper.ThrowInvalidImageContentException("Not enough bytes for TOC");
    }
}
