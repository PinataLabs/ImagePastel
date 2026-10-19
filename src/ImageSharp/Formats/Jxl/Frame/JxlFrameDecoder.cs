// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_frame.h and lib/jxl/dec_frame.cc (DecodeFrame, FrameDecoder: InitFrame, ProcessDCGlobal,
// ProcessDCGroup, ProcessACGroup, ProcessSections, FinalizeFrame), restricted to Modular frames,
// DequantMatrices::DecodeDC from lib/jxl/quant_weights.cc, and the per-frame steps of the frame loop
// in JxlDecoderProcessCodestream (lib/jxl/decode.cc).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
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

    private const float AlmostZero = 1e-8f;

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
        using JxlBlendingStage blending = JxlBlending.NeedsBlending(frameHeader)
            ? new JxlBlendingStage(this.configuration.MemoryAllocator, frameHeader, this.references, (int)frameDim.XSizeUpsampled)
            : null;
        using JxlModularFrameDecoder modularFrameDecoder = new(this.configuration.MemoryAllocator, frameDim);
        int numChannels = this.metadata.M.NumColorPlanes + this.metadata.M.NumExtraChannels;
        using JxlRenderPipeline pipeline = new(this.configuration.MemoryAllocator, frameDim, numChannels);

        ProcessSections(frameHeader, frameDim, sections, patches, modularFrameDecoder, pipeline, cancellationToken);

        foreach (JxlBitReader section in sections)
        {
            section.Close();
        }

        modularFrameDecoder.FinalizeDecoding(frameHeader, pipeline);
        JxlReferenceFrame saved = pipeline.Finish(frameHeader, patchesStage, blending, isLastOfStill ? sink : null);

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
    // decoding badly.
    private static void CheckSupported(JxlFrameHeader frameHeader, JxlCodecMetadata metadata)
    {
        if (frameHeader.Encoding != JxlFrameEncoding.Modular)
        {
            JxlThrowHelper.ThrowNotSupportedException("VarDCT");
        }

        if (frameHeader.ColorTransform != JxlColorTransform.None)
        {
            JxlThrowHelper.ThrowNotSupportedException($"The {frameHeader.ColorTransform} colour transform");
        }

        if (frameHeader.FrameType == JxlFrameType.Dc || (frameHeader.Flags & ~JxlFrameFlags.Patches) != JxlFrameFlags.None)
        {
            JxlThrowHelper.ThrowNotSupportedException("Splines, noise or DC frames");
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

    private static void ProcessSections(JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlBitReader[] sections, JxlPatchDictionary patches, JxlModularFrameDecoder modularFrameDecoder, JxlRenderPipeline pipeline, CancellationToken cancellationToken)
    {
        int numPasses = (int)frameHeader.Passes.NumPasses;
        long numGroups = frameDim.NumGroups;
        long numDcGroups = frameDim.NumDcGroups;
        bool singleSection = numGroups == 1 && numPasses == 1;

        // Section ids: 0 is DC global, then the DC groups, AC global, and the AC groups of each pass.
        JxlBitReader DcGlobal() => sections[0];
        JxlBitReader DcGroup(long i) => singleSection ? sections[0] : sections[1 + i];
        JxlBitReader AcGroup(long g, int p) => singleSection ? sections[0] : sections[JxlToc.AcGroupIndex(p, g, numGroups, numDcGroups)];

        ProcessDcGlobal(DcGlobal(), frameHeader, frameDim, patches, modularFrameDecoder);

        for (long i = 0; i < numDcGroups; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessDcGroup(i, DcGroup(i), frameHeader, frameDim, modularFrameDecoder);
        }

        // AllocateOutput.
        modularFrameDecoder.MaybeDropFullImage();

        // ProcessACGlobal reads only VarDCT data: nothing for a Modular frame.
        for (long g = 0; g < numGroups; g++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessAcGroup(g, numPasses, AcGroup, frameHeader, frameDim, modularFrameDecoder, pipeline);
        }
    }

    private static void ProcessDcGlobal(JxlBitReader br, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlPatchDictionary patches, JxlModularFrameDecoder modularFrameDecoder)
    {
        // libjxl clears the dictionary it keeps across frames when the frame has no patches; here a
        // frame without the flag has no dictionary.
        if ((frameHeader.Flags & JxlFrameFlags.Patches) != 0)
        {
            patches.Decode(br, frameDim.XSizePadded, frameDim.YSizePadded, frameHeader.NonserializedMetadata.M.NumExtraChannels);

            // Unreachable until upsampling arrives (D10): CheckSupported rejects an upsampled frame first.
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
        DecodeDcQuant(br);
        modularFrameDecoder.DecodeGlobalInfo(br, frameHeader);
    }

    private static void ProcessDcGroup(long dcGroupId, JxlBitReader br, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlModularFrameDecoder modularFrameDecoder)
    {
        long gx = dcGroupId % frameDim.XSizeDcGroups;
        long gy = dcGroupId / frameDim.XSizeDcGroups;
        JxlRect mrect = new(gx * frameDim.DcGroupDim, gy * frameDim.DcGroupDim, frameDim.DcGroupDim, frameDim.DcGroupDim);
        modularFrameDecoder.DecodeGroup(frameHeader, mrect, br, 3, 1000, JxlModularStreamId.ModularDc(frameDim, dcGroupId), null, -1);
    }

    private static void ProcessAcGroup(long acGroupId, int numPasses, Func<long, int, JxlBitReader> readers, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlModularFrameDecoder modularFrameDecoder, JxlRenderPipeline pipeline)
    {
        long groupDim = frameDim.GroupDimension;
        long gx = acGroupId % frameDim.XSizeGroups;
        long gy = acGroupId / frameDim.XSizeGroups;

        // don't limit to image dimensions here (is done in DecodeGroup)
        JxlRect mrect = new(gx * groupDim, gy * groupDim, groupDim, groupDim);
        for (int i = 0; i < numPasses; i++)
        {
            frameHeader.Passes.GetDownsamplingBracket(i, out int minShift, out int maxShift);
            modularFrameDecoder.DecodeGroup(frameHeader, mrect, readers(acGroupId, i), minShift, maxShift, JxlModularStreamId.ModularAc(frameDim, acGroupId, i), pipeline, acGroupId);
        }
    }

    // DequantMatrices::DecodeDC: the DC quantization factors, read in every DC global section. Only
    // VarDCT and XYB use them, so they are validated and dropped here.
    private static void DecodeDcQuant(JxlBitReader br)
    {
        bool allDefault = br.ReadBits(1) != 0;
        br.EnsureAllReadsWithinBounds("DecodeDC");
        if (!allDefault)
        {
            for (int c = 0; c < 3; c++)
            {
                if (!JxlFieldCoders.TryReadF16(br, out float dcQuant))
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("F16 infinity or NaN are not supported");
                }

                dcQuant *= 1.0f / 128.0f;

                // Negative values and nearly zero are invalid values.
                if (dcQuant < AlmostZero)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid dc_quant: coefficient is too small.");
                }
            }
        }
    }
}
