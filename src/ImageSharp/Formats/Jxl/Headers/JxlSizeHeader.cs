// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/headers.h and lib/jxl/headers.cc (SizeHeader, PreviewHeader, AnimationHeader).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Headers;

/// <summary>The image size, coded compactly for small sizes and common aspect ratios.</summary>
internal sealed class JxlSizeHeader : JxlFields
{
    private static readonly JxlU32Enc SizeEnc = new(BitsOffset(9, 1), BitsOffset(13, 1), BitsOffset(18, 1), BitsOffset(30, 1));

    // Width and height <= 256 and divisible by 8.
    private bool small;
    private uint ysizeDiv8Minus1;
    private uint ysize;
    private uint ratio;
    private uint xsizeDiv8Minus1;
    private uint xsize;

    public JxlSizeHeader() => JxlBundle.Init(this);

    /// <summary>Gets the image width.</summary>
    public ulong XSize
    {
        get
        {
            if (this.ratio != 0)
            {
                return JxlAspectRatio.MulTruncate(this.ratio, this.YSize);
            }

            return this.small ? (this.xsizeDiv8Minus1 + 1) * 8UL : this.xsize;
        }
    }

    /// <summary>Gets the image height.</summary>
    public ulong YSize => this.small ? (this.ysizeDiv8Minus1 + 1) * 8UL : this.ysize;

    public override void VisitFields(JxlVisitor visitor)
    {
        this.small = visitor.Bool(false, this.small);

        if (visitor.Conditional(this.small))
        {
            this.ysizeDiv8Minus1 = visitor.Bits(5, 0, this.ysizeDiv8Minus1);
        }

        if (visitor.Conditional(!this.small))
        {
            // Could still be small, but not a multiple of 8.
            this.ysize = visitor.U32(SizeEnc, 1, this.ysize);
        }

        this.ratio = visitor.Bits(3, 0, this.ratio);
        if (visitor.Conditional(this.ratio == 0 && this.small))
        {
            this.xsizeDiv8Minus1 = visitor.Bits(5, 0, this.xsizeDiv8Minus1);
        }

        if (visitor.Conditional(this.ratio == 0 && !this.small))
        {
            this.xsize = visitor.U32(SizeEnc, 1, this.xsize);
        }
    }
}
