// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// HlgOOTF_Base (its fields and the constructor from a gamma) in lib/jxl/cms/tone_mapping.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace SixLabors.ImageSharp.Formats.Jxl.Base;

/// <summary>
/// The state of BT.2100's HLG OOTF, which libjxl shares between two OOTFs: the render pipeline's
/// <see cref="JxlHlgOotf"/>, and the ICC builder's scalar one, which tone-maps an HLG profile. They keep
/// libjxl's two separate <c>Apply</c> methods, because they give different values: the pipeline's uses the
/// fast power and sums the luminance from blue to red, the ICC builder's uses <c>powf</c> and sums from red
/// to blue.
/// </summary>
internal class JxlHlgOotfBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JxlHlgOotfBase"/> class from the OOTF's system gamma.
    /// </summary>
    /// <param name="gamma">The system gamma.</param>
    /// <param name="luminances">The luminances of the red, green and blue primaries: the Y row of their XYZ matrix.</param>
    protected JxlHlgOotfBase(float gamma, ReadOnlySpan<float> luminances)
    {
        this.Exponent = gamma - 1;
        this.ApplyOotf = this.Exponent < -0.01f || this.Exponent > 0.01f;
        this.RedY = luminances[0];
        this.GreenY = luminances[1];
        this.BlueY = luminances[2];
    }

    /// <summary>Gets the exponent of the luminance, the system gamma minus 1.</summary>
    protected float Exponent { get; }

    /// <summary>
    /// Gets a value indicating whether the OOTF changes the samples: its exponent is not within 0.01 of 0.
    /// </summary>
    protected bool ApplyOotf { get; }

    /// <summary>Gets the luminance of the red primary.</summary>
    protected float RedY { get; }

    /// <summary>Gets the luminance of the green primary.</summary>
    protected float GreenY { get; }

    /// <summary>Gets the luminance of the blue primary.</summary>
    protected float BlueY { get; }
}
