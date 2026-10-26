// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_frame.cc (GetGroupSizeShift, MakeFrameHeader, LoopFilterFromParams, EncodeGroups,
// EncodeFrameOneShot), lib/jxl/enc_fields.cc (WriteFrameHeader) and
// lib/jxl/enc_quant_weights.cc (DequantMatricesEncodeDC).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Formats.Jxl.Frame;

/// <summary>
/// Encodes one lossless Modular frame, as libjxl's <c>EncodeFrameOneShot</c> does at efforts 3, 5
/// and 7 (Fastest, Default and Thorough): the frame header, the TOC, and the sections.
/// </summary>
/// <remarks>
/// Departure (ADR 0001): for a frame of more than 8 groups, libjxl's lossless encoder takes its
/// streaming path from effort 2 up (<c>EncodeFrameStreaming</c>, chosen by <c>CanDoStreamingEncoding</c>), which
/// codes each LF group with its own tree and histograms. This port always takes the one-shot path,
/// which libjxl takes for 8 groups or fewer. The output is valid either way.
/// </remarks>
internal static partial class JxlFrameEncoder
{
    /// <summary>Encodes <paramref name="image"/> as the only frame of a still image.</summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="metadata">The codestream headers.</param>
    /// <param name="image">The image.</param>
    /// <param name="speedTier">The speed tier, which the effort sets.</param>
    /// <param name="allocator">The allocator.</param>
    /// <param name="writer">The writer, at a byte boundary.</param>
    public static void EncodeFrame<TPixel>(JxlCodecMetadata metadata, Image<TPixel> image, JxlSpeedTier speedTier, MemoryAllocator allocator, JxlBitWriter writer)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        JxlFrameHeader frameHeader = MakeFrameHeader(image.Width, image.Height, metadata);
        JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();
        using JxlModularFrameEncoder encModular = new(frameHeader, speedTier, allocator);
        List<JxlBitWriter> groupCodes = ComputeEncodingData(frameHeader, frameDim, metadata, image, encModular);

