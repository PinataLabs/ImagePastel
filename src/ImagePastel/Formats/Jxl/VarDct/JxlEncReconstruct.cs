// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// ReconstructImage in lib/jxl/enc_heuristics.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The encoder's reconstruction of its own VarDCT frame, as the decoder will render it, from the encoder's
/// quantised coefficients and DC rather than a bitstream: libjxl's <c>ReconstructImage</c>, which dynamic EPF
/// (<c>ComputeARHeuristics</c>) calls once per sharpness it tries.
/// </summary>
/// <remarks>
/// The decoding is the decoder's own: <see cref="JxlVarDctGroup.DecodeGroupForRoundtrip"/> for the
/// dequantisation and inverse transforms of each group, the loop filters of
/// <see cref="JxlLoopFilterStages"/> with the EPF sigma from the encoder's sharpness, and
/// <see cref="JxlRenderPipeline"/>. The DC is the encoder's copy (<see cref="JxlPassesEncoderState.Dc"/>),
/// dequantised and smoothed as the decoder does it.
/// </remarks>
internal static class JxlEncReconstruct
{
    /// <summary>
    /// Reconstructs the frame from the encoder's state, with its loop filters and without its colour
    /// transform: the X, Y and B opsin planes the decoder has before its XYB stage. It needs what
    /// <c>InitializePassesEncoder</c> computes (the coefficients and the DC) and reads the EPF sharpness
    /// (<see cref="JxlPassesEncoderState.EpfSharpness"/>) as it is when called.
    /// </summary>
    /// <param name="origFrameHeader">The frame header the encoder writes, with its loop filters.</param>
    /// <param name="encState">The encoder state, once the coefficients and the DC are computed.</param>
    /// <param name="allocator">The allocator.</param>
    /// <returns>
    /// The X, Y and B planes. Each is at least the frame size, and the frame is its top-left
    /// <see cref="JxlFrameDimensions.XSize"/> by <see cref="JxlFrameDimensions.YSize"/> samples; the samples
    /// past it are padding. The caller disposes them.
    /// </returns>
    public static Buffer2D<float>[] ReconstructImage(JxlFrameHeader origFrameHeader, JxlPassesEncoderState encState, MemoryAllocator allocator)
    {
        JxlFrameDimensions frameDim = encState.FrameDim;

        // The encoder has no patches or splines, so the header's flags already say so (libjxl's UpdateFlag),
        // nor noise, which libjxl would render. libjxl also drops the extra channels from the metadata: here the
        // pipeline is given none, and nothing on this path reads the metadata's.
        JxlFrameHeader frameHeader = CopyFrameHeader(origFrameHeader);
        DebugGuard.IsTrue((frameHeader.Flags & (JxlFrameFlags.Patches | JxlFrameFlags.Splines | JxlFrameFlags.Noise)) == 0, "The encoder writes no patches, splines or noise");
        DebugGuard.IsTrue(!frameHeader.UsesDcFrame && frameHeader.Encoding == JxlFrameEncoding.VarDct, "The encoder's VarDCT frame has its own DC");
        frameHeader.ColorTransform = JxlColorTransform.None;
        frameHeader.ExtraChannelUpsampling.Clear();

        // dec_state->shared = &shared: the decoder's state of the frame on the encoder's choices.
        encState.Matrices.EnsureComputed(UsedStrategies(encState.AcStrategy));
        using JxlVarDctFrameState state = new(allocator, frameHeader, frameDim, encState.Matrices, encState.Quantizer, encState.AcStrategy, encState.Cmap, encState.BlockCtxMap);
        CopyBlockImages(encState, state);

        // PreparePipeline, with the loop filters and no colour transform.
        JxlUpsamplingStages upsampling = JxlUpsamplingStages.Create(frameHeader);
        int numColorPlanes = frameHeader.NumDecodedColorPlanes;
        JxlChannelShift[] channelShifts = JxlRenderPipeline.ChannelShifts(numColorPlanes, frameHeader.ExtraChannelUpsampling.Count, upsampling.Chroma, upsampling.BeforePatches, upsampling.AfterPatches);
        using JxlRenderPipeline pipeline = new(allocator, frameDim, numColorPlanes, channelShifts);

        // DecodeGroupForRoundtrip's GroupDecCache: no passes, room for every strategy.
        JxlVarDctGroupCache groupDecCache = new(frameDim, 0, (1u << JxlAcStrategy.NumValidStrategies) - 1);
        int[][][][] coeffs = { encState.Coeffs };
        for (long groupIndex = 0; groupIndex < frameDim.NumGroups; groupIndex++)
        {
            // The groups are independent: this loop is where libjxl reconstructs them in parallel.
            JxlVarDctGroup.DecodeGroupForRoundtrip(frameHeader, coeffs, groupIndex, state, groupDecCache, pipeline);
        }

        // libjxl computes the EPF sigma of each group with ComputeSigma before decoding it; the stages here
        // compute it for the whole frame at once, which gives the same values (JxlEpfSigma.Compute).
        IJxlNeighbourStage[] loopFilters = JxlLoopFilterStages.Create(new JxlLoopFilterContext(frameHeader.LoopFilter, frameDim, state));
        IJxlColorStage[] colorStages = JxlColorStages.Create(frameHeader, displayEncoded: false);
        DebugGuard.IsTrue(colorStages.Length == 0, "A frame without a colour transform has no colour stages");
        using JxlReferenceFrame saved = pipeline.Finish(frameHeader, upsampling.WithLoopFilters(loopFilters), null, null, upsampling.AfterPatches, null, colorStages, null, null, CancellationToken.None);
        return pipeline.TakeFinishedPlanes();
    }

