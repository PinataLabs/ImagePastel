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

/// <summary>The progressive passes of a frame.</summary>
internal sealed class JxlPasses : JxlFields
{
    /// <summary>The largest number of passes.</summary>
    public const int MaxNumPasses = 11;

    public JxlPasses() => JxlBundle.Init(this);

    /// <summary>Gets or sets the number of passes, at most <see cref="MaxNumPasses"/>.</summary>
    public uint NumPasses { get; set; }

    /// <summary>Gets or sets the number of downsampling targets, at most <see cref="NumPasses"/>.</summary>
    public uint NumDownsample { get; set; }

    public uint[] Downsample { get; } = new uint[MaxNumPasses];

    public uint[] LastPass { get; } = new uint[MaxNumPasses];

    public uint[] Shift { get; } = new uint[MaxNumPasses];

    /// <summary>Returns the range of channel shifts a pass decodes.</summary>
    /// <param name="pass">The pass.</param>
    /// <param name="minShift">The smallest shift.</param>
    /// <param name="maxShift">The largest shift.</param>
    public void GetDownsamplingBracket(int pass, out int minShift, out int maxShift)
    {
        maxShift = 2;
        minShift = 3;
        for (int i = 0; ; i++)
        {
            for (uint j = 0; j < this.NumDownsample; ++j)
            {
                if (i == this.LastPass[j])
                {
                    if (this.Downsample[j] == 8)
                    {
                        minShift = 3;
                    }

                    if (this.Downsample[j] == 4)
                    {
                        minShift = 2;
                    }

                    if (this.Downsample[j] == 2)
                    {
                        minShift = 1;
                    }

                    if (this.Downsample[j] == 1)
                    {
                        minShift = 0;
                    }
                }
            }

            if (i == this.NumPasses - 1)
            {
                minShift = 0;
            }

            if (i == pass)
            {
                return;
            }

            maxShift = minShift - 1;
        }
    }

    public override void VisitFields(JxlVisitor visitor)
    {
        this.NumPasses = visitor.U32(Val(1), Val(2), Val(3), BitsOffset(3, 4), 1, this.NumPasses);

        if (visitor.Conditional(this.NumPasses != 1))
        {
            this.NumDownsample = visitor.U32(Val(0), Val(1), Val(2), BitsOffset(1, 3), 0, this.NumDownsample);
            if (this.NumDownsample > this.NumPasses)
            {
                JxlThrowHelper.ThrowInvalidImageContentException($"num_downsample {this.NumDownsample} > num_passes {this.NumPasses}");
            }

            for (uint i = 0; i < this.NumPasses - 1; i++)
            {
                this.Shift[i] = visitor.Bits(2, 0, this.Shift[i]);
            }

            this.Shift[this.NumPasses - 1] = 0;

            for (uint i = 0; i < this.NumDownsample; ++i)
            {
                this.Downsample[i] = visitor.U32(Val(1), Val(2), Val(4), Val(8), 1, this.Downsample[i]);
                if (i > 0 && this.Downsample[i] >= this.Downsample[i - 1])
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("downsample sequence should be decreasing");
                }
            }

            for (uint i = 0; i < this.NumDownsample; ++i)
            {
                this.LastPass[i] = visitor.U32(Val(0), Val(1), Val(2), Bits(3), 0, this.LastPass[i]);
                if (i > 0 && this.LastPass[i] <= this.LastPass[i - 1])
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("last_pass sequence should be increasing");
                }

                if (this.LastPass[i] >= this.NumPasses)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException($"last_pass {this.LastPass[i]} >= num_passes {this.NumPasses}");
                }
            }
        }
    }
}
