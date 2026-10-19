// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_bit_writer.h and lib/jxl/enc_bit_writer.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SixLabors.ImageSharp.Formats.Jxl.Base;

/// <summary>
/// Writes bits into bytes in increasing addresses, and within a byte least-significant bit first,
/// as <see cref="JxlBitReader"/> reads them.
/// </summary>
/// <remarks>
/// libjxl reserves storage up front with <c>WithMaxBits</c> allotments, which also charge the bits
/// to an <c>AuxOut</c> layer. This port has no <c>AuxOut</c>, so the storage grows on demand instead
/// and there are no allotments. The bytes after the last written bit are always zero, as in libjxl.
/// </remarks>
internal sealed class JxlBitWriter : IJxlBitSink
{
    /// <summary>
    /// The most bits one <see cref="Write"/> call takes. libjxl takes 56; this port splits wider
    /// writes in two, so a whole 64-bit value can be written at once.
    /// </summary>
    public const int MaxBitsPerCall = 64;

    // libjxl's limit for a single store: a 64-bit word shifted by up to 7 bits already in the last byte.
    private const int MaxBitsPerStore = 56;

    private byte[] storage = new byte[64];
    private long bitsWritten;

    /// <summary>Gets the number of bits written so far.</summary>
    public long BitsWritten => this.bitsWritten;

    /// <summary>
    /// Writes the low <paramref name="nBits"/> bits of <paramref name="bits"/>. The other bits must be zero.
    /// </summary>
    /// <param name="nBits">The number of bits, 0 to <see cref="MaxBitsPerCall"/>.</param>
    /// <param name="bits">The bits.</param>
    public void Write(int nBits, ulong bits)
    {
        DebugGuard.IsTrue(nBits is >= 0 and <= MaxBitsPerCall, "nBits must be 0 to 64");
        DebugGuard.IsTrue(nBits == 64 || (bits >> nBits) == 0, "Only the low nBits bits may be set");

        if (nBits > MaxBitsPerStore)
        {
            this.Write(32, bits & uint.MaxValue);
            this.Write(nBits - 32, bits >> 32);
            return;
        }

        // Example: let's assume that 3 bits (Rs below) have been written already:
        // BYTE+0       BYTE+1       BYTE+2
        // 0000 0RRR    ???? ????    ???? ????
        //
        // Now, we could write up to 5 bits by just shifting them left by 3 bits and OR'ing to BYTE-0.
        // For n > 5 bits, we write the lowest 5 bits as above, then write the next lowest bits into
        // BYTE+1 starting from its lower bits and so on.
        int bytesWritten = (int)(this.bitsWritten / 8);
        this.EnsureCapacity(bytesWritten + sizeof(ulong));
        int bitsInFirstByte = (int)(this.bitsWritten % 8);
        bits <<= bitsInFirstByte;

        // The last (partial) byte must already be zero above its valid bits; Write and the appends
        // keep that true. The store writes zeros over the following bytes.
        ulong v = this.storage[bytesWritten];
        DebugGuard.IsTrue(v >> bitsInFirstByte == 0, "The bits after the last written bit must be zero");
        v |= bits;
        BinaryPrimitives.WriteUInt64LittleEndian(this.storage.AsSpan(bytesWritten), v);
        this.bitsWritten += nBits;
    }

    /// <summary>
    /// Pads with zero bits to a byte boundary. This should only rarely be used, for example when the
    /// position is referenced by a byte offset (the TOC points to groups).
    /// </summary>
    public void ZeroPadToByte()
    {
        int remainderBits = (int)((8 - (this.bitsWritten % 8)) % 8);
        if (remainderBits == 0)
        {
            return;
        }

        this.Write(remainderBits, 0);
    }

    /// <summary>Appends <paramref name="span"/>. The writer must be byte-aligned.</summary>
    /// <param name="span">The bytes.</param>
    public void AppendByteAligned(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty)
        {
            return;
        }

        EnsureByteAligned(this.bitsWritten);

        // Concatenate by copying bytes because both source and destination are bytes. The extra zero
        // padding for the next Write comes from EnsureCapacity.
        int pos = (int)(this.bitsWritten / 8);
        this.EnsureCapacity(pos + span.Length + 1);
        span.CopyTo(this.storage.AsSpan(pos));
        this.storage[pos + span.Length] = 0;
        this.bitsWritten += span.Length * 8L;
    }

    /// <summary>Appends the bytes of each of <paramref name="others"/>. Every writer must be byte-aligned.</summary>
    /// <param name="others">The writers.</param>
    public void AppendByteAligned(IReadOnlyList<JxlBitWriter> others)
    {
        // Total size to add so we can preallocate.
        long otherBytes = 0;
        foreach (JxlBitWriter writer in others)
        {
            EnsureByteAligned(writer.BitsWritten);
            otherBytes += writer.BitsWritten / 8;
        }

        if (otherBytes == 0)
        {
            // No bytes to append: this happens for example when creating per-group storage for
            // groups, but not writing anything in them for e.g. lossless images with no alpha.
            return;
        }

        EnsureByteAligned(this.bitsWritten);
        int pos = (int)(this.bitsWritten / 8);
        this.EnsureCapacity(pos + (int)otherBytes + 1);
        foreach (JxlBitWriter writer in others)
        {
            ReadOnlySpan<byte> span = writer.GetSpan();
            span.CopyTo(this.storage.AsSpan(pos));
            pos += span.Length;
        }

        this.storage[pos] = 0; // for next Write
        this.bitsWritten += otherBytes * 8;
    }

    /// <summary>Appends the bits of <paramref name="other"/>, which need not be byte-aligned.</summary>
    /// <param name="other">The writer.</param>
    public void AppendUnaligned(JxlBitWriter other)
    {
        long fullBytes = other.BitsWritten / 8;
        int remainingBits = (int)(other.BitsWritten % 8);
        for (long i = 0; i < fullBytes; ++i)
        {
            this.Write(8, other.storage[i]);
        }

        if (remainingBits > 0)
        {
            this.Write(remainingBits, other.storage[fullBytes] & ((1u << remainingBits) - 1));
        }
    }

    /// <summary>
    /// Returns the bytes written so far. libjxl asks callers to pad to a byte first, to avoid
    /// uninitialized bits; here the bits after the last written bit are always zero, so a partial
    /// last byte is returned as if padded.
    /// </summary>
    /// <returns>The bytes.</returns>
    public ReadOnlySpan<byte> GetSpan() => this.storage.AsSpan(0, (int)((this.bitsWritten + 7) / 8));

    /// <summary>Returns a copy of the bytes written so far, as <see cref="GetSpan"/>.</summary>
    /// <returns>The bytes.</returns>
    public byte[] ToArray() => this.GetSpan().ToArray();

    private static void EnsureByteAligned(long bits)
    {
        if (bits % 8 != 0)
        {
            throw new InvalidOperationException("JPEG XL bit writer must be byte-aligned to append bytes.");
        }
    }

    private void EnsureCapacity(int bytes)
    {
        if (bytes <= this.storage.Length)
        {
            return;
        }

        // New bytes are zero, which keeps the bytes after the last written bit zero.
        Array.Resize(ref this.storage, Math.Max(bytes, this.storage.Length * 2));
    }
}
