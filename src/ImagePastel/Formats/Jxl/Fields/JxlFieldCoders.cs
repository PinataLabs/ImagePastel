// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/fields.h and lib/jxl/fields.cc (BitsCoder, U32Coder, U64Coder and F16Coder), and
// lib/jxl/enc_fields.cc (their Write methods).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Formats.Jxl.Fields;

/// <summary>
/// Reads and writes the field encodings. libjxl has one coder struct per encoding; here each
/// coder is a group of methods. <c>CanEncode</c> checks a value and gives its size, and <c>Write</c>
/// writes a value that <c>CanEncode</c> accepted.
/// </summary>
internal static class JxlFieldCoders
{
    /// <summary>Reads a value coded with <paramref name="enc"/>.</summary>
    /// <param name="enc">The encoding.</param>
    /// <param name="reader">The reader.</param>
    /// <returns>The value.</returns>
    public static uint ReadU32(JxlU32Enc enc, JxlBitReader reader)
    {
        uint selector = reader.ReadBits32(2);
        JxlU32Distr d = enc.GetDistr(selector);
        if (d.IsDirect)
        {
            return d.Direct;
        }

        return unchecked(reader.ReadBits32(d.ExtraBits) + d.Offset);
    }

    /// <summary>Reads a 64-bit value: a selector, then 4, 8 or a varint of 12, 8, ..., 4 bits.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The value.</returns>
    public static ulong ReadU64(JxlBitReader reader)
    {
        ulong selector = reader.ReadBits(2);
        if (selector == 0)
        {
            return 0;
        }

        if (selector == 1)
        {
            return 1 + reader.ReadBits(4);
        }

        if (selector == 2)
        {
            return 17 + reader.ReadBits(8);
        }

        // Selector 3: varint, groups of 12, then 8, and last 4 bits.
        ulong result = reader.ReadBits(12);
        int shift = 12;
        while (reader.ReadBits(1) != 0)
        {
            if (shift == 60)
            {
                result |= reader.ReadBits(4) << shift;
                break;
            }

            result |= reader.ReadBits(8) << shift;
            shift += 8;
        }

        return result;
    }

    /// <summary>
    /// Reads an IEEE 754 half-precision value outside a bundle, where libjxl's <c>F16Coder::Read</c>
    /// fails at once: infinity and NaN throw <see cref="InvalidImageContentException"/>.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The value.</returns>
    public static float ReadF16OrThrow(JxlBitReader reader)
    {
        if (!TryReadF16(reader, out float value))
        {
            JxlThrowHelper.ThrowInvalidImageContentException("F16 infinity or NaN are not supported");
        }

        return value;
    }

    /// <summary>Reads an IEEE 754 half-precision value. Infinity and NaN are invalid.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="value">The value, if valid.</param>
    /// <returns><see langword="true"/> if the value is finite.</returns>
    public static bool TryReadF16(JxlBitReader reader, out float value)
    {
        // netstandard2.1 has no System.Half, so decode by hand as libjxl does.
        uint bits16 = reader.ReadBits32(16);
        uint sign = bits16 >> 15;
        uint biasedExp = (bits16 >> 10) & 0x1F;
        uint mantissa = bits16 & 0x3FF;

        if (biasedExp == 31)
        {
            value = 0;
            return false;
        }

        // Subnormal or zero.
        if (biasedExp == 0)
        {
            value = (1.0f / 16384) * (mantissa * (1.0f / 1024));
            if (sign != 0)
            {
                value = -value;
            }

            return true;
        }

        // Normalized: convert the representation directly.
        uint biasedExp32 = biasedExp + (127 - 15);
        uint mantissa32 = mantissa << (23 - 10);
        uint bits32 = (sign << 31) | (biasedExp32 << 23) | mantissa32;
        value = BitConverter.Int32BitsToSingle(unchecked((int)bits32));
        return true;
    }

