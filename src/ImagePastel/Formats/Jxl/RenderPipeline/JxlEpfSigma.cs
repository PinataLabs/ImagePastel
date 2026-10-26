// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: ComputeSigma, kInvSigmaNum
// and kMinSigma from lib/jxl/epf.cc and lib/jxl/epf.h, the sigma image of PassesDecoderState in
// lib/jxl/dec_cache.h, and its fill for a Modular frame in FrameDecoder::ProcessDCGroup (lib/jxl/dec_frame.cc).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.VarDct;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// The sigma image of the edge-preserving filter: for each 8x8 block of the frame, the inverse of its
/// sigma (libjxl stores <c>1 / sigma</c> in <c>PassesDecoderState::sigma</c>), which every EPF pass reads.
/// </summary>
/// <remarks>
/// libjxl pads the image by <c>kSigmaPadding</c> blocks and mirrors <c>kSigmaBorder</c> of them, for
/// the stages that compute pixels past a group's edge. This full-frame pipeline never asks a stage for
/// a pixel outside the frame (the pipeline mirrors the pixels there), so a stage reads only the blocks
/// of the frame, and the image has no padding. The values are the same.
/// </remarks>
internal sealed class JxlEpfSigma
{
    /// <summary>
    /// libjxl's <c>kInvSigmaNum</c>: <c>4 * (sqrt(0.5) - 1)</c>, so that the weight at sigma is 0.5.
    /// </summary>
    public const float InvSigmaNum = -1.1715728752538099024f;

    /// <summary>
    /// libjxl's <c>kMinSigma</c>, <c>kInvSigmaNum / 0.3</c>: a pass leaves a block whose inverse sigma
    /// is below it as it is.
    /// </summary>
    public const float MinSigma = -3.90524291751269967465540850526868f;

    private const int BlockDim = 8;

    private readonly float[] sigma;
    private readonly int xsizeBlocks;

    // The distance between two block rows in sigma: xsizeBlocks, or 0 for a constant image, which
    // holds one row.
    private readonly int rowStride;

    private JxlEpfSigma(float[] sigma, int xsizeBlocks, int rowStride)
    {
        this.sigma = sigma;
        this.xsizeBlocks = xsizeBlocks;
        this.rowStride = rowStride;
    }

    /// <summary>
    /// Returns a sigma image with the same inverse sigma for every block: that of a Modular frame, which
    /// libjxl fills with <c>kInvSigmaNum / epf_sigma_for_modular</c> for each DC group.
    /// </summary>
    /// <param name="invSigma">The inverse sigma of every block.</param>
    /// <param name="xsizeBlocks">The width of the frame in blocks.</param>
    /// <returns>The sigma image.</returns>
    public static JxlEpfSigma Constant(float invSigma, int xsizeBlocks)
    {
        float[] row = new float[xsizeBlocks];
        Array.Fill(row, invSigma);
        return new JxlEpfSigma(row, xsizeBlocks, 0);
    }

    /// <summary>
    /// Computes the sigma of every block of a VarDCT frame from its quant field, quantiser, strategies
    /// and EPF sharpness, as libjxl's <c>ComputeSigma</c> does for each DC group.
    /// </summary>
    /// <param name="lf">The loop-filter fields.</param>
    /// <param name="state">The VarDCT frame, with every DC group decoded.</param>
    /// <returns>The sigma image.</returns>
    public static JxlEpfSigma Compute(JxlLoopFilter lf, JxlVarDctFrameState state)
    {
        // libjxl's JXL_ENSURE. It is unreachable from a file: the stages are only made when epf_iters > 0.
        if (lf.EpfIters == 0)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("EPF sigma computed with EPF off");
        }

        // The DC groups are whole blocks and no varblock crosses one, so the whole frame at once gives
        // the same values as libjxl's ComputeSigma over each DC group.
        int xsizeBlocks = state.XSizeBlocks;
        int ysizeBlocks = state.YSizeBlocks;
        JxlAcStrategyImage acStrategy = state.AcStrategy;
        float quantScale = state.Quantizer.Scale;
        float[] sharpLut = lf.EpfSharpLut;
        float[] sigma = new float[xsizeBlocks * ysizeBlocks];

        for (int by = 0; by < ysizeBlocks; by++)
        {
            ReadOnlySpan<int> rowQuant = state.RawQuantField.DangerousGetRowSpan(by);
            for (int bx = 0; bx < xsizeBlocks; bx++)
            {
                JxlAcStrategy acs = acStrategy.Get(bx, by);
                if (!acs.IsFirstBlock)
                {
                    continue;
                }

                // quant_scale is smaller for low quality, and roughly 0.08 / butteraugli score. row_quant
                // is smaller for low quality: a quantization multiplier of the form 1.0 / row_quant[bx].
                float sigmaQuant = lf.EpfQuantMul / (quantScale * rowQuant[bx] * InvSigmaNum);
                for (int iy = 0; iy < acs.CoveredBlocksY; iy++)
                {
                    Span<byte> sharpnessRow = state.EpfSharpness.DangerousGetRowSpan(by + iy);
                    for (int ix = 0; ix < acs.CoveredBlocksX; ix++)
                    {
                        float s = sigmaQuant * sharpLut[sharpnessRow[bx + ix]];

                        // Avoid infinities: std::min(-1e-4f, sigma).
                        s = s < -1e-4f ? s : -1e-4f;
                        sigma[((by + iy) * xsizeBlocks) + bx + ix] = 1.0f / s;
                    }
                }
            }
        }

        return new JxlEpfSigma(sigma, xsizeBlocks, xsizeBlocks);
    }

    /// <summary>Returns the inverse sigma of the blocks of pixel row <paramref name="y"/>.</summary>
    /// <param name="y">The pixel row of the frame.</param>
    /// <returns>One value per block of the row.</returns>
    public ReadOnlySpan<float> GetRow(int y) => this.sigma.AsSpan(y / BlockDim * this.rowStride, this.xsizeBlocks);
}
