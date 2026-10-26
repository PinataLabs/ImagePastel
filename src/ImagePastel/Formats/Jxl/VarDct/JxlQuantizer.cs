// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/quantizer.h and lib/jxl/quantizer.cc (Quantizer, with the encoder's ComputeGlobalScaleAndQuant,
// SetQuantFieldRect, ClampVal, GetParams and the DC steps; QuantizerParams), lib/jxl/enc_frame.cc
// (WriteQuantizerParams) and lib/jxl/quantizer-inl.h (AdjustQuantBias).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Memory;
using static PinataLabs.ImagePastel.Formats.Jxl.Fields.JxlU32Distr;

namespace PinataLabs.ImagePastel.Formats.Jxl.VarDct;

/// <summary>
/// The quantiser of a VarDCT frame: the global scale and the DC quantisation, from which the DC and AC
/// steps follow.
/// </summary>
internal sealed class JxlQuantizer
{
    /// <summary>The largest quant field value.</summary>
    public const int QuantMax = 256;

    private const int GlobalScaleDenom = 1 << 16;
    private const int GlobalScaleNumerator = 4096;
    private const int DefaultQuant = 64;

    /// <summary>
    /// libjxl's <c>kDefaultQuantBias</c>, the biases the encoder dequantises with; the decoder takes the
    /// ones the opsin inverse matrix codes, whose defaults these are.
    /// </summary>
    internal static readonly float[] DefaultQuantBias =
    {
        1.0f - 0.05465007330715401f,
        1.0f - 0.07005449891748593f,
        1.0f - 0.049935103337343655f,
        0.145f,
    };

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

    /// <summary>Gets the global scale as a fraction, libjxl's <c>Scale()</c>, which EPF's sigma uses.</summary>
    public float Scale => this.globalScaleFloat;

    /// <summary>Gets the DC step of each channel.</summary>
    public float[] MulDc => this.mulDc;

    /// <summary>
    /// Returns a quant field value clamped to [1, <see cref="QuantMax"/>] and truncated, as libjxl's
    /// <c>ClampVal</c>.
    /// </summary>
    /// <param name="val">The value.</param>
    /// <returns>The raw quant field value.</returns>
    public static int ClampVal(float val) => (int)MathF.Max(1.0f, MathF.Min(val, QuantMax));

    /// <summary>
    /// Sets the global scale and the DC quantisation from the DC quant and the median of the quant field
    /// and its median absolute deviation, as libjxl's <c>ComputeGlobalScaleAndQuant</c>.
    /// </summary>
    /// <param name="quantDcValue">The DC quant.</param>
    /// <param name="quantMedian">The median of the quant field.</param>
    /// <param name="quantMedianAbsd">The median absolute deviation of the quant field.</param>
    public void ComputeGlobalScaleAndQuant(float quantDcValue, float quantMedian, float quantMedianAbsd)
    {
        // Target value for the median value in the quant field.
        const float quantFieldTarget = 5;

        // We reduce the median of the quant field by the median absolute deviation: higher resolution on
        // highly varying quant fields.
        float scale = GlobalScaleDenom * (quantMedian - quantMedianAbsd) / quantFieldTarget;

        // Ensure that new_global_scale is positive and no more than 1<<15.
        if (scale < 1)
        {
            scale = 1;
        }

        if (scale > (1 << 15))
        {
            scale = 1 << 15;
        }

        int newGlobalScale = (int)scale;

        // Ensure that quant_dc_ will always be at least 0.625 * kGlobalScaleDenom/kGlobalScaleNumerator = 10.
        int scaledQuantDc = (int)(quantDcValue * GlobalScaleNumerator * 1.6);
        if (newGlobalScale > scaledQuantDc)
        {
            newGlobalScale = scaledQuantDc;
            if (newGlobalScale <= 0)
            {
                newGlobalScale = 1;
            }
        }

        this.globalScale = newGlobalScale;

        // Code below uses inv_global_scale_.
        this.RecomputeFromGlobalScale();

        float fval = (quantDcValue * this.invGlobalScale) + 0.5f;
        fval = MathF.Min(1 << 16, fval);
        this.quantDc = (int)fval;

        // quant_dc_ was updated, recompute values.
        this.RecomputeFromGlobalScale();
    }

