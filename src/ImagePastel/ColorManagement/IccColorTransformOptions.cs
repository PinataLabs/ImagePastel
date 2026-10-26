// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.ColorManagement;

/// <summary>
/// Options for an <see cref="IccColorTransform"/>.
/// </summary>
/// <remarks>
/// <para>
/// The rendering intent chooses each profile's table: <c>A2B0</c>/<c>B2A0</c> for perceptual,
/// <c>A2B1</c>/<c>B2A1</c> for relative and absolute colorimetric, <c>A2B2</c>/<c>B2A2</c> for
/// saturation. A profile without the intent's table is used through its perceptual table, and a
/// profile with neither through its matrix/TRC tags (or gray curve), as Little CMS does.
/// Absolute colorimetric scales the relative colorimetric result by the ratio of the two media
/// white points (<c>wtpt</c>); a version 2 display profile's media white counts as D50.
/// </para>
/// <para>
/// Black-point compensation follows Adobe's published algorithm as Little CMS implements it: it
/// maps the source black point to the target black point in the profile connection space, so a
/// target with a lighter black doesn't crush the shadows. As in Little CMS, it never applies to
/// absolute colorimetric, and it always applies to perceptual and saturation when the target
/// profile is version 4 (or sRGB, which the engine treats as version 4), whatever
/// <see cref="BlackPointCompensation"/> says.
/// </para>
/// </remarks>
public sealed class IccColorTransformOptions
{
    /// <summary>
    /// Gets or sets the rendering intent. Defaults to <see cref="IccRenderingIntent.Perceptual"/>.
    /// </summary>
    public IccRenderingIntent RenderingIntent { get; set; } = IccRenderingIntent.Perceptual;

    /// <summary>
    /// Gets or sets a value indicating whether to apply black-point compensation. Defaults to <see langword="false"/>.
    /// </summary>
    public bool BlackPointCompensation { get; set; }
}
