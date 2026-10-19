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
    private uint groupBegin;
    private uint groupEnd;

    /// <summary>Gets or sets one bound.</summary>
    /// <param name="property">The static property: 0 for the channel, 1 for the stream id.</param>
    /// <param name="bound">0 for the first value in the range, 1 for the first value after it.</param>
    /// <returns>The bound.</returns>
    public uint this[int property, int bound]
    {
        readonly get => (property, bound) switch
        {
            (0, 0) => this.channelBegin,
            (0, _) => this.channelEnd,
            (_, 0) => this.groupBegin,
            _ => this.groupEnd,
        };

        set
        {
            switch ((property, bound))
            {
                case (0, 0):
                    this.channelBegin = value;
                    break;
                case (0, _):
                    this.channelEnd = value;
                    break;
                case (_, 0):
                    this.groupBegin = value;
                    break;
                default:
                    this.groupEnd = value;
                    break;
            }
        }
    }
}
