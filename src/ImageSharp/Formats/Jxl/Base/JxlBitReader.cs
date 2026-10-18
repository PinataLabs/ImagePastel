// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_bit_reader.h and lib/jxl/dec_bit_reader.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Formats.Jxl.Base;

/// <summary>
/// Reads bits from a byte range, least-significant bit first.
/// <para>
/// As in libjxl, reads past the end of the range return zeros and do not fail at once. The caller
/// checks <see cref="AllReadsWithinBounds"/> (or <see cref="Close"/>) at its next checkpoint. Hot loops
/// can therefore read without a bounds check per symbol.
/// </para>
/// </summary>
internal sealed class JxlBitReader
{
    /// <summary>The most bits one read may return.</summary>
    public const int MaxBitsPerCall = 56;

    private readonly byte[] data;

    private readonly int firstByte;

    // One past the last byte of the range. libjxl keeps end - 8 for the refill check.
    private readonly int end;

    private ulong buf;

    // Always below 64.
    private int bitsInBuf;

    private int nextByte;

    // Number of bytes past the end that were loaded into buf as zeros.
    private long overreadBytes;

    private long checkedOutOfBoundsBits;

    private bool closeCalled;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlBitReader"/> class over a byte range.
    /// The range need not be padded.
    /// </summary>
    /// <param name="data">The bytes.</param>
    /// <param name="offset">The first byte of the range.</param>
    /// <param name="length">The number of bytes in the range.</param>
    public JxlBitReader(byte[] data, int offset, int length)
    {
        this.data = data;
        this.firstByte = offset;
        this.nextByte = offset;
        this.end = offset + length;
        this.Refill();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlBitReader"/> class over a whole array.
    /// </summary>
    /// <param name="data">The bytes.</param>
    public JxlBitReader(byte[] data)
        : this(data, 0, data.Length)
    {
    }

    /// <summary>Gets the number of bytes in the range.</summary>
    public long TotalBytes => this.end - this.firstByte;

    /// <summary>Gets the number of bits consumed so far, including bits read past the end.</summary>
    public long TotalBitsConsumed => ((this.nextByte - this.firstByte + this.overreadBytes) * 8) - this.bitsInBuf;

    /// <summary>
    /// Fills the buffer to at least 56 bits, so several reads can share one refill.
    /// </summary>
    [MethodImpl(InliningOptions.ShortMethod)]
    public void Refill()
    {
        if (this.nextByte > this.end - 8)
        {
            this.BoundsCheckedRefill();
        }
        else
        {
            // Insert valid (possibly nonzero) bits above bitsInBuf.
            this.buf |= BinaryPrimitives.ReadUInt64LittleEndian(this.data.AsSpan(this.nextByte, 8)) << this.bitsInBuf;

            // Advance by the bytes fully absorbed into the buffer.
            this.nextByte += (63 - this.bitsInBuf) >> 3;

            // The lower 3 bits of bitsInBuf stay the same, so the next refill stays aligned.
            this.bitsInBuf |= 56;
        }
    }

    /// <summary>
    /// Returns the next <paramref name="nbits"/> bits without consuming them. Peeking past the end
    /// of the range returns zeros, which prefix codes need.
    /// </summary>
    /// <param name="nbits">The number of bits, at most <see cref="MaxBitsPerCall"/>.</param>
    /// <returns>The bits.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public ulong PeekBits(int nbits) => this.buf & ((1UL << nbits) - 1);

    /// <summary>
    /// Removes bits from the buffer. The buffer must hold at least <paramref name="numBits"/> bits.
    /// </summary>
    /// <param name="numBits">The number of bits.</param>
    [MethodImpl(InliningOptions.ShortMethod)]
    public void Consume(int numBits)
    {
        this.bitsInBuf -= numBits;
        this.buf >>= numBits;
    }

    /// <summary>Reads <paramref name="nbits"/> bits.</summary>
    /// <param name="nbits">The number of bits, at most <see cref="MaxBitsPerCall"/>.</param>
    /// <returns>The bits.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public ulong ReadBits(int nbits)
    {
        this.Refill();
        ulong bits = this.PeekBits(nbits);
        this.Consume(nbits);
        return bits;
    }

    /// <summary>Reads <paramref name="nbits"/> bits as an unsigned 32-bit value.</summary>
    /// <param name="nbits">The number of bits, at most 32.</param>
    /// <returns>The bits.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public uint ReadBits32(int nbits) => (uint)this.ReadBits(nbits);

    /// <summary>
    /// Skips <paramref name="skip"/> bits. Equivalent to reading them one at a time, but faster.
    /// </summary>
    /// <param name="skip">The number of bits.</param>
    public void SkipBits(long skip)
    {
        // The buffer is large enough: don't zero buf below.
        if (skip <= this.bitsInBuf)
        {
            this.Consume((int)skip);
            return;
        }

        // Deduct what the buffer can satisfy.
        skip -= this.bitsInBuf;
        this.bitsInBuf = 0;

        // Not Consume: bits above bitsInBuf may be nonzero.
        this.buf = 0;

        long wholeBytes = skip / 8;
        skip %= 8;
        if (wholeBytes > this.end - this.nextByte)
        {
            // Skipping past the end. Stop at the end so nextByte cannot overflow, and count one
            // more byte as over-read so TotalBitsConsumed exceeds the range.
            this.nextByte = this.end;
            skip += 8;
        }
        else
        {
            this.nextByte += (int)wholeBytes;
        }

        this.Refill();
        this.Consume((int)skip);
    }

    /// <summary>
    /// Skips to the next byte boundary. The skipped bits must be zero.
    /// </summary>
    public void JumpToByteBoundary()
    {
        int remainder = (int)(this.TotalBitsConsumed % 8);
        if (remainder == 0)
        {
            return;
        }

        if (this.ReadBits(8 - remainder) != 0)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Non-zero padding bits");
        }
    }