    /// <summary>
    /// Sets the raw quant field from a float quant field, as libjxl's <c>SetQuantFieldRect</c> does for
    /// one value.
    /// </summary>
    /// <param name="qf">The float quant field value.</param>
    /// <returns>The raw quant field value.</returns>
    public int RawQuantField(float qf) => ClampVal((qf * this.invGlobalScale) + 0.5f);

    /// <summary>
    /// Sets the raw quant field of each block in <paramref name="rect"/> from the float quant field, as
    /// libjxl's <c>SetQuantFieldRect</c>.
    /// </summary>
    /// <param name="qf">The float quant field, one value per block.</param>
    /// <param name="rect">The blocks to set.</param>
    /// <param name="rawQuantField">The raw quant field, row by row.</param>
    /// <param name="stride">The number of blocks in a row of <paramref name="rawQuantField"/>.</param>
    public void SetQuantFieldRect(Buffer2D<float> qf, JxlRect rect, int[] rawQuantField, int stride)
    {
        for (int y = 0; y < rect.YSize; y++)
        {
            int by = (int)rect.Y0 + y;
            ReadOnlySpan<float> rowQf = qf.DangerousGetRowSpan(by).Slice((int)rect.X0, (int)rect.XSize);
            Span<int> rowQi = rawQuantField.AsSpan((by * stride) + (int)rect.X0, (int)rect.XSize);
            for (int x = 0; x < rowQf.Length; x++)
            {
                rowQi[x] = this.RawQuantField(rowQf[x]);
            }
        }
    }

    /// <summary>Returns the dequantization weights of channel <paramref name="c"/> for a strategy.</summary>
    /// <param name="quantKind">The strategy.</param>
    /// <param name="c">The channel.</param>
    /// <returns>The weights.</returns>
    public ReadOnlySpan<float> DequantMatrix(JxlAcStrategyType quantKind, int c) => this.dequant.Matrix(quantKind, c);

    /// <summary>Returns the quantization weights of channel <paramref name="c"/> for a strategy, the inverse of <see cref="DequantMatrix"/>.</summary>
    /// <param name="quantKind">The strategy.</param>
    /// <param name="c">The channel.</param>
    /// <returns>The weights.</returns>
    public ReadOnlySpan<float> InvDequantMatrix(JxlAcStrategyType quantKind, int c) => this.dequant.InvMatrix(quantKind, c);

    /// <summary>Returns the factor that dequantises an AC coefficient of a block with quant field <paramref name="quant"/>.</summary>
    /// <param name="quant">The raw quant field value.</param>
    /// <returns>The factor.</returns>
    public float InvQuantAc(int quant) => this.invGlobalScale / quant;

    /// <summary>Returns the DC quantisation step of channel <paramref name="c"/>.</summary>
    /// <param name="c">The channel.</param>
    /// <returns>The step.</returns>
    public float GetDcStep(int c) => this.invQuantDc * this.dequant.DcQuant(c);

    /// <summary>Returns the inverse of the DC quantisation step of channel <paramref name="c"/>.</summary>
    /// <param name="c">The channel.</param>
    /// <returns>The inverse step.</returns>
    public float GetInvDcStep(int c) => this.dequant.InvDcQuant(c) * (this.globalScaleFloat * this.quantDc);

    /// <summary>Writes the global scale and the DC quantisation, as libjxl's <c>WriteQuantizerParams</c>.</summary>
    /// <param name="writer">The writer.</param>
    public void Encode(JxlBitWriter writer)
    {
        QuantizerParams parameters = new()
        {
            GlobalScale = (uint)this.globalScale,
            QuantDc = (uint)this.quantDc,
        };
        JxlBundle.Write(parameters, writer);
    }

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

        public uint GlobalScale { get; set; }

        public uint QuantDc { get; set; }

        public override void VisitFields(JxlVisitor visitor)
        {
            this.GlobalScale = visitor.U32(BitsOffset(11, 1), BitsOffset(11, 2049), BitsOffset(12, 4097), BitsOffset(16, 8193), 1, this.GlobalScale);
            this.QuantDc = visitor.U32(Val(16), BitsOffset(5, 1), BitsOffset(8, 1), BitsOffset(16, 1), 1, this.QuantDc);
        }
    }
}
