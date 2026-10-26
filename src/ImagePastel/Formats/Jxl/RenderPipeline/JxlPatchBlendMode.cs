// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// PatchBlendMode in lib/jxl/dec_patch_dictionary.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// How a patch, or a frame, blends onto what is under it. In the <c>Above</c> modes the new values are
/// on top; in the <c>Below</c> modes they are underneath.
/// </summary>
internal enum JxlPatchBlendMode : byte
{
    /// <summary>The old values are kept.</summary>
    None = 0,

    /// <summary>The new values replace the old ones.</summary>
    Replace = 1,

    /// <summary>The new values are added to the old ones.</summary>
    Add = 2,

    /// <summary>The old values are multiplied by the new ones.</summary>
    Mul = 3,

    /// <summary>The new values are alpha-blended over the old ones.</summary>
    BlendAbove = 4,

    /// <summary>The new values are alpha-blended under the old ones.</summary>
    BlendBelow = 5,

    /// <summary>The new values, weighted by their alpha, are added to the old ones.</summary>
    AlphaWeightedAddAbove = 6,

    /// <summary>The old values, weighted by their alpha, are added to the new ones.</summary>
    AlphaWeightedAddBelow = 7,
}
