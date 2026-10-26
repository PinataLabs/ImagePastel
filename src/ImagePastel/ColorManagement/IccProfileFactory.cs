// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.ColorManagement.Icc;
using PinataLabs.ImagePastel.ColorSpaces.Conversion;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;

namespace PinataLabs.ImagePastel.ColorManagement;

/// <summary>
/// Creates ICC profiles for standard color spaces, and for RGB and gray spaces given by their primaries,
/// white point and tone curve, without an ICC file.
/// </summary>
/// <remarks>
/// <para>
/// The profiles are ICC v4.4 matrix/TRC display profiles with an XYZ connection space and the tags
/// libjxl's <c>MaybeCreateProfile</c> writes: <c>desc</c>, <c>cprt</c>, <c>wtpt</c>, <c>chad</c> (RGB),
/// <c>rXYZ</c>/<c>gXYZ</c>/<c>bXYZ</c> (RGB) and <c>rTRC</c>/<c>gTRC</c>/<c>bTRC</c> or <c>kTRC</c>. The
/// standard spaces that ITU-T H.273 has code points for (sRGB, linear sRGB, Display P3 and Rec. 2020)
/// also get a <c>cicp</c> tag with those code points, as libjxl writes one. The
/// colorants are adapted to the D50 connection space with the Bradford transform. Unlike libjxl, which
/// adapts to CIE D50 (0.96422, 1, 0.82521), they adapt to the ICC PCS illuminant (0.9642, 1, 0.8249), as
/// Little CMS does, so the colorants add up to the PCS white.
/// </para>
/// <para>
/// Every call returns a new <see cref="IccProfile"/> that has been written and read back, so it passes
/// <see cref="IccProfile.CheckIsValid"/>, its header carries its size, and changing it changes no other
/// profile.
/// </para>
/// </remarks>
public static class IccProfileFactory
{
    private static readonly (double X, double Y)[] AdobeRgbPrimaries = { (0.64, 0.33), (0.21, 0.71), (0.15, 0.06) };

    /// <summary>
    /// Creates an sRGB (IEC 61966-2-1) profile: BT.709 primaries, a D65 white and the sRGB curve.
    /// </summary>
    /// <returns>The profile.</returns>
    public static IccProfile CreateSrgb() => IccStandardRgb.Create(IccStandardRgb.Rec709Primaries, IccToneCurve.Srgb, "sRGB", (IccStandardRgb.PrimariesBt709, IccStandardRgb.TransferSrgb));

    /// <summary>
    /// Creates a linear sRGB profile: BT.709 primaries, a D65 white and a linear curve.
    /// </summary>
    /// <returns>The profile.</returns>
    public static IccProfile CreateLinearSrgb() => IccStandardRgb.Create(IccStandardRgb.Rec709Primaries, IccToneCurve.Linear, "Linear sRGB", (IccStandardRgb.PrimariesBt709, IccStandardRgb.TransferLinear));

    /// <summary>
    /// Creates a Display P3 profile: DCI-P3 primaries, a D65 white and the sRGB curve.
    /// </summary>
    /// <returns>The profile.</returns>
    public static IccProfile CreateDisplayP3() => IccStandardRgb.Create(IccStandardRgb.P3Primaries, IccToneCurve.Srgb, "Display P3", (IccStandardRgb.PrimariesP3D65, IccStandardRgb.TransferSrgb));

    /// <summary>
    /// Creates an ITU-R BT.2020 profile: BT.2020 primaries, a D65 white and the BT.709 curve
    /// (<see cref="IccToneCurve.Rec709"/>).
    /// </summary>
    /// <returns>The profile.</returns>
    public static IccProfile CreateRec2020() => IccStandardRgb.Create(IccStandardRgb.Rec2020Primaries, IccToneCurve.Rec709, "Rec. 2020", (IccStandardRgb.PrimariesBt2020, IccStandardRgb.TransferBt709));

    /// <summary>
    /// Creates a profile compatible with Adobe RGB (1998): its primaries, a D65 white and a gamma of
    /// 563/256 (about 2.2).
    /// </summary>
    /// <returns>The profile.</returns>
    public static IccProfile CreateAdobeRgbCompatible()
        => IccStandardRgb.Create(AdobeRgbPrimaries, IccToneCurve.Gamma(563 / 256F), "Adobe RGB (1998) compatible", null);

    /// <summary>
    /// Creates a gray profile with a D65 white and the given curve, for example
    /// <see cref="IccToneCurve.Srgb"/>, <see cref="IccToneCurve.Linear"/> or a gamma.
    /// </summary>
    /// <param name="toneCurve">The tone curve.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toneCurve"/> is null.</exception>
    public static IccProfile CreateGray(IccToneCurve toneCurve)
    {
        Guard.NotNull(toneCurve, nameof(toneCurve));
        return IccProfileBuilder.Build(
            IccColorSpaceType.Gray,
            new IccProfileBuilderHeader(IccStandardRgb.D65, null, IccConnectionSpace.D50, IccRenderingIntent.Perceptual, "Gray", null),
            new[] { toneCurve.Curve });
    }

