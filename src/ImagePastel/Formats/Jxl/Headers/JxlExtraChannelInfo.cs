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
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using static PinataLabs.ImagePastel.Formats.Jxl.Fields.JxlU32Distr;

namespace PinataLabs.ImagePastel.Formats.Jxl.Headers;

/// <summary>Describes one extra channel: its type, sample format, size and name.</summary>
internal sealed class JxlExtraChannelInfo : JxlFields
{
    private byte[] name = Array.Empty<byte>();

    public JxlExtraChannelInfo() => JxlBundle.Init(this);

    /// <summary>Gets or sets the channel type.</summary>
    public JxlExtraChannelType Type { get; set; } = JxlExtraChannelType.Alpha;

    /// <summary>Gets the sample format.</summary>
    public JxlBitDepth BitDepth { get; } = new();

    /// <summary>Gets or sets the downsampling: the channel is 2^DimShift times smaller on each axis.</summary>
    public uint DimShift { get; set; }

    /// <summary>Gets or sets the UTF-8 name.</summary>
    public ReadOnlySpan<byte> Name
    {
        get => this.name;
        set => this.name = value.ToArray();
    }

    /// <summary>Gets or sets a value indicating whether the alpha is premultiplied. Only for alpha channels.</summary>
    public bool AlphaAssociated { get; set; }

    /// <summary>Gets the spot colour in linear RGBA. Only for spot colour channels.</summary>
    public float[] SpotColor { get; } = new float[4];

    /// <summary>Gets or sets the CFA channel index. Only for CFA channels.</summary>
    public uint CfaChannel { get; set; }

    public override void VisitFields(JxlVisitor visitor)
    {
        if (visitor.AllDefault(this))
        {
            visitor.SetDefault(this);
            return;
        }

        // General
        this.Type = visitor.Enum(JxlExtraChannelType.Alpha, this.Type);

        visitor.VisitNested(this.BitDepth);

        this.DimShift = visitor.U32(Val(0), Val(3), Val(4), BitsOffset(3, 1), 0, this.DimShift);
        if (this.DimShift > 3)
        {
            // libjxl checks (1 << dim_shift) > 8, which is the same for shifts below 32 and avoids
            // the undefined shift above.
            JxlThrowHelper.ThrowInvalidImageContentException($"dim_shift {this.DimShift} too large");
        }

        this.name = JxlFieldHelpers.VisitNameString(visitor, this.name);

        // Conditional
        if (visitor.Conditional(this.Type == JxlExtraChannelType.Alpha))
        {
            this.AlphaAssociated = visitor.Bool(false, this.AlphaAssociated);
        }

        if (visitor.Conditional(this.Type == JxlExtraChannelType.SpotColor))
        {
            for (int i = 0; i < this.SpotColor.Length; i++)
            {
                this.SpotColor[i] = visitor.F16(0, this.SpotColor[i]);
            }
        }

        if (visitor.Conditional(this.Type == JxlExtraChannelType.Cfa))
        {
            this.CfaChannel = visitor.U32(Val(1), Bits(2), BitsOffset(4, 3), BitsOffset(8, 19), 1, this.CfaChannel);
        }

        if (this.Type == JxlExtraChannelType.Unknown)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Unknown extra channel (bits {this.BitDepth.BitsPerSample}, shift {this.DimShift})");
        }
    }
}
