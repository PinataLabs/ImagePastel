// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_xyb.cc (ToXYB), lib/jxl/enc_frame.cc (ParamsPostInit, LoopFilterFromParams, MakeFrameHeader, SimplifyInvisible,
// ComputeChromacityAdjustments, ComputeVarDCTEncodingData, ComputeAllCoeffOrders, TokenizeAllCoefficients, EncodeGlobalDCInfo,
// EncodeGlobalACInfo, the VarDCT parts of EncodeGroups and ComputeEncodingData), lib/jxl/enc_cache.cc
// (InitializePassesEncoder, ComputeACMetadata), lib/jxl/enc_group.cc (EncodeGroupTokenizedCoefficients),
// lib/jxl/image_ops.cc (PadImageToBlockMultipleInPlace), lib/jxl/enc_quant_weights.cc
// (DequantMatricesEncode) and
// lib/jxl/enc_chroma_from_luma.cc (ColorCorrelationEncodeDC), for the default values each writes.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <content>
/// <para>
/// The lossy VarDCT frame, as libjxl's <c>EncodeFrameOneShot</c> writes it at effort 3 (Fastest, libjxl's
/// <c>kFalcon</c>), effort 5 (Default, <c>kHare</c>) and effort 7 (Thorough, <c>kSquirrel</c>): XYB, the
/// DC and AC metadata in Modular streams, and the AC tokens. This is the one place that lists what each
/// effort does; other comments point here.
/// </para>
/// <para>
/// Fastest codes every block as DCT8 with a constant quant field of 0.79 / distance, the default
/// chroma-from-luma map and the default block context map, with no Gaborish. Only the DCT8 order is
/// customised, and the AC metadata uses libjxl's fixed Falcon tree.
/// </para>
/// <para>
/// Default searches, as <see cref="JxlEncHeuristics.LossyFrameHeuristics"/> does per 64x64 tile:
/// the adaptive quant field (<see cref="JxlEncAdaptiveQuantization"/>: <c>InitialQuantField</c>, the global
/// scale from 0.39 / distance, and <c>AdjustQuantField</c>); the strategy of each block
/// (<see cref="JxlEncAcStrategy"/>: DCT8, DCT4X4, DCT2X2 and IDENTITY at the 8x8 level, then the aligned
/// merges up to 64x64); the fast least-squares chroma-from-luma map of each tile
/// (<see cref="JxlEncChromaFromLuma"/>); and the block context map (<see cref="JxlEncBlockCtxMap"/>).
/// The frame then adds Gaborish above distance 0.5, near-lossless DC, <c>AdjustQuantBlockAC</c>, the
/// <c>kACMeta</c> tree for the AC metadata, a learned tree for alpha, and custom coefficient orders for every
/// order the blocks use. <c>FindBestQuantizer</c> does nothing at Default, so there is no butteraugli loop.
/// </para>
/// <para>
/// Thorough is Default with libjxl's Squirrel gates on top. The alpha tree is learned with Squirrel's
/// parameters (7 properties, 96 values). Splines are found as libjxl v0.12 finds them, which is never, and
/// patches and dots are left out (a departure, ADR 0001; see <see cref="JxlEncHeuristics.LossyFrameHeuristics"/>).
/// <c>FindBestQuantizer</c> still does nothing. The 8x8 level also tries AFV0 to AFV3. Where the frame has
/// EPF (from distance 0.7), the EPF sharpness of each block is searched
/// (<see cref="JxlEncHeuristics.ComputeArHeuristics"/>): the encoder reconstructs the frame once per
/// sharpness step (<see cref="JxlEncReconstruct.ReconstructImage"/>) and keeps the step of least masked
/// error against the pre-Gaborish opsin. The other Squirrel stages are still in their Default form, each a
/// temporary departure named in its code with the slice that ports it: the 8x8 level does not try DCT4X8
/// and DCT8X4 (they come with E5b); the merges are the aligned ones (the non-aligned 16x16 and 32x32
/// merges come with E5d); there is no chroma-from-luma pre-pass, and the fit after the search is the fast
/// least squares (the DCT8 pre-pass and the Newton fit come with E5e); and the X and B quantisation scales
/// come from the distance alone (the pixel statistics come with E5f).
/// </para>
/// </content>
/// <remarks>
/// <para>
/// Departure (ADR 0001): from distance 10, libjxl encodes at half resolution with an adjusted distance
/// (<c>ParamsPostInit</c>) and upsamples on decode. That needs its downsampling, which is not ported, so
/// this encoder encodes at the given distance and full resolution. The output stays valid; it is larger
/// than cjxl's there, and the EPF iterations follow the distance given rather than the adjusted one.
/// </para>
/// <para>
/// Departure (ADR 0001): for a frame of more than 8 groups, cjxl takes its streaming path
/// (<c>CanDoStreamingEncoding</c>), which this port does not have; it always takes the one-shot path.
/// </para>
/// </remarks>
internal static partial class JxlFrameEncoder
{
    // libjxl's kMinButteraugliDistance: smaller distances are clamped to it.
    private const float MinButteraugliDistance = 0.05f;

