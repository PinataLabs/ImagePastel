// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/quant_weights.h and lib/jxl/quant_weights.cc (kInvDCQuant, kDCQuant, DequantMatrices: DCQuant,
// InvDCQuant, DecodeDC), restricted to the DC quantization factors.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;

namespace PinataLabs.ImagePastel.Formats.Jxl.Frame;

/// <summary>
/// The dequantization matrices of a frame. A new frame starts from the defaults, as libjxl's
/// <c>InitFrame</c> resets them. This part holds the DC quantization factors, which the Modular XYB
/// conversion and the VarDCT DC both use; the AC tables are in <c>JxlDequantMatrices.AcTables.cs</c>.
/// </summary>
internal sealed partial class JxlDequantMatrices
{
    private const float AlmostZero = 1e-8f;

    // Let's try to keep these 2**N for possible future simplicity.
    private static readonly float[] DefaultInvDcQuant = { 4096.0f, 512.0f, 256.0f };

    private readonly float[] dcQuant = { 1.0f / DefaultInvDcQuant[0], 1.0f / DefaultInvDcQuant[1], 1.0f / DefaultInvDcQuant[2] };
    private readonly float[] invDcQuant = { DefaultInvDcQuant[0], DefaultInvDcQuant[1], DefaultInvDcQuant[2] };

    /// <summary>Returns the DC quantization factor of channel <paramref name="c"/>.</summary>
    /// <param name="c">The channel, 0 to 2.</param>
    /// <returns>The factor.</returns>
    public float DcQuant(int c) => this.dcQuant[c];

    /// <summary>Returns the inverse of the DC quantization factor of channel <paramref name="c"/>.</summary>
    /// <param name="c">The channel, 0 to 2.</param>
    /// <returns>The inverse factor.</returns>
    public float InvDcQuant(int c) => this.invDcQuant[c];

    /// <summary>Reads the DC quantization factors, which every DC global section carries.</summary>
    /// <param name="br">The reader.</param>
    public void DecodeDc(JxlBitReader br)
    {
        bool allDefault = br.ReadBits(1) != 0;
        br.EnsureAllReadsWithinBounds("DecodeDC");
        if (!allDefault)
        {
            for (int c = 0; c < 3; c++)
            {
                float value = JxlFieldCoders.ReadF16OrThrow(br);
                value *= 1.0f / 128.0f;

                // Negative values and nearly zero are invalid values.
                if (value < AlmostZero)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid dc_quant: coefficient is too small.");
                }

                this.dcQuant[c] = value;
                this.invDcQuant[c] = 1.0f / value;
            }
        }
    }
}
