// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// HlgOOTF_Base (its fields, both constructors and Apply) in lib/jxl/cms/tone_mapping.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;

namespace PinataLabs.ImagePastel.Formats.Jxl.Base;

/// <summary>
/// BT.2100's HLG OOTF as libjxl's ICC builder applies it to tone-map an HLG profile, and the state that
/// libjxl shares with the render pipeline's OOTF, <see cref="JxlHlgOotf"/>. They keep libjxl's two separate
/// <c>Apply</c> methods, because they give different values: the pipeline's uses the fast power and sums the
/// luminance from blue to red, this one uses <c>powf</c> and sums from red to blue.
/// </summary>
/// <remarks>
/// The name keeps libjxl's: the "Base" suffix is libjxl's <c>HlgOOTF_Base</c>, as in the port's other scalar
/// classes (<see cref="JxlTfPqBase"/>, <see cref="JxlTfHlgBase"/>, <see cref="JxlRec2408ToneMapperBase"/>).
/// It is not a C# base class. libjxl's <c>HlgOOTF</c> inherits it privately (a C++ <c>class</c> inherits
/// privately by default), so this <c>Apply</c> cannot be called on the pipeline's OOTF. C# has no private
/// inheritance, so <see cref="JxlHlgOotf"/> holds an instance of this class instead and reads its state.
/// </remarks>
internal sealed class JxlHlgOotfBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JxlHlgOotfBase"/> class: the OOTF that takes display
    /// light on a display of <paramref name="sourceLuminance"/> nits to one of
    /// <paramref name="targetLuminance"/> nits, as the ICC builder tone-maps an HLG profile with it.
    /// </summary>
    /// <param name="sourceLuminance">The peak luminance of the source display, in nits.</param>
    /// <param name="targetLuminance">The peak luminance of the target display, in nits.</param>
    /// <param name="primariesLuminances">The luminances of the red, green and blue primaries: the Y row of their XYZ matrix.</param>
    public JxlHlgOotfBase(float sourceLuminance, float targetLuminance, ReadOnlySpan<float> primariesLuminances)
        : this(MathF.Pow(1.111f, Log2(targetLuminance / sourceLuminance)), primariesLuminances)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlHlgOotfBase"/> class from the OOTF's system gamma.
    /// </summary>
    /// <param name="gamma">The system gamma.</param>
    /// <param name="luminances">The luminances of the red, green and blue primaries: the Y row of their XYZ matrix.</param>
    /// <remarks>libjxl's constructor is protected, for <c>HlgOOTF</c>; <see cref="JxlHlgOotf"/> calls this one.</remarks>
    internal JxlHlgOotfBase(float gamma, ReadOnlySpan<float> luminances)
    {
        this.Exponent = gamma - 1;
        this.ApplyOotf = this.Exponent < -0.01f || this.Exponent > 0.01f;
        this.RedY = luminances[0];
        this.GreenY = luminances[1];
        this.BlueY = luminances[2];
    }

    /// <summary>Gets the exponent of the luminance, the system gamma minus 1.</summary>
    internal float Exponent { get; }

    /// <summary>
    /// Gets a value indicating whether the OOTF changes the samples: its exponent is not within 0.01 of 0.
    /// </summary>
    internal bool ApplyOotf { get; }

    /// <summary>Gets the luminance of the red primary.</summary>
    internal float RedY { get; }

    /// <summary>Gets the luminance of the green primary.</summary>
    internal float GreenY { get; }

    /// <summary>Gets the luminance of the blue primary.</summary>
    internal float BlueY { get; }

    /// <summary>
    /// Applies the OOTF to one pixel in place, as libjxl's ICC builder does: with <c>powf</c> and the
    /// luminance summed from red to blue. The render pipeline's <see cref="JxlHlgOotf.Apply"/> differs.
    /// </summary>
    /// <param name="rgb">The red, green and blue samples.</param>
    public void Apply(Span<float> rgb)
    {
        if (!this.ApplyOotf)
        {
            return;
        }

        float luminance = (this.RedY * rgb[0]) + (this.GreenY * rgb[1]) + (this.BlueY * rgb[2]);

        float ratio = JxlScalarMath.StdMin(MathF.Pow(luminance, this.Exponent), 1e9f);
        rgb[0] *= ratio;
        rgb[1] *= ratio;
        rgb[2] *= ratio;
    }

    /// <summary>Returns the base-2 logarithm of a float, C's <c>log2f</c>.</summary>
    /// <param name="x">The value.</param>
    /// <returns>The logarithm.</returns>
    /// <remarks><c>MathF.Log2</c> is not in .NET Standard 2.1.</remarks>
    internal static float Log2(float x)
#if NETCOREAPP3_0_OR_GREATER
        => MathF.Log2(x);
#else
        => (float)(Math.Log(x) / Math.Log(2));
#endif
}
