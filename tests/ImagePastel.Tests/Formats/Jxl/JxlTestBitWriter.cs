// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Text;
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

    /// <summary>Writes an enum field: U32(Val(0), Val(1), BitsOffset(4, 2), BitsOffset(6, 18)).</summary>
    public JxlTestBitWriter WriteEnum(uint value)
    {
        if (value < 2)
        {
            return this.Write(2, value);
        }

        if (value < 18)
        {
            return this.Write(2, 2).Write(4, value - 2);
        }

        return this.Write(2, 3).Write(6, value - 18);
    }

    /// <summary>
    /// Writes a name as libjxl's <c>VisitNameString</c>: the UTF-8 length as
    /// U32(Val(0), Bits(4), BitsOffset(5, 16), BitsOffset(10, 48)), then the bytes.
    /// </summary>
    public JxlTestBitWriter WriteName(string name)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(name ?? string.Empty);
        if (bytes.Length == 0)
        {
            this.Write(2, 0);
        }
        else if (bytes.Length < 16)
        {
            this.Write(2, 1).Write(4, (ulong)bytes.Length);
        }
        else
        {
            this.Write(2, 2).Write(5, (ulong)bytes.Length - 16);
        }

        foreach (byte b in bytes)
        {
            this.Write(8, b);
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
