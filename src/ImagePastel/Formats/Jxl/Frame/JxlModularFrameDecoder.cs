// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_modular.h and lib/jxl/dec_modular.cc (ModularStreamId, ModularFrameDecoder: DecodeGlobalInfo,
// MaybeDropFullImage, DecodeGroup, ModularImageToDecodedRect, FinalizeDecoding, int_to_float, MultiplySum).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Entropy;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.Formats.Jxl.Modular;
using PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.Frame;

/// <summary>
/// Decodes the Modular parts of a frame: the global stream with the tree, histograms and global
/// channels, then each group, and finally undoes the global transforms and converts to float.
/// </summary>
internal sealed partial class JxlModularFrameDecoder : IDisposable
{
    private readonly MemoryAllocator allocator;
    private readonly JxlFrameDimensions frameDim;
    private readonly JxlDequantMatrices matrices;
    private readonly CancellationToken cancellationToken;
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

    /// <summary>Initializes a new instance of the <see cref="JxlModularFrameDecoder"/> class.</summary>
    /// <param name="allocator">The allocator.</param>
    /// <param name="frameDim">The frame dimensions.</param>
    /// <param name="matrices">
    /// The frame's dequantization matrices, libjxl's <c>shared->matrices</c>. The DC global section fills
    /// in their DC factors before any group is converted.
    /// </param>
    /// <param name="cancellationToken">
    /// The decode's cancellation token, which every Modular stream of the frame checks: per channel, per
    /// transform and per squeeze step. libjxl has no cancellation (#293).
    /// </param>
    public JxlModularFrameDecoder(MemoryAllocator allocator, JxlFrameDimensions frameDim, JxlDequantMatrices matrices, CancellationToken cancellationToken)
    {
        this.allocator = allocator;
        this.frameDim = frameDim;
        this.matrices = matrices;
        this.cancellationToken = cancellationToken;
    }

    /// <summary>Gets a value indicating whether the frame keeps a full-frame Modular image until the end.</summary>
    public bool UsesFullImage => this.useFullImage;

    /// <summary>Gets the transforms that the global stream's group header lists, after <see cref="DecodeGlobalInfo"/>.</summary>
    public IReadOnlyList<JxlTransform> GlobalTransforms => this.globalHeader.Transforms;

    /// <summary>Gets the global MA tree, as <see cref="DecodeGlobalInfo"/> read it; empty when the frame has none.</summary>
    public IReadOnlyList<JxlPropertyDecisionNode> GlobalTree => this.tree;

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
            JxlModularDecoder.ModularGenericDecompress(reader, gi, this.globalHeader, JxlModularStreamId.Global, options, undoTransforms: false, this.tree, this.code, this.contextMap, this.cancellationToken);

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
        JxlModularDecoder.ModularGenericDecompress(reader, gi, null, (int)streamId, options, undoTransforms: true, this.tree, this.code, this.contextMap, this.cancellationToken);