    /// <summary>
    /// Creates a gray profile with the given white point and curve.
    /// </summary>
    /// <param name="whitePoint">The white point as CIE xy chromaticity.</param>
    /// <param name="toneCurve">The tone curve.</param>
    /// <param name="renderingIntent">The rendering intent the header names.</param>
    /// <param name="description">The profile description, or null for "Gray".</param>
    /// <returns>The profile.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toneCurve"/> is null.</exception>
    /// <exception cref="ArgumentException">The white point has a y of zero.</exception>
    public static IccProfile CreateGray(
        CieXyChromaticityCoordinates whitePoint,
        IccToneCurve toneCurve,
        IccRenderingIntent renderingIntent = IccRenderingIntent.Perceptual,
        string description = null)
    {
        Guard.NotNull(toneCurve, nameof(toneCurve));
        return IccProfileBuilder.Build(
            IccColorSpaceType.Gray,
            new IccProfileBuilderHeader((whitePoint.X, whitePoint.Y), null, IccConnectionSpace.D50, renderingIntent, description ?? "Gray", null),
            new[] { toneCurve.Curve });
    }

    /// <summary>
    /// Creates an RGB profile from its primaries, white point and one tone curve for all three channels.
    /// </summary>
    /// <param name="primaries">The red, green and blue primaries as CIE xy chromaticity.</param>
    /// <param name="whitePoint">The white point as CIE xy chromaticity.</param>
    /// <param name="toneCurve">The tone curve.</param>
    /// <param name="renderingIntent">The rendering intent the header names.</param>
    /// <param name="description">The profile description, or null for "RGB".</param>
    /// <returns>The profile.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toneCurve"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The white point is outside 0 &lt;= x &lt;= 1, 0 &lt; y &lt;= 1, or the primaries are degenerate.
    /// </exception>
    public static IccProfile CreateRgb(
        RgbPrimariesChromaticityCoordinates primaries,
        CieXyChromaticityCoordinates whitePoint,
        IccToneCurve toneCurve,
        IccRenderingIntent renderingIntent = IccRenderingIntent.Perceptual,
        string description = null)
        => CreateRgb(primaries, whitePoint, toneCurve, toneCurve, toneCurve, renderingIntent, description);

    /// <summary>
    /// Creates an RGB profile from its primaries, white point and a tone curve for each channel.
    /// </summary>
    /// <param name="primaries">The red, green and blue primaries as CIE xy chromaticity.</param>
    /// <param name="whitePoint">The white point as CIE xy chromaticity.</param>
    /// <param name="redToneCurve">The tone curve of the red channel.</param>
    /// <param name="greenToneCurve">The tone curve of the green channel.</param>
    /// <param name="blueToneCurve">The tone curve of the blue channel.</param>
    /// <param name="renderingIntent">The rendering intent the header names.</param>
    /// <param name="description">The profile description, or null for "RGB".</param>
    /// <returns>The profile.</returns>
    /// <exception cref="ArgumentNullException">A tone curve is null.</exception>
    /// <exception cref="ArgumentException">
    /// The white point is outside 0 &lt;= x &lt;= 1, 0 &lt; y &lt;= 1, or the primaries are degenerate.
    /// </exception>
    public static IccProfile CreateRgb(
        RgbPrimariesChromaticityCoordinates primaries,
        CieXyChromaticityCoordinates whitePoint,
        IccToneCurve redToneCurve,
        IccToneCurve greenToneCurve,
        IccToneCurve blueToneCurve,
        IccRenderingIntent renderingIntent = IccRenderingIntent.Perceptual,
        string description = null)
    {
        Guard.NotNull(redToneCurve, nameof(redToneCurve));
        Guard.NotNull(greenToneCurve, nameof(greenToneCurve));
        Guard.NotNull(blueToneCurve, nameof(blueToneCurve));
        return IccProfileBuilder.Build(
            IccColorSpaceType.Rgb,
            new IccProfileBuilderHeader(
                (whitePoint.X, whitePoint.Y),
                new (double X, double Y)[] { (primaries.R.X, primaries.R.Y), (primaries.G.X, primaries.G.Y), (primaries.B.X, primaries.B.Y) },
                IccConnectionSpace.D50,
                renderingIntent,
                description ?? "RGB",
                null),
            new[] { redToneCurve.Curve, greenToneCurve.Curve, blueToneCurve.Curve });
    }
}