    /// <summary>
    /// Encodes <paramref name="image"/> as the only frame of a still image, lossy at
    /// <paramref name="distance"/>. The image is sRGB; its samples become XYB.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="metadata">The codestream headers, XYB-encoded.</param>
    /// <param name="image">The image.</param>
    /// <param name="speedTier">The speed tier, which the effort sets.</param>
    /// <param name="distance">The butteraugli distance, above 0.</param>
    /// <param name="naturalCoeffOrders">Whether to keep every coefficient order natural (for tests).</param>
    /// <param name="arHeuristicsProbe">
    /// A callback for tests, run once the EPF sharpness is set (<see cref="IJxlEncoderOptions.ArHeuristicsProbe"/>),
    /// or <see langword="null"/>.
    /// </param>
    /// <param name="allocator">The allocator.</param>
    /// <param name="writer">The writer, at a byte boundary.</param>
    public static void EncodeLossyFrame<TPixel>(JxlCodecMetadata metadata, Image<TPixel> image, JxlSpeedTier speedTier, float distance, bool naturalCoeffOrders, Action<JxlFrameHeader, JxlPassesEncoderState> arHeuristicsProbe, MemoryAllocator allocator, JxlBitWriter writer)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // JxlEncoderSetFrameDistance and ParamsPostInit clamp small distances. libjxl then keeps a copy as
        // original_butteraugli_distance, which sets the X quantisation scale, because its resampling from
        // distance 10 changes butteraugli_distance. Resampling is not ported (see the remarks), so the two
        // are always equal and this keeps one.
        if (distance < MinButteraugliDistance)
        {
            distance = MinButteraugliDistance;
        }

        JxlFrameHeader frameHeader = MakeVarDctFrameHeader(image.Width, image.Height, metadata, speedTier, distance);
        JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();
        using JxlPassesEncoderState encState = new(frameDim, speedTier, distance, allocator) { NaturalCoeffOrders = naturalCoeffOrders };
        using JxlModularFrameEncoder encModular = new(frameHeader, speedTier, allocator);
        List<JxlBitWriter> groupCodes = ComputeVarDctEncodingData(frameHeader, frameDim, metadata, image, encState, encModular, arHeuristicsProbe, allocator);

