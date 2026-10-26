// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.ColorSpaces.Companding;

namespace PinataLabs.ImagePastel.PixelFormats;

/// <summary>
/// Flags responsible to select additional operations which could be efficiently applied in
/// <see cref="PixelOperations{TPixel}.ToVector4(PinataLabs.ImagePastel.Configuration,System.ReadOnlySpan{TPixel},System.Span{System.Numerics.Vector4},PinataLabs.ImagePastel.PixelFormats.PixelConversionModifiers)"/>
/// or
/// <see cref="PixelOperations{TPixel}.FromVector4Destructive(PinataLabs.ImagePastel.Configuration,System.Span{System.Numerics.Vector4},System.Span{TPixel},PinataLabs.ImagePastel.PixelFormats.PixelConversionModifiers)"/>
/// knowing the pixel type.
/// </summary>
[Flags]
public enum PixelConversionModifiers
{
    /// <summary>
    /// No special operation is selected
    /// </summary>
    None = 0,

    /// <summary>
    /// Select <see cref="IPixel.ToScaledVector4"/> and <see cref="IPixel.FromScaledVector4"/> instead the standard (non scaled) variants.
    /// </summary>
    Scale = 1 << 0,

    /// <summary>
    /// Enable alpha premultiplication / unpremultiplication
    /// </summary>
    Premultiply = 1 << 1,

    /// <summary>
    /// Enable SRGB companding (defined in <see cref="SRgbCompanding"/>).
    /// </summary>
    SRgbCompand = 1 << 2,
}
