// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// HlgOOTF (ToSceneLight and the vector Apply) in lib/jxl/cms/tone_mapping-inl.h, for one lane.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Formats.Jxl.Base;

/// <summary>
/// BT.2100's HLG OOTF as libjxl's render pipeline applies it: it scales the red, green and blue samples
/// of a pixel by a power of their luminance.
/// </summary>
/// <remarks>
/// A deliberate departure from libjxl (ADR 0004): each <c>MulAdd</c> is split into a multiply and an add,
/// and <see cref="JxlFastMath.FastPowf"/> stands for libjxl's fast power.
/// <para>
/// libjxl's <c>HlgOOTF</c> inherits <c>HlgOOTF_Base</c> privately, so that class's scalar <c>Apply</c>,
/// which gives other values, is not part of it. C# has no private inheritance, so this class holds a
/// <see cref="JxlHlgOotfBase"/> for the state they share and does not derive from it.
/// </para>
/// </remarks>
internal sealed class JxlHlgOotf
{
    // HlgOOTF_Base's state: the exponent, whether to apply the OOTF, and the luminances of the primaries.
    private readonly JxlHlgOotfBase state;

    private JxlHlgOotf(float gamma, ReadOnlySpan<float> luminances)
        => this.state = new JxlHlgOotfBase(gamma, luminances);

    /// <summary>
    /// Returns the OOTF that takes display light on a display of <paramref name="displayLuminance"/> nits
    /// back to scene light, libjxl's <c>HlgOOTF::ToSceneLight</c>. Near 300 nits it changes nothing.
    /// </summary>
    /// <param name="displayLuminance">The peak luminance of the display, in nits.</param>
    /// <param name="primariesLuminances">The luminances of the red, green and blue primaries.</param>
    /// <returns>The OOTF.</returns>
    public static JxlHlgOotf ToSceneLight(float displayLuminance, ReadOnlySpan<float> primariesLuminances)
        => new((1 / 1.2f) * MathF.Pow(1.111f, -JxlHlgOotfBase.Log2(displayLuminance / 1000.0f)), primariesLuminances);

    /// <summary>Applies the OOTF to one pixel.</summary>
    /// <param name="red">The red sample.</param>
    /// <param name="green">The green sample.</param>
    /// <param name="blue">The blue sample.</param>
    [MethodImpl(InliningOptions.ShortMethod)]
    public void Apply(ref float red, ref float green, ref float blue)
    {
        JxlHlgOotfBase s = this.state;
        if (!s.ApplyOotf)
        {
            return;
        }

        // libjxl: MulAdd(Set(df, red_Y_), *red, MulAdd(Set(df, green_Y_), *green, Mul(Set(df, blue_Y_), *blue))).
        float luminance = s.BlueY * blue;
        luminance = (s.GreenY * green) + luminance;
        luminance = (s.RedY * red) + luminance;
        float ratio = MathF.Min(JxlFastMath.FastPowf(luminance, s.Exponent), 1e9f);
        red *= ratio;
        green *= ratio;
        blue *= ratio;
    }
}
