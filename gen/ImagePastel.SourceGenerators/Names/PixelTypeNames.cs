// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.SourceGenerators.Names;

/// <summary>
/// The pixel types that the library's pixel operations are generated for. Ported from
/// <c>PixelFormats/PixelImplementations/PixelOperations/Generated/_Common.ttinclude</c> and
/// <c>PixelFormats/PixelOperations{TPixel}.Generated.tt</c>, which held the same common list.
/// </summary>
internal static class PixelTypeNames
{
    /// <summary>
    /// The pixel types with specialized <c>PixelOperations</c>, in the order of the generated
    /// conversion methods.
    /// </summary>
    public static readonly string[] CommonPixelTypes =
    [
        "Argb32",
        "Abgr32",
        "Bgr24",
        "Bgra32",
        "L8",
        "L16",
        "La16",
        "La32",
        "Rgb24",
        "Rgba32",
        "Rgb48",
        "Rgba64",
        "Bgra5551",
    ];

    /// <summary>
    /// The pixel types that convert to each other through <c>PixelConverter</c>.
    /// </summary>
    public static readonly string[] OptimizedPixelTypes =
    [
        "Rgba32",
        "Argb32",
        "Abgr32",
        "Bgra32",
        "Rgb24",
        "Bgr24",
    ];

    /// <summary>
    /// The pixel types with an Rgba32-compatible conversion to and from <c>Vector4</c>.
    /// </summary>
    public static readonly string[] Rgba32CompatibleTypes =
    [
        "Argb32",
        "Abgr32",
        "Bgra32",
        "Rgb24",
        "Bgr24",
    ];
}
