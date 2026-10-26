// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/frame_header.h and lib/jxl/frame_header.cc (YCbCrChromaSubsampling, BlendingInfo,
// AnimationFrame, Passes, FrameHeader), and lib/jxl/frame_dimensions.h (FrameDimensions).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using static PinataLabs.ImagePastel.Formats.Jxl.Fields.JxlU32Distr;

namespace PinataLabs.ImagePastel.Formats.Jxl.Frame;

/// <summary>The header of a frame.</summary>
internal sealed class JxlFrameHeader : JxlFields
{
    private static readonly JxlU32Enc CropEnc = new(Bits(8), BitsOffset(11, 256), BitsOffset(14, 2304), BitsOffset(30, 18688));

    private byte[] name = Array.Empty<byte>();

    public JxlFrameHeader(JxlCodecMetadata metadata)
    {
        this.NonserializedMetadata = metadata;
        this.AnimationFrame = new JxlAnimationFrame(metadata);
        JxlBundle.Init(this);
    }

    public JxlFrameEncoding Encoding { get; set; } = JxlFrameEncoding.Modular;

    public JxlFrameType FrameType { get; set; } = JxlFrameType.Regular;

    public JxlFrameFlags Flags { get; set; }

    public JxlColorTransform ColorTransform { get; set; } = JxlColorTransform.Xyb;

    public JxlYCbCrChromaSubsampling ChromaSubsampling { get; } = new();

    /// <summary>Gets or sets the group size shift. Modular only.</summary>
    public uint GroupSizeShift { get; set; }

    /// <summary>Gets or sets the X quantization matrix scale. VarDCT with XYB only.</summary>
    public uint XQmScale { get; set; }

    /// <summary>Gets or sets the B quantization matrix scale. VarDCT with XYB only.</summary>
    public uint BQmScale { get; set; }

    public JxlPasses Passes { get; } = new();

    /// <summary>Gets or sets a value indicating whether the frame has its own size or origin.</summary>
    public bool CustomSizeOrOrigin { get; set; }

    public uint FrameSizeX { get; set; }

    public uint FrameSizeY { get; set; }

    public int FrameOriginX0 { get; set; }

    public int FrameOriginY0 { get; set; }

    /// <summary>Gets or sets the upsampling of the colour channels: 1, 2, 4 or 8.</summary>
    public uint Upsampling { get; set; }

    /// <summary>Gets the upsampling of each extra channel.</summary>
    public List<uint> ExtraChannelUpsampling { get; } = new();

    public JxlBlendingInfo BlendingInfo { get; } = new();

    public List<JxlBlendingInfo> ExtraChannelBlendingInfo { get; } = new();

    public JxlAnimationFrame AnimationFrame { get; }

    public bool IsLast { get; set; }

    /// <summary>Gets or sets the reference slot the frame is saved to.</summary>
    public uint SaveAsReference { get; set; }

    /// <summary>Gets or sets a value indicating whether the frame is saved before its colour transform.</summary>
    public bool SaveBeforeColorTransform { get; set; }

    /// <summary>Gets or sets the DC level, 1 to 4 for a DC frame; 0 otherwise.</summary>
    public uint DcLevel { get; set; }

    public JxlLoopFilter LoopFilter { get; } = new();

    public ulong Extensions { get; set; }

