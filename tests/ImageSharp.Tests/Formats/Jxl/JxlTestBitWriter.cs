// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// A fluent wrapper over the production <see cref="JxlBitWriter"/>, to build small test bitstreams.
/// </summary>
internal sealed class JxlTestBitWriter
{
    private readonly JxlBitWriter writer = new();

    /// <summary>Writes the low <paramref name="nbits"/> bits of <paramref name="value"/>.</summary>
    public JxlTestBitWriter Write(int nbits, ulong value)
    {
        ulong mask = nbits >= 64 ? ulong.MaxValue : (1UL << nbits) - 1;
        this.writer.Write(nbits, value & mask);
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
        this.writer.ZeroPadToByte();
        return this;
    }

    /// <summary>Gets the bytes written so far.</summary>
    public byte[] ToArray() => this.writer.ToArray();

    /// <summary>Returns a reader over the bytes written so far.</summary>
    public JxlBitReader ToReader() => new(this.ToArray());
}
