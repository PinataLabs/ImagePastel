// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_ans.h (SizeWriter).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Formats.Jxl.Base;

/// <summary>
/// Takes bits. libjxl writes histograms through a <c>Writer</c> template parameter that is either its
/// bit writer or a <c>SizeWriter</c> that only counts; this interface is that parameter.
/// </summary>
internal interface IJxlBitSink
{
    /// <summary>Writes the low <paramref name="nBits"/> bits of <paramref name="bits"/>.</summary>
    /// <param name="nBits">The number of bits.</param>
    /// <param name="bits">The bits.</param>
    void Write(int nBits, ulong bits);
}

/// <summary>Counts the bits written to it, to estimate a cost without writing.</summary>
internal sealed class JxlSizeWriter : IJxlBitSink
{
    /// <summary>Gets the number of bits written.</summary>
    public long Size { get; private set; }

    /// <inheritdoc/>
    public void Write(int nBits, ulong bits) => this.Size += nBits;
}
