// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_frame.h and lib/jxl/dec_frame.cc (DecodeFrame, FrameDecoder: InitFrame, ProcessDCGlobal,
// ProcessDCGroup, FinalizeDC, AllocateOutput, ProcessACGlobal, ProcessACGroup, ProcessSections, FinalizeFrame), the
// per-frame steps of the frame loop in JxlDecoderProcessCodestream (lib/jxl/decode.cc), the
// pipeline set-up of PassesDecoderState::PreparePipeline (lib/jxl/dec_cache.cc) with its spline stage and
// its DC-slot write, the DC-frame part of InitializePassesSharedState (lib/jxl/passes_state.cc), and the
// frame counters that seed the noise (visible_frame_index and nonvisible_frame_index in
// lib/jxl/dec_cache.h).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <summary>
/// Decodes one frame: its header and TOC, then its sections, into a render pipeline.
/// </summary>
/// <remarks>
/// The frame loop is serial. Each group decodes from its own section reader into its own rectangle
/// and shares no mutable state, so the loop can become a <c>Parallel.For</c> later.
/// </remarks>
internal sealed class JxlFrameDecoder
{
    /// <summary>The largest frame, in pixels: the Level 5 limit (2^28).</summary>
    public const long MaxPixelsPerFrame = 1L << 28;

    private readonly Configuration configuration;
    private readonly JxlCodecMetadata metadata;
    private readonly JxlReferenceFrames references;

    // Used for seeding noise: libjxl's visible_frame_index and nonvisible_frame_index, which its
    // PassesDecoderState keeps across the frames of an image.
    private long visibleFrameIndex;
    private long nonvisibleFrameIndex;

