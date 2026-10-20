// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// FromLinearStage with OpRgb, OpHlg, Op709 and OpGamma, and the choice of GetFromLinearStage, in
// lib/jxl/render_pipeline/stage_from_linear.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// Encodes the linear colour planes with the output transfer function: sRGB, HLG, BT.709, or a gamma,
/// which DCI also is. The linear one changes nothing, so it needs no stage.
/// </summary>
internal sealed class JxlFromLinearStage : IJxlColorStage
{
    private readonly Op op;

    // OpGamma's exponent, libjxl's inverse_gamma.
    private readonly float inverseGamma;

    // OpHlg's OOTF to scene light; null for the other operations.
    private readonly JxlHlgOotf hlgOotf;

    private JxlFromLinearStage(Op op, float inverseGamma, JxlHlgOotf hlgOotf = null)
    {
        this.op = op;
        this.inverseGamma = inverseGamma;
        this.hlgOotf = hlgOotf;
    }

    // libjxl's operations that this port has; OpLinear needs no stage.
    private enum Op
    {
        Rgb,
        Hlg,
        Bt709,
        Gamma,
    }

    /// <summary>
    /// Returns the stage for the output transfer function, as libjxl's <c>GetFromLinearStage</c>, or
    /// <see langword="null"/> for a linear output (libjxl's <c>OpLinear</c>, which does nothing). Throws
    /// <see cref="NotSupportedException"/> for PQ, whose operation is not ported yet.
    /// </summary>
    /// <param name="output">The output encoding.</param>
    /// <returns>The stage, or <see langword="null"/>.</returns>
    public static JxlFromLinearStage Create(JxlOutputEncoding output)
    {
        if (output.IsLinear)
        {
            return null;
        }

        // libjxl's order: linear, sRGB, PQ, HLG, BT.709, then a gamma or DCI. Each of libjxl's IsX()
        // means no gamma and the transfer function X.
        JxlCustomTransferFunction tf = output.Tf;
        JxlTransferFunction named = tf.HaveGamma ? JxlTransferFunction.Unknown : tf.TransferFunction;
        if (named == JxlTransferFunction.Srgb)
        {
            return new JxlFromLinearStage(Op.Rgb, output.InverseGamma);
        }

        if (named == JxlTransferFunction.Pq)
        {
            JxlThrowHelper.ThrowNotSupportedException($"XYB output with the {named} transfer function");
        }

        if (named == JxlTransferFunction.Hlg)
        {
            JxlHlgOotf ootf = JxlHlgOotf.ToSceneLight(output.DesiredIntensityTarget, output.Luminances);
            return new JxlFromLinearStage(Op.Hlg, output.InverseGamma, ootf);
        }

        if (named == JxlTransferFunction.Bt709)
        {
            return new JxlFromLinearStage(Op.Bt709, output.InverseGamma);
        }

        if (tf.HaveGamma || named == JxlTransferFunction.Dci)
        {
            return new JxlFromLinearStage(Op.Gamma, output.InverseGamma);
        }

        // libjxl: JXL_DEBUG_ABORT("Invalid target encoding"), a programming error. An unknown transfer
        // function cannot get here: CanOutputToColorEncoding sends it to the fallback.
        throw new InvalidOperationException("Invalid target encoding");
    }

    public void ProcessRow(Buffer2D<float>[] planes, int xsize, int y)
    {
        if (this.op == Op.Hlg)
        {
            this.ProcessHlgRow(planes, xsize, y);
            return;
        }

        for (int c = 0; c < 3; c++)
        {
            Span<float> row = planes[c].DangerousGetRowSpan(y).Slice(0, xsize);
            switch (this.op)
            {
                case Op.Rgb:
                    for (int x = 0; x < row.Length; x++)
                    {
                        // OpRgb: libjxl builds with JXL_HIGH_PRECISION set by default, so it takes
                        // TF_SRGB().EncodedFromDisplay rather than FastLinearToSRGB.
                        row[x] = JxlTransferFunctions.SrgbEncodedFromDisplay(row[x]);
                    }

                    break;

                case Op.Bt709:
                    for (int x = 0; x < row.Length; x++)
                    {
                        row[x] = JxlTransferFunctions.Bt709EncodedFromDisplay(row[x]);
                    }

                    break;

                default:
                    for (int x = 0; x < row.Length; x++)
                    {
                        row[x] = JxlTransferFunctions.GammaEncodedFromDisplay(row[x], this.inverseGamma);
                    }

                    break;
            }
        }
    }

    // OpHlg: the OOTF scales the three samples of a pixel together, so it needs the three rows at once,
    // before the curve encodes each sample.
    private void ProcessHlgRow(Buffer2D<float>[] planes, int xsize, int y)
    {
        Span<float> r = planes[0].DangerousGetRowSpan(y).Slice(0, xsize);
        Span<float> g = planes[1].DangerousGetRowSpan(y).Slice(0, xsize);
        Span<float> b = planes[2].DangerousGetRowSpan(y).Slice(0, xsize);
        JxlHlgOotf ootf = this.hlgOotf;
        for (int x = 0; x < r.Length; x++)
        {
            ootf.Apply(ref r[x], ref g[x], ref b[x]);
            r[x] = JxlTransferFunctions.HlgEncodedFromDisplay(r[x]);
            g[x] = JxlTransferFunctions.HlgEncodedFromDisplay(g[x]);
            b[x] = JxlTransferFunctions.HlgEncodedFromDisplay(b[x]);
        }
    }
}
