// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/color_encoding_internal.h and lib/jxl/color_encoding_internal.cc (Customxy,
// CustomTransferFunction, ColorEncoding fields), with the storage defaults and gamma limits of
// lib/jxl/cms/color_encoding_cms.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using static PinataLabs.ImagePastel.Formats.Jxl.Fields.JxlU32Distr;

namespace PinataLabs.ImagePastel.Formats.Jxl.Headers;

/// <summary>A CIE xy chromaticity, as fixed-point values scaled by 10^6.</summary>
internal sealed class JxlCustomXy : JxlFields
{
    private static readonly JxlU32Enc Enc = new(Bits(19), BitsOffset(19, 524288), BitsOffset(20, 1048576), BitsOffset(21, 2097152));

    public JxlCustomXy() => JxlBundle.Init(this);

    /// <summary>Gets or sets the x chromaticity, times 10^6.</summary>
    public int X { get; set; }

    /// <summary>Gets or sets the y chromaticity, times 10^6.</summary>
    public int Y { get; set; }

    /// <summary>Gets the chromaticity as CIE xy (Customxy::GetValue).</summary>
    public (double X, double Y) Value => (this.X * (1.0 / 1000000), this.Y * (1.0 / 1000000));

    /// <summary>Copies the fields of <paramref name="other"/> into this instance.</summary>
    /// <param name="other">The chromaticity to copy.</param>
    public void CopyFrom(JxlCustomXy other)
    {
        this.X = other.X;
        this.Y = other.Y;
    }

    public override void VisitFields(JxlVisitor visitor)
    {
        this.X = JxlBits.UnpackSigned(visitor.U32(Enc, 0, JxlBits.PackSigned(this.X)));
        this.Y = JxlBits.UnpackSigned(visitor.U32(Enc, 0, JxlBits.PackSigned(this.Y)));
    }
}
