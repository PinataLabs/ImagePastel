// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_frame.h and lib/jxl/dec_frame.cc (DecodeFrame, FrameDecoder: InitFrame, ProcessDCGlobal,
// ProcessDCGroup, ProcessACGroup, ProcessSections, FinalizeFrame), restricted to Modular frames, and
// DequantMatrices::DecodeDC from lib/jxl/quant_weights.cc.
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

    public JxlFrameDecoder(Configuration configuration, JxlCodecMetadata metadata)
    {
        this.configuration = configuration;
        this.metadata = metadata;
    }

    /// <summary>
    /// Decodes the frame that starts at byte <paramref name="offset"/> and hands it to <paramref name="sink"/>.
    /// Throws <see cref="InvalidImageContentException"/> for invalid data, <see cref="NotSupportedException"/>
    /// for features of later slices, and <see cref="InvalidMemoryOperationException"/> for frames over the
    /// pixel cap.
    /// </summary>
    /// <param name="data">The codestream.</param>
    /// <param name="offset">The first byte of the frame.</param>
    /// <param name="sink">The sink.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The frame header.</returns>
    public JxlFrameHeader DecodeFrame(byte[] data, int offset, IJxlRenderSink sink, CancellationToken cancellationToken)
    {
        int availIn = data.Length - offset;
        JxlBitReader reader = new(data, offset, availIn);
        JxlFrameHeader frameHeader = new(this.metadata);
        JxlBundle.Read(reader, frameHeader);
        JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();
        CheckSupported(frameHeader, this.metadata);

        // The pixel cap is checked before anything is allocated for the frame.
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

        using JxlModularFrameDecoder modularFrameDecoder = new(this.configuration.MemoryAllocator, frameDim);
        using JxlRenderPipeline pipeline = new(this.configuration.MemoryAllocator, frameDim, 3 + this.metadata.M.NumExtraChannels);

        ProcessSections(frameHeader, frameDim, sections, modularFrameDecoder, pipeline, cancellationToken);

        foreach (JxlBitReader section in sections)
        {
            section.Close();
        }

        modularFrameDecoder.FinalizeDecoding(frameHeader, pipeline);
        pipeline.Finish(sink);
        return frameHeader;
    }

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

        foreach (JxlExtraChannelInfo eci in metadata.M.ExtraChannelInfo)
        {
            if (eci.Type != JxlExtraChannelType.Alpha)
            {
                JxlThrowHelper.ThrowNotSupportedException($"The {eci.Type} extra channel");
            }
        }

        if (frameHeader.FrameType != JxlFrameType.Regular || !frameHeader.IsLast)
        {
            JxlThrowHelper.ThrowNotSupportedException("More than one frame");
        }

        if (frameHeader.Flags != JxlFrameFlags.None)
        {
            JxlThrowHelper.ThrowNotSupportedException("Patches, splines, noise or DC frames");
        }

        if (frameHeader.Upsampling != 1 || frameHeader.ExtraChannelUpsampling.Exists(u => u != 1))
        {
            JxlThrowHelper.ThrowNotSupportedException("Upsampling");
        }

        if (frameHeader.Passes.NumPasses != 1)
        {
            JxlThrowHelper.ThrowNotSupportedException("Progressive passes");
        }

        if (frameHeader.CustomSizeOrOrigin || frameHeader.BlendingInfo.Mode != JxlBlendMode.Replace)
        {
            JxlThrowHelper.ThrowNotSupportedException("Cropped or blended frames");
        }

        if (frameHeader.LoopFilter.Gab || frameHeader.LoopFilter.EpfIters > 0)
        {
            JxlThrowHelper.ThrowNotSupportedException("Loop filters");
        }
    }

    private static void ProcessSections(JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlBitReader[] sections, JxlModularFrameDecoder modularFrameDecoder, JxlRenderPipeline pipeline, CancellationToken cancellationToken)
    {
        int numPasses = (int)frameHeader.Passes.NumPasses;
        long numGroups = frameDim.NumGroups;
        long numDcGroups = frameDim.NumDcGroups;
        bool singleSection = numGroups == 1 && numPasses == 1;

        // Section ids: 0 is DC global, then the DC groups, AC global, and the AC groups of each pass.
        JxlBitReader DcGlobal() => sections[0];
        JxlBitReader DcGroup(long i) => singleSection ? sections[0] : sections[1 + i];
        JxlBitReader AcGroup(long g, int p) => singleSection ? sections[0] : sections[JxlToc.AcGroupIndex(p, g, numGroups, numDcGroups)];

        ProcessDcGlobal(DcGlobal(), frameHeader, modularFrameDecoder);

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

    private static void ProcessDcGlobal(JxlBitReader br, JxlFrameHeader frameHeader, JxlModularFrameDecoder modularFrameDecoder)
    {
        // Patches, splines and noise are rejected by CheckSupported.
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