    /// <summary>Gets or sets the codestream metadata, which is not serialized here.</summary>
    public JxlCodecMetadata NonserializedMetadata { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the frame is the preview frame, which is not serialized
    /// (libjxl's <c>nonserialized_is_preview</c>). A preview frame defaults to the preview size, and must
    /// be a regular frame with no blending and no custom size or origin.
    /// </summary>
    public bool NonserializedIsPreview { get; set; }

    /// <summary>Gets or sets the frame name, UTF-8.</summary>
    public ReadOnlySpan<byte> Name
    {
        get => this.name;
        set => this.name = value.ToArray();
    }

    /// <summary>Gets a value indicating whether the frame can be saved as a reference.</summary>
    public bool CanBeReferenced
        => !this.IsLast && this.FrameType != JxlFrameType.Dc && (this.AnimationFrame.Duration == 0 || this.SaveAsReference != 0);

    /// <summary>Gets a value indicating whether the frame takes its DC from a DC frame (<c>kUseDcFrame</c>).</summary>
    public bool UsesDcFrame
        => (this.Flags & JxlFrameFlags.UseDcFrame) != 0;

    /// <summary>
    /// Gets the number of colour planes the frame decodes into the render pipeline. A VarDCT frame, or a
    /// frame with a colour transform, decodes three even for a grey image, as libjxl does (its
    /// <c>nb_chans</c> in <c>ModularFrameDecoder::DecodeGlobalInfo</c>). A Modular frame with no colour
    /// transform decodes <see cref="JxlImageMetadata.NumColorPlanes"/>: one for a grey image, which libjxl
    /// copies into three (<c>rgb_from_gray</c>) and this port keeps as one until a stage needs three.
    /// </summary>
    public int NumDecodedColorPlanes
        => this.Encoding == JxlFrameEncoding.VarDct || this.ColorTransform != JxlColorTransform.None ? 3 : this.NonserializedMetadata.M.NumColorPlanes;

    /// <summary>
    /// Gets a value indicating whether the frame is a grey image's YCbCr frame, which drops to one colour
    /// plane only after its colour transform. libjxl keeps three colour planes to the output. This port
    /// drops the other frames of a grey image that is not XYB to one plane where no later stage mixes or
    /// keeps the colour planes (<see cref="RenderPipeline.JxlRenderPipeline.Finish"/>). The
    /// YCbCr transform mixes the three planes, so such a frame keeps them through its patches, its save
    /// before the colour transform and the transform itself, as libjxl does.
    /// </summary>
    public bool DropsToGreyAfterColorTransform
        => this.ColorTransform == JxlColorTransform.YCbCr && this.NonserializedMetadata.M.NumColorPlanes == 1;

    // default_xsize and default_ysize: the preview frame defaults to the preview size.
    private long DefaultXSize => this.NonserializedMetadata == null ? 0
        : this.NonserializedIsPreview ? (long)this.NonserializedMetadata.M.PreviewSize.XSize
        : (long)this.NonserializedMetadata.XSize;

    private long DefaultYSize => this.NonserializedMetadata == null ? 0
        : this.NonserializedIsPreview ? (long)this.NonserializedMetadata.M.PreviewSize.YSize
        : (long)this.NonserializedMetadata.YSize;

    /// <summary>Returns the dimensions of the frame.</summary>
    /// <returns>The dimensions.</returns>
    public JxlFrameDimensions ToFrameDimensions()
    {
        long xsize = this.DefaultXSize;
        long ysize = this.DefaultYSize;

        xsize = this.FrameSizeX != 0 ? this.FrameSizeX : xsize;
        ysize = this.FrameSizeY != 0 ? this.FrameSizeY : ysize;

        if (this.DcLevel != 0)
        {
            xsize = JxlBits.DivCeil(xsize, 1L << (int)(3 * this.DcLevel));
            ysize = JxlBits.DivCeil(ysize, 1L << (int)(3 * this.DcLevel));
        }

        return new JxlFrameDimensions(
            xsize,
            ysize,
            (int)this.GroupSizeShift,
            this.ChromaSubsampling.MaxHShift,
            this.ChromaSubsampling.MaxVShift,
            this.Encoding == JxlFrameEncoding.Modular,
            (int)this.Upsampling);
    }

    public override void VisitFields(JxlVisitor visitor)
    {
        if (visitor.AllDefault(this))
        {
            visitor.SetDefault(this);
            return;
        }

        this.FrameType = (JxlFrameType)visitor.U32(Val((uint)JxlFrameType.Regular), Val((uint)JxlFrameType.Dc), Val((uint)JxlFrameType.ReferenceOnly), Val((uint)JxlFrameType.SkipProgressive), (uint)JxlFrameType.Regular, (uint)this.FrameType);
        if (visitor.IsReading && this.NonserializedIsPreview && this.FrameType != JxlFrameType.Regular)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Only regular frame could be a preview");
        }

        bool isModular = this.Encoding == JxlFrameEncoding.Modular;
        isModular = visitor.Bool(false, isModular);
        this.Encoding = isModular ? JxlFrameEncoding.Modular : JxlFrameEncoding.VarDct;

        this.Flags = (JxlFrameFlags)visitor.U64(0, (ulong)this.Flags);

        // Color transform
        bool xybEncoded = this.NonserializedMetadata == null || this.NonserializedMetadata.M.XybEncoded;

        if (xybEncoded)
        {
            this.ColorTransform = JxlColorTransform.Xyb;
        }
        else
        {
            // Alternate if kYCbCr.
            bool alternate = this.ColorTransform == JxlColorTransform.YCbCr;
            alternate = visitor.Bool(false, alternate);
            this.ColorTransform = alternate ? JxlColorTransform.YCbCr : JxlColorTransform.None;
        }

        // Chroma subsampling for YCbCr, if no DC frame is used.
        if (visitor.Conditional(this.ColorTransform == JxlColorTransform.YCbCr && !this.UsesDcFrame))
        {
            visitor.VisitNested(this.ChromaSubsampling);
        }

        int numExtraChannels = this.NonserializedMetadata?.M.NumExtraChannels ?? 0;

        // Upsampling
        if (visitor.Conditional(!this.UsesDcFrame))
        {
            this.Upsampling = visitor.U32(Val(1), Val(2), Val(4), Val(8), 1, this.Upsampling);
            if (this.NonserializedMetadata != null && visitor.Conditional(numExtraChannels != 0))
            {
                List<JxlExtraChannelInfo> extraChannels = this.NonserializedMetadata.M.ExtraChannelInfo;
                while (this.ExtraChannelUpsampling.Count < extraChannels.Count)
                {
                    this.ExtraChannelUpsampling.Add(1);
                }

                this.ExtraChannelUpsampling.RemoveRange(extraChannels.Count, this.ExtraChannelUpsampling.Count - extraChannels.Count);
                for (int i = 0; i < extraChannels.Count; ++i)
                {
                    int dimShift = (int)extraChannels[i].DimShift;
                    uint ecUpsampling = this.ExtraChannelUpsampling[i] >> dimShift;
                    ecUpsampling = visitor.U32(Val(1), Val(2), Val(4), Val(8), 1, ecUpsampling);
                    ecUpsampling <<= dimShift;
                    if (ecUpsampling < this.Upsampling)
                    {
                        JxlThrowHelper.ThrowInvalidImageContentException($"EC upsampling ({ecUpsampling}) < color upsampling ({this.Upsampling}), which is invalid.");
                    }

                    if (ecUpsampling > 8)
                    {
                        JxlThrowHelper.ThrowInvalidImageContentException($"EC upsampling too large ({ecUpsampling})");
                    }

                    this.ExtraChannelUpsampling[i] = ecUpsampling;
                }
            }
            else
            {
                this.ExtraChannelUpsampling.Clear();
            }
        }

        // Modular- or VarDCT-specific data.
        if (visitor.Conditional(this.Encoding == JxlFrameEncoding.Modular))
        {
            this.GroupSizeShift = visitor.Bits(2, 1, this.GroupSizeShift);
        }

        if (visitor.Conditional(this.Encoding == JxlFrameEncoding.VarDct && this.ColorTransform == JxlColorTransform.Xyb))
        {
            this.XQmScale = visitor.Bits(3, 3, this.XQmScale);
            this.BQmScale = visitor.Bits(3, 2, this.BQmScale);
        }
        else
        {
            this.XQmScale = this.BQmScale = 2; // noop
        }

        // Not useful for kPatchSource
        if (visitor.Conditional(this.FrameType != JxlFrameType.ReferenceOnly))
        {
            visitor.VisitNested(this.Passes);
        }

        if (visitor.Conditional(this.FrameType == JxlFrameType.Dc))
        {
            this.DcLevel = visitor.U32(Val(1), Val(2), Val(3), Val(4), 1, this.DcLevel);
        }

        if (this.FrameType != JxlFrameType.Dc)
        {
            this.DcLevel = 0;
        }

        bool isPartialFrame = false;
        if (visitor.Conditional(this.FrameType != JxlFrameType.Dc))
        {
            this.CustomSizeOrOrigin = visitor.Bool(false, this.CustomSizeOrOrigin);
            if (visitor.Conditional(this.CustomSizeOrOrigin))
            {
                // The crop is the same for both cases.
                if (visitor.Conditional(this.FrameType is JxlFrameType.Regular or JxlFrameType.SkipProgressive))
                {
                    uint ux0 = JxlBits.PackSigned(this.FrameOriginX0);
                    uint uy0 = JxlBits.PackSigned(this.FrameOriginY0);
                    ux0 = visitor.U32(CropEnc, 0, ux0);
                    uy0 = visitor.U32(CropEnc, 0, uy0);
                    this.FrameOriginX0 = JxlBits.UnpackSigned(ux0);
                    this.FrameOriginY0 = JxlBits.UnpackSigned(uy0);
                }

                // Frame size
                this.FrameSizeX = visitor.U32(CropEnc, 0, this.FrameSizeX);
                this.FrameSizeY = visitor.U32(CropEnc, 0, this.FrameSizeY);
                if (this.CustomSizeOrOrigin && (this.FrameSizeX == 0 || this.FrameSizeY == 0))
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Invalid crop dimensions for frame: zero width or height");
                }

                long imageXSize = this.DefaultXSize;
                long imageYSize = this.DefaultYSize;
                if (this.FrameType is JxlFrameType.Regular or JxlFrameType.SkipProgressive)
                {
                    isPartialFrame |= this.FrameOriginX0 > 0;
                    isPartialFrame |= this.FrameOriginY0 > 0;

                    // libjxl compares in 32-bit signed arithmetic.
                    isPartialFrame |= unchecked((int)this.FrameSizeX + this.FrameOriginX0) < (int)imageXSize;
                    isPartialFrame |= unchecked((int)this.FrameSizeY + this.FrameOriginY0) < (int)imageYSize;
                }
            }
        }