    /// <summary>Returns whether <paramref name="value"/> fits in <paramref name="bits"/> bits.</summary>
    /// <param name="bits">The number of bits, 1 to 32.</param>
    /// <param name="value">The value.</param>
    /// <param name="encodedBits">The number of bits the value takes.</param>
    /// <returns><see langword="true"/> if the value can be encoded.</returns>
    public static bool CanEncodeBits(int bits, uint value, out int encodedBits)
    {
        encodedBits = bits;
        return value < (1UL << bits);
    }

    /// <summary>Writes <paramref name="value"/> in <paramref name="bits"/> bits. <see cref="CanEncodeBits"/> must accept it.</summary>
    /// <param name="bits">The number of bits, 1 to 32.</param>
    /// <param name="value">The value.</param>
    /// <param name="writer">The writer.</param>
    public static void WriteBits(int bits, uint value, JxlBitWriter writer)
    {
        DebugGuard.IsTrue(value < (1UL << bits), "Value too large for its bits");
        writer.Write(bits, value);
    }

    /// <summary>Returns whether <paramref name="value"/> can be coded with <paramref name="enc"/>.</summary>
    /// <param name="enc">The encoding.</param>
    /// <param name="value">The value.</param>
    /// <param name="encodedBits">The number of bits the value takes, or 0 if it cannot be encoded.</param>
    /// <returns><see langword="true"/> if the value can be encoded.</returns>
    public static bool CanEncodeU32(JxlU32Enc enc, uint value, out int encodedBits)
    {
        bool ok = ChooseSelector(enc, value, out _, out int totalBits);
        encodedBits = ok ? totalBits : 0;
        return ok;
    }

    /// <summary>Writes <paramref name="value"/> coded with <paramref name="enc"/>. <see cref="CanEncodeU32"/> must accept it.</summary>
    /// <param name="enc">The encoding.</param>
    /// <param name="value">The value.</param>
    /// <param name="writer">The writer.</param>
    public static void WriteU32(JxlU32Enc enc, uint value, JxlBitWriter writer)
    {
        bool ok = ChooseSelector(enc, value, out uint selector, out int totalBits);
        DebugGuard.IsTrue(ok, "No feasible selector");

        writer.Write(2, selector);

        JxlU32Distr d = enc.GetDistr(selector);
        if (!d.IsDirect)
        {
            // Nothing more to write for direct encoding.
            uint offset = d.Offset;
            DebugGuard.IsTrue(value >= offset, "Value below the offset");
            writer.Write(totalBits - 2, value - offset);
        }
    }

    /// <summary>
    /// Returns the number of bits a 64-bit value takes. Every value can be encoded; libjxl's
    /// <c>U64Coder::CanEncode</c> exists for the size.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The number of bits.</returns>
    public static int U64EncodedBits(ulong value)
    {
        if (value == 0)
        {
            return 2; // 2 selector bits
        }

        if (value <= 16)
        {
            return 2 + 4; // 2 selector bits + 4 payload bits
        }

        if (value <= 272)
        {
            return 2 + 8; // 2 selector bits + 8 payload bits
        }

        int encodedBits = 2 + 12; // 2 selector bits + 12 payload bits
        value >>= 12;
        int shift = 12;
        while (value > 0 && shift < 60)
        {
            encodedBits += 1 + 8; // 1 continuation bit + 8 payload bits
            value >>= 8;
            shift += 8;
        }

        if (value > 0)
        {
            // This only could happen if shift == N - 4.
            encodedBits += 1 + 4; // 1 continuation bit + 4 payload bits
        }
        else
        {
            encodedBits += 1; // 1 stop bit
        }

        return encodedBits;
    }

