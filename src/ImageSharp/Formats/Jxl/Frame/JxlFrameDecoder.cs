// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_frame.h and lib/jxl/dec_frame.cc (DecodeFrame, FrameDecoder: InitFrame, ProcessDCGlobal,
// ProcessDCGroup, FinalizeDC, AllocateOutput, ProcessACGlobal, ProcessACGroup, ProcessSections, FinalizeFrame), the
// per-frame steps of the frame loop in JxlDecoderProcessCodestream (lib/jxl/decode.cc), the
// pipeline set-up of PassesDecoderState::PreparePipeline (lib/jxl/dec_cache.cc), and the frame counters
// that seed the noise (visible_frame_index and nonvisible_frame_index in lib/jxl/dec_cache.h).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
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
    /// Decodes the frame that starts at byte <paramref name="offset"/>: blends it onto its reference
    /// slots, saves it to its own slot if it can be referenced, and hands it to <paramref name="sink"/>
    /// if it is displayed. Throws <see cref="InvalidImageContentException"/> for invalid data,
    /// <see cref="NotSupportedException"/> for features of later slices, and
    /// <see cref="InvalidMemoryOperationException"/> for frames over the pixel cap.
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
        JxlFrameHeader frameHeader = new(this.metadata);
        JxlBundle.Read(reader, frameHeader);
        JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();
        CheckSupported(frameHeader);

        // InitFrame counts the shown frames, and the frames since the last shown one. libjxl also counts a
        // preview frame as not shown; JxlDecoderCore rejects images with a preview.
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

        long numPasses = frameHeader.Passes.NumPasses;
        long numGroups = frameDim.NumGroups;
        long tocEntries = JxlToc.NumTocEntries(numGroups, frameDim.NumDcGroups, numPasses);
        uint[] sizes = JxlToc.ReadToc(tocEntries, reader);

        reader.EnsureAllReadsWithinBounds("frame header");
        long headerBytes = reader.TotalBitsConsumed / 8;
        reader.Close();

        // One reader per section.
        JxlBitReader[] sections = new JxlBitReader[tocEntries];
        long pos = headerBytes;
        for (long i = 0; i < tocEntries; i++)
        {
            if (pos + sizes[i] > availIn)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Not enough bytes for the frame sections");
            }

            sections[i] = new JxlBitReader(data, offset + (int)pos, (int)sizes[i]);
            pos += sizes[i];
        }

        frameEnd = offset + (int)pos;

        bool isLastOfStill = IsLastOfStill(frameHeader);

        // PreparePipeline: the blending stage checks its reference slots before any section is decoded.
        // The patch dictionary is read with the DC global section, and the patches stage uses it then.
        // The patches run at the coded size, before the upsampling; the blending at the upsampled size.
        JxlPatchDictionary patches = (frameHeader.Flags & JxlFrameFlags.Patches) != 0 ? new JxlPatchDictionary(this.references) : null;
        JxlNoiseParams noise = (frameHeader.Flags & JxlFrameFlags.Noise) != 0 ? new JxlNoiseParams() : null;
        using JxlPatchesStage patchesStage = patches != null
            ? new JxlPatchesStage(this.configuration.MemoryAllocator, patches, this.metadata.M, (int)frameDim.XSize)
            : null;
        IJxlColorStage[] colorStages = JxlColorStages.Create(frameHeader, sink.DisplayEncoded);
        using JxlBlendingStage blending = JxlBlending.NeedsBlending(frameHeader)
            ? new JxlBlendingStage(this.configuration.MemoryAllocator, frameHeader, this.references, (int)frameDim.XSizeUpsampled)
            : null;

        // InitFrame resets the dequantization matrices to their defaults for each frame.
        JxlDequantMatrices matrices = new();
        using JxlModularFrameDecoder modularFrameDecoder = new(this.configuration.MemoryAllocator, frameDim, matrices);

        // The upsampling stages give the channel shifts, so they are built with the pipeline; the loop
        // filters come once the sections are decoded.
        JxlUpsamplingStages upsampling = JxlUpsamplingStages.Create(frameHeader);
        int numColorPlanes = frameHeader.NumDecodedColorPlanes;
        JxlChannelShift[] channelShifts = JxlRenderPipeline.ChannelShifts(numColorPlanes, frameHeader.ExtraChannelUpsampling.Count, upsampling.Chroma, upsampling.BeforePatches, upsampling.AfterPatches);
        using JxlRenderPipeline pipeline = new(this.configuration.MemoryAllocator, frameDim, numColorPlanes, channelShifts, withNoisePlanes: noise != null);
        using JxlVarDctFrameState varDct = frameHeader.Encoding == JxlFrameEncoding.VarDct ? new JxlVarDctFrameState(this.configuration.MemoryAllocator, frameHeader, frameDim, matrices) : null;

        ProcessSections(frameHeader, frameDim, sections, patches, noise, matrices, modularFrameDecoder, varDct, pipeline, cancellationToken);

        // libjxl's ProcessACGroup fills each group's noise planes once the group is decoded. Each group
        // seeds generators of its own, so filling them once every group is in gives the same planes.
        if (noise != null)
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

        // The add-noise stage takes the base ratios of the colour-correlation map, which a VarDCT frame
        // decodes and a Modular frame has at their defaults: libjxl makes a new map for each frame.
        JxlNoiseStages noiseStages = noise != null
            ? new JxlNoiseStages(noise, this.metadata.M, varDct?.Cmap ?? new JxlColorCorrelation(frameDim.XSize, frameDim.YSize))
            : null;
        JxlReferenceFrame saved = pipeline.Finish(frameHeader, upsampling.WithLoopFilters(loopFilters), patchesStage, upsampling.AfterPatches, colorStages, blending, isLastOfStill ? sink : null, cancellationToken, noiseStages);

        // FinalizeFrame: the slot changes only once the frame is done, as the frame may blend from it.
        if (saved != null)
        {
            this.references.Save(frameHeader.SaveAsReference, saved);
        }

        return frameHeader;
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
    internal static void ProcessDcGlobal(JxlBitReader br, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlPatchDictionary patches, JxlDequantMatrices matrices, JxlModularFrameDecoder modularFrameDecoder, JxlVarDctFrameState varDct, CancellationToken cancellationToken, JxlNoiseParams noise = null)
    {
        // libjxl clears the dictionary it keeps across frames when the frame has no patches; here a
        // frame without the flag has no dictionary, so the caller passes null.
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

        // Splines are rejected by CheckSupported.
        noise?.Decode(br);
        matrices.DecodeDc(br);
        varDct?.DecodeGlobalDcInfo(br);
        modularFrameDecoder.DecodeGlobalInfo(br, frameHeader);
    }

    internal static void ProcessDcGroup(long dcGroupId, JxlBitReader br, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlModularFrameDecoder modularFrameDecoder, JxlVarDctFrameState varDct)
    {
        long gx = dcGroupId % frameDim.XSizeDcGroups;
        long gy = dcGroupId / frameDim.XSizeDcGroups;

        // A frame that uses a DC frame (D13) has no VarDCT DC of its own; CheckSupported rejects it.
        if (varDct != null)
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

    // Rejects everything a later slice of the port decodes, so such files fail cleanly instead of
    // decoding badly. Each feature has a check of its own, so a slice can lift one at a time.
    private static void CheckSupported(JxlFrameHeader frameHeader)
    {
        // A departure from libjxl: a grey image's YCbCr frame keeps three planes until after its colour
        // transform (JxlFrameHeader.DropsToGreyAfterColorTransform), but grey reference slots and patches
        // hold one. So such a frame cannot be saved before its colour transform, nor take patches, which
        // libjxl allows with three planes. JPEG transcodes never write such a frame.
        if (frameHeader.DropsToGreyAfterColorTransform)
        {
            if (frameHeader.CanBeReferenced && frameHeader.SaveBeforeColorTransform)
            {
                JxlThrowHelper.ThrowNotSupportedException("A YCbCr frame of a grey image saved before its colour transform");
            }

            if ((frameHeader.Flags & JxlFrameFlags.Patches) != 0)
            {
                JxlThrowHelper.ThrowNotSupportedException("Patches on a YCbCr frame of a grey image");
            }
        }

        if (frameHeader.FrameType == JxlFrameType.Dc)
        {
            JxlThrowHelper.ThrowNotSupportedException("Splines, noise or DC frames (a DC frame)");
        }

        JxlFrameFlags flags = frameHeader.Flags;
        if ((flags & JxlFrameFlags.Splines) != 0)
        {
            JxlThrowHelper.ThrowNotSupportedException("Splines, noise or DC frames (splines)");
        }

        if ((flags & JxlFrameFlags.UseDcFrame) != 0)
        {
            JxlThrowHelper.ThrowNotSupportedException("Splines, noise or DC frames (the frame uses a DC frame)");
        }

        // libjxl ignores the flags it does not define. This port has always rejected them, and still does.
        const JxlFrameFlags knownFlags = JxlFrameFlags.Noise | JxlFrameFlags.Patches | JxlFrameFlags.Splines | JxlFrameFlags.UseDcFrame | JxlFrameFlags.SkipAdaptiveDcSmoothing;
        if ((flags & ~knownFlags) != 0)
        {
            JxlThrowHelper.ThrowNotSupportedException($"Unknown frame flags 0x{(ulong)(flags & ~knownFlags):X}");
        }

        if (frameHeader.Passes.NumPasses != 1)
        {
            JxlThrowHelper.ThrowNotSupportedException("Progressive passes");
        }
    }

    private static void ProcessSections(JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlBitReader[] sections, JxlPatchDictionary patches, JxlNoiseParams noise, JxlDequantMatrices matrices, JxlModularFrameDecoder modularFrameDecoder, JxlVarDctFrameState varDct, JxlRenderPipeline pipeline, CancellationToken cancellationToken)
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

        ProcessDcGlobal(DcGlobal(), frameHeader, frameDim, patches, matrices, modularFrameDecoder, varDct, cancellationToken, noise);

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