    // FrameHeader's copy: a bundle round trip copies every field, with the same metadata.
    private static JxlFrameHeader CopyFrameHeader(JxlFrameHeader frameHeader)
    {
        JxlBitWriter writer = new();
        JxlBundle.Write(frameHeader, writer);
        writer.ZeroPadToByte();
        JxlBitReader reader = new(writer.ToArray());
        JxlFrameHeader copy = new(frameHeader.NonserializedMetadata) { NonserializedIsPreview = frameHeader.NonserializedIsPreview };
        JxlBundle.Read(reader, copy);
        reader.Close();
        return copy;
    }

    // The strategies of the frame's varblocks, one bit per raw strategy, whose dequantisation tables the
    // decode reads.
    private static uint UsedStrategies(JxlAcStrategyImage acStrategy)
    {
        uint used = 0;
        for (int by = 0; by < acStrategy.YSize; by++)
        {
            for (int bx = 0; bx < acStrategy.XSize; bx++)
            {
                used |= 1u << acStrategy.Get(bx, by).RawStrategy;
            }
        }

        return used;
    }

    // The per-block images the decode and the EPF sigma read, from the encoder's: the quant field and EPF
    // sharpness, which the AC metadata codes, and the DC and its buckets, which the DC groups code.
    private static void CopyBlockImages(JxlPassesEncoderState encState, JxlVarDctFrameState state)
    {
        int xsize = encState.XSizeBlocks;
        for (int by = 0; by < encState.YSizeBlocks; by++)
        {
            int row = by * xsize;
            encState.RawQuantField.AsSpan(row, xsize).CopyTo(state.RawQuantField.DangerousGetRowSpan(by));
            encState.EpfSharpness.AsSpan(row, xsize).CopyTo(state.EpfSharpness.DangerousGetRowSpan(by));
            encState.QuantDc.DangerousGetRowSpan(by).Slice(0, xsize).CopyTo(state.QuantDc.DangerousGetRowSpan(by));
            for (int c = 0; c < 3; c++)
            {
                encState.Dc[c].DangerousGetRowSpan(by).Slice(0, xsize).CopyTo(state.Dc[c].DangerousGetRowSpan(by));
            }
        }
    }
}
