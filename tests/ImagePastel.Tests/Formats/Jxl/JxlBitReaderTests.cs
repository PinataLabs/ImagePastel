// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Formats.Jxl.Base;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

[Trait("Format", "Jxl")]
public class JxlBitReaderTests
{
    [Fact]
    public void ReadBits_IsLeastSignificantBitFirst()
    {
        JxlBitReader reader = new(new byte[] { 0b1011_0010, 0b0000_0001 });

        Assert.Equal(0UL, reader.ReadBits(1));
        Assert.Equal(0b001UL, reader.ReadBits(3));
        Assert.Equal(0b1_1011UL, reader.ReadBits(5));
        Assert.Equal(9, reader.TotalBitsConsumed);
        Assert.True(reader.AllReadsWithinBounds());
    }

    [Fact]
    public void ReadBits_PastTheEnd_ReturnsZerosWithoutThrowing()
    {
        JxlBitReader reader = new(new byte[] { 0xFF, 0xFF });

        Assert.Equal(0xFFFFUL, reader.ReadBits(16));
        Assert.Equal(0UL, reader.ReadBits(32));
        Assert.Equal(0UL, reader.ReadBits(56));
        Assert.Equal(16 + 32 + 56, reader.TotalBitsConsumed);
    }

    [Fact]
    public void AllReadsWithinBounds_IsFalseOnlyAfterReadingPastTheEnd()
    {
        JxlBitReader reader = new(new byte[] { 0xAB, 0xCD });

        reader.ReadBits(16);
        Assert.True(reader.AllReadsWithinBounds());

        reader.ReadBits(1);
        Assert.False(reader.AllReadsWithinBounds());
    }

    [Fact]
    public void Close_AfterUncheckedOverRead_ThrowsInvalidImageContent()
    {
        JxlBitReader reader = new(new byte[] { 0x01 });

        // The over-read itself does not throw: the error is deferred to the next checkpoint.
        reader.ReadBits(12);

        Assert.Throws<InvalidImageContentException>(reader.Close);
    }

    [Fact]
    public void Close_AfterCheckedOverRead_DoesNotThrow()
    {
        JxlBitReader reader = new(new byte[] { 0x01 });
        reader.ReadBits(12);

        // A caller that checked the bounds has handled the over-read itself.
        Assert.False(reader.AllReadsWithinBounds());
        reader.Close();
    }

    [Fact]
    public void Close_WithinBounds_DoesNotThrow()
    {
        JxlBitReader reader = new(new byte[] { 0x01, 0x02 });
        reader.ReadBits(16);

        reader.Close();
    }

    [Fact]
    public void EnsureAllReadsWithinBounds_AfterOverRead_ThrowsInvalidImageContent()
    {
        JxlBitReader reader = new(new byte[] { 0x01 });
        reader.ReadBits(9);

        Assert.Throws<InvalidImageContentException>(() => reader.EnsureAllReadsWithinBounds("test"));
    }

    [Fact]
    public void SkipBits_PastTheEnd_IsOutOfBounds()
    {
        JxlBitReader reader = new(new byte[16]);

        reader.SkipBits(1000);

        Assert.False(reader.AllReadsWithinBounds());
        Assert.Equal(0UL, reader.ReadBits(8));
    }

    [Fact]
    public void SkipBits_WithinTheRange_MatchesReading()
    {
        byte[] data = { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0, 0x11, 0x22, 0x33, 0x44 };
        JxlBitReader skipped = new(data);
        JxlBitReader read = new(data);

        skipped.SkipBits(70);
        for (int i = 0; i < 70; i++)
        {
            read.ReadBits(1);
        }

        Assert.Equal(read.ReadBits(20), skipped.ReadBits(20));
    }

    [Fact]
    public void JumpToByteBoundary_WithNonZeroPadding_ThrowsInvalidImageContent()
    {
        JxlBitReader reader = new(new byte[] { 0b0000_0100 });
        reader.ReadBits(1);

        Assert.Throws<InvalidImageContentException>(reader.JumpToByteBoundary);
    }

    [Fact]
    public void JumpToByteBoundary_WithZeroPadding_SkipsToTheNextByte()
    {
        JxlBitReader reader = new(new byte[] { 0b0000_0001, 0x7F });
        reader.ReadBits(1);

        reader.JumpToByteBoundary();

        Assert.Equal(8, reader.TotalBitsConsumed);
        Assert.Equal(0x7FUL, reader.ReadBits(8));
    }

    [Fact]
    public void Reader_OverASubRange_ReadsOnlyThatRange()
    {
        JxlBitReader reader = new(new byte[] { 0xFF, 0x5A, 0xFF }, 1, 1);

        Assert.Equal(0x5AUL, reader.ReadBits(8));
        Assert.Equal(0UL, reader.ReadBits(8));
        Assert.False(reader.AllReadsWithinBounds());
    }
}
