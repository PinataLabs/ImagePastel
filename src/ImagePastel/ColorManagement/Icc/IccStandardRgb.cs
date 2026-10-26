// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Metadata.Profiles.Icc;

namespace PinataLabs.ImagePastel.ColorManagement.Icc;

/// <summary>
/// The standard RGB color spaces that <see cref="IccProfileFactory"/> builds and that a PNG <c>cICP</c>
/// chunk can name: their ITU-T H.273 code points, D65 white and primaries, and the profile builder call.
/// </summary>
internal static class IccStandardRgb
{
    /// <summary>H.273 color primaries: BT.709 (and sRGB).</summary>
    public const byte PrimariesBt709 = 1;

    /// <summary>H.273 color primaries: BT.2020 and BT.2100.</summary>
    public const byte PrimariesBt2020 = 9;

    /// <summary>H.273 color primaries: SMPTE EG 432-1 (Display P3, D65 white).</summary>
    public const byte PrimariesP3D65 = 12;

    /// <summary>H.273 transfer characteristics: BT.709.</summary>
    public const byte TransferBt709 = 1;

    /// <summary>H.273 transfer characteristics: BT.601, the same curve as BT.709.</summary>
    public const byte TransferBt601 = 6;

    /// <summary>H.273 transfer characteristics: linear.</summary>
    public const byte TransferLinear = 8;

    /// <summary>H.273 transfer characteristics: sRGB (IEC 61966-2-1).</summary>
    public const byte TransferSrgb = 13;

    /// <summary>H.273 transfer characteristics: BT.2020 10-bit, the same curve as BT.709.</summary>
    public const byte TransferBt2020TenBit = 14;

    /// <summary>H.273 transfer characteristics: BT.2020 12-bit, the same curve as BT.709.</summary>
    public const byte TransferBt2020TwelveBit = 15;

    /// <summary>Gets the D65 white point chromaticity.</summary>
    public static (double X, double Y) D65 { get; } = (0.3127, 0.3290);

    /// <summary>Gets the BT.709 (sRGB) primaries: red, green, blue.</summary>
    public static (double X, double Y)[] Rec709Primaries { get; } = [(0.64, 0.33), (0.30, 0.60), (0.15, 0.06)];

    /// <summary>Gets the Display P3 primaries: red, green, blue.</summary>
    public static (double X, double Y)[] P3Primaries { get; } = [(0.680, 0.320), (0.265, 0.690), (0.150, 0.060)];

    /// <summary>Gets the BT.2020 primaries: red, green, blue.</summary>
    public static (double X, double Y)[] Rec2020Primaries { get; } = [(0.708, 0.292), (0.170, 0.797), (0.131, 0.046)];

    /// <summary>
    /// Gets the primaries an H.273 color primaries code point names, for the code points above.
    /// </summary>
    /// <param name="code">The code point.</param>
    /// <param name="primaries">The primaries, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for a code point listed above.</returns>
    public static bool TryGetPrimaries(byte code, out (double X, double Y)[] primaries)
    {
        primaries = code switch
        {
            PrimariesBt709 => Rec709Primaries,
            PrimariesBt2020 => Rec2020Primaries,
            PrimariesP3D65 => P3Primaries,
            _ => null,
        };

        return primaries is not null;
    }

    /// <summary>
    /// Gets the tone curve an H.273 transfer characteristics code point names, for the code points above.
    /// </summary>
    /// <param name="code">The code point.</param>
    /// <returns>The tone curve, or <see langword="null"/> for any other code point.</returns>
    public static IccToneCurve GetToneCurve(byte code) => code switch
    {
        TransferBt709 or TransferBt601 or TransferBt2020TenBit or TransferBt2020TwelveBit => IccToneCurve.Rec709,
        TransferSrgb => IccToneCurve.Srgb,
        TransferLinear => IccToneCurve.Linear,
        _ => null,
    };

    /// <summary>
    /// Builds a perceptual RGB display profile with a D65 white and one tone curve for all three channels.
    /// </summary>
    /// <param name="primaries">The red, green and blue primaries.</param>
    /// <param name="curve">The tone curve.</param>
    /// <param name="description">The profile description.</param>
    /// <param name="cicp">The H.273 code points for a <c>cicp</c> tag, or <see langword="null"/> for none.</param>
    /// <returns>The profile.</returns>
    public static IccProfile Create(
        (double X, double Y)[] primaries,
        IccToneCurve curve,
        string description,
        (byte ColorPrimaries, byte TransferCharacteristics)? cicp)
        => IccProfileBuilder.Build(
            IccColorSpaceType.Rgb,
            new IccProfileBuilderHeader(D65, primaries, IccConnectionSpace.D50, IccRenderingIntent.Perceptual, description, null, cicp),
            [curve.Curve, curve.Curve, curve.Curve]);
}
