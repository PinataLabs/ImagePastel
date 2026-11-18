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
    /// <summary>The most bits that one read may take.</summary>
    public const int MaxBitsPerRead = 24;

    private const int HuffmanTableBits = 8;

    private readonly ReadOnlySpan<byte> input;

    // The next byte of the input to load.
    private int position;

    // The bits that are loaded and not taken yet. The next bit is the lowest.
    private ulong value;
    private int bitCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrotliBitReader"/> struct.
    /// </summary>
    /// <param name="input">The rest of the stream.</param>
    /// <param name="heldBits">The bits, fewer than 8, that an earlier reader left over.</param>
    /// <param name="heldBitCount">The number of bits in <paramref name="heldBits"/>.</param>
    public BrotliBitReader(ReadOnlySpan<byte> input, uint heldBits, int heldBitCount)
    {
        this.input = input;
        this.position = 0;
        this.value = heldBits;
        this.bitCount = heldBitCount;
    }

    /// <summary>
    /// Reads <paramref name="count"/> bits, at most <see cref="MaxBitsPerRead"/>.
    /// </summary>
    /// <param name="count">The number of bits.</param>
    /// <returns>The bits.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadBits(int count)
    {
        this.Fill();
        int bits = (int)((uint)this.value & ((1u << count) - 1));
        this.DropBits(count);
        return bits;
    }

    /// <summary>
    /// Returns the next <paramref name="count"/> bits, at most <see cref="MaxBitsPerRead"/>, without
    /// taking them. Bits past the end of the input are zero.
    /// </summary>
    /// <param name="count">The number of bits.</param>
    /// <returns>The bits.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int PeekBits(int count)
    {
        this.Fill();
        return (int)((uint)this.value & ((1u << count) - 1));
    }

    /// <summary>
    /// Takes <paramref name="count"/> bits that <see cref="PeekBits"/> returned.
    /// </summary>
    /// <param name="count">The number of bits.</param>
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

    /// <summary>
    /// Reads the next symbol of a prefix code, which takes at most 15 bits.
    /// </summary>
    /// <param name="table">The array that holds the lookup table.</param>
    /// <param name="offset">The index of the lookup table in <paramref name="table"/>.</param>
    /// <returns>The symbol.</returns>
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
            // A second-level table: its offset is relative to the root entry.
            int extraBits = length - HuffmanTableBits;
            index += code.Value + (int)((bits >> HuffmanTableBits) & ((1u << extraBits) - 1));
            code = table[index];
            length = HuffmanTableBits + code.Bits;
        }

        this.DropBits(length);
        return code.Value;
    }

    /// <summary>
    /// Skips the bits up to the next byte of the stream (<c>BrotliJumpToByteBoundary</c>).
    /// </summary>
    /// <returns><see langword="false"/> if a skipped bit is not zero.</returns>
    public bool JumpToByteBoundary()
    {
        // Whole bytes are loaded, so the bits of a byte that is partly taken are the low ones.
        int padBitCount = this.bitCount & 7;
        uint padBits = (uint)this.value & ((1u << padBitCount) - 1);
        this.value >>= padBitCount;
        this.bitCount -= padBitCount;
        return padBits == 0;
    }

    /// <summary>
    /// Copies the next bytes of the stream, which is at a byte boundary, to fill
    /// <paramref name="destination"/> (<c>BrotliCopyBytes</c>).
    /// </summary>
    /// <param name="destination">The destination.</param>
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

    /// <summary>
    /// Skips the next <paramref name="count"/> bytes of the stream, which is at a byte boundary.
    /// </summary>
    /// <param name="count">The number of bytes.</param>
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

    /// <summary>
    /// Gives the loaded bytes that are not used back to the input (<c>BrotliBitReaderUnload</c>).
    /// </summary>
    /// <param name="heldBits">The bits, fewer than 8, of the byte that is partly taken.</param>
    /// <param name="heldBitCount">The number of bits in <paramref name="heldBits"/>.</param>
    /// <returns>The number of bytes of the input that are used.</returns>
    public int Unload(out uint heldBits, out int heldBitCount)
    {
        // The held bits of an earlier reader are fewer than 8, so every whole byte is from this input.
        this.position -= this.bitCount >> 3;
        this.bitCount &= 7;
        this.value &= (1u << this.bitCount) - 1;
        heldBits = (uint)this.value;
        heldBitCount = this.bitCount;
        return this.position;
    }

    // Loads input until more than 32 bits are held, or to the end of the input.
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
