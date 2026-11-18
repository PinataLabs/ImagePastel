// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from Brotli (https://github.com/google/brotli) at tag v1.2.0 (commit 028fb5a2):
// CmdLutElement in c/dec/prefix.h.
// Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors. MIT License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Compression.Brotli;

internal struct BrotliCommand
{
    public byte InsertLenExtraBits;

    public byte CopyLenExtraBits;

    public sbyte DistanceCode;

    public byte Context;

    public ushort InsertLenOffset;

    public ushort CopyLenOffset;
}
