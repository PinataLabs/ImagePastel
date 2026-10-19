// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/options.h (StaticPropRange).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace SixLabors.ImageSharp.Formats.Jxl.Modular;

/// <summary>
/// A box over the two static properties, channel and stream id: for each, the half-open range
/// [begin, end). libjxl's <c>std::array&lt;std::array&lt;uint32_t, 2&gt;, 2&gt;</c>, a value type.
/// </summary>
internal unsafe struct JxlStaticPropRange
{
    // The bounds in libjxl's order: the channel's begin and end, then the stream id's.
    private fixed uint bounds[4];

    /// <summary>Gets or sets one bound.</summary>
    /// <param name="property">The static property: 0 for the channel, 1 for the stream id.</param>
    /// <param name="bound">0 for the first value in the range, 1 for the first value after it.</param>
    /// <returns>The bound.</returns>
    public uint this[int property, int bound]
    {
        readonly get => this.bounds[Index(property, bound)];
        set => this.bounds[Index(property, bound)] = value;
    }

    private static int Index(int property, int bound)
    {
        DebugGuard.MustBeBetweenOrEqualTo(property, 0, 1, nameof(property));
        DebugGuard.MustBeBetweenOrEqualTo(bound, 0, 1, nameof(bound));
        return (property * 2) + bound;
    }
}