        WriteFrameHeader(frameHeader, writer);
        WriteSections(groupCodes, writer);
    }

    // The TOC, then the sections.
    private static void WriteSections(List<JxlBitWriter> groupCodes, JxlBitWriter writer)
    {
        // PermuteGroups returns no permutation: libjxl's centerfirst is off by default.
        List<uint> groupSizes = new(groupCodes.Count);
        foreach (JxlBitWriter bw in groupCodes)
        {
            DebugGuard.IsTrue(bw.BitsWritten % 8 == 0, "Sections are byte-aligned");
            groupSizes.Add((uint)(bw.BitsWritten / 8));
        }

        JxlTocWriter.WriteTocPermutation(writer);
        JxlTocWriter.WriteTocSizes(groupSizes, writer);

        writer.AppendByteAligned(groupCodes);
    }

    /// <summary>
    /// Returns libjxl's group size shift for a Modular frame: no group larger than the image, and
    /// groups no smaller than needed to fill one well.
    /// </summary>
    /// <param name="xsize">The frame width.</param>
    /// <param name="ysize">The frame height.</param>
    /// <returns>The shift: the groups are 128 &lt;&lt; shift pixels square.</returns>
    public static uint GetGroupSizeShift(long xsize, long ysize)
    {
        // libjxl takes cparams.modular_group_size_shift when set, and uses the smallest groups for
        // the faster decoding tiers; neither is ported, so the encoder always picks.
        // No point using groups when only one group is full and the others are less than half full:
        // multithreading will not really help much, while compression does suffer; but no reason to
        // have group larger than image.
        if (xsize <= 128 && ysize <= 128)
        {
            return 0;
        }

        if (xsize <= 256 && ysize <= 256)
        {
            return 1;
        }

        if (xsize <= 400 && ysize <= 400)
        {
            return 2;
        }

        return 1;
    }

    // libjxl's MakeFrameHeader with LoopFilterFromParams and FrameFlagsFromParams, for a lossless
    // Modular frame that is the last frame of a still image.
    private static JxlFrameHeader MakeFrameHeader(long xsize, long ysize, JxlCodecMetadata metadata)
    {
        JxlFrameHeader frameHeader = new(metadata)
        {
            IsLast = true,
            FrameType = JxlFrameType.Regular,
            Encoding = JxlFrameEncoding.Modular,
            GroupSizeShift = GetGroupSizeShift(xsize, ysize),
            ColorTransform = JxlColorTransform.None,

            // No noise, patches, splines or DC frame at distance 0.
            Flags = JxlFrameFlags.None,

            // The frame covers the image, so there is no custom size or origin.
            FrameSizeX = (uint)xsize,
            FrameSizeY = (uint)ysize,
            CustomSizeOrOrigin = false,
            Upsampling = 1,
        };

        // One pass: libjxl's progressive splitter has a single pass for a non-progressive frame.
        frameHeader.Passes.NumPasses = 1;

        // Gaborish is VarDCT-only by default, and EPF is off for Modular frames. The loop filter's
        // defaults are gab on and two EPF iterations, so both are set.
        frameHeader.LoopFilter.Gab = false;
        frameHeader.LoopFilter.EpfIters = 0;

        frameHeader.ExtraChannelUpsampling.Clear();
        for (int i = 0; i < metadata.M.NumExtraChannels; i++)
        {
            frameHeader.ExtraChannelUpsampling.Add(1);
        }

        return frameHeader;
    }

    private static void WriteFrameHeader(JxlFrameHeader frame, JxlBitWriter writer) => JxlBundle.Write(frame, writer);

    // libjxl's frame-level ComputeEncodingData for a Modular frame: fill the Modular images, build the
    // tree and the tokens, then write the sections.
    private static List<JxlBitWriter> ComputeEncodingData<TPixel>(JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlCodecMetadata metadata, Image<TPixel> image, JxlModularFrameEncoder encModular)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        encModular.ComputeEncodingData(frameHeader, metadata.M, image, doColor: true);
        encModular.ComputeTree();
        encModular.ComputeTokens();
        return EncodeGroups(frameHeader, frameDim, encModular);
    }

    // Writes each section into its own writer. With one group and one pass, every section goes into
    // the single TOC entry.
    private static List<JxlBitWriter> EncodeGroups(JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlModularFrameEncoder encModular)
    {
        long numGroups = frameDim.NumGroups;
        long numPasses = frameHeader.Passes.NumPasses;
        bool isSmallImage = numGroups == 1 && numPasses == 1;
        long numTocEntries = JxlToc.NumTocEntries(numGroups, frameDim.NumDcGroups, numPasses);
        List<JxlBitWriter> groupCodes = new((int)numTocEntries);
        for (long i = 0; i < numTocEntries; ++i)
        {
            groupCodes.Add(new JxlBitWriter());
        }

        JxlBitWriter GetOutput(long index) => groupCodes[isSmallImage ? 0 : (int)index];

        // LF global. Patches, splines and noise are never written for a lossless frame.
        DequantMatricesEncodeDC(GetOutput(0));
        encModular.EncodeGlobalInfo(GetOutput(0));
        encModular.EncodeStream(GetOutput(0), JxlModularStreamId.Global);

        // LF groups. The groups are independent: this loop and the next are where libjxl encodes them
        // in parallel.
        for (long groupIndex = 0; groupIndex < frameDim.NumDcGroups; groupIndex++)
        {
            encModular.EncodeStream(GetOutput(groupIndex + 1), JxlModularStreamId.ModularDc(frameDim, groupIndex));
        }

        // HF global holds only VarDCT data.
        for (long groupIndex = 0; groupIndex < numGroups; groupIndex++)
        {
            for (long i = 0; i < numPasses; i++)
            {
                // Write all modular encoded data (color?, alpha, depth, extra channels)
                JxlBitWriter output = GetOutput(JxlToc.AcGroupIndex(i, groupIndex, numGroups, frameDim.NumDcGroups));
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

    // The DC quantization factors, which every LF global section starts with. The encoder keeps the
    // defaults, which is a single bit.
    private static void DequantMatricesEncodeDC(JxlBitWriter writer) => writer.Write(1, 1);
}
