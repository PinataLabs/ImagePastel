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

/// <summary>The inverse opsin matrix and biases used by XYB images.</summary>
internal sealed class JxlOpsinInverseMatrix : JxlFields
{
    private static readonly float[] DefaultInverseMatrix =
    {
        11.031566901960783f, -9.866943921568629f, -0.16462299647058826f,
        -3.254147380392157f, 4.418770392156863f, -0.16462299647058826f,
        -3.6588512862745097f, 2.7129230470588235f, 1.9459282392156863f,
    };

    private const float OpsinAbsorbanceBias = 0.0037930732552754493f;

    private static readonly float[] DefaultQuantBias =
    {
        1.0f - 0.05465007330715401f,
        1.0f - 0.07005449891748593f,
        1.0f - 0.049935103337343655f,
        0.145f,
    };

    public JxlOpsinInverseMatrix() => JxlBundle.Init(this);

    /// <summary>Gets the 3x3 inverse matrix, row-major.</summary>
    public float[] InverseMatrix { get; } = new float[9];

    /// <summary>Gets the opsin biases.</summary>
    public float[] OpsinBiases { get; } = new float[3];

    /// <summary>Gets the quantization biases.</summary>
    public float[] QuantBiases { get; } = new float[4];

    public override void VisitFields(JxlVisitor visitor)
    {
        if (visitor.AllDefault(this))
        {
            visitor.SetDefault(this);
            return;
        }

        for (int i = 0; i < 9; ++i)
        {
            this.InverseMatrix[i] = visitor.F16(DefaultInverseMatrix[i], this.InverseMatrix[i]);
        }

        for (int i = 0; i < 3; ++i)
        {
            this.OpsinBiases[i] = visitor.F16(-OpsinAbsorbanceBias, this.OpsinBiases[i]);
        }

        for (int i = 0; i < 4; ++i)
        {
            this.QuantBiases[i] = visitor.F16(DefaultQuantBias[i], this.QuantBiases[i]);
        }
    }
}
