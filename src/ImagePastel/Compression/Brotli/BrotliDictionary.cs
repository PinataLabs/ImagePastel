// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from Brotli (https://github.com/google/brotli) at tag v1.2.0 (commit 028fb5a2):
// c/common/dictionary.h and c/common/dictionary.c.
// Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors. MIT License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace PinataLabs.ImagePastel.Compression.Brotli;

/// <summary>
/// The static dictionary of RFC 7932, appendix A: words of 4 to 24 bytes, grouped by length.
/// </summary>
internal static partial class BrotliDictionary
{
    /// <summary>The shortest word.</summary>
    public const int MinWordLength = 4;

    /// <summary>The longest word.</summary>
    public const int MaxWordLength = 24;

    /// <summary>
    /// Gets the number of bits of a word index, by word length. 0 means that there are no words of that
    /// length.
    /// </summary>
    public static ReadOnlySpan<byte> SizeBitsByLength => new byte[]
    {
        0, 0, 0, 0, 10, 10, 11, 11,
        10, 10, 10, 10, 10, 9, 9, 8,
        7, 7, 8, 7, 7, 6, 6, 5,
        5, 0, 0, 0, 0, 0, 0, 0,
    };

    /// <summary>Gets the offset in <see cref="Data"/> of the first word of each length.</summary>
    public static int[] OffsetsByLength { get; } =
    {
        0, 0, 0, 0, 0, 4096, 9216, 21504,
        35840, 44032, 53248, 63488, 74752, 87040, 93696, 100864,
        104704, 106752, 108928, 113536, 115968, 118528, 119872, 121280,
        122016, 122784, 122784, 122784, 122784, 122784, 122784, 122784,
    };
}
