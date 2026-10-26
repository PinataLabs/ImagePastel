// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/headers.h and lib/jxl/headers.cc (SizeHeader, PreviewHeader, AnimationHeader).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using static PinataLabs.ImagePastel.Formats.Jxl.Fields.JxlU32Distr;

namespace PinataLabs.ImagePastel.Formats.Jxl.Headers;

/// <summary>The size of the preview frame.</summary>
internal sealed class JxlPreviewHeader : JxlFields
{
    private static readonly JxlU32Enc Div8Enc = new(Val(16), Val(32), BitsOffset(5, 1), BitsOffset(9, 33));
    private static readonly JxlU32Enc SizeEnc = new(BitsOffset(6, 1), BitsOffset(8, 65), BitsOffset(10, 321), BitsOffset(12, 1345));

    // Width and height divisible by 8.
    private bool div8;
    private uint ysizeDiv8;
    private uint ysize;
    private uint ratio;
    private uint xsizeDiv8;
    private uint xsize;

    public JxlPreviewHeader() => JxlBundle.Init(this);

    /// <summary>Gets the preview width.</summary>
    public ulong XSize
    {
        get
        {
            if (this.ratio != 0)
            {
                return JxlAspectRatio.MulTruncate(this.ratio, this.YSize);
            }

            return this.div8 ? this.xsizeDiv8 * 8UL : this.xsize;
        }
    }

    /// <summary>Gets the preview height.</summary>
    public ulong YSize => this.div8 ? this.ysizeDiv8 * 8UL : this.ysize;

    /// <summary>
    /// Sets the size, choosing the most compact form. Throws <see cref="ImageFormatException"/> for an
    /// empty size. A size the header cannot hold is rejected when it is written.
    /// </summary>
    /// <param name="xsize">The width.</param>
    /// <param name="ysize">The height.</param>
    public void Set(ulong xsize, ulong ysize)
    {
        // libjxl truncates to 32 bits here; the U32 fields reject anything that large anyway.
        uint xsize32 = (uint)Math.Min(xsize, uint.MaxValue);
        uint ysize32 = (uint)Math.Min(ysize, uint.MaxValue);
        if (xsize == 0 || ysize == 0)
        {
            JxlThrowHelper.ThrowImageFormatException("Empty preview");
        }

        this.div8 = (xsize % 8) == 0 && (ysize % 8) == 0;
        if (this.div8)
        {
            this.ysizeDiv8 = ysize32 / 8;
        }
        else
        {
            this.ysize = ysize32;
        }

        this.ratio = JxlAspectRatio.Find(xsize32, ysize32);
        if (this.ratio == 0)
        {
            if (this.div8)
            {
                this.xsizeDiv8 = xsize32 / 8;
            }
            else
            {
                this.xsize = xsize32;
            }
        }
    }

    public override void VisitFields(JxlVisitor visitor)
    {
        this.div8 = visitor.Bool(false, this.div8);

        if (visitor.Conditional(this.div8))
        {
            this.ysizeDiv8 = visitor.U32(Div8Enc, 1, this.ysizeDiv8);
        }

        if (visitor.Conditional(!this.div8))
        {
            this.ysize = visitor.U32(SizeEnc, 1, this.ysize);
        }

        this.ratio = visitor.Bits(3, 0, this.ratio);
        if (visitor.Conditional(this.ratio == 0 && this.div8))
        {
            this.xsizeDiv8 = visitor.U32(Div8Enc, 1, this.xsizeDiv8);
        }

        if (visitor.Conditional(this.ratio == 0 && !this.div8))
        {
            this.xsize = visitor.U32(SizeEnc, 1, this.xsize);
        }
    }
}
