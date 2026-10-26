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
internal struct JxlStaticPropRange
{
    private uint channelBegin;
    private uint channelEnd;
    private uint streamBegin;
    private uint streamEnd;

    /// <summary>Gets or sets one bound.</summary>
    /// <param name="property">The static property: 0 for the channel, 1 for the stream id.</param>
    /// <param name="bound">0 for the first value in the range, 1 for the first value after it.</param>
    /// <returns>The bound.</returns>
    public uint this[int property, int bound]
    {
        get => Bound(ref this, property, bound);
        set => Bound(ref this, property, bound) = value;
    }

    // The one place that maps libjxl's [property][bound] index to a field.
    private static ref uint Bound(ref JxlStaticPropRange range, int property, int bound)
    {
        DebugGuard.MustBeBetweenOrEqualTo(property, 0, 1, nameof(property));
        DebugGuard.MustBeBetweenOrEqualTo(bound, 0, 1, nameof(bound));
        switch ((property * 2) + bound)
        {
            case 0:
                return ref range.channelBegin;
            case 1:
                return ref range.channelEnd;
            case 2:
                return ref range.streamBegin;
            default:
                return ref range.streamEnd;
        }
    }
}
