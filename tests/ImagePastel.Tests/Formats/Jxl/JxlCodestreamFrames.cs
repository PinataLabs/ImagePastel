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
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Reads the header of every frame of a codestream, displayed or not, without decoding the frames,
/// so that a test can check that a generated file has the frames it is meant to test.
/// </summary>
internal static class JxlCodestreamFrames
{
    /// <summary>Reads the frame headers.</summary>
    /// <param name="file">A bare codestream or a container, with no preview.</param>
    /// <returns>The header of each frame, in codestream order.</returns>
    public static List<JxlFrameHeader> Read(byte[] file)
        => ReadWithToc(file).ConvertAll(frame => frame.Header);

    /// <summary>Reads the frame headers and their TOCs.</summary>
    /// <param name="file">A bare codestream or a container, with no preview.</param>
    /// <returns>The header and the TOC of each frame, in codestream order.</returns>
    public static List<(JxlFrameHeader Header, JxlGroupOffsets Toc)> ReadWithToc(byte[] file)
    {
        byte[] codestream = JxlBoxList.Codestream(file);
        JxlCodecMetadata metadata = ReadImageHeader(codestream, out int offset);
        Assert.False(metadata.M.HavePreview);

        List<(JxlFrameHeader Header, JxlGroupOffsets Toc)> frames = new();
        JxlFrameHeader frameHeader;
        do
        {
            JxlBitReader reader = new(codestream, offset, codestream.Length - offset);
            frameHeader = new JxlFrameHeader(metadata);
            JxlBundle.Read(reader, frameHeader);
            JxlFrameDimensions frameDim = frameHeader.ToFrameDimensions();
            long tocEntries = JxlToc.NumTocEntries(frameDim.NumGroups, frameDim.NumDcGroups, frameHeader.Passes.NumPasses);
            JxlGroupOffsets toc = JxlToc.ReadGroupOffsets(tocEntries, reader);
            reader.EnsureAllReadsWithinBounds("frame header");
            offset += (int)(reader.TotalBitsConsumed / 8);
            reader.Close();
            offset += (int)toc.TotalSize;

            frames.Add((frameHeader, toc));
        }
        while (!frameHeader.IsLast);

        return frames;
    }

    /// <summary>
    /// Reads the image header of a bare codestream, as <c>JxlDecoderCore.ReadHeaders</c> does: the size
    /// header, the image metadata, the transform data and the ICC profile, if any, which it skips.
    /// </summary>
    /// <param name="codestream">A bare codestream.</param>
    /// <param name="frameOffset">The byte at which the first frame header (or the preview) starts.</param>
    /// <returns>The metadata.</returns>
    public static JxlCodecMetadata ReadImageHeader(byte[] codestream, out int frameOffset)
    {
        Assert.Equal(JxlConstants.CodestreamSignature, codestream[..2]);
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
        frameOffset = 2 + (int)(reader.TotalBitsConsumed / 8);
        reader.Close();
        return metadata;
    }

    /// <summary>Reads the colour encoding from the image header of a file.</summary>
    /// <param name="file">A bare codestream or a container.</param>
    /// <returns>The colour encoding header bundle.</returns>
    public static JxlColorEncodingHeader ReadColorEncoding(byte[] file)
        => ReadImageHeader(JxlBoxList.Codestream(file), out _).M.ColorEncoding;
}
