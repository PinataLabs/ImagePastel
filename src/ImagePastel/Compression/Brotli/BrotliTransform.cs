// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from Brotli (https://github.com/google/brotli) at tag v1.2.0 (commit 028fb5a2):
// c/common/transform.h and c/common/transform.c.
// Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors. MIT License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace PinataLabs.ImagePastel.Compression.Brotli;

/// <summary>
/// The word transforms of RFC 7932, appendix B: a prefix, a change to a dictionary word, and a suffix.
/// </summary>
internal static class BrotliTransform
{
    /// <summary>The number of transforms.</summary>
    public const int NumTransforms = 121;

    /// <summary>The transform that copies the word as it is.</summary>
    public const int Identity = 0;

    private const int OmitLast9 = 9;
    private const int UppercaseFirst = 10;
    private const int UppercaseAll = 11;
    private const int OmitFirst1 = 12;
    private const int OmitFirst9 = 20;

    // kPrefixSuffix: strings, each after its length.
    private static ReadOnlySpan<byte> PrefixSuffix => new byte[]
    {
        1, 32, 2, 44, 32, 8, 32, 111, 102, 32, 116, 104, 101, 32, 4, 32, 111, 102, 32, 2, 115, 32, 1, 46,
        5, 32, 97, 110, 100, 32, 4, 32, 105, 110, 32, 1, 34, 4, 32, 116, 111, 32, 2, 34, 62, 1, 10, 2,
        46, 32, 1, 93, 5, 32, 102, 111, 114, 32, 3, 32, 97, 32, 6, 32, 116, 104, 97, 116, 32, 1, 39, 6,
        32, 119, 105, 116, 104, 32, 6, 32, 102, 114, 111, 109, 32, 4, 32, 98, 121, 32, 1, 40, 6, 46, 32, 84,
        104, 101, 32, 4, 32, 111, 110, 32, 4, 32, 97, 115, 32, 4, 32, 105, 115, 32, 4, 105, 110, 103, 32, 2,
        10, 9, 1, 58, 3, 101, 100, 32, 2, 61, 34, 4, 32, 97, 116, 32, 3, 108, 121, 32, 1, 44, 2, 61,
        39, 5, 46, 99, 111, 109, 47, 7, 46, 32, 84, 104, 105, 115, 32, 5, 32, 110, 111, 116, 32, 3, 101, 114,
        32, 3, 97, 108, 32, 4, 102, 117, 108, 32, 4, 105, 118, 101, 32, 5, 108, 101, 115, 115, 32, 4, 101, 115,
        116, 32, 4, 105, 122, 101, 32, 2, 194, 160, 4, 111, 117, 115, 32, 5, 32, 116, 104, 101, 32, 2, 101, 32,
        0,
    };

    // kPrefixSuffixMap: the offset of each string in PrefixSuffix.
    private static ReadOnlySpan<byte> PrefixSuffixMap => new byte[]
    {
        0, 2, 5, 14, 19, 22, 24, 30, 35, 37, 42, 45, 47, 50, 52, 58, 62, 69, 71, 78, 85, 90, 92, 99, 104,
        109, 114, 119, 122, 124, 128, 131, 136, 140, 142, 145, 151, 159, 165, 169, 173, 178, 183, 189, 194, 199, 202, 207, 213, 216,
    };

