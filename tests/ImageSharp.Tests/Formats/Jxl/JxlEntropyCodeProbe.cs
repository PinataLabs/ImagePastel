// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Icc;
using SixLabors.ImageSharp.Formats.Jxl.Modular;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Reads an encoded codestream with the decoder's own parse, up to the entropy codes of the global
/// Modular stream, and reports which streams use prefix codes and which use ANS. It reads the first
/// frame only, and expects a Modular frame with a global tree, as the encoder writes.
/// </summary>
internal static class JxlEntropyCodeProbe
{
    /// <summary>Reads the codes of the MA tree stream and of the global image stream.</summary>
    /// <param name="codestream">A bare codestream.</param>
    /// <param name="treeCode">The codes of the tree tokens.</param>
    /// <param name="imageCode">The codes of the image tokens, shared by every group.</param>
    public static void ReadGlobalCodes(byte[] codestream, out JxlAnsCode treeCode, out JxlAnsCode imageCode)
    {
        long treeStart = FindGlobalTree(codestream, out long treeSizeLimit);

        // The tree reader keeps its codes to itself, so read them once on their own, then again with
        // the tree to get past it.
        JxlBitReader reader = ReaderAt(codestream, treeStart);
        treeCode = new JxlAnsCode();
        JxlEntropyDecoder.DecodeHistograms(reader, 6, treeCode);
        reader.Close();

        reader = ReaderAt(codestream, treeStart);
        List<JxlPropertyDecisionNode> tree = JxlMaTree.DecodeTree(reader, treeSizeLimit);
        imageCode = new JxlAnsCode();
        JxlEntropyDecoder.DecodeHistograms(reader, (tree.Count + 1) / 2, imageCode);
        reader.Close();
    }

    /// <summary>Reads the codes of the embedded ICC profile stream.</summary>
    /// <param name="codestream">A bare codestream with an embedded ICC profile.</param>
    /// <returns>The codes of the ICC tokens.</returns>
    public static JxlAnsCode ReadIccCode(byte[] codestream)
    {
        JxlCodecMetadata metadata = new();
        JxlBitReader reader = ReaderAt(codestream, 0);
        JxlBundle.Read(reader, metadata.Size);
        JxlBundle.Read(reader, metadata.M);
        metadata.TransformData.NonserializedXybEncoded = metadata.M.XybEncoded;
        JxlBundle.Read(reader, metadata.TransformData);
        Assert.True(metadata.M.ColorEncoding.WantIcc);

        // As JxlIccReader.Read: the encoded size, then the histograms of the 41 ICC contexts.
        JxlFieldCoders.ReadU64(reader);
        JxlAnsCode code = new();
        JxlEntropyDecoder.DecodeHistograms(reader, JxlIccCodecCommon.NumIccContexts, code);
        reader.Close();
        return code;
    }

    // Returns the bit offset of the global tree, after the headers, the frame header, the TOC, the
    // DC quantization bit and the has_tree bit.
    private static long FindGlobalTree(byte[] codestream, out long treeSizeLimit)
    {
        JxlCodecMetadata metadata = new();
        JxlBitReader reader = ReaderAt(codestream, 0);
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
        Assert.Equal(JxlFrameEncoding.Modular, frameHeader.Encoding);
        JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();
        JxlToc.ReadGroupOffsets(JxlToc.NumTocEntries(frameDim.NumGroups, frameDim.NumDcGroups, frameHeader.Passes.NumPasses), reader);

        // The DC global section comes first: the DequantMatrices DC all_default bit, then has_tree.
        Assert.Equal(1UL, reader.ReadBits(1));
        Assert.Equal(1UL, reader.ReadBits(1));

        int nbChans = metadata.M.ColorEncoding.IsGray ? 1 : 3;
        treeSizeLimit = System.Math.Min(1L << 22, 1024 + (frameDim.XSize * frameDim.YSize * (nbChans + metadata.M.ExtraChannelInfo.Count) / 16));
        long treeStart = reader.TotalBitsConsumed;
        reader.Close();
        return treeStart;
    }

    private static JxlBitReader ReaderAt(byte[] codestream, long bitOffset)
    {
        JxlBitReader reader = new(codestream, 2, codestream.Length - 2);
        reader.SkipBits(bitOffset);
        return reader;
    }
}
