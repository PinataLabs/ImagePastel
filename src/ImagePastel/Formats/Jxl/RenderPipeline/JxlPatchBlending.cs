// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// PatchBlending in lib/jxl/dec_patch_dictionary.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// How one channel group blends: the colour planes, or one extra channel. Patches and frame blending
/// both use it, as in libjxl.
/// </summary>
internal readonly struct JxlPatchBlending
{
    public JxlPatchBlending(JxlPatchBlendMode mode, uint alphaChannel, bool clamp)
    {
        this.Mode = mode;
        this.AlphaChannel = alphaChannel;
        this.Clamp = clamp;
    }

    public JxlPatchBlendMode Mode { get; }

    /// <summary>Gets the extra channel used as alpha by blend modes that need one.</summary>
    public uint AlphaChannel { get; }

    /// <summary>Gets a value indicating whether the alpha, or the multiplier of <see cref="JxlPatchBlendMode.Mul"/>, is clamped to [0, 1].</summary>
    public bool Clamp { get; }
}
