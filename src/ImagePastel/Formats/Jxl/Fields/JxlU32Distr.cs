// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/field_encodings.h (U32Distr, U32Enc, Val, Bits, BitsOffset).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Formats.Jxl.Fields;

/// <summary>
/// One of the four distributions of a <see cref="JxlU32Enc"/>: either a direct value, or a number
/// of extra bits plus an offset.
/// </summary>
internal readonly struct JxlU32Distr
{
    private const uint DirectFlag = 0x80000000u;

    private readonly uint d;

    private JxlU32Distr(uint d) => this.d = d;

    /// <summary>Gets a value indicating whether the distribution is a single direct value.</summary>
    public bool IsDirect => (this.d & DirectFlag) != 0;

    /// <summary>Gets the direct value. Only valid if <see cref="IsDirect"/>.</summary>
    public uint Direct => this.d & (DirectFlag - 1);

    /// <summary>Gets the number of extra bits. Only valid if not <see cref="IsDirect"/>.</summary>
    public int ExtraBits => (int)(this.d & 0x1F) + 1;

    /// <summary>Gets the offset added to the extra bits. Only valid if not <see cref="IsDirect"/>.</summary>
    public uint Offset => (this.d >> 5) & 0x3FFFFFF;

    /// <summary>A distribution that always yields <paramref name="value"/>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The distribution.</returns>
    public static JxlU32Distr Val(uint value) => new(value | DirectFlag);

    /// <summary>A distribution that reads <paramref name="bits"/> bits and adds <paramref name="offset"/>.</summary>
    /// <param name="bits">The number of bits, 1 to 32.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The distribution.</returns>
    public static JxlU32Distr BitsOffset(uint bits, uint offset) => new(((bits - 1) & 0x1F) + ((offset & 0x3FFFFFF) << 5));

    /// <summary>A distribution that reads <paramref name="bits"/> bits.</summary>
    /// <param name="bits">The number of bits, 1 to 32.</param>
    /// <returns>The distribution.</returns>
    public static JxlU32Distr Bits(uint bits) => BitsOffset(bits, 0);
}
