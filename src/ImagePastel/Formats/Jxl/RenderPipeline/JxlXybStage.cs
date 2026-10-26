// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// XYBStage in lib/jxl/render_pipeline/stage_xyb.cc and XybToRgb in lib/jxl/dec_xyb-inl.h, scalar.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// Turns the XYB colour planes into linear samples of the output colour encoding. Avoids clamping to
/// [0, 1]: out of (sRGB) gamut values may be in-gamut after transforming to a wider space.
/// </summary>
/// <remarks>
/// Deliberate departures from libjxl: there is no FMA (ADR 0004), so each <c>MulAdd</c> rounds twice.
/// The branch that outputs scaled XYB, for a caller that asks for XYB, is not ported: this port always
/// outputs the image's encoding or linear sRGB. Nor is <c>FastXYBTosRGB8</c>, which libjxl runs only on NEON.
/// </remarks>
internal sealed class JxlXybStage : IJxlColorStage
{
    private readonly float[] m;
    private readonly float[] biases;
    private readonly float[] biasesCbrt;

    /// <summary>Initializes a new instance of the <see cref="JxlXybStage"/> class.</summary>
    /// <param name="output">The output encoding, which holds the opsin parameters.</param>
    public JxlXybStage(JxlOutputEncoding output)
    {
        this.m = output.InverseOpsinMatrix;
        this.biases = output.OpsinBiases;
        this.biasesCbrt = output.OpsinBiasesCbrt;
    }

    public void ProcessRow(Buffer2D<float>[] planes, int xsize, int y)
    {
        Span<float> row0 = planes[0].DangerousGetRowSpan(y).Slice(0, xsize);
        Span<float> row1 = planes[1].DangerousGetRowSpan(y).Slice(0, xsize);
        Span<float> row2 = planes[2].DangerousGetRowSpan(y).Slice(0, xsize);
        float[] m = this.m;
        float negBiasR = this.biases[0];
        float negBiasG = this.biases[1];
        float negBiasB = this.biases[2];
        float biasCbrtR = this.biasesCbrt[0];
        float biasCbrtG = this.biasesCbrt[1];
        float biasCbrtB = this.biasesCbrt[2];
        for (int x = 0; x < xsize; x++)
        {
            float opsinX = row0[x];
            float opsinY = row1[x];
            float opsinB = row2[x];

            // Color space: XYB -> RGB
            float gammaR = (opsinY + opsinX) - biasCbrtR;
            float gammaG = (opsinY - opsinX) - biasCbrtG;
            float gammaB = opsinB - biasCbrtB;

            // Undo gamma compression: linear = gamma^3 for efficiency.
            float gammaR2 = gammaR * gammaR;
            float gammaG2 = gammaG * gammaG;
            float gammaB2 = gammaB * gammaB;
            float mixedR = (gammaR2 * gammaR) + negBiasR;
            float mixedG = (gammaG2 * gammaG) + negBiasG;
            float mixedB = (gammaB2 * gammaB) + negBiasB;

            // Unmix (multiply by 3x3 inverse_matrix)
            float linearR = m[0] * mixedR;
            float linearG = m[3] * mixedR;
            float linearB = m[6] * mixedR;
            linearR = (m[1] * mixedG) + linearR;
            linearG = (m[4] * mixedG) + linearG;
            linearB = (m[7] * mixedG) + linearB;
            linearR = (m[2] * mixedB) + linearR;
            linearG = (m[5] * mixedB) + linearG;
            linearB = (m[8] * mixedB) + linearB;

            row0[x] = linearR;
            row1[x] = linearG;
            row2[x] = linearB;
        }
    }
}