        // Blending info, animation info and whether this is the last frame or not.
        if (visitor.Conditional(this.FrameType is JxlFrameType.Regular or JxlFrameType.SkipProgressive))
        {
            this.BlendingInfo.NonserializedNumExtraChannels = numExtraChannels;
            this.BlendingInfo.NonserializedIsPartialFrame = isPartialFrame;
            visitor.VisitNested(this.BlendingInfo);
            if (visitor.IsReading)
            {
                this.ExtraChannelBlendingInfo.Clear();
                for (int i = 0; i < numExtraChannels; i++)
                {
                    this.ExtraChannelBlendingInfo.Add(new JxlBlendingInfo());
                }
            }
            else
            {
                // libjxl resizes only when reading, so a writer with the wrong count writes a stream
                // the reader cannot parse. Resize for every visitor, as for the upsampling above.
                while (this.ExtraChannelBlendingInfo.Count < numExtraChannels)
                {
                    this.ExtraChannelBlendingInfo.Add(new JxlBlendingInfo());
                }

                this.ExtraChannelBlendingInfo.RemoveRange(numExtraChannels, this.ExtraChannelBlendingInfo.Count - numExtraChannels);
            }

            foreach (JxlBlendingInfo ecBlendingInfo in this.ExtraChannelBlendingInfo)
            {
                ecBlendingInfo.NonserializedIsPartialFrame = isPartialFrame;
                ecBlendingInfo.NonserializedNumExtraChannels = numExtraChannels;
                visitor.VisitNested(ecBlendingInfo);
            }

            if (visitor.IsReading && this.NonserializedIsPreview)
            {
                bool replaceAll = this.BlendingInfo.Mode == JxlBlendMode.Replace;
                foreach (JxlBlendingInfo ecBlendingInfo in this.ExtraChannelBlendingInfo)
                {
                    replaceAll &= ecBlendingInfo.Mode == JxlBlendMode.Replace;
                }

                if (!replaceAll || this.CustomSizeOrOrigin)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Preview is not compatible with blending");
                }
            }

