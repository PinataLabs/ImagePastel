// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// FromLinearStage with OpRgb, and the choice of GetFromLinearStage, in
// lib/jxl/render_pipeline/stage_from_linear.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// Encodes the linear colour planes with the output transfer function. Only the sRGB curve is ported;
/// the linear one changes nothing, so it needs no stage.
/// </summary>
internal sealed class JxlFromLinearStage : IJxlColorStage
{
    private JxlFromLinearStage()
    {
    }

    /// <summary>
    /// Returns the stage for the output transfer function, as libjxl's <c>GetFromLinearStage</c>, or
    /// <see langword="null"/> for a linear output (libjxl's <c>OpLinear</c>, which does nothing). Throws
    /// <see cref="NotSupportedException"/> for the transfer functions of a later slice.
    /// </summary>
    /// <param name="output">The output encoding.</param>
    /// <returns>The stage, or <see langword="null"/>.</returns>
    public static JxlFromLinearStage Create(JxlOutputEncoding output)
    {
        if (output.IsLinear)
        {
            return null;
        }

        JxlCustomTransferFunction tf = output.Tf;
        if (!tf.HaveGamma && tf.TransferFunction == JxlTransferFunction.Srgb)
        {
            return new JxlFromLinearStage();
        }

        // OpPq, OpHlg, Op709 and OpGamma (gamma and DCI) come with the transfer functions slice (D11).
        string name = tf.HaveGamma ? "gamma" : tf.TransferFunction.ToString();
        JxlThrowHelper.ThrowNotSupportedException($"XYB output with the {name} transfer function");
        return null;
    }

    public void ProcessRow(Buffer2D<float>[] planes, int xsize, int y)
    {
        for (int c = 0; c < 3; c++)
        {
            Span<float> row = planes[c].DangerousGetRowSpan(y).Slice(0, xsize);
            for (int x = 0; x < row.Length; x++)
            {
                // OpRgb: libjxl builds with JXL_HIGH_PRECISION set by default, so it takes
                // TF_SRGB().EncodedFromDisplay rather than FastLinearToSRGB.
                row[x] = JxlTransferFunctions.SrgbEncodedFromDisplay(row[x]);
            }
        }
    }
}
