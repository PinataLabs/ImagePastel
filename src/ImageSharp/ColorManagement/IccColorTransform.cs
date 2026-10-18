// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;
using SixLabors.ImageSharp.ColorManagement.Icc;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.ColorManagement;

/// <summary>
/// Converts colors from one ICC profile to another: from the source profile's device values to the
/// profile connection space (PCS), and from there to the target profile's device values. Build one
/// for a pair of profiles and use it for as many images or rows as needed; after construction it
/// holds no mutable state, so one instance can be used from several threads at once.
/// </summary>
/// <remarks>
/// <para>
/// A <see langword="null"/> profile means sRGB, the color space of an image without a pixel profile.
/// The engine describes sRGB itself from the IEC 61966-2-1 parameters.
/// </para>
/// <para>
/// Supported so far: RGB matrix/TRC profiles (<c>rXYZ</c>, <c>gXYZ</c>, <c>bXYZ</c>, <c>rTRC</c>,
/// <c>gTRC</c>, <c>bTRC</c>) and gray profiles (<c>kTRC</c>), with an XYZ or Lab PCS, ICC v2 or v4.
/// Tone curves may be <c>curveType</c> (identity, gamma or table) or <c>parametricCurveType</c>
/// (function types 0 to 4).
/// </para>
/// </remarks>
public sealed class IccColorTransform
{
    private readonly IccPipeline pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="IccColorTransform"/> class with the default options.
    /// </summary>
    /// <param name="source">The profile of the colors to convert, or <see langword="null"/> for sRGB.</param>
    /// <param name="target">The profile to convert to, or <see langword="null"/> for sRGB.</param>
    /// <exception cref="InvalidImageContentException">A profile is corrupt.</exception>
    /// <exception cref="NotSupportedException">The engine does not support a profile yet (for example a LUT-based or CMYK profile).</exception>
    public IccColorTransform(IccProfile source, IccProfile target)
        : this(source, target, new IccColorTransformOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IccColorTransform"/> class.
    /// </summary>
    /// <param name="source">The profile of the colors to convert, or <see langword="null"/> for sRGB.</param>
    /// <param name="target">The profile to convert to, or <see langword="null"/> for sRGB.</param>
    /// <param name="options">The options. Their values are copied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">A profile is corrupt.</exception>
    /// <exception cref="NotSupportedException">
    /// The engine does not support a profile or an option yet (for example a LUT-based or CMYK
    /// profile, a rendering intent other than perceptual, or black-point compensation).
    /// </exception>
    public IccColorTransform(IccProfile source, IccProfile target, IccColorTransformOptions options)
    {
        Guard.NotNull(options, nameof(options));

        if (options.RenderingIntent != IccRenderingIntent.Perceptual)
        {
            throw new NotSupportedException($"The {options.RenderingIntent} rendering intent is not supported yet.");
        }

        if (options.BlackPointCompensation)
        {
            throw new NotSupportedException("Black-point compensation is not supported yet.");
        }

        IccProfileModel sourceModel = source is null ? IccSrgb.Create("source") : IccProfileModel.Read(source, asSource: true);
        IccProfileModel targetModel = target is null ? IccSrgb.Create("target") : IccProfileModel.Read(target, asSource: false);

        List<IccStage> stages = [];
        sourceModel.AddDeviceToPcs(stages);
        if (sourceModel.ConnectionSpace != targetModel.ConnectionSpace)
        {
            stages.Add(sourceModel.ConnectionSpace == IccColorSpaceType.CieLab ? IccLabToXyzStage.Instance : IccXyzToLabStage.Instance);
        }

        targetModel.AddPcsToDevice(stages);
        this.pipeline = new IccPipeline(stages);
    }

    /// <summary>
    /// Converts colors in place. Each <see cref="Vector4"/> holds red, green, blue and alpha (or gray
    /// in all three color channels) scaled to [0, 1], as <c>PixelConversionModifiers.Scale</c>
    /// gives them. Alpha (W) is not changed. Results outside the target gamut are not clamped.
    /// </summary>
    /// <param name="colors">The colors to convert.</param>
    public void Transform(Span<Vector4> colors) => this.pipeline.Invoke(colors);
}