    public JxlFrameDecoder(Configuration configuration, JxlCodecMetadata metadata, JxlReferenceFrames references)
    {
        this.configuration = configuration;
        this.metadata = metadata;
        this.references = references;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the frames skip their colour-transform stages, so that an XYB frame
    /// reaches the sink as its opsin planes after the loop filters. No libjxl output does this: it is for the
    /// tests that compare the encoder's reconstruction of its frame (<see cref="JxlEncReconstruct"/>) with the
    /// decoded file.
    /// </summary>
    public bool SkipColorTransform { get; set; }

    /// <summary>
    /// Decodes the frame that starts at byte <paramref name="offset"/>: blends it onto its reference
    /// slots, saves it to its own slot if it can be referenced, and hands it to <paramref name="sink"/>
    /// if it is displayed. Throws <see cref="InvalidImageContentException"/> for invalid data, and
    /// <see cref="InvalidMemoryOperationException"/> for frames over the pixel cap or the sample budget.
    /// </summary>
    /// <param name="data">The codestream.</param>
    /// <param name="offset">The first byte of the frame.</param>
    /// <param name="sink">The sink for the displayed frame.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="frameEnd">The byte after the frame's last section, where the next frame starts.</param>
    /// <returns>The frame header.</returns>
    public JxlFrameHeader DecodeFrame(byte[] data, int offset, IJxlRenderSink sink, CancellationToken cancellationToken, out int frameEnd)
    {
        int availIn = data.Length - offset;
        JxlBitReader reader = new(data, offset, availIn);

        // libjxl's FrameHeader reads the flags as a U64 and never checks the bits it does not define (4, 8,
        // 64, and 256 and up), so a frame with them decodes as the same frame without them. This port does
        // the same.
        JxlFrameHeader frameHeader = new(this.metadata);
        JxlBundle.Read(reader, frameHeader);
        JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();

        // InitFrame counts the shown frames, and the frames since the last shown one. A DC frame is never
        // shown, so it counts as not shown. A preview frame counts as not shown too (SkipPreviewFrame).
        if ((frameHeader.IsLast || frameHeader.AnimationFrame.Duration > 0) &&
            (frameHeader.FrameType == JxlFrameType.Regular || frameHeader.FrameType == JxlFrameType.SkipProgressive))
        {
            this.visibleFrameIndex++;
            this.nonvisibleFrameIndex = 0;
        }
        else
        {
            this.nonvisibleFrameIndex++;
        }

        // The pixel cap is checked before anything is allocated for the frame. It also covers libjxl's
        // CheckSizeLimit in decode.cc, which only guards against size_t overflow.
        if (frameDim.XSizeUpsampled * frameDim.YSizeUpsampled > MaxPixelsPerFrame)
        {
            throw new InvalidMemoryOperationException($"The JPEG XL frame is {frameDim.XSizeUpsampled}x{frameDim.YSizeUpsampled} pixels, more than the limit of {MaxPixelsPerFrame}.");
        }

        // The sample budget, a departure from libjxl (see JxlSampleBudget), is checked with it.
        JxlSampleBudget.CheckFrame(frameDim, frameHeader, this.metadata.M.NumExtraChannels);

        long numPasses = frameHeader.Passes.NumPasses;
        long numGroups = frameDim.NumGroups;
        long tocEntries = JxlToc.NumTocEntries(numGroups, frameDim.NumDcGroups, numPasses);
        JxlGroupOffsets groupOffsets = JxlToc.ReadGroupOffsets(tocEntries, reader);

        reader.EnsureAllReadsWithinBounds("frame header");
        long headerBytes = reader.TotalBitsConsumed / 8;
        reader.Close();

        // A departure from libjxl, whose JxlDecoderProcessSections (decode.cc) checks the bytes of each
        // section as the data arrives. This decoder has the whole codestream, so it checks once that the
        // frame holds all its sections. Every section lies inside the total size, so the check is the same.
        if (headerBytes + groupOffsets.TotalSize > availIn)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Not enough bytes for the frame sections");
        }

        // One reader per section, in the order the sections are decoded in. A permuted TOC only changes
        // where each section is stored.
        JxlBitReader[] sections = new JxlBitReader[tocEntries];
        for (long i = 0; i < tocEntries; i++)
        {
            sections[i] = new JxlBitReader(data, offset + (int)(headerBytes + groupOffsets.Offsets[i]), (int)groupOffsets.Sizes[i]);
        }

        frameEnd = offset + (int)(headerBytes + groupOffsets.TotalSize);

        JxlReferenceFrame dcFrame = frameHeader.UsesDcFrame ? this.GetDcFrame(frameHeader, frameDim) : null;

        bool isLastOfStill = IsLastOfStill(frameHeader);

        // PreparePipeline: the blending stage checks its reference slots before any section is decoded.
        // The patch dictionary is read with the DC global section, and the patches stage uses it then.
        // The patches run at the coded size, before the upsampling; the blending at the upsampled size.
        // The splines and the noise parameters are read with the DC global section too. The splines are
        // drawn after the patches, and the noise is added after the upsampling.
        JxlImageFeatures features = new(
            (frameHeader.Flags & JxlFrameFlags.Patches) != 0 ? new JxlPatchDictionary(this.references) : null,
            (frameHeader.Flags & JxlFrameFlags.Splines) != 0 ? new JxlSplines() : null,
            (frameHeader.Flags & JxlFrameFlags.Noise) != 0 ? new JxlNoiseParams() : null);
        using JxlPatchesStage patchesStage = features.Patches != null
            ? new JxlPatchesStage(this.configuration.MemoryAllocator, features.Patches, this.metadata.M, (int)frameDim.XSize)
            : null;
        JxlSplinesStage splinesStage = features.Splines != null ? new JxlSplinesStage(features.Splines) : null;
        IJxlColorStage[] colorStages = this.SkipColorTransform ? Array.Empty<IJxlColorStage>() : JxlColorStages.Create(frameHeader, sink.DisplayEncoded);
        using JxlBlendingStage blending = JxlBlending.NeedsBlending(frameHeader)
            ? new JxlBlendingStage(this.configuration.MemoryAllocator, frameHeader, this.references, (int)frameDim.XSizeUpsampled)
            : null;

        // InitFrame resets the dequantization matrices to their defaults for each frame.
        JxlDequantMatrices matrices = new();
        using JxlModularFrameDecoder modularFrameDecoder = new(this.configuration.MemoryAllocator, frameDim, matrices, cancellationToken);

        // The upsampling stages give the channel shifts, so they are built with the pipeline; the loop
        // filters come once the sections are decoded.
        JxlUpsamplingStages upsampling = JxlUpsamplingStages.Create(frameHeader);
        int numColorPlanes = frameHeader.NumDecodedColorPlanes;
        JxlChannelShift[] channelShifts = JxlRenderPipeline.ChannelShifts(numColorPlanes, frameHeader.ExtraChannelUpsampling.Count, upsampling.Chroma, upsampling.BeforePatches, upsampling.AfterPatches);
        using JxlRenderPipeline pipeline = new(this.configuration.MemoryAllocator, frameDim, numColorPlanes, channelShifts, withNoisePlanes: features.Noise != null);
        using JxlVarDctFrameState varDct = frameHeader.Encoding == JxlFrameEncoding.VarDct ? new JxlVarDctFrameState(this.configuration.MemoryAllocator, frameHeader, frameDim, matrices, dcFrame) : null;

        // libjxl makes a new colour-correlation map for each frame. A VarDCT frame decodes its own; a
        // Modular frame keeps the defaults, base ratios 0 and 1. The splines and the noise take it.
        JxlColorCorrelation cmap = varDct?.Cmap ?? new JxlColorCorrelation(frameDim.XSize, frameDim.YSize);

        ProcessSections(frameHeader, frameDim, sections, features, cmap, matrices, modularFrameDecoder, varDct, pipeline, cancellationToken);

        // libjxl's ProcessACGroup fills each group's noise planes once the group is decoded. Each group
        // seeds generators of its own, so filling them once every group is in gives the same planes.
        if (features.Noise != null)
        {
            for (long g = 0; g < numGroups; g++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JxlNoise.PrepareNoiseInput(pipeline, frameDim, frameHeader.Upsampling, g, this.visibleFrameIndex, this.nonvisibleFrameIndex);
            }
        }

        foreach (JxlBitReader section in sections)
        {
            section.Close();
        }

        modularFrameDecoder.FinalizeDecoding(frameHeader, pipeline);

        // PreparePipeline adds the loop-filter stages before the sections are decoded, and they read
        // the EPF sigma image once the groups are in. Here they are built once every section is decoded,
        // so a stage can take what the sections decoded (the quant field, the EPF sharpness) as it is built.
        IJxlNeighbourStage[] loopFilters = JxlLoopFilterStages.Create(new JxlLoopFilterContext(frameHeader.LoopFilter, frameDim, varDct));

        JxlNoiseStages noiseStages = features.Noise != null ? new JxlNoiseStages(features.Noise, cmap, pipeline.NoiseChannel) : null;
        JxlReferenceFrame saved = pipeline.Finish(frameHeader, upsampling.WithLoopFilters(loopFilters), patchesStage, splinesStage, upsampling.AfterPatches, noiseStages, colorStages, blending, isLastOfStill ? sink : null, cancellationToken);

        // FinalizeFrame: the slot changes only once the frame is done, as the frame may blend from it. A DC
        // frame writes its DC slot (libjxl writes it in the pipeline); no stage of the frame reads that slot.
        if (saved != null)
        {
            if (frameHeader.DcLevel != 0)
            {
                this.references.SaveDc(frameHeader.DcLevel - 1, saved);
            }
            else
            {
                this.references.Save(frameHeader.SaveAsReference, saved);
            }
        }

        return frameHeader;
    }

