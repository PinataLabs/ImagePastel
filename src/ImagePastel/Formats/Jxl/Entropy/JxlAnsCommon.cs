// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/ans_common.h (GetPopulationCountPrecision, CreateFlatHistogram).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;

namespace PinataLabs.ImagePastel.Formats.Jxl.Entropy;

/// <summary>The histogram helpers the ANS reader and writer share.</summary>
internal static class JxlAnsCommon
{
    /// <summary>
    /// Returns the precision (number of bits) that should be used to store a histogram count such
    /// that Log2Floor(count) == <paramref name="logCount"/>.
    /// </summary>
    /// <param name="logCount">The log2 of the count.</param>
    /// <param name="shift">The histogram's shift.</param>
    /// <returns>The number of bits.</returns>
    public static int GetPopulationCountPrecision(int logCount, int shift)
    {
        int r = Math.Min(logCount, shift - ((JxlAnsParams.AnsLogTabSize - logCount) >> 1));
        return r < 0 ? 0 : r;
    }

    /// <summary>
    /// Returns a histogram where the counts are positive, differ by at most 1, and add up to
    /// <paramref name="totalCount"/>. The bigger counts (if any) are at the beginning of the histogram.
    /// </summary>
    /// <param name="length">The number of symbols.</param>
    /// <param name="totalCount">The sum of the counts.</param>
    /// <returns>The counts.</returns>
    public static List<int> CreateFlatHistogram(int length, int totalCount)
    {
        int count = totalCount / length;
        List<int> result = new(length);
        for (int i = 0; i < length; i++)
        {
            result.Add(count);
        }

        int remCounts = totalCount % length;
        for (int i = 0; i < remCounts; ++i)
        {
            ++result[i];
        }

        return result;
    }
}
