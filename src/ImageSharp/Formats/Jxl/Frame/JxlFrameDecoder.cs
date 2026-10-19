// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_frame.h and lib/jxl/dec_frame.cc (DecodeFrame, FrameDecoder: InitFrame, ProcessDCGlobal,
// ProcessDCGroup, FinalizeDC, AllocateOutput, ProcessACGlobal, ProcessACGroup, ProcessSections, FinalizeFrame), and the
// per-frame steps of the frame loop in JxlDecoderProcessCodestream (lib/jxl/decode.cc).
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
        CheckSupported(frameHeader, this.metadata);

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
        JxlPatchDictionary patches = (frameHeader.Flags & JxlFrameFlags.Patches) != 0 ? new JxlPatchDictionary(this.references) : null;
        using JxlPatchesStage patchesStage = patches != null
            ? new JxlPatchesStage(this.configuration.MemoryAllocator, patches, this.metadata.M, (int)frameDim.XSizeUpsampled)
            : null;
        IJxlColorStage[] colorStages = JxlColorStages.Create(frameHeader);
        using JxlBlendingStage blending = JxlBlending.NeedsBlending(frameHeader)
            ? new JxlBlendingStage(this.configuration.MemoryAllocator, frameHeader, this.references, (int)frameDim.XSizeUpsampled)
            : null;

        // InitFrame resets the dequantization matrices to their defaults for each frame.
        JxlDequantMatrices matrices = new();
        using JxlModularFrameDecoder modularFrameDecoder = new(this.configuration.MemoryAllocator, frameDim, matrices);
        using JxlRenderPipeline pipeline = new(this.configuration.MemoryAllocator, frameDim, this.metadata.M.NumColorPlanes, this.metadata.M.NumExtraChannels);
        JxlVarDctFrameState varDct = frameHeader.Encoding == JxlFrameEncoding.VarDct ? new JxlVarDctFrameState(frameHeader, frameDim, matrices) : null;

        ProcessSections(frameHeader, frameDim, sections, patches, matrices, modularFrameDecoder, varDct, pipeline, cancellationToken);

        foreach (JxlBitReader section in sections)
        {
            section.Close();
        }

        modularFrameDecoder.FinalizeDecoding(frameHeader, pipeline);
        JxlReferenceFrame saved = pipeline.Finish(frameHeader, patchesStage, colorStages, blending, isLastOfStill ? sink : null, cancellationToken);

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

    // Rejects everything a later slice of the port decodes, so such files fail cleanly instead of
    // decoding badly. Each feature has a check of its own, so a slice can lift one at a time.
    private static void CheckSupported(JxlFrameHeader frameHeader, JxlCodecMetadata metadata)
    {
        if (frameHeader.Encoding == JxlFrameEncoding.VarDct && frameHeader.ColorTransform == JxlColorTransform.Xyb)
        {
            JxlThrowHelper.ThrowNotSupportedException("VarDCT with the XYB colour transform");
        }

        if (frameHeader.ColorTransform == JxlColorTransform.YCbCr)
        {
            JxlThrowHelper.ThrowNotSupportedException("The YCbCr colour transform");
        }

        if (frameHeader.FrameType == JxlFrameType.Dc)
        {
            JxlThrowHelper.ThrowNotSupportedException("Splines, noise or DC frames (a DC frame)");
        }

        JxlFrameFlags flags = frameHeader.Flags;
        if ((flags & JxlFrameFlags.Noise) != 0)
        {
            JxlThrowHelper.ThrowNotSupportedException("Splines, noise or DC frames (noise)");
        }

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

        // An extra channel with a dim_shift has an upsampling of at least 2 here.
        if (frameHeader.Upsampling != 1 || frameHeader.ExtraChannelUpsampling.Exists(u => u != 1))
        {
            JxlThrowHelper.ThrowNotSupportedException("Upsampling");
        }

        if (frameHeader.Passes.NumPasses != 1)
        {
            JxlThrowHelper.ThrowNotSupportedException("Progressive passes");
        }

        if (frameHeader.LoopFilter.Gab || frameHeader.LoopFilter.EpfIters > 0)
        {
            JxlThrowHelper.ThrowNotSupportedException("Loop filters");
        }
    }

    private static void ProcessSections(JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlBitReader[] sections, JxlPatchDictionary patches, JxlDequantMatrices matrices, JxlModularFrameDecoder modularFrameDecoder, JxlVarDctFrameState varDct, JxlRenderPipeline pipeline, CancellationToken cancellationToken)
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

        ProcessDcGlobal(DcGlobal(), frameHeader, frameDim, patches, matrices, modularFrameDecoder, varDct, cancellationToken);

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
        varDct?.ProcessAcGlobal(AcGlobal(), numPasses);

        for (long g = 0; g < numGroups; g++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessAcGroup(g, numPasses, AcGroup, frameHeader, frameDim, modularFrameDecoder, varDct, pipeline);
        }
    }

    private static void ProcessDcGlobal(JxlBitReader br, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlPatchDictionary patches, JxlDequantMatrices matrices, JxlModularFrameDecoder modularFrameDecoder, JxlVarDctFrameState varDct, CancellationToken cancellationToken)
    {
        // libjxl clears the dictionary it keeps across frames when the frame has no patches; here a
        // frame without the flag has no dictionary, so the caller passes null.
        if (patches != null)
        {
            patches.Decode(br, frameDim.XSizePadded, frameDim.YSizePadded, frameHeader.NonserializedMetadata.M.NumExtraChannels, cancellationToken);

            // Unreachable until upsampling arrives (D10): CheckSupported rejects an upsampled frame first.
            // The check is unconditional on purpose: libjxl's Decode reports uses_extra_channels, but at
            // ef67fde2 ProcessDCGlobal never reads it, so this port does not compute it.
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

        // Splines and noise are rejected by CheckSupported.
        matrices.DecodeDc(br);
        varDct?.DecodeGlobalDcInfo(br);
        modularFrameDecoder.DecodeGlobalInfo(br, frameHeader);
    }

    private static void ProcessDcGroup(long dcGroupId, JxlBitReader br, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlModularFrameDecoder modularFrameDecoder, JxlVarDctFrameState varDct)
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

    private static void ProcessAcGroup(long acGroupId, int numPasses, Func<long, int, JxlBitReader> readers, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlModularFrameDecoder modularFrameDecoder, JxlVarDctFrameState varDct, JxlRenderPipeline pipeline)
    {
        long groupDim = frameDim.GroupDimension;
        long gx = acGroupId % frameDim.XSizeGroups;
        long gy = acGroupId / frameDim.XSizeGroups;
        if (varDct != null)
        {
            JxlBitReader[] passReaders = new JxlBitReader[numPasses];
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
