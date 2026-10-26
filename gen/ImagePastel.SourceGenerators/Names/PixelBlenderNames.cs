// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.SourceGenerators.Names;

/// <summary>
/// The Porter-Duff composers and the color blenders. Each pair of a blender and a composer
/// is one blending function and one default pixel blender. Ported from the arrays that
/// <c>PorterDuffFunctions.Generated.tt</c> and <c>DefaultPixelBlenders.Generated.tt</c>
/// both declared.
/// </summary>
internal static class PixelBlenderNames
{
    /// <summary>
    /// The Porter-Duff composition models.
    /// </summary>
    public static readonly string[] Composers =
    [
        "Src",
        "SrcAtop",
        "SrcOver",
        "SrcIn",
        "SrcOut",
        "Dest",
        "DestAtop",
        "DestOver",
        "DestIn",
        "DestOut",
        "Clear",
        "Xor",
    ];

    /// <summary>
    /// The color blending modes.
    /// </summary>
    public static readonly string[] Blenders =
    [
        "Normal",
        "Multiply",
        "Add",
        "Subtract",
        "Screen",
        "Darken",
        "Lighten",
        "Overlay",
        "HardLight",
    ];
}
