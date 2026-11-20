// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from Brotli (https://github.com/google/brotli) at tag v1.2.0 (commit 028fb5a2):
// HuffmanCode in c/dec/huffman.h.
// Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors. MIT License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Compression.Brotli;

internal readonly struct BrotliHuffmanCode
{
    public readonly byte Bits;

    public readonly ushort Value;

    public BrotliHuffmanCode(int bits, int value)
    {
        this.Bits = (byte)bits;
        this.Value = (ushort)value;
    }
}
