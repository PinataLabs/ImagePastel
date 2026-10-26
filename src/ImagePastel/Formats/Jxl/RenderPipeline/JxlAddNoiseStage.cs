// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// AddNoiseStage, AddNoiseToRGB, NoiseStrength, Clamp0ToMax and StrengthEvalLut (its HWY_SCALAR path)
// in lib/jxl/render_pipeline/stage_noise.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// The add-noise stage: adds the convolved noise planes to the XYB colour planes, in place, with a
/// strength that the noise look-up table gives for the intensity of each pixel. The red and green noise
/// share most of a third, correlated plane, and the colour-correlation map's base ratios carry the noise
/// into X and B.
/// </summary>
/// <remarks>
/// libjxl evaluates the look-up table and adds the noise with <c>MulAdd</c>, which is a fused
/// multiply-add on AVX2. This port rounds each product and each sum (ADR 0004), so its output can differ
/// from libjxl's in the last bits.
/// </remarks>
internal sealed class JxlAddNoiseStage
{
    // 127/128
    private const float RgCorr = 0.9921875f;

    // 1/128
    private const float RgnCorr = 0.0078125f;

    // With the prior subtract-random Laplacian approximation, rnd_* ranges were
    // about [-1.5, 1.6]; Laplacian3 about doubles this to [-3.6, 3.6], so the
    // normalizer is half of what it was before (0.5).
    private const float NormConst = 0.22f;

    private readonly JxlNoiseParams noiseParams;
    private readonly float ytox;
    private readonly float ytob;
    private readonly int firstC;

    /// <summary>Initializes a new instance of the <see cref="JxlAddNoiseStage"/> class.</summary>
    /// <param name="noiseParams">The noise parameters of the frame.</param>
    /// <param name="ytox">The colour-correlation map's base <c>YtoXRatio(0)</c>.</param>
    /// <param name="ytob">The colour-correlation map's base <c>YtoBRatio(0)</c>.</param>
    /// <param name="firstC">The first noise channel: three colour channels, then the extra channels, then the noise.</param>
    public JxlAddNoiseStage(JxlNoiseParams noiseParams, float ytox, float ytob, int firstC)
    {
        this.noiseParams = noiseParams;
        this.ytox = ytox;
        this.ytob = ytob;
        this.firstC = firstC;
    }

    /// <summary>Adds the noise to row <paramref name="y"/> of the three colour planes.</summary>
    /// <param name="planes">
    /// The planes, numbered as libjxl's channels: three colour planes, the extra channels, then the three
    /// convolved noise planes.
    /// </param>
    /// <param name="xsize">The width of the frame.</param>
    /// <param name="y">The row.</param>
    public void ProcessRow(Buffer2D<float>[] planes, int xsize, int y)
    {
        if (!this.noiseParams.HasAny)
        {
            return;
        }

        ReadOnlySpan<float> lut = this.noiseParams.Lut;
        Span<float> rowX = planes[0].DangerousGetRowSpan(y).Slice(0, xsize);
        Span<float> rowY = planes[1].DangerousGetRowSpan(y).Slice(0, xsize);
        Span<float> rowB = planes[2].DangerousGetRowSpan(y).Slice(0, xsize);
        ReadOnlySpan<float> rowRndR = planes[this.firstC].DangerousGetRowSpan(y).Slice(0, xsize);
        ReadOnlySpan<float> rowRndG = planes[this.firstC + 1].DangerousGetRowSpan(y).Slice(0, xsize);
        ReadOnlySpan<float> rowRndC = planes[this.firstC + 2].DangerousGetRowSpan(y).Slice(0, xsize);
        for (int x = 0; x < xsize; x++)
        {
            float vx = rowX[x];
            float vy = rowY[x];
            float inG = vy - vx;
            float inR = vy + vx;
            float noiseStrengthG = NoiseStrength(lut, inG * 0.5f);
            float noiseStrengthR = NoiseStrength(lut, inR * 0.5f);
            float additRndNoiseRed = rowRndR[x] * NormConst;
            float additRndNoiseGreen = rowRndG[x] * NormConst;
            float additRndNoiseCorrelated = rowRndC[x] * NormConst;

            // AddNoiseToRGB.
            // libjxl: Mul(noise_strength_r, MulAdd(kRGNCorr, rnd_noise_r, Mul(kRGCorr, rnd_noise_cor))).
            float redNoise = noiseStrengthR * ((RgnCorr * additRndNoiseRed) + (RgCorr * additRndNoiseCorrelated));
            float greenNoise = noiseStrengthG * ((RgnCorr * additRndNoiseGreen) + (RgCorr * additRndNoiseCorrelated));
            float rgNoise = redNoise + greenNoise;

            // libjxl: Add(MulAdd(Set(d, ytox), rg_noise, Sub(red_noise, green_noise)), vx).
            rowX[x] = ((this.ytox * rgNoise) + (redNoise - greenNoise)) + vx;
            rowY[x] = vy + rgNoise;

            // libjxl: MulAdd(Set(d, ytob), rg_noise, vb).
            rowB[x] = (this.ytob * rgNoise) + rowB[x];
        }
    }

    // NoiseStrength: the look-up table's value, clamped to [0, 1]. x is in [0+delta, 1+delta], delta ~= 0.06.
    private static float NoiseStrength(ReadOnlySpan<float> lut, float x)
    {
        float value = StrengthEvalLut(lut, x);

        // Clamp0ToMax: Min(x, max_value), then ZeroIfNegative.
        value = MathF.Min(value, 1.0f);
        return value < 0 ? 0 : value;
    }

    // StrengthEvalLut: interpolates the look-up table at x.
    private static float StrengthEvalLut(ReadOnlySpan<float> lut, float vx)
    {
        const int scale = JxlNoiseParams.NumNoisePoints - 2;

        // A departure from libjxl: a NaN sample, which only a broken Modular float image can give, takes
        // the first entry. libjxl converts the NaN floor to an integer and looks it up, which depends on
        // the SIMD target and, on the scalar target, reads outside the table.
        float scaledX = vx * scale;
        if (!(scaledX > 0))
        {
            scaledX = 0;
        }

        float floorX = MathF.Floor(scaledX);
        float fracX = scaledX - floorX;
        if (scaledX >= scale + 1)
        {
            floorX = scale;
            fracX = 1;
        }

        int floorXInt = (int)floorX;
        float low = lut[floorXInt];
        float hi = lut[floorXInt + 1];

        // libjxl: MulAdd(Sub(hi, low), frac_x, low).
        return ((hi - low) * fracX) + low;
    }
}
