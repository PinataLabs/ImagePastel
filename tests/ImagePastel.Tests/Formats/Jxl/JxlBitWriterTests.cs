// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

[Trait("Format", "Jxl")]
public class JxlBitWriterTests
{
    [Fact]
    public void Write_MixedWidths_PacksBitsLeastSignificantFirst()
    {
        JxlBitWriter writer = new();

        writer.Write(3, 0b101);
        writer.Write(64, 0x8123456789ABCDEF);
        writer.Write(5, 0x1F);
        writer.Write(10, 0x2AB);
        Assert.Equal(82, writer.BitsWritten);
        writer.ZeroPadToByte();

        Assert.Equal(88, writer.BitsWritten);
        Assert.Equal(new byte[] { 0x7D, 0x6F, 0x5E, 0x4D, 0x3C, 0x2B, 0x1A, 0x09, 0xFC, 0xAB, 0x02 }, writer.ToArray());
    }

    [Fact]
    public void Write_64BitsAfterSevenBits_SpansNineBytes()
    {
        JxlBitWriter writer = new();

        writer.Write(7, 0x55);
        writer.Write(64, 0xFEDCBA9876543210);
        writer.Write(1, 1);

        Assert.Equal(new byte[] { 0x55, 0x08, 0x19, 0x2A, 0x3B, 0x4C, 0x5D, 0x6E, 0xFF }, writer.ToArray());
    }

    [Fact]
    public void Write_ManyBits_GrowsStorage()
    {
        JxlBitWriter writer = new();

        for (int i = 0; i < 1000; i++)
        {
            writer.Write(8, (ulong)(i & 0xFF));
        }

        byte[] bytes = writer.ToArray();
        Assert.Equal(1000, bytes.Length);
        Assert.Equal(0xE7, bytes[999]);
    }

    [Fact]
    public void ZeroPadToByte_WhenAligned_WritesNothing()
    {
        JxlBitWriter writer = new();
        writer.Write(8, 0xA5);

        writer.ZeroPadToByte();

        Assert.Equal(8, writer.BitsWritten);
    }

    [Fact]
    public void AppendUnaligned_AppendsTheOtherWritersBits()
    {
        JxlBitWriter writer = new();
        writer.Write(3, 0b101);
        writer.Write(2, 0b11);
        JxlBitWriter other = new();
        other.Write(16, 0x1234);
        other.Write(3, 0b101);

        writer.AppendUnaligned(other);

        Assert.Equal(24, writer.BitsWritten);
        Assert.Equal(new byte[] { 0x9D, 0x46, 0xA2 }, writer.ToArray());
    }

    [Fact]
    public void AppendByteAligned_AppendsBytesThenKeepsWriting()
    {
        JxlBitWriter writer = new();
        writer.Write(8, 0x01);

        writer.AppendByteAligned(new byte[] { 0xFF, 0xEE });
        writer.Write(4, 0x3);

        Assert.Equal(new byte[] { 0x01, 0xFF, 0xEE, 0x03 }, writer.ToArray());
    }

    [Fact]
    public void AppendByteAligned_AppendsEachWriter()
    {
        JxlBitWriter first = new();
        first.Write(16, 0xBBAA);
        JxlBitWriter empty = new();
        JxlBitWriter second = new();
        second.Write(8, 0xCC);
        JxlBitWriter writer = new();
        writer.Write(8, 0x01);

        writer.AppendByteAligned(new[] { first, empty, second });
        writer.Write(1, 1);

        Assert.Equal(new byte[] { 0x01, 0xAA, 0xBB, 0xCC, 0x01 }, writer.ToArray());
    }

    [Fact]
    public void AppendByteAligned_WhenUnaligned_Throws()
    {
        JxlBitWriter writer = new();
        writer.Write(3, 0);

        Assert.Throws<InvalidOperationException>(() => writer.AppendByteAligned(new byte[] { 1 }));
    }
}