        if (!this.useFullImage)
        {
            foreach (JxlTransform t in this.globalTransform)
            {
                t.Inverse(gi, this.globalHeader.WpHeader, this.cancellationToken);
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
        gi.UndoTransforms(this.globalHeader.WpHeader, this.cancellationToken);
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

        bool accurate = this.fullImage.BitDepth >= 23;
        int c = 0;
        if (this.doColor)
        {
            // libjxl copies a grey channel into all three colour channels (rgb_from_gray); this port
            // renders it as one plane, so it is converted once. A grey image whose frame has a colour
            // transform codes three channels, which the pipeline is given as three planes.
            int numColorPlanes = frameHeader.NumDecodedColorPlanes;
            DebugGuard.IsTrue(numColorPlanes == pipeline.NumColorPlanes, "The pipeline has a plane for each colour channel");

            bool xyb = frameHeader.ColorTransform == JxlColorTransform.Xyb;
            bool fp = metadata.BitDepth.FloatingPointSample && !xyb;
            int bits = (int)metadata.BitDepth.BitsPerSample;
            int expBits = (int)metadata.BitDepth.ExponentBitsPerSample;
            for (; c < numColorPlanes; c++)
            {
                double factor = this.fullImage.BitDepth < 32 ? 1.0 / ((1u << this.fullImage.BitDepth) - 1) : 0;
                int cIn = c;
                if (xyb)
                {
                    factor = this.matrices.DcQuant(c);

                    // XYB is encoded as YX(B-Y)
                    if (c < 2)
                    {
                        cIn = 1 - c;
                    }
                }

                if (cIn >= gi.Channel.Count)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Missing colour channel");
                }

                JxlModularChannel chIn = gi.Channel[cIn];
                if (chIn.W == 0 || chIn.H == 0)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Empty image");
                }

                // Each channel has its own rectangle in the pipeline (MakeChannelRect), at its shift.
                JxlRect r = pipeline.GetGroupRect(groupId, c);
                JxlRect mr = CropRect(modularRect, chIn);
                if (r.YSize != mr.YSize || r.XSize != mr.XSize)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException($"Dimension mismatch: trying to fit a {mr.XSize}x{mr.YSize} modular channel into a {r.XSize}x{r.YSize} rect");
                }

                if (xyb && c == 2)
                {
                    // B is rebuilt from the coded B-Y and Y.
                    JxlModularChannel chY = gi.Channel[0];
                    for (int y = 0; y < r.YSize; y++)
                    {
                        ReadOnlySpan<int> rowIn = chIn.Row((int)(mr.Y0 + y)).Slice((int)mr.X0, (int)mr.XSize);
                        ReadOnlySpan<int> rowInY = chY.Row((int)(mr.Y0 + y)).Slice((int)mr.X0, (int)mr.XSize);
                        MultiplySum(rowIn, rowInY, (float)factor, OutRow(pipeline, c, r, y));
                    }

                    continue;
                }

                for (int y = 0; y < r.YSize; y++)
                {
                    ReadOnlySpan<int> rowIn = chIn.Row((int)(mr.Y0 + y)).Slice((int)mr.X0, (int)mr.XSize);
                    ConvertRow(rowIn, fp, bits, expBits, factor, accurate, OutRow(pipeline, c, r, y));
                }
            }
        }

        int numExtraChannels = metadata.NumExtraChannels;
        for (int ec = 0; ec < numExtraChannels; ec++, c++)
        {
            JxlBitDepth bitDepth = metadata.ExtraChannelInfo[ec].BitDepth;
            int bits = (int)bitDepth.BitsPerSample;
            int expBits = (int)bitDepth.ExponentBitsPerSample;
            bool fp = bitDepth.FloatingPointSample;

            // This and the next two checks are libjxl's JXL_ENSURE checks. They are unreachable from a
            // file: the header rejects integers over 31 bits, and the channels are made from the header.
            if (!fp && bits >= 32)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Integer extra channels have fewer than 32 bits");
            }

            double factor = fp ? 0 : 1.0 / ((1u << bits) - 1);
            if (c >= gi.Channel.Count)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Missing extra channel");
            }

            JxlModularChannel chIn = gi.Channel[c];
            JxlRect r = pipeline.GetGroupRect(groupId, pipeline.NumColorPlanes + ec);
            JxlRect mr = CropRect(modularRect, chIn);
            if (r.YSize != mr.YSize || r.XSize != mr.XSize)
            {
                JxlThrowHelper.ThrowInvalidImageContentException($"Dimension mismatch: trying to fit a {mr.XSize}x{mr.YSize} modular channel into a {r.XSize}x{r.YSize} rect");
            }

            for (int y = 0; y < r.YSize; y++)
            {
                ReadOnlySpan<int> rowIn = chIn.Row((int)(mr.Y0 + y)).Slice((int)mr.X0, (int)mr.XSize);
                ConvertRow(rowIn, fp, bits, expBits, factor, accurate, OutRow(pipeline, pipeline.NumColorPlanes + ec, r, y));
            }
        }
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

    private static void ConvertRow(ReadOnlySpan<int> rowIn, bool fp, int bits, int expBits, double factor, bool accurate, Span<float> rowOut)
    {
        if (fp)
        {
            IntToFloat(rowIn, rowOut, bits, expBits);
        }
        else
        {
            SingleFromSingle(rowIn, factor, accurate, rowOut);
        }
    }

    /// <summary>
    /// Reassembles floats of <paramref name="bits"/> bits with <paramref name="expBits"/> exponent bits,
    /// stored as integers, into binary32.
    /// </summary>
    /// <remarks>
    /// Both of its errors are libjxl's <c>JXL_ENSURE</c> checks. The bit depth bounds of the header make
    /// them unreachable from a file: a 32-bit float has at most 23 mantissa bits, so 8 exponent bits, and
    /// with 2 to 8 exponent bits the binary32 exponent cannot go below 0.
    /// </remarks>
    /// <param name="rowIn">The stored samples.</param>
    /// <param name="rowOut">The floats.</param>
    /// <param name="bits">The bits per sample.</param>
    /// <param name="expBits">The exponent bits per sample.</param>
    internal static void IntToFloat(ReadOnlySpan<int> rowIn, Span<float> rowOut, int bits, int expBits)
    {
        if (bits == 32)
        {
            if (expBits != 8)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("32-bit floats have 8 exponent bits");
            }

            MemoryMarshal.Cast<int, float>(rowIn).Slice(0, rowOut.Length).CopyTo(rowOut);
            return;
        }

        int expBias = (1 << (expBits - 1)) - 1;
        int signShift = bits - 1;
        int mantBits = bits - expBits - 1;
        int mantShift = 23 - mantBits;
        for (int x = 0; x < rowOut.Length; ++x)
        {
            uint f = unchecked((uint)rowIn[x]);
            int signbit = (int)(f >> signShift);
            f &= (1u << signShift) - 1;
            if (f == 0)
            {
                rowOut[x] = signbit != 0 ? -0F : 0F;
                continue;
            }

            int exp = (int)(f >> mantBits);
            int mantissa = (int)(f & ((1u << mantBits) - 1));
            if (exp == (1 << expBits) - 1)
            {
                // NaN or infinity
                f = signbit != 0 ? 0x80000000 : 0;
                f |= 0b11111111u << 23;
                f |= (uint)mantissa << mantShift;
                rowOut[x] = BitConverter.Int32BitsToSingle(unchecked((int)f));
                continue;
            }

            mantissa <<= mantShift;

            // Try to normalize only if there is space for maneuver.
            if (exp == 0 && expBits < 8)
            {
                // subnormal number
                while ((mantissa & 0x800000) == 0)
                {
                    mantissa <<= 1;
                    exp--;
                }

                exp++;

                // remove leading 1 because it is implicit now
                mantissa &= 0x7fffff;
            }

            exp -= expBias;

            // broke up the arbitrary float into its parts, now reassemble into binary32
            exp += 127;
            if (exp < 0)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Float exponent out of range");
            }

            f = signbit != 0 ? 0x80000000 : 0;
            f |= (uint)exp << 23;
            f |= (uint)mantissa;
            rowOut[x] = BitConverter.Int32BitsToSingle(unchecked((int)f));
        }
    }

    // MultiplySum: the two samples are added as integers, then converted and scaled in float.
    private static void MultiplySum(ReadOnlySpan<int> rowIn, ReadOnlySpan<int> rowInY, float factor, Span<float> rowOut)
    {
        for (int x = 0; x < rowOut.Length; x++)
        {
            rowOut[x] = unchecked(rowIn[x] + rowInY[x]) * factor;
        }
    }

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