    /// <summary>
    /// Skips the preview frame that starts at byte <paramref name="offset"/>, as libjxl does when the
    /// preview is not wanted (<c>JxlDecoderProcessCodestream</c> in decode.cc, with <c>InitFrame</c> in
    /// dec_frame.cc): reads its header at the preview size and its TOC, counts it as a frame that is not
    /// shown, and steps over its sections. It is never decoded, shown, blended or saved to a reference
    /// slot. Throws <see cref="InvalidImageContentException"/> for an invalid header or a truncated frame.
    /// </summary>
    /// <param name="data">The codestream.</param>
    /// <param name="offset">The first byte of the preview frame.</param>
    /// <returns>The byte after the preview frame's last section, where the first frame starts.</returns>
    public int SkipPreviewFrame(byte[] data, int offset)
    {
        int availIn = data.Length - offset;
        JxlBitReader reader = new(data, offset, availIn);
        JxlFrameHeader frameHeader = new(this.metadata) { NonserializedIsPreview = true };
        JxlBundle.Read(reader, frameHeader);
        JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();

        // InitFrame counts a preview frame as not shown, whatever its header says.
        this.nonvisibleFrameIndex++;

        // libjxl checks the frame size with CheckSizeLimit, which only guards against size_t overflow, and
        // allocates nothing for a preview it skips. Neither does this decoder, so DecodeFrame's pixel cap
        // does not apply.
        long tocEntries = JxlToc.NumTocEntries(frameDim.NumGroups, frameDim.NumDcGroups, frameHeader.Passes.NumPasses);
        JxlGroupOffsets groupOffsets = JxlToc.ReadGroupOffsets(tocEntries, reader);

        reader.EnsureAllReadsWithinBounds("preview frame header");
        long headerBytes = reader.TotalBitsConsumed / 8;
        reader.Close();

        // libjxl advances past SumSectionSizes() and asks for more input when the bytes run out, which
        // djxl reports as a truncated file. This decoder has the whole codestream, so it checks here.
        if (headerBytes + groupOffsets.TotalSize > availIn)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Not enough bytes for the preview frame sections");
        }

