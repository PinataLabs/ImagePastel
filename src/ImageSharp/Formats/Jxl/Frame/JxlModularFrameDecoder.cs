// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_modular.h and lib/jxl/dec_modular.cc (ModularStreamId, ModularFrameDecoder: DecodeGlobalInfo,
// MaybeDropFullImage, DecodeGroup, ModularImageToDecodedRect, FinalizeDecoding).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Modular;
using SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <summary>
/// Decodes the Modular parts of a frame: the global stream with the tree, histograms and global
/// channels, then each group, and finally undoes the global transforms and converts to float.
/// </summary>
internal sealed class JxlModularFrameDecoder : IDisposable
{
    private readonly MemoryAllocator allocator;
    private readonly JxlFrameDimensions frameDim;
    private readonly JxlGroupHeader globalHeader = new();
    private JxlModularImage fullImage;
    private List<JxlTransform> globalTransform = new();
    private bool doColor;
    private bool haveSomething;
    private bool useFullImage = true;
    private bool allSameShift;
    private List<JxlPropertyDecisionNode> tree = new();
    private JxlAnsCode code = new();
    private byte[] contextMap = Array.Empty<byte>();

    public JxlModularFrameDecoder(MemoryAllocator allocator, JxlFrameDimensions frameDim)
    {
        this.allocator = allocator;
        this.frameDim = frameDim;
    }

    /// <summary>Gets a value indicating whether the frame keeps a full-frame Modular image until the end.</summary>
    public bool UsesFullImage => this.useFullImage;

