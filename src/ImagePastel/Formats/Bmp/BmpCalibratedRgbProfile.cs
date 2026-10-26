// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.ColorManagement;
using PinataLabs.ImagePastel.ColorSpaces.Conversion;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;

namespace PinataLabs.ImagePastel.Formats.Bmp;

/// <summary>
/// Builds the ICC profile that an <c>LCS_CALIBRATED_RGB</c> BITMAPV4HEADER or BITMAPV5HEADER describes
/// with its CIEXYZ endpoints and per-channel gamma values.
/// </summary>
/// <remarks>
/// <para>
/// Only the chromaticity of each endpoint is used: writers store either real XYZ values or the xyz
/// chromaticity itself, and both give the same x and y. The header has no white point; the endpoints
/// of a chromaticity-only writer don't add up to one, so the white is taken to be D65, as Firefox does.
/// </para>
/// <para>
/// Endpoints that are all zero (what this encoder and many other writers put in a V4 header that
/// describes no color space), negative, or degenerate, and gammas of zero, give no profile.
/// </para>
/// </remarks>
internal static class BmpCalibratedRgbProfile
{
    // Unsigned 16.16 fixed point. The endpoints are FXPT2DOT30, but their scale cancels out of x and y.
    private const double GammaScale = 1D / 65536;

    private static readonly CieXyChromaticityCoordinates D65 = new(0.3127F, 0.3290F);

    /// <summary>
    /// Builds the profile the header's endpoints and gamma values describe.
    /// </summary>
    /// <param name="header">The info header. Its <see cref="BmpInfoHeader.CsType"/> is not checked.</param>
    /// <returns>The profile, or null when the values describe no usable color space.</returns>
    public static IccProfile Create(in BmpInfoHeader header)
    {
        if (!TryGetChromaticity(header.RedX, header.RedY, header.RedZ, out CieXyChromaticityCoordinates red)
            || !TryGetChromaticity(header.GreenX, header.GreenY, header.GreenZ, out CieXyChromaticityCoordinates green)
            || !TryGetChromaticity(header.BlueX, header.BlueY, header.BlueZ, out CieXyChromaticityCoordinates blue)
            || header.GammaRed == 0
            || header.GammaGreen == 0
            || header.GammaBlue == 0)
        {
            return null;
        }

        try
        {
            return IccProfileFactory.CreateRgb(
                new RgbPrimariesChromaticityCoordinates(red, green, blue),
                D65,
                GetGamma(header.GammaRed),
                GetGamma(header.GammaGreen),
                GetGamma(header.GammaBlue),
                GetRenderingIntent(header.Intent),
                "Calibrated RGB");
        }
        catch (ArgumentException)
        {
            // Collinear primaries, or a gamma too large for an ICC profile.
            return null;
        }
    }

    private static bool TryGetChromaticity(int x, int y, int z, out CieXyChromaticityCoordinates chromaticity)
    {
        if (x < 0 || y <= 0 || z < 0)
        {
            chromaticity = default;
            return false;
        }

        double sum = (double)x + y + z;
        chromaticity = new CieXyChromaticityCoordinates((float)(x / sum), (float)(y / sum));
        return true;
    }

    private static IccToneCurve GetGamma(int gamma) => IccToneCurve.Gamma((float)((uint)gamma * GammaScale));

    /// <summary>
    /// Maps a <c>bV5Intent</c> value to the ICC rendering intent the Microsoft documentation pairs it with.
    /// A V4 header has no intent field, and gets perceptual.
    /// </summary>
    private static IccRenderingIntent GetRenderingIntent(BmpRenderingIntent intent) => intent switch
    {
        BmpRenderingIntent.Business => IccRenderingIntent.Saturation,
        BmpRenderingIntent.Graphics => IccRenderingIntent.MediaRelativeColorimetric,
        BmpRenderingIntent.AbsoluteColorimetric => IccRenderingIntent.AbsoluteColorimetric,
        _ => IccRenderingIntent.Perceptual,
    };
}
