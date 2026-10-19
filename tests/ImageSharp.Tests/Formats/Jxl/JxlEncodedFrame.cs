// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Icc;
using SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Reads the first frame of an encoded codestream with the decoder's own parse, so encoder tests can
/// check what the encoder chose without reaching into the encoder.
/// </summary>
internal static class JxlEncodedFrame
{
    /// <summary>
    /// Returns the transforms of the first frame's global Modular stream, in the order the encoder
    /// applied them.
    /// </summary>
    /// <param name="codestream">A bare codestream.</param>
    /// <returns>The transforms.</returns>
    public static List<JxlTransform> ReadGlobalTransforms(byte[] codestream)
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
            JxlIccReader.Read(reader);
        }

        reader.JumpToByteBoundary();
        JxlFrameHeader frameHeader = new(metadata);
        JxlBundle.Read(reader, frameHeader);
        JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();
        uint[] sizes = JxlToc.ReadToc(JxlToc.NumTocEntries(frameDim.NumGroups, frameDim.NumDcGroups, frameHeader.Passes.NumPasses), reader);
        reader.EnsureAllReadsWithinBounds("frame header");
        int sectionStart = 2 + (int)(reader.TotalBitsConsumed / 8);
        reader.Close();

        // The DC global section: the all-default DC quantisation bit, then the global Modular stream.
        JxlBitReader section = new(codestream, sectionStart, (int)sizes[0]);
        Assert.Equal(1UL, section.ReadBits(1));
        using JxlModularFrameDecoder decoder = new(Configuration.Default.MemoryAllocator, frameDim, new JxlDequantMatrices());
        decoder.DecodeGlobalInfo(section, frameHeader);
        section.Close();
        return new List<JxlTransform>(decoder.GlobalTransforms);
    }
}
