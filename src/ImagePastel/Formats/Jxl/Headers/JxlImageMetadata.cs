// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/image_metadata.h and lib/jxl/image_metadata.cc (BitDepth, ExtraChannelInfo,
// OpsinInverseMatrix, ToneMapping, CustomTransformData, ImageMetadata, CodecMetadata),
// VisitNameString from lib/jxl/frame_header.h, and the default constants of
// lib/jxl/cms/opsin_params.h, lib/jxl/quantizer.h and lib/jxl/base/common.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Headers;

/// <summary>The image metadata of the codestream header.</summary>
internal sealed class JxlImageMetadata : JxlFields
{
    private uint orientation = 1;

    public JxlImageMetadata() => JxlBundle.Init(this);

    /// <summary>Gets the sample format of the colour channels.</summary>
    public JxlBitDepth BitDepth { get; } = new();

    /// <summary>Gets or sets a value indicating whether 16-bit buffers suffice for Modular decoding; otherwise 32 bits are needed.</summary>
    public bool Modular16BitBufferSufficient { get; set; }

    /// <summary>Gets or sets a value indicating whether the colour is stored as XYB.</summary>
    public bool XybEncoded { get; set; }

    /// <summary>Gets the colour encoding.</summary>
    public JxlColorEncodingHeader ColorEncoding { get; } = new();

    /// <summary>Gets or sets the EXIF orientation, 1 to 8.</summary>
    public uint Orientation
    {
        get => this.orientation;
        set => this.orientation = value;
    }

    /// <summary>Gets or sets a value indicating whether a preview frame comes first.</summary>
    public bool HavePreview { get; set; }

    /// <summary>Gets or sets a value indicating whether the image is an animation.</summary>
    public bool HaveAnimation { get; set; }

    /// <summary>Gets or sets a value indicating whether the image has an intrinsic size.</summary>
    public bool HaveIntrinsicSize { get; set; }

    /// <summary>Gets the intrinsic size. Only valid if <see cref="HaveIntrinsicSize"/>.</summary>
    public JxlSizeHeader IntrinsicSize { get; } = new();

    /// <summary>Gets the tone-mapping information.</summary>
    public JxlToneMapping ToneMapping { get; } = new();

    /// <summary>Gets the extra channels, in codestream order.</summary>
    public List<JxlExtraChannelInfo> ExtraChannelInfo { get; } = new();

    /// <summary>Gets the preview size. Only valid if <see cref="HavePreview"/>.</summary>
    public JxlPreviewHeader PreviewSize { get; } = new();

    /// <summary>Gets the animation header. Only valid if <see cref="HaveAnimation"/>.</summary>
    public JxlAnimationHeader Animation { get; } = new();

    /// <summary>Gets or sets the extension bits.</summary>
    public ulong Extensions { get; set; }

    /// <summary>Gets the number of extra channels.</summary>
    public int NumExtraChannels => this.ExtraChannelInfo.Count;

    /// <summary>
    /// Gets the number of colour planes the decoder renders: one for grey stored as grey, three
    /// otherwise. libjxl always renders three and copies grey into each; one plane is the layout of
    /// the conformance seam (issue #111). XYB grey is three planes until the XYB conversion makes it grey.
    /// </summary>
    public int NumColorPlanes => this.ColorEncoding.IsGray && !this.XybEncoded ? 1 : 3;

    /// <summary>
    /// Gets the number of colour planes the sinks receive: one for a grey image, three otherwise. It
    /// differs from <see cref="NumColorPlanes"/> only for XYB grey, which the pipeline renders as three
    /// equal planes and hands over as one.
    /// </summary>
    public int NumOutputColorPlanes => this.ColorEncoding.IsGray ? 1 : 3;

    /// <summary>
    /// Returns the index of the first extra channel of type <paramref name="type"/>, or -1. libjxl's
    /// <c>Find</c> returns the channel itself; the index also gives its render plane.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The index, or -1.</returns>
    public int Find(JxlExtraChannelType type) => this.ExtraChannelInfo.FindIndex(eci => eci.Type == type);

    /// <summary>Returns the orientation, as libjxl's <c>GetOrientation</c>.</summary>
    /// <returns>The orientation.</returns>
    public JxlOrientation GetOrientation() => (JxlOrientation)this.orientation;

    public override void VisitFields(JxlVisitor visitor)
    {
        if (visitor.AllDefault(this))
        {
            visitor.SetDefault(this);
            return;
        }

        // Bundle::AllDefault does not allow usage when reading (it may abort the program when a
        // codestream has invalid values), but when reading we overwrite the extra_fields value, so do
        // not need to call AllDefault.
        bool toneMappingDefault = !visitor.IsReading && JxlBundle.AllDefault(this.ToneMapping);

        bool extraFields = this.orientation != 1 || this.HavePreview || this.HaveAnimation || this.HaveIntrinsicSize || !toneMappingDefault;
        extraFields = visitor.Bool(false, extraFields);
        if (visitor.Conditional(extraFields))
        {
            // The encoded value is orientation - 1.
            this.orientation = visitor.Bits(3, 0, unchecked(this.orientation - 1)) + 1;

            // Intrinsic size, preview and animation follow the orientation, in that order.
            this.HaveIntrinsicSize = visitor.Bool(false, this.HaveIntrinsicSize);
            if (visitor.Conditional(this.HaveIntrinsicSize))
            {
                visitor.VisitNested(this.IntrinsicSize);
            }

            this.HavePreview = visitor.Bool(false, this.HavePreview);
            if (visitor.Conditional(this.HavePreview))
            {
                visitor.VisitNested(this.PreviewSize);
            }

            this.HaveAnimation = visitor.Bool(false, this.HaveAnimation);
            if (visitor.Conditional(this.HaveAnimation))
            {
                visitor.VisitNested(this.Animation);
            }
        }
        else
        {
            this.orientation = 1; // identity
            this.HaveIntrinsicSize = false;
            this.HavePreview = false;
            this.HaveAnimation = false;
        }

        visitor.VisitNested(this.BitDepth);
        this.Modular16BitBufferSufficient = visitor.Bool(true, this.Modular16BitBufferSufficient);

        uint numExtraChannels = visitor.U32(Val(0), Val(1), BitsOffset(4, 2), BitsOffset(12, 1), 0, (uint)this.ExtraChannelInfo.Count);

        if (visitor.Conditional(numExtraChannels != 0))
        {
            if (visitor.IsReading)
            {
                this.ExtraChannelInfo.Clear();
                for (uint i = 0; i < numExtraChannels; i++)
                {
                    this.ExtraChannelInfo.Add(new JxlExtraChannelInfo());
                }
            }

            foreach (JxlExtraChannelInfo eci in this.ExtraChannelInfo)
            {
                visitor.VisitNested(eci);
            }
        }

        this.XybEncoded = visitor.Bool(true, this.XybEncoded);
        visitor.VisitNested(this.ColorEncoding);
        if (visitor.Conditional(extraFields))
        {
            visitor.VisitNested(this.ToneMapping);
        }

        // Treat as if only the fields up to extra channels exist.
        this.Extensions = visitor.BeginExtensions(this.Extensions);

        // Extensions: in chronological order of being added to the format.
        visitor.EndExtensions();
    }
}