    /// <summary>Reads the global Modular stream from the DC global section.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="frameHeader">The frame header.</param>
    public void DecodeGlobalInfo(JxlBitReader reader, JxlFrameHeader frameHeader)
    {
        bool decodeColor = frameHeader.Encoding == JxlFrameEncoding.Modular;
        JxlImageMetadata metadata = frameHeader.NonserializedMetadata.M;
        bool isGray = metadata.ColorEncoding.IsGray;
        int nbChans = 3;
        if (isGray && frameHeader.ColorTransform == JxlColorTransform.None)
        {
            nbChans = 1;
        }

        this.doColor = decodeColor;
        int nbExtra = metadata.ExtraChannelInfo.Count;
        bool hasTree = reader.ReadBits(1) != 0;
        if (hasTree)
        {
            long treeSizeLimit = Math.Min(1L << 22, 1024 + (this.frameDim.XSize * this.frameDim.YSize * (nbChans + nbExtra) / 16));
            this.tree = JxlMaTree.DecodeTree(reader, treeSizeLimit);
            this.code = new JxlAnsCode();
            this.contextMap = JxlEntropyDecoder.DecodeHistograms(reader, (this.tree.Count + 1) / 2, this.code);
        }

        if (!this.doColor)
        {
            nbChans = 0;
        }

        bool fp = metadata.BitDepth.FloatingPointSample;

        // bits_per_sample is just metadata for XYB images.
        if (metadata.BitDepth.BitsPerSample >= 32 && this.doColor && frameHeader.ColorTransform != JxlColorTransform.Xyb)
        {
            if (metadata.BitDepth.BitsPerSample == 32 && !fp)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("uint32_t not supported in dec_modular");
            }
            else if (metadata.BitDepth.BitsPerSample > 32)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("bits_per_sample > 32 not supported");
            }
        }

        JxlModularImage gi = JxlModularImage.Create(this.allocator, (int)this.frameDim.XSize, (int)this.frameDim.YSize, (int)metadata.BitDepth.BitsPerSample, nbChans + nbExtra);
        try
        {
            this.allSameShift = true;
            if (frameHeader.ColorTransform == JxlColorTransform.YCbCr)
            {
                for (int c = 0; c < nbChans; c++)
                {
                    JxlModularChannel ch = gi.Channel[c];
                    ch.HShift = frameHeader.ChromaSubsampling.HShift(c);
                    ch.VShift = frameHeader.ChromaSubsampling.VShift(c);
                    long xsizeShifted = JxlBits.DivCeil(this.frameDim.XSize, 1L << ch.HShift);
                    long ysizeShifted = JxlBits.DivCeil(this.frameDim.YSize, 1L << ch.VShift);
                    ch.Shrink((int)xsizeShifted, (int)ysizeShifted);
                    if (ch.HShift != gi.Channel[0].HShift || ch.VShift != gi.Channel[0].VShift)
                    {
                        this.allSameShift = false;
                    }
                }
            }

            for (int ec = 0, c = nbChans; ec < nbExtra; ec++, c++)
            {
                uint ecups = frameHeader.ExtraChannelUpsampling[ec];
                JxlModularChannel ch = gi.Channel[c];
                ch.Shrink((int)JxlBits.DivCeil(this.frameDim.XSizeUpsampled, ecups), (int)JxlBits.DivCeil(this.frameDim.YSizeUpsampled, ecups));
                ch.HShift = ch.VShift = JxlBits.CeilLog2Nonzero(ecups) - JxlBits.CeilLog2Nonzero(frameHeader.Upsampling);
                if (ch.HShift != gi.Channel[0].HShift || ch.VShift != gi.Channel[0].VShift)
                {
                    this.allSameShift = false;
                }
            }

            JxlModularOptions options = new()
            {
                MaxChanSize = this.frameDim.GroupDimension,
                GroupDim = this.frameDim.GroupDimension,
            };
            JxlModularDecoder.ModularGenericDecompress(reader, gi, this.globalHeader, JxlModularStreamId.Global, options, undoTransforms: false, this.tree, this.code, this.contextMap);

            this.haveSomething = false;
            for (int c = 0; c < gi.Channel.Count; c++)
            {
                JxlModularChannel gic = gi.Channel[c];
                if (c >= gi.NbMetaChannels && gic.W <= this.frameDim.GroupDimension && gic.H <= this.frameDim.GroupDimension)
                {
                    this.haveSomething = true;
                }
            }

            // move global transforms to groups if possible
            if (!this.haveSomething && this.allSameShift)
            {
                if (gi.Transform.Count == 1 && gi.Transform[0].Id == JxlTransformId.Rct)
                {
                    this.globalTransform = gi.Transform;
                    gi.Transform = new List<JxlTransform>();
                }
            }
        }
        catch
        {
            gi.Dispose();
            throw;
        }

        this.fullImage = gi;
    }

    /// <summary>
    /// Drops the full-frame image when groups can be decoded and converted on their own: no global
    /// transforms are left, no channel was decoded in the global stream, and every channel has the same shift.
    /// </summary>
    public void MaybeDropFullImage()
    {
        if (this.fullImage.Transform.Count == 0 && !this.haveSomething && this.allSameShift)
        {
            this.useFullImage = false;
            foreach (JxlModularChannel ch in this.fullImage.Channel)
            {
                ch.DropPlane();
            }
        }
    }

    /// <summary>
    /// Decodes the channels of one group whose shift is in [<paramref name="minShift"/>, <paramref name="maxShift"/>].
    /// </summary>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="rect">The group rectangle, in frame coordinates.</param>
    /// <param name="reader">The reader.</param>
    /// <param name="minShift">The smallest channel shift to decode.</param>
    /// <param name="maxShift">The largest channel shift to decode.</param>
    /// <param name="streamId">The stream id.</param>
    /// <param name="pipeline">The render pipeline, which receives the group when the full image is not kept.</param>
    /// <param name="groupId">The group, for <paramref name="pipeline"/>.</param>
    public void DecodeGroup(JxlFrameHeader frameHeader, JxlRect rect, JxlBitReader reader, int minShift, int maxShift, long streamId, JxlRenderPipeline pipeline, long groupId)
    {
        using JxlModularImage gi = JxlModularImage.Create(this.allocator, (int)rect.XSize, (int)rect.YSize, this.fullImage.BitDepth, 0);
        int c = this.fullImage.NbMetaChannels;
        for (; c < this.fullImage.Channel.Count; c++)
        {
            JxlModularChannel fc = this.fullImage.Channel[c];
            if (fc.W > this.frameDim.GroupDimension || fc.H > this.frameDim.GroupDimension)
            {
                break;
            }
        }

        int beginc = c;
        for (; c < this.fullImage.Channel.Count; c++)
        {
            JxlModularChannel fc = this.fullImage.Channel[c];
            int shift = Math.Min(fc.HShift, fc.VShift);
            if (shift > maxShift || shift < minShift)
            {
                continue;
            }

            JxlRect r = ChannelRect(rect, fc);
            if (r.XSize == 0 || r.YSize == 0)
            {
                continue;
            }

            JxlModularChannel gc = JxlModularChannel.Create(this.allocator, (int)r.XSize, (int)r.YSize);
            gc.HShift = fc.HShift;
            gc.VShift = fc.VShift;
            gi.Channel.Add(gc);
        }

        if (gi.Channel.Count == 0)
        {
            // Nothing to decode.
            return;
        }

        JxlModularOptions options = new();
        JxlModularDecoder.ModularGenericDecompress(reader, gi, null, (int)streamId, options, undoTransforms: true, this.tree, this.code, this.contextMap);

        if (!this.useFullImage)
        {
            foreach (JxlTransform t in this.globalTransform)
            {
                t.Inverse(gi, this.globalHeader.WpHeader);
            }

            this.ModularImageToDecodedRect(frameHeader, gi, pipeline, groupId, new JxlRect(0, 0, gi.W, gi.H));
            return;
        }

        int gic = 0;
        for (c = beginc; c < this.fullImage.Channel.Count; c++)
        {
            JxlModularChannel fc = this.fullImage.Channel[c];
            int shift = Math.Min(fc.HShift, fc.VShift);
            if (shift > maxShift || shift < minShift)
            {
                continue;
            }

            JxlRect r = ChannelRect(rect, fc);
            if (r.XSize == 0 || r.YSize == 0)
            {
                continue;
            }

            JxlModularChannel from = gi.Channel[gic];
            for (int y = 0; y < r.YSize; y++)
            {
                from.Row(y).Slice(0, (int)r.XSize).CopyTo(fc.Row((int)(r.Y0 + y)).Slice((int)r.X0));
            }

            gic++;
        }
    }

    /// <summary>
    /// Undoes the global transforms of the full image and converts every group into the pipeline.
    /// </summary>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="pipeline">The render pipeline.</param>
    public void FinalizeDecoding(JxlFrameHeader frameHeader, JxlRenderPipeline pipeline)
    {
        if (!this.useFullImage)
        {
            return;
        }

        // libjxl clones the image unless it decodes in place; the final decode is always in place.
        JxlModularImage gi = this.fullImage;
        gi.UndoTransforms(this.globalHeader.WpHeader);
        if (gi.Error)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Undoing transforms failed");
        }

        for (long i = 0; i < this.frameDim.NumGroups; i++)
        {
            this.ModularImageToDecodedRect(frameHeader, gi, pipeline, i, this.frameDim.GroupRect(i));
        }
    }

    public void Dispose() => this.fullImage?.Dispose();

    private static JxlRect ChannelRect(JxlRect rect, JxlModularChannel fc)
        => new(rect.X0 >> fc.HShift, rect.Y0 >> fc.VShift, rect.XSize >> fc.HShift, rect.YSize >> fc.VShift, fc.W, fc.H);

    // Converts integer samples to float, scaled to [0, 1] for integer images.
    private void ModularImageToDecodedRect(JxlFrameHeader frameHeader, JxlModularImage gi, JxlRenderPipeline pipeline, long groupId, JxlRect modularRect)
    {
        JxlImageMetadata metadata = frameHeader.NonserializedMetadata.M;
        if (gi.Transform.Count != 0)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Transforms left over");
        }

        JxlRect r = pipeline.GetGroupRect(groupId);
        if (this.doColor)
        {
            bool rgbFromGray = metadata.ColorEncoding.IsGray && frameHeader.ColorTransform == JxlColorTransform.None;
            bool accurate = this.fullImage.BitDepth >= 23;
            for (int c = 0; c < 3; c++)
            {
                // libjxl's float and XYB conversions come with later slices; the frame decoder
                // rejects those images, so only this path is reached.
                double factor = 1.0 / ((1u << this.fullImage.BitDepth) - 1);
                int cIn = rgbFromGray ? 0 : c;
                if (cIn >= gi.Channel.Count)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Missing colour channel");
                }

                JxlModularChannel chIn = gi.Channel[cIn];
                if (chIn.W == 0 || chIn.H == 0)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Empty image");
                }

                JxlRect mr = CropRect(modularRect, chIn);
                if (r.YSize != mr.YSize || r.XSize != mr.XSize)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException($"Dimension mismatch: trying to fit a {mr.XSize}x{mr.YSize} modular channel into a {r.XSize}x{r.YSize} rect");
                }

                for (int y = 0; y < r.YSize; y++)
                {
                    ReadOnlySpan<int> rowIn = chIn.Row((int)(mr.Y0 + y)).Slice((int)mr.X0, (int)mr.XSize);
                    if (rgbFromGray)
                    {
                        for (int cc = 0; cc < 3; cc++)
                        {
                            SingleFromSingle(rowIn, factor, accurate, OutRow(pipeline, cc, r, y));
                        }
                    }
                    else
                    {
                        SingleFromSingle(rowIn, factor, accurate, OutRow(pipeline, c, r, y));
                    }
                }

                if (rgbFromGray)
                {
                    break;
                }
            }
        }

        // Extra channels arrive with a later slice; the frame decoder rejects images that have any.
    }

    private static JxlRect CropRect(JxlRect modularRect, JxlModularChannel chIn)
    {
        JxlRect mr = new(
            modularRect.X0 >> chIn.HShift,
            modularRect.Y0 >> chIn.VShift,
            JxlBits.DivCeil(modularRect.XSize, 1L << chIn.HShift),
            JxlBits.DivCeil(modularRect.YSize, 1L << chIn.VShift));

        // Rect::Crop clips the rectangle to the plane.
        return new JxlRect(mr.X0, mr.Y0, mr.XSize, mr.YSize, chIn.W, chIn.H);
    }

    private static Span<float> OutRow(JxlRenderPipeline pipeline, int c, JxlRect r, int y)
        => pipeline.GetPlane(c).DangerousGetRowSpan((int)(r.Y0 + y)).Slice((int)r.X0, (int)r.XSize);

    // Below 23 bits libjxl converts with Highway in float (SingleFromSingle), which converting each
    // sample to float and multiplying in float matches; from 23 bits it multiplies in double
    // (SingleFromSingleAccurate).
    private static void SingleFromSingle(ReadOnlySpan<int> rowIn, double factor, bool accurate, Span<float> rowOut)
    {
        if (accurate)
        {
            for (int x = 0; x < rowOut.Length; x++)
            {
                rowOut[x] = (float)(rowIn[x] * factor);
            }

            return;
        }

        float factorF = (float)factor;
        for (int x = 0; x < rowOut.Length; x++)
        {
            rowOut[x] = rowIn[x] * factorF;
        }
    }
}
