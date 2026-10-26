// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;
using PinataLabs.ImagePastel.ColorManagement.Icc;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;

namespace PinataLabs.ImagePastel.ColorManagement;

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
/// Supported so far: RGB, gray and CMYK profiles with an XYZ or Lab PCS, ICC v2 or v4, either
/// matrix/TRC (<c>rXYZ</c>, <c>gXYZ</c>, <c>bXYZ</c>, <c>rTRC</c>, <c>gTRC</c>, <c>bTRC</c>),
/// gray (<c>kTRC</c>), or LUT-based (<c>A2Bn</c> for a source and <c>B2An</c> for a target, of
/// type <c>lut8Type</c>, <c>lut16Type</c>, <c>lutAToBType</c> or <c>lutBToAType</c>). A profile
/// with a LUT-based tag for the rendering intent (or for perceptual) is used through it; Little CMS
/// does the same. <see cref="IccColorTransformOptions"/> describes the intents and black-point
/// compensation. Tone curves may be
/// <c>curveType</c> (identity, gamma or table) or <c>parametricCurveType</c> (function types 0 to 4).
/// Tables are interpolated as Little CMS interpolates them: tetrahedrally for three inputs, except
/// trilinearly for the Lab side of a target's table.
/// </para>
/// <para>
/// CMYK colors fill all four channels of a <see cref="Vector4"/>: cyan, magenta, yellow and black
/// in X, Y, Z and W, each from 0 (no ink) to 1 (full ink). Converting from a CMYK profile gives
/// opaque colors (W is 1); converting to one writes black over W.
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
    /// <exception cref="NotSupportedException">
    /// The engine does not support a profile (for example a device link profile, a profile of another
    /// color space, or one with a <c>D2B0</c> multi-process element table).
    /// </exception>
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
    /// <exception cref="ArgumentOutOfRangeException">
    /// The rendering intent of <paramref name="options"/> is not one of the four ICC intents.
    /// </exception>
    /// <exception cref="InvalidImageContentException">A profile is corrupt.</exception>
    /// <exception cref="NotSupportedException">
    /// The engine does not support a profile (for example a device link profile, a profile of another
    /// color space, or one with a <c>D2Bn</c> or <c>B2Dn</c> multi-process element table for the intent).
    /// </exception>
    public IccColorTransform(IccProfile source, IccProfile target, IccColorTransformOptions options)
    {
        Guard.NotNull(options, nameof(options));

        IccRenderingIntent intent = options.RenderingIntent;
        if (intent is not (IccRenderingIntent.Perceptual or IccRenderingIntent.MediaRelativeColorimetric or IccRenderingIntent.Saturation or IccRenderingIntent.AbsoluteColorimetric))
        {
            throw new ArgumentOutOfRangeException(nameof(options), intent, "The rendering intent is not one the ICC specification defines.");
        }

        List<IccStage> stages = IccIntentLink.Link(source, target, intent, options.BlackPointCompensation);
        this.pipeline = new IccPipeline(stages);
        this.SourceColorSpace = source?.Header.DataColorSpace ?? IccColorSpaceType.Rgb;
        this.TargetColorSpace = target?.Header.DataColorSpace ?? IccColorSpaceType.Rgb;
    }

    /// <summary>
    /// Gets the data color space of the source profile: RGB, gray or CMYK.
    /// </summary>
    internal IccColorSpaceType SourceColorSpace { get; }

    /// <summary>
    /// Gets the data color space of the target profile: RGB, gray or CMYK.
    /// </summary>
    internal IccColorSpaceType TargetColorSpace { get; }

    /// <summary>
    /// Converts colors in place. Each <see cref="Vector4"/> holds red, green, blue and alpha (or gray
    /// in all three color channels, or CMYK in all four) scaled to [0, 1], as
    /// <c>PixelConversionModifiers.Scale</c> gives them. Alpha (W) is not changed unless a profile
    /// is CMYK. Results outside the target gamut are not clamped.
    /// </summary>
    /// <param name="colors">The colors to convert.</param>
    public void Transform(Span<Vector4> colors) => this.pipeline.Invoke(colors);
}
