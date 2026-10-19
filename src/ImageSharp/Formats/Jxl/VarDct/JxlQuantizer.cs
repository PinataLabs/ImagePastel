// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/quantizer.h and lib/jxl/quantizer.cc (Quantizer: the decoder side, QuantizerParams) and
// lib/jxl/quantizer-inl.h (AdjustQuantBias).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The quantiser of a VarDCT frame: the global scale and the DC quantisation, from which the DC and AC
/// steps follow.
/// </summary>
internal sealed class JxlQuantizer
{
    /// <summary>The largest quant field value.</summary>
    public const int QuantMax = 256;

    private const int GlobalScaleDenom = 1 << 16;
    private const int DefaultQuant = 64;

    private readonly JxlDequantMatrices dequant;
    private readonly float[] mulDc = new float[4];
    private int globalScale;
    private int quantDc;
    private float invGlobalScale;
    private float globalScaleFloat;
    private float invQuantDc;

    /// <summary>Initializes a new instance of the <see cref="JxlQuantizer"/> class with libjxl's defaults.</summary>
    /// <param name="dequant">The frame's dequantization matrices, whose DC factors the DC steps use.</param>
    public JxlQuantizer(JxlDequantMatrices dequant)
    {
        this.dequant = dequant;
        this.globalScale = GlobalScaleDenom / DefaultQuant;
        this.quantDc = DefaultQuant;
        this.RecomputeFromGlobalScale();
        this.invQuantDc = this.invGlobalScale / this.quantDc;
    }

    /// <summary>Gets the reciprocal of the global scale.</summary>
    public float InvGlobalScale => this.invGlobalScale;

    /// <summary>Gets the DC step of each channel.</summary>
    public float[] MulDc => this.mulDc;

    /// <summary>Reads the quantiser parameters from the DC global section.</summary>
    /// <param name="reader">The reader.</param>
    public void Decode(JxlBitReader reader)
    {
        QuantizerParams parameters = new();
        JxlBundle.Read(reader, parameters);
        this.globalScale = (int)parameters.GlobalScale;
        this.quantDc = (int)parameters.QuantDc;
        this.RecomputeFromGlobalScale();
    }

    /// <summary>
    /// Returns the expected value of a coefficient quantised to <paramref name="quantI"/>: 0 for 0,
    /// ±<c>biases[c]</c> for ±1, else <c>quant - biases[3] / quant</c>.
    /// </summary>
    /// <remarks>
    /// libjxl computes the last case with <c>NegMulAdd</c> and Highway's <c>ApproximateReciprocal</c>,
    /// which on x86 is the 12-bit <c>rcpps</c> instruction, "about 2E-5 worse than ReciprocalNR or
    /// division". Its result depends on the CPU, so this port takes the exact reciprocal, and multiplies
    /// and subtracts separately (ADR 0004).
    /// </remarks>
    /// <param name="c">The channel.</param>
    /// <param name="quantI">The quantised coefficient.</param>
    /// <param name="biases">The four quantisation biases.</param>
    /// <returns>The adjusted coefficient.</returns>
    public static float AdjustQuantBias(int c, int quantI, float[] biases)
    {
        float quant = quantI;
        float absQuant = System.MathF.Abs(quant);
        if (absQuant < 1.125f)
        {
            if (!(absQuant > 0))
            {
                return 0;
            }

            return quant < 0 ? -biases[c] : biases[c];
        }

        return quant - (biases[3] * (1.0f / quant));
    }

    // Recomputes other derived fields after globalScale has changed.
    private void RecomputeFromGlobalScale()
    {
        this.globalScaleFloat = (float)(this.globalScale * (1.0 / GlobalScaleDenom));
        this.invGlobalScale = (float)(1.0 * GlobalScaleDenom / this.globalScale);
        this.invQuantDc = this.invGlobalScale / this.quantDc;
        for (int c = 0; c < 3; c++)
        {
            this.mulDc[c] = this.invQuantDc * this.dequant.DcQuant(c);
        }
    }

    private sealed class QuantizerParams : JxlFields
    {
        public QuantizerParams() => JxlBundle.Init(this);

        public uint GlobalScale { get; private set; }

        public uint QuantDc { get; private set; }

        public override void VisitFields(JxlVisitor visitor)
        {
            this.GlobalScale = visitor.U32(BitsOffset(11, 1), BitsOffset(11, 2049), BitsOffset(12, 4097), BitsOffset(16, 8193), 1, this.GlobalScale);
            this.QuantDc = visitor.U32(Val(16), BitsOffset(5, 1), BitsOffset(8, 1), BitsOffset(16, 1), 1, this.QuantDc);
        }
    }
}
