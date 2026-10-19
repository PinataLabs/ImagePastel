// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/fields.h and lib/jxl/fields.cc (U32Coder, U64Coder and F16Coder read paths).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Formats.Jxl.Fields;

/// <summary>
/// Reads the variable-length field encodings. Only the read side is ported.
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
}
