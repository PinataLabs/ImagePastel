// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/image_metadata.h and lib/jxl/image_metadata.cc (BitDepth, ExtraChannelInfo,
// OpsinInverseMatrix, ToneMapping, CustomTransformData, ImageMetadata, CodecMetadata),
// VisitNameString from lib/jxl/frame_header.h, and the default constants of
// lib/jxl/cms/opsin_params.h, lib/jxl/quantizer.h and lib/jxl/base/common.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Headers;

/// <summary>Tone-mapping information of the image.</summary>
internal sealed class JxlToneMapping : JxlFields
{
    private const float DefaultIntensityTarget = 255;

    public JxlToneMapping() => JxlBundle.Init(this);

    /// <summary>Gets or sets the upper bound on the intensity level of the image, in nits.</summary>
    public float IntensityTarget { get; set; }

    /// <summary>Gets or sets the lower bound on the intensity level of the image, in nits.</summary>
    public float MinNits { get; set; }

    /// <summary>Gets or sets a value indicating whether <see cref="LinearBelow"/> is a fraction of the display maximum.</summary>
    public bool RelativeToMaxDisplay { get; set; }

    /// <summary>Gets or sets the level below which tone mapping keeps the image linear.</summary>
    public float LinearBelow { get; set; }

    public override void VisitFields(JxlVisitor visitor)
    {
        if (visitor.AllDefault(this))
        {
            visitor.SetDefault(this);
            return;
        }

        this.IntensityTarget = visitor.F16(DefaultIntensityTarget, this.IntensityTarget);
        if (this.IntensityTarget <= 0.0f)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("invalid intensity target");
        }

        this.MinNits = visitor.F16(0.0f, this.MinNits);
        if (this.MinNits < 0.0f || this.MinNits > this.IntensityTarget)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"invalid min {this.MinNits} vs max {this.IntensityTarget}");
        }

        this.RelativeToMaxDisplay = visitor.Bool(false, this.RelativeToMaxDisplay);

        this.LinearBelow = visitor.F16(0.0f, this.LinearBelow);
        if (this.LinearBelow < 0 || (this.RelativeToMaxDisplay && this.LinearBelow > 1.0f))
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"invalid linear_below {this.LinearBelow}");
        }
    }
}
