// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// SplineSegment in lib/jxl/splines.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// A single "drawable unit" of a spline, i.e. a line of the region in which we render each Gaussian.
/// The structure doesn't actually depend on the exact row, which allows reuse for different y values
/// (which are tracked separately).
/// </summary>
internal readonly struct JxlSplineSegment
{
    public JxlSplineSegment(float centerX, float centerY, float maximumDistance, float invSigma, float sigmaOver4TimesIntensity, float colorX, float colorY, float colorB)
    {
        this.CenterX = centerX;
        this.CenterY = centerY;
        this.MaximumDistance = maximumDistance;
        this.InvSigma = invSigma;
        this.SigmaOver4TimesIntensity = sigmaOver4TimesIntensity;
        this.ColorX = colorX;
        this.ColorY = colorY;
        this.ColorB = colorB;
    }

    public float CenterX { get; }

    public float CenterY { get; }

    /// <summary>Gets the distance from the centre beyond which the segment adds nothing worth drawing.</summary>
    public float MaximumDistance { get; }

    public float InvSigma { get; }

    public float SigmaOver4TimesIntensity { get; }

    public float ColorX { get; }

    public float ColorY { get; }

    public float ColorB { get; }
}
