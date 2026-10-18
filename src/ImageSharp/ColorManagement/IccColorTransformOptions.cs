// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.ColorManagement;

/// <summary>
/// Options for an <see cref="IccColorTransform"/>.
/// </summary>
/// <remarks>
/// The engine supports only the defaults so far: the perceptual rendering intent, without
/// black-point compensation. It uses a profile's perceptual tables where the profile has them and
/// the relative colorimetric result otherwise, which for matrix/TRC and gray profiles is the only
/// result they define. Any other value makes <see cref="IccColorTransform"/> throw
/// <see cref="System.NotSupportedException"/>.
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
