// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from Brotli (https://github.com/google/brotli) at tag v1.2.0 (commit 028fb5a2):
// c/common/constants.h, c/common/constants.c, c/dec/prefix.h and c/dec/prefix.c.
// Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors. MIT License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace PinataLabs.ImagePastel.Compression.Brotli;

internal static class BrotliPrefix
{
    public const int NumBlockLengthSymbols = 26;

    public const int NumCommandSymbols = 704;

    public static BrotliCommand[] Commands { get; } = CreateCommands();

    public static ushort[] BlockLengthOffset { get; } =
    {
        1, 5, 9, 13, 17, 25, 33, 41, 49, 65, 81, 97, 113, 145, 177, 209, 241, 305, 369, 497, 753, 1265, 2289, 4337, 8433, 16625,
    };

    public static ReadOnlySpan<byte> BlockLengthBits => new byte[]
    {
        2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 7, 8, 9, 10, 11, 12, 13, 24,
    };

    private static ReadOnlySpan<byte> InsertLengthExtraBits => new byte[]
    {
        0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 7, 8, 9, 10, 12, 14, 24,
    };

    private static ReadOnlySpan<byte> CopyLengthExtraBits => new byte[]
    {
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 7, 8, 9, 10, 24,
    };

    private static ReadOnlySpan<byte> CellPos => new byte[] { 0, 1, 0, 1, 8, 9, 2, 16, 10, 17, 18 };

    // BrotliDecoderInitCmdLut. The C library can also embed the table (prefix_inc.h); the two are equal.
    private static BrotliCommand[] CreateCommands()
    {
        Span<ushort> insertLengthOffsets = stackalloc ushort[24];
        Span<ushort> copyLengthOffsets = stackalloc ushort[24];
        insertLengthOffsets[0] = 0;
        copyLengthOffsets[0] = 2;
        for (int i = 0; i < 23; ++i)
        {
            insertLengthOffsets[i + 1] = (ushort)(insertLengthOffsets[i] + (1 << InsertLengthExtraBits[i]));
            copyLengthOffsets[i + 1] = (ushort)(copyLengthOffsets[i] + (1 << CopyLengthExtraBits[i]));
        }

        BrotliCommand[] commands = new BrotliCommand[NumCommandSymbols];
        for (int symbol = 0; symbol < commands.Length; ++symbol)
        {
            int cellIdx = symbol >> 6;
            int cellPos = CellPos[cellIdx];
            int copyCode = ((cellPos << 3) & 0x18) + (symbol & 0x7);
            int insertCode = (cellPos & 0x18) + ((symbol >> 3) & 0x7);
            ushort copyLenOffset = copyLengthOffsets[copyCode];
            commands[symbol] = new BrotliCommand
            {
                InsertLenExtraBits = InsertLengthExtraBits[insertCode],
                CopyLenExtraBits = CopyLengthExtraBits[copyCode],
                DistanceCode = (sbyte)(cellIdx >= 2 ? -1 : 0),
                Context = (byte)(copyLenOffset > 4 ? 3 : copyLenOffset - 2),
                InsertLenOffset = insertLengthOffsets[insertCode],
                CopyLenOffset = copyLenOffset,
            };
        }

        return commands;
    }
}
