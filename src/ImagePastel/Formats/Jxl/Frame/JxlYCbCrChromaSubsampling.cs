// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/frame_header.h and lib/jxl/frame_header.cc (YCbCrChromaSubsampling, BlendingInfo,
// AnimationFrame, Passes, FrameHeader), and lib/jxl/frame_dimensions.h (FrameDimensions).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using static PinataLabs.ImagePastel.Formats.Jxl.Fields.JxlU32Distr;

namespace PinataLabs.ImagePastel.Formats.Jxl.Frame;

/// <summary>The chroma subsampling of a YCbCr frame.</summary>
internal sealed class JxlYCbCrChromaSubsampling : JxlFields
{
    private static readonly byte[] HShiftTable = { 0, 1, 1, 0 };
    private static readonly byte[] VShiftTable = { 0, 1, 0, 1 };

    private readonly uint[] channelMode = new uint[3];

    public JxlYCbCrChromaSubsampling() => JxlBundle.Init(this);

    /// <summary>Gets the largest horizontal shift.</summary>
    public int MaxHShift { get; private set; }

    /// <summary>Gets the largest vertical shift.</summary>
    public int MaxVShift { get; private set; }

    /// <summary>Returns the horizontal shift of channel <paramref name="c"/> (Cb, Y, Cr order).</summary>
    /// <param name="c">The channel.</param>
    /// <returns>The shift.</returns>
    public int HShift(int c) => this.MaxHShift - HShiftTable[this.channelMode[c]];

    /// <summary>Returns the vertical shift of channel <paramref name="c"/> (Cb, Y, Cr order).</summary>
    /// <param name="c">The channel.</param>
    /// <returns>The shift.</returns>
    public int VShift(int c) => this.MaxVShift - VShiftTable[this.channelMode[c]];

    public override void VisitFields(JxlVisitor visitor)
    {
        for (int i = 0; i < 3; i++)
        {
            this.channelMode[i] = visitor.Bits(2, 0, this.channelMode[i]);
        }

        this.Recompute();
    }

    private void Recompute()
    {
        this.MaxHShift = 0;
        this.MaxVShift = 0;
        foreach (uint ch in this.channelMode)
        {
            this.MaxHShift = Math.Max(this.MaxHShift, HShiftTable[ch]);
            this.MaxVShift = Math.Max(this.MaxVShift, VShiftTable[ch]);
        }
    }
}
