// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_toc.cc (WriteTocPermutation, WriteTocSizes).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;

namespace PinataLabs.ImagePastel.Formats.Jxl.Frame;

/// <summary>Writes the table of contents, as <see cref="JxlToc.ReadGroupOffsets"/> reads it.</summary>
internal static class JxlTocWriter
{
    /// <summary>
    /// Writes whether the sections are permuted, then pads to a byte. libjxl writes the permutation
    /// itself when there is one; the encoder never permutes (libjxl's <c>centerfirst</c> is off by
    /// default). The decoder reads permutations since D13a.
    /// </summary>
    /// <param name="writer">The writer.</param>
    public static void WriteTocPermutation(JxlBitWriter writer)
    {
        // no permutation
        writer.Write(1, 0);

        // before TOC entries
        writer.ZeroPadToByte();
    }

    /// <summary>Writes the byte size of each section, then pads to a byte.</summary>
    /// <param name="groupSizes">The section sizes.</param>
    /// <param name="writer">The writer.</param>
    public static void WriteTocSizes(IReadOnlyList<uint> groupSizes, JxlBitWriter writer)
    {
        foreach (uint groupSize in groupSizes)
        {
            if (!JxlFieldCoders.CanEncodeU32(JxlToc.TocDist, groupSize, out _))
            {
                JxlThrowHelper.ThrowImageFormatException($"Section of {groupSize} bytes is too large for the TOC");
            }

            JxlFieldCoders.WriteU32(JxlToc.TocDist, groupSize, writer);
        }

        // before first group
        writer.ZeroPadToByte();
    }
}