            if (visitor.Conditional(this.NonserializedMetadata != null && this.NonserializedMetadata.M.HaveAnimation))
            {
                this.AnimationFrame.NonserializedMetadata = this.NonserializedMetadata;
                visitor.VisitNested(this.AnimationFrame);
            }

            this.IsLast = visitor.Bool(true, this.IsLast);
        }
        else
        {
            this.IsLast = false;
        }

        // ID of that can be used to refer to this frame. 0 for a non-zero-duration frame means that
        // it will not be referenced. Not necessary for the last frame.
        if (visitor.Conditional(this.FrameType != JxlFrameType.Dc && !this.IsLast))
        {
            this.SaveAsReference = visitor.U32(Val(0), Val(1), Val(2), Val(3), 0, this.SaveAsReference);
        }

        // If this frame is not blended on another frame post-color-transform, it may be stored for
        // being referenced either before or after the color transform. If it is blended
        // post-color-transform, it must be blended after. It must also be blended after if this is a
        // kRegular frame that does not cover the full frame, as samples outside the partial region
        // are from a post-color-transform frame.
        if (this.FrameType != JxlFrameType.Dc)
        {
            if (visitor.Conditional(this.CanBeReferenced && this.BlendingInfo.Mode == JxlBlendMode.Replace && !isPartialFrame && this.FrameType is JxlFrameType.Regular or JxlFrameType.SkipProgressive))
            {
                this.SaveBeforeColorTransform = visitor.Bool(false, this.SaveBeforeColorTransform);
            }
            else if (visitor.Conditional(this.FrameType == JxlFrameType.ReferenceOnly))
            {
                this.SaveBeforeColorTransform = visitor.Bool(true, this.SaveBeforeColorTransform);
                long xsize = this.CustomSizeOrOrigin ? this.FrameSizeX : this.DefaultXSize;
                long ysize = this.CustomSizeOrOrigin ? this.FrameSizeY : this.DefaultYSize;
                if (!this.SaveBeforeColorTransform && (xsize < this.DefaultXSize || ysize < this.DefaultYSize || this.FrameOriginX0 != 0 || this.FrameOriginY0 != 0))
                {
                    JxlThrowHelper.ThrowInvalidImageContentException($"non-patch reference frame with invalid crop: {xsize}x{ysize}{this.FrameOriginX0:+0;-0}{this.FrameOriginY0:+0;-0}");
                }
            }
        }
        else
        {
            this.SaveBeforeColorTransform = true;
        }

        this.name = JxlFieldHelpers.VisitNameString(visitor, this.name);

        this.LoopFilter.NonserializedIsModular = isModular;
        visitor.VisitNested(this.LoopFilter);

        this.Extensions = visitor.BeginExtensions(this.Extensions);

        // Extensions: in chronological order of being added to the format.
        visitor.EndExtensions();
    }
}
