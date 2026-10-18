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

/// <summary>The ids of the Modular sub-streams of a frame, which are a static property of the MA tree.</summary>
internal static class JxlModularStreamId
{
    /// <summary>The number of quantization tables (libjxl's <c>kNumQuantTables</c>).</summary>
    private const int NumQuantTables = 17;

    /// <summary>The id of the global stream.</summary>
    public const int Global = 0;

    /// <summary>Returns the id of the Modular DC stream of DC group <paramref name="groupId"/>.</summary>
    /// <param name="frameDim">The frame dimensions.</param>
    /// <param name="groupId">The DC group.</param>
    /// <returns>The id.</returns>
    public static long ModularDc(in JxlFrameDimensions frameDim, long groupId) => 1 + frameDim.NumDcGroups + groupId;

    /// <summary>Returns the id of the Modular AC stream of group <paramref name="groupId"/> in pass <paramref name="passId"/>.</summary>
    /// <param name="frameDim">The frame dimensions.</param>
    /// <param name="groupId">The group.</param>
    /// <param name="passId">The pass.</param>
    /// <returns>The id.</returns>
    public static long ModularAc(in JxlFrameDimensions frameDim, long groupId, long passId)
        => 1 + (3 * frameDim.NumDcGroups) + NumQuantTables + (frameDim.NumGroups * passId) + groupId;
}
