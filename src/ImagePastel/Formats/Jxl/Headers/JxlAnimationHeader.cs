// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/headers.h and lib/jxl/headers.cc (SizeHeader, PreviewHeader, AnimationHeader).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Headers;

/// <summary>Timing of an animation.</summary>
internal sealed class JxlAnimationHeader : JxlFields
{
    public JxlAnimationHeader() => JxlBundle.Init(this);

    /// <summary>Gets or sets the numerator of the ticks per second (a rational, to support NTSC).</summary>
    public uint TpsNumerator { get; set; }

    /// <summary>Gets or sets the denominator of the ticks per second.</summary>
    public uint TpsDenominator { get; set; }

    /// <summary>Gets or sets the number of loops; 0 repeats forever.</summary>
    public uint NumLoops { get; set; }

    /// <summary>Gets or sets a value indicating whether frames carry a timecode.</summary>
    public bool HaveTimecodes { get; set; }

    public override void VisitFields(JxlVisitor visitor)
    {
        this.TpsNumerator = visitor.U32(Val(100), Val(1000), BitsOffset(10, 1), BitsOffset(30, 1), 1, this.TpsNumerator);
        this.TpsDenominator = visitor.U32(Val(1), Val(1001), BitsOffset(8, 1), BitsOffset(10, 1), 1, this.TpsDenominator);
        this.NumLoops = visitor.U32(Val(0), Bits(3), Bits(16), Bits(32), 0, this.NumLoops);
        this.HaveTimecodes = visitor.Bool(false, this.HaveTimecodes);
    }
}
