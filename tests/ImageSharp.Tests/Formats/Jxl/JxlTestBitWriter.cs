// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Writes bits least-significant first, as the JPEG XL bit reader reads them, to build small test bitstreams.
/// </summary>
internal sealed class JxlTestBitWriter
{
    private readonly List<byte> bytes = new();
    private int bitCount;

    /// <summary>Writes the low <paramref name="nbits"/> bits of <paramref name="value"/>.</summary>
    public JxlTestBitWriter Write(int nbits, ulong value)
    {
        for (int i = 0; i < nbits; i++)
        {
            if ((this.bitCount & 7) == 0)
            {
                this.bytes.Add(0);
            }

            if (((value >> i) & 1) != 0)
            {
                this.bytes[^1] |= (byte)(1 << (this.bitCount & 7));
            }

            this.bitCount++;
        }

        return this;
    }

    /// <summary>Writes one bit.</summary>
    public JxlTestBitWriter Bit(bool value) => this.Write(1, value ? 1UL : 0UL);

    /// <summary>Writes <paramref name="count"/> copies of a bit.</summary>
    public JxlTestBitWriter Bits(int count, bool value)
    {
        for (int i = 0; i < count; i++)
        {
            this.Bit(value);
        }

        return this;
    }

    /// <summary>Pads to a byte boundary with zero bits.</summary>
    public JxlTestBitWriter ZeroPadToByte()
    {
        this.bitCount = (this.bitCount + 7) & ~7;
        return this;
    }

    /// <summary>Gets the bytes written so far.</summary>
    public byte[] ToArray() => this.bytes.ToArray();

    /// <summary>Returns a reader over the bytes written so far.</summary>
    public JxlBitReader ToReader() => new(this.ToArray());
}
