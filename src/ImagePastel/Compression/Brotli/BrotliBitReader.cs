// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from Brotli (https://github.com/google/brotli) at tag v1.2.0 (commit 028fb5a2):
// c/dec/bit_reader.h and c/dec/bit_reader.c, and DecodeSymbol in c/dec/decode.c.
// Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors. MIT License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace PinataLabs.ImagePastel.Compression.Brotli;

/// <summary>
/// Reads bits, least significant first, from input that holds the rest of the stream.
/// </summary>
/// <remarks>
/// The C reader can stop where the input ends and resume with more, so it has a "safe" variant of each
/// read that checks the input first. This reader has all the input, so it has one variant: a peek past
/// the end reads zeros, and taking bits that the input does not have throws.
/// </remarks>
internal ref struct BrotliBitReader
{
    public const int MaxBitsPerRead = 24;

    private const int HuffmanTableBits = 8;

    private readonly ReadOnlySpan<byte> input;

    private int position;

    private ulong value;
    private int bitCount;

    public BrotliBitReader(ReadOnlySpan<byte> input, uint heldBits, int heldBitCount)
    {
        this.input = input;
        this.position = 0;
        this.value = heldBits;
        this.bitCount = heldBitCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadBits(int count)
    {
        this.Fill();
        int bits = (int)((uint)this.value & ((1u << count) - 1));
        this.DropBits(count);
        return bits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int PeekBits(int count)
    {
        this.Fill();
        return (int)((uint)this.value & ((1u << count) - 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DropBits(int count)
    {
        if (count > this.bitCount)
        {
            BrotliThrowHelper.ThrowTruncated();
        }

        this.value >>= count;
        this.bitCount -= count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadSymbol(BrotliHuffmanCode[] table, int offset)
    {
        this.Fill();
        uint bits = (uint)this.value;
        int index = offset + (int)(bits & ((1 << HuffmanTableBits) - 1));
        BrotliHuffmanCode code = table[index];
        int length = code.Bits;
        if (length > HuffmanTableBits)
        {
            int extraBits = length - HuffmanTableBits;
            index += code.Value + (int)((bits >> HuffmanTableBits) & ((1u << extraBits) - 1));
            code = table[index];
            length = HuffmanTableBits + code.Bits;
        }

        this.DropBits(length);
        return code.Value;
    }

    public bool JumpToByteBoundary()
    {
        int padBitCount = this.bitCount & 7;
        uint padBits = (uint)this.value & ((1u << padBitCount) - 1);
        this.value >>= padBitCount;
        this.bitCount -= padBitCount;
        return padBits == 0;
    }

    public void CopyBytes(Span<byte> destination)
    {
        int copied = 0;
        while (this.bitCount >= 8 && copied < destination.Length)
        {
            destination[copied++] = (byte)this.value;
            this.value >>= 8;
            this.bitCount -= 8;
        }

        int count = destination.Length - copied;
        if (count > this.input.Length - this.position)
        {
            BrotliThrowHelper.ThrowTruncated();
        }

        this.input.Slice(this.position, count).CopyTo(destination.Slice(copied));
        this.position += count;
    }

    public void SkipBytes(int count)
    {
        while (this.bitCount >= 8 && count > 0)
        {
            this.value >>= 8;
            this.bitCount -= 8;
            count--;
        }

        if (count > this.input.Length - this.position)
        {
            BrotliThrowHelper.ThrowTruncated();
        }

        this.position += count;
    }

    public int Unload(out uint heldBits, out int heldBitCount)
    {
        this.position -= this.bitCount >> 3;
        this.bitCount &= 7;
        this.value &= (1u << this.bitCount) - 1;
        heldBits = (uint)this.value;
        heldBitCount = this.bitCount;
        return this.position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Fill()
    {
        if (this.bitCount <= 32)
        {
            if (this.position <= this.input.Length - 4)
            {
                this.value |= (ulong)BinaryPrimitives.ReadUInt32LittleEndian(this.input.Slice(this.position)) << this.bitCount;
                this.position += 4;
                this.bitCount += 32;
            }
            else
            {
                this.FillFromLastBytes();
            }
        }
    }

    private void FillFromLastBytes()
    {
        while (this.bitCount <= 56 && this.position < this.input.Length)
        {
            this.value |= (ulong)this.input[this.position++] << this.bitCount;
            this.bitCount += 8;
        }
    }
}
