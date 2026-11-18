// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from Brotli (https://github.com/google/brotli) at tag v1.2.0 (commit 028fb5a2):
// CmdLutElement in c/dec/prefix.h.
// Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors. MIT License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Compression.Brotli;

/// <summary>An insert-and-copy length symbol, decoded (<c>CmdLutElement</c>).</summary>
internal struct BrotliCommand
{
    /// <summary>The number of extra bits of the insert length.</summary>
    public byte InsertLenExtraBits;

    /// <summary>The number of extra bits of the copy length.</summary>
    public byte CopyLenExtraBits;

    /// <summary>0 when the command reuses the last distance, -1 when it reads one.</summary>
    public sbyte DistanceCode;

    /// <summary>The distance context ID.</summary>
    public byte Context;

    /// <summary>The first insert length of the symbol.</summary>
    public ushort InsertLenOffset;

    /// <summary>The first copy length of the symbol.</summary>
    public ushort CopyLenOffset;
}