    // kTransformsData: a [prefix ID, type, suffix ID] triplet for each transform.
    private static ReadOnlySpan<byte> Transforms => new byte[]
    {
        49, 0, 49, 49, 0, 0, 0, 0, 0, 49, 12, 49, 49, 10, 0, 49, 0, 47, 0, 0, 49, 4, 0, 0, 49, 0, 3, 49, 10, 49,
        49, 0, 6, 49, 13, 49, 49, 1, 49, 1, 0, 0, 49, 0, 1, 0, 10, 0, 49, 0, 7, 49, 0, 9, 48, 0, 0, 49, 0, 8,
        49, 0, 5, 49, 0, 10, 49, 0, 11, 49, 3, 49, 49, 0, 13, 49, 0, 14, 49, 14, 49, 49, 2, 49, 49, 0, 15, 49, 0, 16,
        0, 10, 49, 49, 0, 12, 5, 0, 49, 0, 0, 1, 49, 15, 49, 49, 0, 18, 49, 0, 17, 49, 0, 19, 49, 0, 20, 49, 16, 49,
        49, 17, 49, 47, 0, 49, 49, 4, 49, 49, 0, 22, 49, 11, 49, 49, 0, 23, 49, 0, 24, 49, 0, 25, 49, 7, 49, 49, 1, 26,
        49, 0, 27, 49, 0, 28, 0, 0, 12, 49, 0, 29, 49, 20, 49, 49, 18, 49, 49, 6, 49, 49, 0, 21, 49, 10, 1, 49, 8, 49,
        49, 0, 31, 49, 0, 32, 47, 0, 3, 49, 5, 49, 49, 9, 49, 0, 10, 1, 49, 10, 8, 5, 0, 21, 49, 11, 0, 49, 10, 10,
        49, 0, 30, 0, 0, 5, 35, 0, 49, 47, 0, 2, 49, 10, 17, 49, 0, 36, 49, 0, 33, 5, 0, 0, 49, 10, 21, 49, 10, 5,
        49, 0, 37, 0, 0, 30, 49, 0, 38, 0, 11, 0, 49, 0, 39, 0, 11, 49, 49, 0, 34, 49, 11, 8, 49, 10, 12, 0, 0, 21,
        49, 0, 40, 0, 10, 12, 49, 0, 41, 49, 0, 42, 49, 11, 17, 49, 0, 43, 0, 10, 5, 49, 11, 10, 0, 0, 34, 49, 10, 33,
        49, 0, 44, 49, 11, 5, 45, 0, 49, 0, 0, 33, 49, 10, 30, 49, 11, 30, 49, 0, 46, 49, 11, 1, 49, 10, 34, 0, 10, 33,
        0, 11, 30, 0, 11, 1, 49, 11, 33, 49, 11, 21, 49, 11, 12, 0, 11, 5, 49, 11, 34, 0, 11, 12, 0, 10, 30, 0, 11, 34,
        0, 10, 34,
    };

    /// <summary>
    /// Writes a transformed dictionary word to <paramref name="dst"/>, which has room for the longest
    /// prefix, word and suffix, and two bytes more.
    /// </summary>
    /// <param name="dst">The destination.</param>
    /// <param name="word">The dictionary word.</param>
    /// <param name="transformIdx">The transform, below <see cref="NumTransforms"/>.</param>
    /// <returns>The number of bytes written.</returns>
    public static int TransformDictionaryWord(Span<byte> dst, ReadOnlySpan<byte> word, int transformIdx)
    {
        int idx = 0;
        ReadOnlySpan<byte> prefixSuffix = PrefixSuffix;
        int prefix = PrefixSuffixMap[Transforms[transformIdx * 3]];
        int type = Transforms[(transformIdx * 3) + 1];
        int suffix = PrefixSuffixMap[Transforms[(transformIdx * 3) + 2]];
        int len = word.Length;

        int prefixLen = prefixSuffix[prefix++];
        while (prefixLen-- != 0)
        {
            dst[idx++] = prefixSuffix[prefix++];
        }

        // The RFC 7932 transforms have no shift types (BROTLI_TRANSFORM_SHIFT_FIRST and _ALL), which only a
        // custom dictionary can use, so they are not ported.
        if (type <= OmitLast9)
        {
            len -= type;
        }
        else if (type is >= OmitFirst1 and <= OmitFirst9)
        {
            int skip = type - (OmitFirst1 - 1);
            word = skip < word.Length ? word.Slice(skip) : default;
            len -= skip;
        }

        for (int i = 0; i < len; i++)
        {
            dst[idx++] = word[i];
        }

        if (type == UppercaseFirst)
        {
            ToUpperCase(dst.Slice(idx - len));
        }
        else if (type == UppercaseAll)
        {
            int uppercase = idx - len;
            while (len > 0)
            {
                int step = ToUpperCase(dst.Slice(uppercase));
                uppercase += step;
                len -= step;
            }
        }

        int suffixLen = prefixSuffix[suffix++];
        while (suffixLen-- != 0)
        {
            dst[idx++] = prefixSuffix[suffix++];
        }

        return idx;
    }

    private static int ToUpperCase(Span<byte> p)
    {
        if (p[0] < 0xC0)
        {
            if (p[0] is >= (byte)'a' and <= (byte)'z')
            {
                p[0] ^= 32;
            }

            return 1;
        }

        // An overly simplified uppercasing model for UTF-8.
        if (p[0] < 0xE0)
        {
            p[1] ^= 32;
            return 2;
        }

        // An arbitrary transform for three byte characters.
        p[2] ^= 5;
        return 3;
    }
}
