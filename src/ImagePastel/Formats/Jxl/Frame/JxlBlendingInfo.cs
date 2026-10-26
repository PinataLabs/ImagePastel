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

/// <summary>How a frame, or one of its extra channels, blends onto a reference frame.</summary>
internal sealed class JxlBlendingInfo : JxlFields
{
    public JxlBlendingInfo() => JxlBundle.Init(this);

    public JxlBlendMode Mode { get; set; }

    /// <summary>Gets or sets the extra channel used as alpha by blend modes that need one.</summary>
    public uint AlphaChannel { get; set; }

    public bool Clamp { get; set; }

    /// <summary>Gets or sets the reference frame to blend onto.</summary>
    public uint Source { get; set; }

    /// <summary>Gets or sets the number of extra channels, which is not serialized here.</summary>
    public int NonserializedNumExtraChannels { get; set; }

    /// <summary>Gets or sets a value indicating whether the frame does not cover the whole image, which is not serialized here.</summary>
    public bool NonserializedIsPartialFrame { get; set; }

    public override void VisitFields(JxlVisitor visitor)
    {
        uint encoded = visitor.U32(Val((uint)JxlBlendMode.Replace), Val((uint)JxlBlendMode.Add), Val((uint)JxlBlendMode.Blend), BitsOffset(2, 3), (uint)JxlBlendMode.Replace, (uint)this.Mode);
        if (encoded > (uint)JxlBlendMode.Mul)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid blend_mode");
        }

        this.Mode = (JxlBlendMode)encoded;
        bool usesAlpha = this.NonserializedNumExtraChannels > 0 && this.Mode is JxlBlendMode.Blend or JxlBlendMode.AlphaWeightedAdd;
        if (visitor.Conditional(usesAlpha))
        {
            this.AlphaChannel = visitor.U32(Val(0), Val(1), Val(2), BitsOffset(3, 3), 0, this.AlphaChannel);

            // libjxl checks this only when reading. Checking it whenever the field is used also stops
            // the writer writing a channel the reader rejects. The default visitors visit the field
            // even when it is unused, so they skip the check.
            if (usesAlpha && this.AlphaChannel >= this.NonserializedNumExtraChannels)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid alpha channel for blending");
            }
        }

        if (visitor.Conditional(usesAlpha || this.Mode == JxlBlendMode.Mul))
        {
            this.Clamp = visitor.Bool(false, this.Clamp);
        }

        // 'source' answers the question "what image is used as the other input?"
        if (visitor.Conditional(this.Mode != JxlBlendMode.Replace || this.NonserializedIsPartialFrame))
        {
            this.Source = visitor.U32(Val(0), Val(1), Val(2), Val(3), 0, this.Source);
        }
    }
}
