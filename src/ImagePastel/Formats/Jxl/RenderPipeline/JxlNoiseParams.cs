// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// NoiseParams and kNoisePrecision in lib/jxl/noise.h, and DecodeNoise and DecodeFloatParam in
// lib/jxl/dec_noise.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// The noise parameters of a frame with the noise flag: a look-up table of the noise strength by
/// intensity, which the frame's DC global section holds after its patches and splines.
/// </summary>
internal sealed class JxlNoiseParams
{
    /// <summary>The number of entries of the look-up table: libjxl's <c>kNumNoisePoints</c>.</summary>
    public const int NumNoisePoints = 8;

    // 10 bits.
    private const float NoisePrecision = 1024.0f;

    private readonly float[] lut = new float[NumNoisePoints];

    /// <summary>
    /// Gets the look-up table. The index is an intensity of pixel / mean intensity of patch.
    /// </summary>
    public ReadOnlySpan<float> Lut => this.lut;

    /// <summary>Gets a value indicating whether any entry is large enough to add noise.</summary>
    public bool HasAny
    {
        get
        {
            foreach (float i in this.lut)
            {
                if (MathF.Abs(i) > 1e-3f)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Reads the look-up table: each entry is 10 bits, in units of 1/1024.</summary>
    /// <param name="br">The reader of the DC global section.</param>
    public void Decode(JxlBitReader br)
    {
        for (int i = 0; i < NumNoisePoints; i++)
        {
            int absvalQuant = (int)br.ReadBits(10);
            this.lut[i] = absvalQuant / NoisePrecision;
        }
    }
}
