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

/// <summary>The sample format of a channel: unsigned integers or floating point.</summary>
internal sealed class JxlBitDepth : JxlFields
{
    public JxlBitDepth() => JxlBundle.Init(this);

    /// <summary>Gets or sets a value indicating whether samples are floating point.</summary>
    public bool FloatingPointSample { get; set; }

    /// <summary>Gets or sets the number of bits per sample, including sign and exponent for floats.</summary>
    public uint BitsPerSample { get; set; }

    /// <summary>Gets or sets the number of exponent bits of floating-point samples; 0 for integers.</summary>
    public uint ExponentBitsPerSample { get; set; }

    public override void VisitFields(JxlVisitor visitor)
    {
        this.FloatingPointSample = visitor.Bool(false, this.FloatingPointSample);

        // The same fields (bits_per_sample) are read in both cases, with different encodings.
        if (!this.FloatingPointSample)
        {
            this.BitsPerSample = visitor.U32(Val(8), Val(10), Val(12), BitsOffset(6, 1), 8, this.BitsPerSample);
            this.ExponentBitsPerSample = 0;
        }
        else
        {
            this.BitsPerSample = visitor.U32(Val(32), Val(16), Val(24), BitsOffset(6, 1), 32, this.BitsPerSample);

            // The encoded value is exponent_bits_per_sample - 1.
            const uint offset = 1;
            this.ExponentBitsPerSample = visitor.Bits(4, 8 - offset, unchecked(this.ExponentBitsPerSample - offset)) + offset;
        }

        // Error-checking for floating point ranges.
        if (this.FloatingPointSample)
        {
            if (this.ExponentBitsPerSample is < 2 or > 8)
            {
                JxlThrowHelper.ThrowInvalidImageContentException($"Invalid exponent_bits_per_sample: {this.ExponentBitsPerSample}");
            }

            int mantissaBits = (int)this.BitsPerSample - (int)this.ExponentBitsPerSample - 1;
            if (mantissaBits is < 2 or > 23)
            {
                JxlThrowHelper.ThrowInvalidImageContentException($"Invalid bits_per_sample: {this.BitsPerSample}");
            }
        }
        else if (this.BitsPerSample > 31)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Invalid bits_per_sample: {this.BitsPerSample}");
        }
    }
}