        return offset + (int)(headerBytes + groupOffsets.TotalSize);
    }

    /// <summary>
    /// Returns whether the frame is displayed, as decode.cc's <c>is_last_of_still</c> with coalescing: it
    /// is the last frame, or the last of a still (an animation frame with a duration). Every other frame
    /// only feeds later frames through its reference slot.
    /// </summary>
    /// <param name="frameHeader">The frame header.</param>
    /// <returns><see langword="true"/> if the frame is displayed.</returns>
    public static bool IsLastOfStill(JxlFrameHeader frameHeader)
        => frameHeader.IsLast || frameHeader.AnimationFrame.Duration > 0;

    // Internal, as ProcessDcGroup, so that tests can read the state the DC sections fill (the AC
    // metadata, the quant field and the chroma-from-luma maps), which no output shows on its own.
    // cmap is the frame's colour-correlation map: varDct.Cmap for a VarDCT frame, which DecodeGlobalDcInfo
    // fills here, and a default one for a Modular frame.
    internal static void ProcessDcGlobal(JxlBitReader br, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlImageFeatures features, JxlColorCorrelation cmap, JxlDequantMatrices matrices, JxlModularFrameDecoder modularFrameDecoder, JxlVarDctFrameState varDct, CancellationToken cancellationToken)
    {
        DebugGuard.IsTrue(varDct == null || cmap == varDct.Cmap, "A VarDCT frame's map is the one it decodes");

        // libjxl clears the dictionary it keeps across frames when the frame has no patches; here a
        // frame without the flag has no dictionary.
        JxlPatchDictionary patches = features.Patches;
        if (patches != null)
        {
            patches.Decode(br, frameDim.XSizePadded, frameDim.YSizePadded, frameHeader.NonserializedMetadata.M.NumExtraChannels, cancellationToken);

            // The patches run at the coded size, before the colour is upsampled, so the extra channels must
            // be at that size too: upsampled late, with the colour (UpsamplingStages). The check is
            // unconditional on purpose: libjxl's Decode reports uses_extra_channels, but at ef67fde2
            // ProcessDCGlobal never reads it, so this port does not compute it.
            if (frameHeader.Upsampling != 1)
            {
                foreach (uint ecups in frameHeader.ExtraChannelUpsampling)
                {
                    if (ecups != frameHeader.Upsampling)
                    {
                        JxlThrowHelper.ThrowInvalidImageContentException("Cannot use extra channels in patches if color channels are subsampled differently from extra channels");
                    }
                }
            }
        }

        // libjxl clears its splines on every frame; here a frame without the flag has none.
        JxlSplines splines = features.Splines;
        splines?.Decode(br, frameDim.XSize * frameDim.YSize, cancellationToken);
        features.Noise?.Decode(br);
        matrices.DecodeDc(br);
        varDct?.DecodeGlobalDcInfo(br);

        // Splines' draw cache uses the color correlation map: the one the frame decodes on a VarDCT
        // frame, and the default one on a Modular frame.
        if (splines != null)
        {
            splines.InitializeDrawCache(frameDim.XSizeUpsampled, frameDim.YSizeUpsampled, cmap.YtoXRatio(0), cmap.YtoBRatio(0), cancellationToken);
        }

        modularFrameDecoder.DecodeGlobalInfo(br, frameHeader);
    }

    internal static void ProcessDcGroup(long dcGroupId, JxlBitReader br, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlModularFrameDecoder modularFrameDecoder, JxlVarDctFrameState varDct)
    {
        long gx = dcGroupId % frameDim.XSizeDcGroups;
        long gy = dcGroupId / frameDim.XSizeDcGroups;

        // A frame that uses a DC frame has no VarDCT DC of its own: its DC is the DC frame's.
        if (varDct != null && !frameHeader.UsesDcFrame)
        {
            modularFrameDecoder.DecodeVarDctDc(frameHeader, dcGroupId, br, varDct);
        }

        JxlRect mrect = new(gx * frameDim.DcGroupDim, gy * frameDim.DcGroupDim, frameDim.DcGroupDim, frameDim.DcGroupDim);
        modularFrameDecoder.DecodeGroup(frameHeader, mrect, br, 3, 1000, JxlModularStreamId.ModularDc(frameDim, dcGroupId), null, -1);
        if (varDct != null)
        {
            modularFrameDecoder.DecodeAcMetadata(frameHeader, dcGroupId, br, varDct);
        }
    }

    // InitializePassesSharedState: a frame that uses a DC frame takes its DC from the DC slot of its own
    // DC level, which a DC frame of the next level wrote. Returns that slot.
    private JxlReferenceFrame GetDcFrame(JxlFrameHeader frameHeader, JxlFrameDimensions frameDim)
    {
        uint dcLevel = frameHeader.DcLevel;
        if (dcLevel == JxlReferenceFrames.NumDcSlots)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Invalid DC level for kUseDcFrame: {dcLevel}");
        }

        JxlReferenceFrame dcFrame = this.references.GetDc(dcLevel);
        if (dcFrame == null)
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"kUseDcFrame specified for dc_level {dcLevel}, but no frame was decoded with level {dcLevel + 1}");
        }

        // libjxl's check in DecodeGroupImpl (dec_group.cc), moved here: each AC group's blocks must lie inside
        // the DC image, which for such a frame is the DC frame's. The DC frame is the image size divided by 8
        // per level, rounded up, so it covers the blocks of a frame of the image size, but not those of a
        // frame with a larger custom size. A frame that uses a DC frame has no chroma subsampling, so this
        // rejects the frames libjxl rejects. JxlVarDctGroup's copy of the check cannot do it: it compares
        // with the frame's own DC planes, which are copied at the frame's block size.
        if (frameHeader.Encoding == JxlFrameEncoding.VarDct && (dcFrame.Width < frameDim.XSizeBlocks || dcFrame.Height < frameDim.YSizeBlocks))
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Frame dimensions are too big for the image.");
        }

        return dcFrame;
    }

    private static void ProcessSections(JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlBitReader[] sections, JxlImageFeatures features, JxlColorCorrelation cmap, JxlDequantMatrices matrices, JxlModularFrameDecoder modularFrameDecoder, JxlVarDctFrameState varDct, JxlRenderPipeline pipeline, CancellationToken cancellationToken)
    {
        int numPasses = (int)frameHeader.Passes.NumPasses;
        long numGroups = frameDim.NumGroups;
        long numDcGroups = frameDim.NumDcGroups;
        bool singleSection = numGroups == 1 && numPasses == 1;

        // Section ids: 0 is DC global, then the DC groups, AC global, and the AC groups of each pass.
        JxlBitReader DcGlobal() => sections[0];
        JxlBitReader DcGroup(long i) => singleSection ? sections[0] : sections[1 + i];
        JxlBitReader AcGlobal() => singleSection ? sections[0] : sections[1 + numDcGroups];
        JxlBitReader AcGroup(long g, int p) => singleSection ? sections[0] : sections[JxlToc.AcGroupIndex(p, g, numGroups, numDcGroups)];

        ProcessDcGlobal(DcGlobal(), frameHeader, frameDim, features, cmap, matrices, modularFrameDecoder, varDct, cancellationToken);

        for (long i = 0; i < numDcGroups; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessDcGroup(i, DcGroup(i), frameHeader, frameDim, modularFrameDecoder, varDct);
        }

        // FinalizeDC: adaptive DC smoothing must happen between all the DC groups and the AC groups.
        varDct?.FinalizeDc(frameHeader);

        // AllocateOutput.
        modularFrameDecoder.MaybeDropFullImage();
        varDct?.InitForAc(numPasses);

        // ProcessACGlobal reads only VarDCT data: nothing for a Modular frame.
        varDct?.ProcessAcGlobal(AcGlobal(), numPasses, modularFrameDecoder);

        for (long g = 0; g < numGroups; g++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessAcGroup(g, numPasses, AcGroup, frameHeader, frameDim, modularFrameDecoder, varDct, pipeline);
        }
    }

    private static void ProcessAcGroup(long acGroupId, int numPasses, Func<long, int, JxlBitReader> readers, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlModularFrameDecoder modularFrameDecoder, JxlVarDctFrameState varDct, JxlRenderPipeline pipeline)
    {
        long groupDim = frameDim.GroupDimension;
        long gx = acGroupId % frameDim.XSizeGroups;
        long gy = acGroupId / frameDim.XSizeGroups;
        if (varDct != null)
        {
            JxlBitReader[] passReaders = varDct.GroupCache.Readers;
            for (int i = 0; i < numPasses; i++)
            {
                passReaders[i] = readers(acGroupId, i);
            }

            JxlVarDctGroup.DecodeGroup(frameHeader, passReaders, numPasses, acGroupId, varDct, pipeline);
        }

        // don't limit to image dimensions here (is done in DecodeGroup)
        JxlRect mrect = new(gx * groupDim, gy * groupDim, groupDim, groupDim);
        for (int i = 0; i < numPasses; i++)
        {
            frameHeader.Passes.GetDownsamplingBracket(i, out int minShift, out int maxShift);
            modularFrameDecoder.DecodeGroup(frameHeader, mrect, readers(acGroupId, i), minShift, maxShift, JxlModularStreamId.ModularAc(frameDim, acGroupId, i), pipeline, acGroupId);
        }
    }
}
