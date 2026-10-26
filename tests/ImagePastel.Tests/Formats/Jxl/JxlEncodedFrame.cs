// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.Formats.Jxl.Icc;
using PinataLabs.ImagePastel.Formats.Jxl.Modular;
using PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Reads the first frame of an encoded file, a bare codestream or a container, with the decoder's own
/// parse, so encoder tests can check what the encoder chose without reaching into the encoder.
/// </summary>
internal static class JxlEncodedFrame
{
    /// <summary>
    /// Returns the transforms of the first frame's global Modular stream, in the order the encoder
    /// applied them.
    /// </summary>
    /// <param name="codestream">A bare codestream or a container.</param>
    /// <returns>The transforms.</returns>
    public static List<JxlTransform> ReadGlobalTransforms(byte[] codestream)
    {
        using JxlModularFrameDecoder decoder = ReadGlobalInfo(codestream);
        return new List<JxlTransform>(decoder.GlobalTransforms);
    }

    /// <summary>
    /// Returns the first frame's global MA tree, as the decoder reads it: numbered breadth first, with
    /// each leaf's <see cref="JxlPropertyDecisionNode.LChild"/> its context.
    /// </summary>
    /// <param name="codestream">A bare codestream or a container.</param>
    /// <returns>The tree.</returns>
    public static List<JxlPropertyDecisionNode> ReadGlobalTree(byte[] codestream)
    {
        using JxlModularFrameDecoder decoder = ReadGlobalInfo(codestream);
        return new List<JxlPropertyDecisionNode>(decoder.GlobalTree);
    }

    /// <summary>
    /// Returns the transforms of each group's Modular stream in the first frame, in group order, or
    /// none for a frame of one group, whose global stream codes everything. A group with no channels
    /// has an empty list.
    /// </summary>
    /// <param name="codestream">A bare codestream or a container.</param>
    /// <returns>The transforms of each group.</returns>
    public static List<List<JxlTransform>> ReadGroupTransforms(byte[] codestream)
    {
        codestream = JxlBoxList.Codestream(codestream);
        List<List<JxlTransform>> groups = new();
        (JxlFrameHeader frameHeader, uint[] sizes, int sectionStart) = ReadFrameHeader(codestream);
        JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();
        if (sizes.Length == 1)
        {
            return groups;
        }

        // A Modular frame's AC group section holds just the group's Modular stream (one pass).
        for (long g = 0; g < frameDim.NumGroups; g++)
        {
            long index = JxlToc.AcGroupIndex(0, g, frameDim.NumGroups, frameDim.NumDcGroups);
            int start = sectionStart;
            for (long i = 0; i < index; i++)
            {
                start += (int)sizes[i];
            }

            JxlGroupHeader header = new();
            if (sizes[index] != 0)
            {
                JxlBitReader section = new(codestream, start, (int)sizes[index]);
                JxlBundle.Read(section, header);
                section.Close();
            }

            groups.Add(new List<JxlTransform>(header.Transforms));
        }

        return groups;
    }

    /// <summary>Reads the first frame up to the end of its global Modular stream.</summary>
    /// <param name="codestream">A bare codestream or a container.</param>
    /// <param name="cancellationToken">The token the frame decoder checks.</param>
    /// <returns>The frame decoder, which the caller disposes.</returns>
    public static JxlModularFrameDecoder ReadGlobalInfo(byte[] codestream, CancellationToken cancellationToken = default)
    {
        codestream = JxlBoxList.Codestream(codestream);
        (JxlFrameHeader frameHeader, uint[] sizes, int sectionStart) = ReadFrameHeader(codestream);

        // The DC global section: the all-default DC quantisation bit, then the global Modular stream.
        JxlBitReader section = new(codestream, sectionStart, (int)sizes[0]);
        Assert.Equal(1UL, section.ReadBits(1));
        JxlModularFrameDecoder decoder = new(Configuration.Default.MemoryAllocator, frameHeader.ToFrameDimensions(), new JxlDequantMatrices(), cancellationToken);
        try
        {
            decoder.DecodeGlobalInfo(section, frameHeader);
        }
        catch
        {
            decoder.Dispose();
            throw;
        }

        section.Close();
        return decoder;
    }

    // Reads the headers and the first frame's header and TOC: the section sizes, and where the first
    // section starts.
    private static (JxlFrameHeader FrameHeader, uint[] Sizes, int SectionStart) ReadFrameHeader(byte[] codestream)
    {
        Assert.Equal(JxlConstants.CodestreamSignature, codestream[..2]);

        // The same steps as JxlDecoderCore.ReadHeaders and JxlFrameDecoder.DecodeFrame.
        JxlCodecMetadata metadata = new();
        JxlBitReader reader = new(codestream, 2, codestream.Length - 2);
        JxlBundle.Read(reader, metadata.Size);
        JxlBundle.Read(reader, metadata.M);
        metadata.TransformData.NonserializedXybEncoded = metadata.M.XybEncoded;
        JxlBundle.Read(reader, metadata.TransformData);
        if (metadata.M.ColorEncoding.WantIcc)
        {
            JxlIccReader.Read(reader, CancellationToken.None);
        }

        reader.JumpToByteBoundary();
        JxlFrameHeader frameHeader = new(metadata);
        JxlBundle.Read(reader, frameHeader);
        JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();
        uint[] sizes = JxlToc.ReadGroupOffsets(JxlToc.NumTocEntries(frameDim.NumGroups, frameDim.NumDcGroups, frameHeader.Passes.NumPasses), reader).Sizes;
        reader.EnsureAllReadsWithinBounds("frame header");
        int sectionStart = 2 + (int)(reader.TotalBitsConsumed / 8);
        reader.Close();
        return (frameHeader, sizes, sectionStart);
    }
}
