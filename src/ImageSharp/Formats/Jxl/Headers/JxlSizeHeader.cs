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

    /// <summary>
    /// Sets the size, choosing the most compact form. Throws <see cref="ImageFormatException"/> for an
    /// empty size or one above 32 bits. A height the header cannot hold is rejected when it is written.
    /// </summary>
    /// <param name="xsize">The width.</param>
    /// <param name="ysize">The height.</param>
    public void Set(ulong xsize, ulong ysize)
    {
        if (xsize > uint.MaxValue || ysize > uint.MaxValue)
        {
            JxlThrowHelper.ThrowImageFormatException("Image too large");
        }

        if (xsize == 0 || ysize == 0)
        {
            JxlThrowHelper.ThrowImageFormatException("Empty image");
        }

        uint xsize32 = (uint)xsize;
        uint ysize32 = (uint)ysize;
        this.ratio = JxlAspectRatio.Find(xsize32, ysize32);
        this.small = ysize <= 256 && (ysize % 8) == 0 && (this.ratio != 0 || (xsize <= 256 && (xsize % 8) == 0));
        if (this.small)
        {
            this.ysizeDiv8Minus1 = (ysize32 / 8) - 1;
        }
        else
        {
            this.ysize = ysize32;
        }

        if (this.ratio == 0)
        {
            if (this.small)
            {
                this.xsizeDiv8Minus1 = (xsize32 / 8) - 1;
            }
            else
            {
                this.xsize = xsize32;
            }
        }
    }

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
