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

/// <summary>The duration and timecode of an animation frame.</summary>
internal sealed class JxlAnimationFrame : JxlFields
{
    public JxlAnimationFrame(JxlCodecMetadata metadata)
    {
        this.NonserializedMetadata = metadata;
        JxlBundle.Init(this);
    }

    /// <summary>Gets or sets the duration, in ticks.</summary>
    public uint Duration { get; set; }

    /// <summary>Gets or sets the timecode, 0xHHMMSSFF.</summary>
    public uint Timecode { get; set; }

    /// <summary>Gets or sets the codestream metadata, which is not serialized here.</summary>
    public JxlCodecMetadata NonserializedMetadata { get; set; }

    public override void VisitFields(JxlVisitor visitor)
    {
        if (visitor.Conditional(this.NonserializedMetadata != null && this.NonserializedMetadata.M.HaveAnimation))
        {
            this.Duration = visitor.U32(Val(0), Val(1), Bits(8), Bits(32), 0, this.Duration);
        }

        if (visitor.Conditional(this.NonserializedMetadata != null && this.NonserializedMetadata.M.Animation.HaveTimecodes))
        {
            this.Timecode = visitor.Bits(32, 0, this.Timecode);
        }
    }
}
