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

/// <summary>The codestream headers: size, image metadata and transform data.</summary>
internal sealed class JxlCodecMetadata
{
    /// <summary>Gets the image metadata.</summary>
    public JxlImageMetadata M { get; } = new();

    /// <summary>Gets the image size.</summary>
    public JxlSizeHeader Size { get; } = new();

    /// <summary>Gets the transform data.</summary>
    public JxlCustomTransformData TransformData { get; } = new();

    /// <summary>Gets the image width.</summary>
    public ulong XSize => this.Size.XSize;

    /// <summary>Gets the image height.</summary>
    public ulong YSize => this.Size.YSize;

    /// <summary>
    /// Gets the width of the image once it is oriented: libjxl's <c>oriented_xsize</c> without keeping
    /// the orientation, which this decoder always undoes. Orientations 5 to 8 swap width and height.
    /// </summary>
    public ulong OrientedXSize => (uint)this.M.GetOrientation() > 4 ? this.YSize : this.XSize;

    /// <summary>Gets the height of the image once it is oriented: libjxl's <c>oriented_ysize</c>.</summary>
    public ulong OrientedYSize => (uint)this.M.GetOrientation() > 4 ? this.XSize : this.YSize;
}