        WriteFrameHeader(frameHeader, writer);
        WriteSections(groupCodes, writer);
    }

    // libjxl's MakeFrameHeader with LoopFilterFromParams and FrameFlagsFromParams, for a lossy VarDCT frame
    // that is the last frame of a still image.
    private static JxlFrameHeader MakeVarDctFrameHeader(long xsize, long ysize, JxlCodecMetadata metadata, JxlSpeedTier speedTier, float distance)
    {
        JxlFrameHeader frameHeader = new(metadata)
        {
            IsLast = true,
            FrameType = JxlFrameType.Regular,
            Encoding = JxlFrameEncoding.VarDct,

            // GetGroupSizeShift gives 1 for VarDCT: 256-pixel groups.
            GroupSizeShift = 1,
            ColorTransform = JxlColorTransform.Xyb,

            // Noise starts at distance 99 (kMinButteraugliForNoise), above the largest distance; patches,
            // splines and DC frames are off.
            Flags = JxlFrameFlags.None,

            FrameSizeX = (uint)xsize,
            FrameSizeY = (uint)ysize,
            CustomSizeOrOrigin = false,
            Upsampling = 1,
        };

        frameHeader.Passes.NumPasses = 1;

        // Gaborish defaults to enabled in Hare or slower, above distance 0.5, with the default weights
        // (gab_custom off).
        frameHeader.LoopFilter.Gab = speedTier <= JxlSpeedTier.Hare && distance > 0.5f;

        // EPF iterations by distance, at decoding speed tier 0.
        float[] thresholds = { 0.7f, 1.5f, 4.0f };
        frameHeader.LoopFilter.EpfIters = 0;
        foreach (float threshold in thresholds)
        {
            if (distance >= threshold)
            {
                frameHeader.LoopFilter.EpfIters++;
            }
        }

        frameHeader.ExtraChannelUpsampling.Clear();
        for (int i = 0; i < metadata.M.NumExtraChannels; i++)
        {
            frameHeader.ExtraChannelUpsampling.Add(1);
        }

        return frameHeader;
    }

    // libjxl's frame-level ComputeEncodingData for a VarDCT frame that is not a JPEG transcode.
    private static List<JxlBitWriter> ComputeVarDctEncodingData<TPixel>(JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlCodecMetadata metadata, Image<TPixel> image, JxlPassesEncoderState encState, JxlModularFrameEncoder encModular, Action<JxlFrameHeader, JxlPassesEncoderState> arHeuristicsProbe, MemoryAllocator allocator)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Allocating a large enough image avoids a copy when padding.
        int paddedX = (int)(frameDim.XSizeBlocks * JxlFrameDimensions.BlockDim);
        int paddedY = (int)(frameDim.YSizeBlocks * JxlFrameDimensions.BlockDim);
        Buffer2D<float>[] color = new Buffer2D<float>[3];
        Buffer2D<float>[] origOpsin = null;
        try
        {
            for (int c = 0; c < 3; c++)
            {
                color[c] = allocator.Allocate2D<float>(paddedX, paddedY, AllocationOptions.Clean);
            }

            // A grey image fills all three planes with its grey, as libjxl's CopyColorChannels does, so
            // every colour space below takes three planes.
            JxlEncoderInput<TPixel>.CopyColorPlanes(image, color);

            ToXyb(metadata.M.ColorEncoding, metadata.M.ToneMapping.IntensityTarget, color, image.Width, image.Height);

            // libjxl simplifies the invisible pixels of straight alpha in a regular frame, unless
            // keep_invisible is set (by default it is set only for lossless), the extra channels are
            // resampled differently from the colour (never here: ec_resampling is the colour's 1), or
            // perceptual optimizations are disabled (they are not).
            int alphaIndex = metadata.M.Find(JxlExtraChannelType.Alpha);
            if (alphaIndex >= 0 && !metadata.M.ExtraChannelInfo[alphaIndex].AlphaAssociated && frameHeader.FrameType == JxlFrameType.Regular)
            {
                using Buffer2D<float> alpha = allocator.Allocate2D<float>(image.Width, image.Height);
                JxlEncoderInput<TPixel>.CopyAlphaPlane(image, alpha);
                SimplifyInvisible(color, alpha, image.Width, image.Height, lossless: false);
            }

            PadImageToBlockMultipleInPlace(color, image.Width, image.Height, paddedX, paddedY);
            ComputeChromacityAdjustments(frameHeader, encState);

            // ComputeVarDCTEncodingData. Save the pre-Gaborish opsin for the AR control field heuristics,
            // shrunk to the frame size: LossyFrameHeuristics applies the inverse Gaborish filter in place.
            // libjxl copies it for every frame; only ComputeARHeuristics's search reads it, so the copy is
            // made only when the search runs.
            if (JxlEncHeuristics.UsesDynamicAr(encState, frameHeader.LoopFilter.EpfIters))
            {
                origOpsin = new Buffer2D<float>[3];
                for (int c = 0; c < 3; c++)
                {
                    origOpsin[c] = allocator.Allocate2D<float>((int)frameDim.XSize, (int)frameDim.YSize);
                    JxlPlanes.CopyRows(color[c], origOpsin[c], (int)frameDim.XSize, (int)frameDim.YSize);
                }
            }

            JxlEncHeuristics.LossyFrameHeuristics(encState, frameHeader.LoopFilter.Gab, color, allocator);
            InitializePassesEncoder(frameHeader, color, encState, encModular);
            JxlEncHeuristics.ComputeArHeuristics(frameHeader, encState, origOpsin, allocator);
            arHeuristicsProbe?.Invoke(frameHeader, encState);
            ComputeAcMetadata(encState, encModular);
        }
        finally
        {
            JxlPlanes.DisposeAll(color);
            if (origOpsin != null)
            {
                JxlPlanes.DisposeAll(origOpsin);
            }
        }

        ComputeAllCoeffOrders(encState);
        encState.NumHistograms = 1;
        for (long groupIndex = 0; groupIndex < frameDim.NumGroups; groupIndex++)
        {
            // The groups are independent: this loop is where libjxl tokenizes them in parallel.
            JxlAcTokenizer.TokenizeCoefficients(encState, groupIndex);
        }

        // The extra channels (alpha) are coded losslessly in Modular, without the colour; a frame without
        // them has no Modular image.
        if (metadata.M.NumExtraChannels > 0)
        {
            encModular.ComputeEncodingData(frameHeader, metadata.M, image, doColor: false);
        }

        encModular.ComputeTree();
        encModular.ComputeTokens();
        return EncodeVarDctGroups(frameHeader, frameDim, encState, encModular);
    }

    // ToXYB: the colour planes of the image's colour encoding to X, Y and B in place. Linear sRGB goes
    // straight through the opsin matrix and sRGB through its curve first (libjxl's IsSRGB covers grey too,
    // so a grey image, whose grey is in all three planes, takes the same branches). Any other encoding is
    // converted to linear sRGB, or linear grey (JxlLinearSrgbTransform), then goes through the opsin
    // matrix. libjxl keeps a linear copy from Kitten down, for its butteraugli search; Falcon does not.
    private static void ToXyb(JxlColorEncodingHeader c, float intensityTarget, Buffer2D<float>[] color, int width, int height)
    {
        float[] premulAbsorb = JxlXybEncoder.ComputePremulAbsorb(intensityTarget);
        bool srgbSpace = c.WhitePoint == JxlWhitePoint.D65 && (c.IsGray || (c.ColorSpace == JxlColorSpace.Rgb && c.Primaries == JxlPrimaries.Srgb));
        bool linearSrgb = srgbSpace && c.Tf.IsLinear;
        bool srgb = srgbSpace && c.Tf.IsSrgb;
        JxlLinearSrgbTransform transform = linearSrgb || srgb ? null : new JxlLinearSrgbTransform(c, intensityTarget);
        for (int y = 0; y < height; y++)
        {
            Span<float> row0 = color[0].DangerousGetRowSpan(y).Slice(0, width);
            Span<float> row1 = color[1].DangerousGetRowSpan(y).Slice(0, width);
            Span<float> row2 = color[2].DangerousGetRowSpan(y).Slice(0, width);
            if (srgb)
            {
                JxlXybEncoder.SrgbRowToXyb(row0, row1, row2, premulAbsorb);
                continue;
            }

            transform?.ConvertRow(row0, row1, row2);
            JxlXybEncoder.LinearRgbRowToXyb(row0, row1, row2, premulAbsorb);
        }
    }

    // Replace invisible pixels with a weighted average of the pixel to the left, the pixel to the
    // topright, and non-invisible neighbours. Produces downward-blurry smears, with in the upwards
    // direction only a 1px edge duplication but not more. It would probably be better to smear in all
    // directions. That requires an alpha-weighed convolution with a large enough kernel though, which
    // might be overkill...
    // The image is the xsize x ysize top-left of the planes; each plane is done in turn, in place, so a
    // pixel reads the new values of its left and upper neighbours.
    private static void SimplifyInvisible(Buffer2D<float>[] image, Buffer2D<float> alpha, int xsize, int ysize, bool lossless)
    {
        for (int c = 0; c < 3; ++c)
        {
            for (int y = 0; y < ysize; ++y)
            {
                Span<float> row = image[c].DangerousGetRowSpan(y);
                Span<float> prow = y > 0 ? image[c].DangerousGetRowSpan(y - 1) : default;
                Span<float> nrow = y + 1 < ysize ? image[c].DangerousGetRowSpan(y + 1) : default;
                Span<float> a = alpha.DangerousGetRowSpan(y);
                Span<float> pa = y > 0 ? alpha.DangerousGetRowSpan(y - 1) : default;
                Span<float> na = y + 1 < ysize ? alpha.DangerousGetRowSpan(y + 1) : default;
                for (int x = 0; x < xsize; ++x)
                {
                    if (a[x] != 0)
                    {
                        continue;
                    }

                    if (lossless)
                    {
                        row[x] = 0;
                        continue;
                    }

                    float d = 0f;
                    row[x] = 0;
                    if (x > 0)
                    {
                        row[x] += row[x - 1];
                        d++;
                        if (a[x - 1] > 0f)
                        {
                            row[x] += row[x - 1];
                            d++;
                        }
                    }

                    if (x + 1 < xsize)
                    {
                        if (y > 0)
                        {
                            row[x] += prow[x + 1];
                            d++;
                        }

                        if (a[x + 1] > 0f)
                        {
                            row[x] += 2f * row[x + 1];
                            d += 2f;
                        }

                        if (y > 0 && pa[x + 1] > 0f)
                        {
                            row[x] += 2f * prow[x + 1];
                            d += 2f;
                        }

                        if (y + 1 < ysize && na[x + 1] > 0f)
                        {
                            row[x] += 2f * nrow[x + 1];
                            d += 2f;
                        }
                    }

                    if (y > 0 && pa[x] > 0f)
                    {
                        row[x] += 2f * prow[x];
                        d += 2f;
                    }

                    if (y + 1 < ysize && na[x] > 0f)
                    {
                        row[x] += 2f * nrow[x];
                        d += 2f;
                    }

                    if (d > 1f)
                    {
                        row[x] /= d;
                    }
                }
            }
        }
    }

    // The last column and row are replicated up to a multiple of the block size.
    private static void PadImageToBlockMultipleInPlace(Buffer2D<float>[] image, int xsizeOrig, int ysizeOrig, int xsize, int ysize)
    {
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < ysizeOrig; y++)
            {
                Span<float> row = image[c].DangerousGetRowSpan(y);
                for (int x = xsizeOrig; x < xsize; x++)
                {
                    row[x] = row[xsizeOrig - 1];
                }
            }

            Span<float> rowSrc = image[c].DangerousGetRowSpan(ysizeOrig - 1).Slice(0, xsize);
            for (int y = ysizeOrig; y < ysize; y++)
            {
                rowSrc.CopyTo(image[c].DangerousGetRowSpan(y));
            }
        }
    }

    // The distance-based chromacity adjustment of libjxl's ComputeChromacityAdjustments. The pixel-based
    // one runs from Squirrel down; without it, its adjustments are 0, so the B scale is 2.
    private static void ComputeChromacityAdjustments(JxlFrameHeader frameHeader, JxlPassesEncoderState encState)
    {
        // Temporary departure (E5a): from Squirrel down libjxl measures how pixelized the X and B channels
        // are (PixelStatsForChromacityAdjustment, speed_tier <= kSquirrel) and may raise both scales. E5f
        // ports it; until then its adjustments are 0 at every tier, as above Squirrel.
        float[] xQmScaleSteps = { 2.5f, 5.5f, 9.5f };
        frameHeader.XQmScale = 3;
        foreach (float step in xQmScaleSteps)
        {
            // libjxl compares cparams.original_butteraugli_distance, equal to the distance here.
            if (encState.Distance > step)
            {
                frameHeader.XQmScale++;
            }
        }

        // For X take the most severe adjustment: max(x_qm_scale, 2 + 0).
        frameHeader.BQmScale = 2;
    }

    // libjxl's InitializePassesEncoder for one pass and no DC frame: the X and B multipliers, the
    // coefficients of every group, then the DC streams of every LF group.
    private static void InitializePassesEncoder(JxlFrameHeader frameHeader, Buffer2D<float>[] opsin, JxlPassesEncoderState encState, JxlModularFrameEncoder encModular)
    {
        // Exact powers of 1.25 for the scales the encoder writes.
        encState.XQmMultiplier = MathF.Pow(1.25f, frameHeader.XQmScale - 2.0f);
        encState.BQmMultiplier = MathF.Pow(1.25f, frameHeader.BQmScale - 2.0f);

        // libjxl rescales the global scale and the DC quantisation by cparams.quant_ac_rescale here, which
        // is 1, so both stay as they are.
        JxlFrameDimensions frameDim = encState.FrameDim;
        float[][] dc = new float[3][];
        for (int c = 0; c < 3; c++)
        {
            dc[c] = new float[encState.XSizeBlocks * encState.YSizeBlocks];
        }

        for (long groupIdx = 0; groupIdx < frameDim.NumGroups; groupIdx++)
        {
            // The groups are independent: this loop is where libjxl computes them in parallel.
            JxlEncGroup.ComputeCoefficients(groupIdx, encState, opsin, dc);
        }

        for (long groupIndex = 0; groupIndex < frameDim.NumDcGroups; groupIndex++)
        {
            JxlRect r = frameDim.DcGroupRect(groupIndex);
            encModular.AddVarDctDc(frameHeader, dc, encState.XSizeBlocks, r, groupIndex, encState.SpeedTier < JxlSpeedTier.Falcon, encState);
        }

        // The encoder's own copy of the DC, dequantised by AddVarDctDc, is smoothed as the decoder's is
        // (FinalizeDC). Only the reconstruction of the frame reads it (JxlEncReconstruct).
        if ((frameHeader.Flags & JxlFrameFlags.SkipAdaptiveDcSmoothing) == 0)
        {
            JxlCompressedDc.AdaptiveDcSmoothing(encState.Allocator, encState.Quantizer.MulDc, encState.Dc, encState.XSizeBlocks, encState.YSizeBlocks);
        }
    }

    private static void ComputeAcMetadata(JxlPassesEncoderState encState, JxlModularFrameEncoder encModular)
    {
        JxlFrameDimensions frameDim = encState.FrameDim;
        for (long groupIndex = 0; groupIndex < frameDim.NumDcGroups; groupIndex++)
        {
            encModular.AddAcMetadata(frameDim.DcGroupRect(groupIndex), groupIndex, encState);
        }
    }

    private static void ComputeAllCoeffOrders(JxlPassesEncoderState encState)
    {
        (uint usedOrders, uint customizedOrders) = JxlEncCoeffOrder.ComputeUsedOrders(encState.SpeedTier, encState.AcStrategy);
        if (encState.NaturalCoeffOrders)
        {
            customizedOrders = 0;
        }

        uint allUsedOrders = 0;
        JxlEncCoeffOrder.ComputeCoeffOrder(encState.SpeedTier, encState.Coeffs, encState.AcStrategy, encState.FrameDim, ref allUsedOrders, usedOrders, customizedOrders, encState.CoeffOrders);
        encState.UsedOrders = allUsedOrders;
        encState.UsedAcs |= usedOrders;
    }

    // Writes each section of a VarDCT frame into its own writer, as libjxl's EncodeGroups.
    private static List<JxlBitWriter> EncodeVarDctGroups(JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlPassesEncoderState encState, JxlModularFrameEncoder encModular)
    {
        long numGroups = frameDim.NumGroups;
        long numPasses = frameHeader.Passes.NumPasses;
        long globalAcIndex = frameDim.NumDcGroups + 1;
        bool isSmallImage = numGroups == 1 && numPasses == 1;
        long numTocEntries = JxlToc.NumTocEntries(numGroups, frameDim.NumDcGroups, numPasses);
        List<JxlBitWriter> groupCodes = new((int)numTocEntries);
        for (long i = 0; i < numTocEntries; ++i)
        {
            groupCodes.Add(new JxlBitWriter());
        }

        JxlBitWriter GetOutput(long index) => groupCodes[isSmallImage ? 0 : (int)index];

        // LF global: no patches, splines or noise.
        DequantMatricesEncodeDC(GetOutput(0));
        EncodeGlobalDcInfo(encState, GetOutput(0));
        encModular.EncodeGlobalInfo(GetOutput(0));
        encModular.EncodeStream(GetOutput(0), JxlModularStreamId.Global);

        // LF groups. The groups are independent: this loop and the last are where libjxl encodes them in
        // parallel.
        for (long groupIndex = 0; groupIndex < frameDim.NumDcGroups; groupIndex++)
        {
            JxlBitWriter output = GetOutput(groupIndex + 1);
            output.Write(2, (ulong)encModular.ExtraDcPrecision[groupIndex]);
            encModular.EncodeStream(output, JxlModularStreamId.VarDctDc(frameDim, groupIndex));
            encModular.EncodeStream(output, JxlModularStreamId.ModularDc(frameDim, groupIndex));
            JxlRect rect = frameDim.DcGroupRect(groupIndex);
            int nbBits = JxlBits.CeilLog2Nonzero((ulong)(rect.XSize * rect.YSize));
            if (nbBits != 0)
            {
                output.Write(nbBits, (ulong)(encModular.AcMetadataSize[groupIndex] - 1));
            }

            encModular.EncodeStream(output, JxlModularStreamId.AcMetadata(frameDim, groupIndex));
        }

        EncodeGlobalAcInfo(encState, GetOutput(globalAcIndex));

        for (long groupIndex = 0; groupIndex < numGroups; groupIndex++)
        {
            for (long i = 0; i < numPasses; i++)
            {
                JxlBitWriter output = GetOutput(JxlToc.AcGroupIndex(i, groupIndex, numGroups, frameDim.NumDcGroups));
                EncodeGroupTokenizedCoefficients(groupIndex, encState, output);

                // Write all modular encoded data (color?, alpha, depth, extra channels)
                encModular.EncodeStream(output, JxlModularStreamId.ModularAc(frameDim, groupIndex, i));
            }
        }

        foreach (JxlBitWriter bw in groupCodes)
        {
            // end of group.
            bw.ZeroPadToByte();
        }

        return groupCodes;
    }

    // The quantiser, the block context map, then the chroma-from-luma DC as its default bit.
    private static void EncodeGlobalDcInfo(JxlPassesEncoderState encState, JxlBitWriter writer)
    {
        encState.Quantizer.Encode(writer);

        JxlEncBlockCtxMap.EncodeBlockCtxMap(encState.BlockCtxMap, writer);

        // ColorCorrelationEncodeDC: the default factors and DC, written as one bit.
        writer.Write(1, 1);
    }

    private static void EncodeGlobalAcInfo(JxlPassesEncoderState encState, JxlBitWriter writer)
    {
        // DequantMatricesEncode: every table is the library default, written as one bit.
        writer.Write(1, 1);
        int numHistoBits = JxlBits.CeilLog2Nonzero((ulong)encState.FrameDim.NumGroups);
        if (numHistoBits != 0)
        {
            writer.Write(numHistoBits, (ulong)(encState.NumHistograms - 1));
        }

        // Encode coefficient orders (one pass).
        JxlFieldCoders.WriteU32(JxlVarDctFrameState.OrderEnc, encState.UsedOrders, writer);
        JxlEncCoeffOrder.EncodeCoeffOrders(encState.UsedOrders, encState.CoeffOrders, writer);

        // Encode histograms. libjxl caps them at 6 from decoding speed tier 1, which the encoder has no
        // option for.
        JxlHistogramParams histParams = JxlHistogramParams.ForAc(encState.SpeedTier);
        JxlEntropyEncoder.BuildAndEncodeHistograms(histParams, encState.NumHistograms * encState.BlockCtxMap.NumAcContexts, encState.AcTokens, encState.Codes, writer);
    }

    private static void EncodeGroupTokenizedCoefficients(long groupIdx, JxlPassesEncoderState encState, JxlBitWriter writer)
    {
        // Select which histogram to use among those of the current pass: there is one set.
        int histoSelectorBits = JxlBits.CeilLog2Nonzero((uint)encState.NumHistograms);
        if (histoSelectorBits != 0)
        {
            writer.Write(histoSelectorBits, 0);
        }

        JxlEntropyEncoder.WriteTokens(encState.AcTokens[groupIdx], encState.Codes, 0, writer);
    }
}