    /// <summary>Writes a 64-bit value: a selector, then 4, 8 or a varint of 12, 8, ..., 4 bits.</summary>
    /// <param name="value">The value.</param>
    /// <param name="writer">The writer.</param>
    public static void WriteU64(ulong value, JxlBitWriter writer)
    {
        if (value == 0)
        {
            // Selector: use 0 bits, value 0
            writer.Write(2, 0);
        }
        else if (value <= 16)
        {
            // Selector: use 4 bits, value 1..16
            writer.Write(2, 1);
            writer.Write(4, value - 1);
        }
        else if (value <= 272)
        {
            // Selector: use 8 bits, value 17..272
            writer.Write(2, 2);
            writer.Write(8, value - 17);
        }
        else
        {
            // Selector: varint, first a 12-bit group, after that per 8-bit group.
            writer.Write(2, 3);
            writer.Write(12, value & 4095);
            value >>= 12;
            int shift = 12;
            while (value > 0 && shift < 60)
            {
                // Indicate varint not done
                writer.Write(1, 1);
                writer.Write(8, value & 255);
                value >>= 8;
                shift += 8;
            }

            if (value > 0)
            {
                // This only could happen if shift == N - 4.
                writer.Write(1, 1);
                writer.Write(4, value & 15);

                // Implicitly closed sequence, no extra stop bit is required.
            }
            else
            {
                // Indicate end of varint
                writer.Write(1, 0);
            }
        }
    }

    /// <summary>
    /// Returns whether <paramref name="value"/> can be stored as a half-precision float: it must be
    /// finite and at most 65504 in magnitude. Other values lose precision but are accepted.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="encodedBits">The number of bits the value takes.</param>
    /// <returns><see langword="true"/> if the value can be encoded.</returns>
    public static bool CanEncodeF16(float value, out int encodedBits)
    {
        encodedBits = 16;
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return false;
        }

        return Math.Abs(value) <= 65504.0f;
    }

    /// <summary>
    /// Writes <paramref name="value"/> as a half-precision float, truncating the mantissa as libjxl
    /// does. <see cref="CanEncodeF16"/> must accept it.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="writer">The writer.</param>
    public static void WriteF16(float value, JxlBitWriter writer)
    {
        uint bits32 = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        uint sign = bits32 >> 31;
        uint biasedExp32 = (bits32 >> 23) & 0xFF;
        uint mantissa32 = bits32 & 0x7FFFFF;

        int exp = (int)biasedExp32 - 127;
        DebugGuard.IsTrue(exp <= 15, "Too big to encode, CanEncodeF16 should return false");

        // Tiny or zero => zero.
        if (exp < -24)
        {
            writer.Write(16, 0);
            return;
        }

        uint biasedExp16;
        uint mantissa16;

        // exp = [-24, -15] => subnormal
        if (exp < -14)
        {
            biasedExp16 = 0;
            int subExp = -14 - exp;
            mantissa16 = (1u << (10 - subExp)) + (mantissa32 >> (13 + subExp));
        }
        else
        {
            // exp = [-14, 15]
            biasedExp16 = (uint)(exp + 15);
            mantissa16 = mantissa32 >> 13;
        }

        uint bits16 = (sign << 15) | (biasedExp16 << 10) | mantissa16;
        writer.Write(16, bits16);
    }

    /// <summary>
    /// Chooses the selector of <paramref name="enc"/> that codes <paramref name="value"/> in the fewest bits.
    /// </summary>
    /// <param name="enc">The encoding.</param>
    /// <param name="value">The value.</param>
    /// <param name="selector">The selector.</param>
    /// <param name="totalBits">The number of bits, including the 2 selector bits.</param>
    /// <returns><see langword="false"/> if no distribution holds the value.</returns>
    private static bool ChooseSelector(JxlU32Enc enc, uint value, out uint selector, out int totalBits)
    {
        selector = 0;

        // It is difficult to verify whether the distributions are sorted, so check all selectors
        // and keep the one with the fewest total bits.
        totalBits = 64; // more than any valid encoding
        for (uint s = 0; s < 4; ++s)
        {
            JxlU32Distr d = enc.GetDistr(s);
            if (d.IsDirect)
            {
                if (d.Direct == value)
                {
                    selector = s;
                    totalBits = 2;
                    return true; // Done, direct is always the best possible.
                }

                continue;
            }

            int extraBits = d.ExtraBits;
            uint offset = d.Offset;
            if (value < offset || value >= offset + (1UL << extraBits))
            {
                continue;
            }

            // Better than prior encoding, remember it:
            if (2 + extraBits < totalBits)
            {
                selector = s;
                totalBits = 2 + extraBits;
            }
        }

        return totalBits != 64;
    }
}