    /// <summary>
    /// Forces the reader out of bounds after the caller found a corrupt stream. Later reads still
    /// return zeros, but <see cref="AllReadsWithinBounds"/> and <see cref="Close"/> report failure.
    /// </summary>
    public void MarkUnhealthy()
    {
        this.nextByte = this.end;
        this.overreadBytes += 8;
    }

    /// <summary>
    /// Returns whether every bit read so far was inside the range, and marks that the caller has
    /// checked up to here.
    /// </summary>
    /// <returns><see langword="true"/> if no read went past the end.</returns>
    public bool AllReadsWithinBounds()
    {
        this.checkedOutOfBoundsBits = this.TotalBitsConsumed;
        return this.TotalBitsConsumed <= this.TotalBytes * 8;
    }

    /// <summary>
    /// Throws <see cref="InvalidImageContentException"/> if any read went past the end.
    /// </summary>
    /// <param name="what">What was being read, for the message.</param>
    public void EnsureAllReadsWithinBounds(string what)
    {
        if (!this.AllReadsWithinBounds())
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Not enough bytes for {what}");
        }
    }

    /// <summary>
    /// Closes the reader. Throws <see cref="InvalidImageContentException"/> if a read went past the end
    /// and the caller did not check it with <see cref="AllReadsWithinBounds"/>. Call it once.
    /// </summary>
    public void Close()
    {
        if (this.closeCalled)
        {
            return;
        }

        this.closeCalled = true;
        long consumed = this.TotalBitsConsumed;
        if (consumed > this.checkedOutOfBoundsBits && consumed > this.TotalBytes * 8)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Read more bits than available in the bit reader");
        }
    }

    // Separate method keeps the cold code out of the inlined refill.
    [MethodImpl(InliningOptions.ColdPath)]
    private void BoundsCheckedRefill()
    {
        // Read whole bytes until there are [56, 64) bits, as an unaligned 64-bit load would.
        for (; this.bitsInBuf < 64 - 8; this.bitsInBuf += 8)
        {
            if (this.nextByte >= this.end)
            {
                break;
            }

            this.buf |= (ulong)this.data[this.nextByte++] << this.bitsInBuf;
        }

        // Add zero bytes past the end. If they are consumed, Close() fails.
        int extraBytes = (63 - this.bitsInBuf) / 8;
        this.overreadBytes += extraBytes;
        this.bitsInBuf += extraBytes * 8;
    }
}
