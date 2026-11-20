// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.Compression.Brotli;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Compression.Brotli;

public class BrotliBitReaderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(1000)]
    public void ReadBits_GivesTheBitsOfTheInput_LeastSignificantFirst(int length)
    {
        byte[] input = RandomBytes(length, seed: length);
        Random random = new(length + 1);
        BrotliBitReader reader = new(input, 0, 0);

        int position = 0;
        while (position < length * 8)
        {
            int count = Math.Min(random.Next(BrotliBitReader.MaxBitsPerRead + 1), (length * 8) - position);

            Assert.Equal(Bits(input, position, count), reader.ReadBits(count));
            position += count;
        }
    }

    [Fact]
    public void PeekBits_PastTheEnd_ReadsZeros()
    {
        BrotliBitReader reader = new(new byte[] { 0xFF }, 0, 0);

        Assert.Equal(0x1F, reader.ReadBits(5));
        Assert.Equal(0x7, reader.PeekBits(16));
    }

    [Fact]
    public void ReadBits_PastTheEnd_ThrowsInvalidImageContent()
    {
        Assert.Throws<InvalidImageContentException>(() =>
        {
            BrotliBitReader reader = new(new byte[] { 0xFF, 0xFF }, 0, 0);
            reader.ReadBits(12);
            reader.ReadBits(5);
        });
    }

    [Fact]
    public void DropBits_OfAnEmptyInput_ThrowsInvalidImageContent()
    {
        Assert.Throws<InvalidImageContentException>(() =>
        {
            BrotliBitReader reader = new(ReadOnlySpan<byte>.Empty, 0, 0);
            reader.DropBits(1);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(13)]
    [InlineData(40)]
    [InlineData(61)]
    public void Unload_GivesBackTheBytesThatAreNotUsed_AndANewReaderGoesOn(int bitsBefore)
    {
        byte[] input = RandomBytes(32, seed: bitsBefore);
        BrotliBitReader first = new(input, 0, 0);
        for (int left = bitsBefore; left > 0; left -= Math.Min(left, 17))
        {
            first.ReadBits(Math.Min(left, 17));
        }

        int consumed = first.Unload(out uint heldBits, out int heldBitCount);

        Assert.Equal((bitsBefore + 7) / 8, consumed);
        Assert.Equal((8 - (bitsBefore % 8)) % 8, heldBitCount);

        BrotliBitReader second = new(input.AsSpan(consumed), heldBits, heldBitCount);
        Assert.Equal(Bits(input, bitsBefore, 24), second.ReadBits(24));
        Assert.Equal(Bits(input, bitsBefore + 24, 7), second.ReadBits(7));
    }

    [Theory]
    [InlineData(new byte[] { 0b0000_0101, 0xAB }, true)]
    [InlineData(new byte[] { 0b0100_0101, 0xAB }, false)]
    public void JumpToByteBoundary_SaysWhetherThePaddingIsZero(byte[] input, bool expected)
    {
        BrotliBitReader reader = new(input, 0, 0);
        reader.ReadBits(3);

        Assert.Equal(expected, reader.JumpToByteBoundary());
        Assert.Equal(0xAB, reader.ReadBits(8));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(30)]
    public void CopyBytes_CopiesTheBytesAfterTheOnesAlreadyRead(int count)
    {
        byte[] input = RandomBytes(40, seed: count);
        BrotliBitReader reader = new(input, 0, 0);
        reader.ReadBits(16);
        byte[] destination = new byte[count];

        reader.CopyBytes(destination);

        Assert.Equal(input.AsSpan(2, count).ToArray(), destination);
        Assert.Equal(input[2 + count], reader.ReadBits(8));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(30)]
    public void SkipBytes_SkipsTheBytesAfterTheOnesAlreadyRead(int count)
    {
        byte[] input = RandomBytes(40, seed: count);
        BrotliBitReader reader = new(input, 0, 0);
        reader.ReadBits(16);

        reader.SkipBytes(count);

        Assert.Equal(input[2 + count], reader.ReadBits(8));
    }

    [Fact]
    public void CopyBytes_PastTheEnd_ThrowsInvalidImageContent()
    {
        Assert.Throws<InvalidImageContentException>(() =>
        {
            BrotliBitReader reader = new(new byte[10], 0, 0);
            reader.ReadBits(8);
            reader.CopyBytes(new byte[10]);
        });
    }

    [Fact]
    public void SkipBytes_PastTheEnd_ThrowsInvalidImageContent()
    {
        Assert.Throws<InvalidImageContentException>(() =>
        {
            BrotliBitReader reader = new(new byte[10], 0, 0);
            reader.SkipBytes(11);
        });
    }

    private static byte[] RandomBytes(int length, int seed)
    {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static int Bits(byte[] input, int position, int count)
    {
        int bits = 0;
        for (int i = 0; i < count; i++)
        {
            int bit = (input[(position + i) >> 3] >> ((position + i) & 7)) & 1;
            bits |= bit << i;
        }

        return bits;
    }
}
