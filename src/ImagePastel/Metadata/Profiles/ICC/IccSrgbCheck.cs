// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;

namespace PinataLabs.ImagePastel.Metadata.Profiles.Icc;

/// <summary>
/// Decides whether a profile is equivalent to sRGB. <see cref="IccProfile.IsSrgb"/> documents the rules.
/// </summary>
internal static class IccSrgbCheck
{
    /// <summary>
    /// The largest difference allowed in each XYZ component of a colorant or of the media white point.
    /// </summary>
    public const float XyzTolerance = 0.002F;

    /// <summary>
    /// The largest difference allowed, in 8-bit code values, when a code value is decoded with the
    /// profile's tone curve and encoded again with the sRGB curve.
    /// </summary>
    public const float CodeValueTolerance = 0.5F;

    // IEC 61966-2-1 primaries with a D65 white, Bradford-adapted to D50 (as tabulated by B. Lindbloom).
    private static readonly Vector3 Red = new(0.4360747F, 0.2225045F, 0.0139322F);
    private static readonly Vector3 Green = new(0.3850649F, 0.7168786F, 0.0971045F);
    private static readonly Vector3 Blue = new(0.1430804F, 0.0606169F, 0.7141733F);

    // D65 from its IEC 61966-2-1 chromaticity (0.3127, 0.3290), and the ICC D50 connection-space white.
    private static readonly Vector3 D65 = new(0.9505F, 1F, 1.0891F);
    private static readonly Vector3 D50 = new(0.9642F, 1F, 0.8249F);

    public static bool IsSrgb(IccProfile profile)
    {
        IccProfileHeader header = profile.Header;
        if (header.DataColorSpace != IccColorSpaceType.Rgb || header.ProfileConnectionSpace != IccColorSpaceType.CieXyz)
        {
            return false;
        }

        foreach (IccTagDataEntry entry in profile.Entries)
        {
            if (IsLookupTableTag(entry.TagSignature))
            {
                return false;
            }
        }

        return HasColorant(profile, IccProfileTag.RedMatrixColumn, Red)
            && HasColorant(profile, IccProfileTag.GreenMatrixColumn, Green)
            && HasColorant(profile, IccProfileTag.BlueMatrixColumn, Blue)
            && HasSrgbWhitePoint(profile)
            && HasSrgbCurve(profile, IccProfileTag.RedTrc)
            && HasSrgbCurve(profile, IccProfileTag.GreenTrc)
            && HasSrgbCurve(profile, IccProfileTag.BlueTrc);
    }

    // AToBn, BToAn, DToBn and BToDn: the first three bytes of the signature, whatever the intent digit.
    private static bool IsLookupTableTag(IccProfileTag signature)
        => ((uint)signature >> 8) is 0x413242 or 0x423241 or 0x443242 or 0x423244;

    private static bool HasColorant(IccProfile profile, IccProfileTag signature, Vector3 expected)
        => profile.TryGetTag(signature, out IccXyzTagDataEntry entry)
            && entry.Data.Length > 0
            && IsClose(entry.Data[0], expected);

    private static bool HasSrgbWhitePoint(IccProfile profile)
    {
        if (!profile.TryGetTag(IccProfileTag.MediaWhitePoint, out IccTagDataEntry entry))
        {
            return true;
        }

        return entry is IccXyzTagDataEntry white
            && white.Data.Length > 0
            && (IsClose(white.Data[0], D65) || IsClose(white.Data[0], D50));
    }

    private static bool HasSrgbCurve(IccProfile profile, IccProfileTag signature)
    {
        if (!profile.TryGetTag(signature, out IccTagDataEntry curve))
        {
            return false;
        }

        for (int code = 0; code < 256; code++)
        {
            if (!IccCurveEvaluator.TryEvaluate(curve, code / 255F, out float linear))
            {
                return false;
            }

            float roundTrip = SrgbEncode(linear) * 255F;
            if (!(MathF.Abs(roundTrip - code) < CodeValueTolerance))
            {
                return false;
            }
        }

        return true;
    }

    // The sRGB encoding (inverse transfer) function of IEC 61966-2-1.
    private static float SrgbEncode(float linear)
    {
        linear = Numerics.Clamp(linear, 0, 1);
        return linear <= 0.0031308F ? 12.92F * linear : (1.055F * MathF.Pow(linear, 1 / 2.4F)) - 0.055F;
    }

    private static bool IsClose(Vector3 actual, Vector3 expected)
    {
        Vector3 difference = Vector3.Abs(actual - expected);
        return difference.X <= XyzTolerance && difference.Y <= XyzTolerance && difference.Z <= XyzTolerance;
    }
}
