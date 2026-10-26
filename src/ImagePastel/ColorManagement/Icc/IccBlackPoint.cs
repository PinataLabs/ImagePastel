// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from Little CMS (https://github.com/mm2/Little-CMS) at commit a57322d8 (Little CMS 2.20):
// CreateRoundtripXForm, BlackPointAsDarkerColorant, BlackPointUsingPerceptualBlack,
// cmsDetectBlackPoint, RootOfLeastSquaresFitQuadraticCurve and cmsDetectDestinationBlackPoint in
// src/cmssamp.c, and the darkest colorants of _cmsEndPointsBySpace in src/cmspcs.c.
// Copyright (c) Marti Maria Saguer. MIT License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Numerics;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// Finds the black point of a profile in PCS XYZ, for black-point compensation, as described in
/// Adobe's black-point compensation paper and implemented by Little CMS. Each black point is made
/// neutral (a* = b* = 0) and no lighter than L* = 50.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Version 4 profiles with the perceptual or saturation intent have the fixed perceptual
/// black of the ICC specification, except matrix/TRC profiles, which share their tables with
/// relative colorimetric.</item>
/// <item>A source black point is the darkest colorant (RGB 0, gray 0, CMYK 100% of each ink)
/// through the profile. For a CMYK output profile with relative colorimetric, it is the PCS black
/// sent through the perceptual table and back through the relative colorimetric one, which
/// discounts ink limits.</item>
/// <item>A target black point of a profile with a table for the intent comes from a round trip of
/// an L* ramp through the profile and a quadratic fit of its shadows.</item>
/// </list>
/// A profile that cannot be used in the direction a step needs has a black point of zero.
/// </remarks>
internal static class IccBlackPoint
{
    // The black of the ICC version 4 perceptual reference medium, in PCS XYZ (cmsPERCEPTUAL_BLACK_X/Y/Z).
    private static readonly (double X, double Y, double Z) PerceptualBlack = (0.00336, 0.0034731, 0.00287);

    private static readonly (double X, double Y, double Z) Zero = (0, 0, 0);

    /// <summary>
    /// Finds the black point of a profile used as the source (device to PCS).
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="intent">The rendering intent: perceptual, relative colorimetric or saturation.</param>
    /// <returns>The black point in PCS XYZ, or zero when there is none.</returns>
    public static (double X, double Y, double Z) Detect(IccProfileInfo profile, IccRenderingIntent intent)
    {
        if (!IsBlackPointIntent(intent))
        {
            return Zero;
        }

        // Version 4 perceptual and saturation tables have a well-specified black.
        if (HasPerceptualBlack(profile, intent))
        {
            return profile.IsMatrixShaper
                ? AsDarkerColorant(profile, IccRenderingIntent.MediaRelativeColorimetric)
                : PerceptualBlack;
        }

        // An output CMYK profile: discount its ink limit.
        if (intent == IccRenderingIntent.MediaRelativeColorimetric
            && profile.Class == IccProfileClass.OutputDevice
            && profile.DataColorSpace == IccColorSpaceType.Cmyk)
        {
            return UsingPerceptualBlack(profile);
        }

        return AsDarkerColorant(profile, intent);
    }

    /// <summary>
    /// Finds the black point of a profile used as the target (PCS to device).
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="intent">The rendering intent: perceptual, relative colorimetric or saturation.</param>
    /// <returns>The black point in PCS XYZ, or zero when there is none.</returns>
    public static (double X, double Y, double Z) DetectDestination(IccProfileInfo profile, IccRenderingIntent intent)
    {
        if (!IsBlackPointIntent(intent))
        {
            return Zero;
        }

        if (HasPerceptualBlack(profile, intent))
        {
            return profile.IsMatrixShaper
                ? AsDarkerColorant(profile, IccRenderingIntent.MediaRelativeColorimetric)
                : PerceptualBlack;
        }

        // Only a table for the intent needs the round trip; otherwise this is the source case.
        if (!profile.IsClut(intent, asSource: false))
        {
            return Detect(profile, intent);
        }

        // A first guess, which is right for good profiles.
        (double L, double A, double B) initialLab = (0, 0, 0);
        if (intent == IccRenderingIntent.MediaRelativeColorimetric)
        {
            initialLab = XyzToLab(Detect(profile, intent));
        }

        IccPipeline roundTrip;
        try
        {
            roundTrip = RoundTrip(profile, intent);
        }
        catch (Exception ex) when (ex is InvalidImageContentException or NotSupportedException)
        {
            return Zero;
        }

        // Send an L* ramp through the profile and back.
        const int count = 256;
        double[] inRamp = new double[count];
        double[] outRamp = new double[count];
        Vector4[] lab = new Vector4[1];
        float a = (float)Math.Min(50, Math.Max(-50, initialLab.A));
        float b = (float)Math.Min(50, Math.Max(-50, initialLab.B));
        for (int l = 0; l < count; l++)
        {
            double lightness = l * 100D / 255D;
            lab[0] = new Vector4((float)lightness, a, b, 1);
            roundTrip.Invoke(lab);
            inRamp[l] = lightness;
            outRamp[l] = lab[0].X;
        }

        // Make the result monotonic.
        for (int l = count - 2; l > 0; l--)
        {
            outRamp[l] = Math.Min(outRamp[l], outRamp[l + 1]);
        }

        if (!(outRamp[0] < outRamp[count - 1]))
        {
            return Zero;
        }

        double minL = outRamp[0];
        double maxL = outRamp[count - 1];

        // With relative colorimetric, a mid range that comes back nearly straight means the first guess is right.
        if (intent == IccRenderingIntent.MediaRelativeColorimetric)
        {
            bool nearlyStraightMidrange = true;
            for (int l = 0; l < count; l++)
            {
                if (!(inRamp[l] <= minL + (0.2 * (maxL - minL)) || Math.Abs(inRamp[l] - outRamp[l]) < 4))
                {
                    nearlyStraightMidrange = false;
                }
            }

            if (nearlyStraightMidrange)
            {
                return LabToXyz(initialLab.L, initialLab.A, initialLab.B);
            }
        }

        // The round trip is nearly constant at the black point, then turns into a nearly straight
        // line to white. Fit a quadratic to the shadows and take its vertex.
        double lo = intent == IccRenderingIntent.MediaRelativeColorimetric ? 0.1 : 0.03;
        double hi = intent == IccRenderingIntent.MediaRelativeColorimetric ? 0.5 : 0.25;
        List<double> x = [];
        List<double> y = [];
        for (int l = 0; l < count; l++)
        {
            double ff = (outRamp[l] - minL) / (maxL - minL);
            if (ff >= lo && ff < hi)
            {
                x.Add(inRamp[l]);
                y.Add(ff);
            }
        }

        if (x.Count < 3)
        {
            return Zero;
        }

        double vertex = Math.Max(0, RootOfLeastSquaresFitQuadraticCurve(x, y));
        return LabToXyz(vertex, initialLab.A, initialLab.B);
    }

    /// <summary>
    /// Gets a value indicating whether the profile's black point for an intent is the fixed one of
    /// version 4 perceptual tables (or, for a matrix/TRC profile, its relative colorimetric one).
    /// </summary>
    private static bool HasPerceptualBlack(IccProfileInfo profile, IccRenderingIntent intent)
        => profile.IsVersion4 && intent is IccRenderingIntent.Perceptual or IccRenderingIntent.Saturation;

    private static bool IsBlackPointIntent(IccRenderingIntent intent)
        => intent is IccRenderingIntent.Perceptual or IccRenderingIntent.MediaRelativeColorimetric or IccRenderingIntent.Saturation;

    // The darkest colorant through the profile, made neutral.
    private static (double X, double Y, double Z) AsDarkerColorant(IccProfileInfo profile, IccRenderingIntent intent)
    {
        if (!profile.IsIntentSupported(intent, asSource: true))
        {
            return Zero;
        }

        Vector4[] color = [profile.DataColorSpace == IccColorSpaceType.Cmyk ? Vector4.One : new Vector4(0, 0, 0, 1)];
        try
        {
            IccProfileModel model = profile.ReadModel(asSource: true, intent);
            List<IccStage> stages = [];
            model.AddDeviceToPcs(stages);
            IccIntentLink.AddConversion(stages, model.ConnectionSpace, IccColorSpaceType.CieLab, null);
            new IccPipeline(stages).Invoke(color);
        }
        catch (Exception ex) when (ex is InvalidImageContentException or NotSupportedException)
        {
            return Zero;
        }

        // Neutral, with L* in a sensible range: a darkest colorant over L* = 95 marks a negative profile.
        double lightness = color[0].X;
        if (lightness > 95 || lightness < 0)
        {
            lightness = 0;
        }
        else if (lightness > 50)
        {
            lightness = 50;
        }

        return LabToXyz(lightness, 0, 0);
    }

    // PCS black through the perceptual table and back through the relative colorimetric one.
    private static (double X, double Y, double Z) UsingPerceptualBlack(IccProfileInfo profile)
    {
        if (!profile.IsIntentSupported(IccRenderingIntent.Perceptual, asSource: true))
        {
            return Zero;
        }

        Vector4[] lab = [new Vector4(0, 0, 0, 1)];
        try
        {
            RoundTrip(profile, IccRenderingIntent.Perceptual).Invoke(lab);
        }
        catch (Exception ex) when (ex is InvalidImageContentException or NotSupportedException)
        {
            return Zero;
        }

        return LabToXyz(Math.Min(lab[0].X, 50), 0, 0);
    }

    // PCS Lab -> the profile as the target with the intent -> device values -> the profile as the
    // source with relative colorimetric -> PCS Lab. The step into the profile is linked as any
    // conversion is, so version 4 perceptual and saturation get black-point compensation from the
    // zero black of the Lab side.
    private static IccPipeline RoundTrip(IccProfileInfo profile, IccRenderingIntent intent)
    {
        IccProfileModel output = profile.ReadModel(asSource: false, intent);
        IccProfileModel input = profile.ReadModel(asSource: true, IccRenderingIntent.MediaRelativeColorimetric);

        IccMatrix? conversion = null;
        if (HasPerceptualBlack(profile, intent))
        {
            (double X, double Y, double Z) black = DetectDestination(profile, intent);
            if (black != Zero)
            {
                conversion = IccIntentLink.BlackPointCompensation(Zero, black);
            }
        }

        List<IccStage> stages = [];
        IccIntentLink.AddConversion(stages, IccColorSpaceType.CieLab, output.ConnectionSpace, conversion);
        output.AddPcsToDevice(stages);
        input.AddDeviceToPcs(stages);
        IccIntentLink.AddConversion(stages, input.ConnectionSpace, IccColorSpaceType.CieLab, null);
        return new IccPipeline(stages);
    }

    // The vertex of the least-squares quadratic through the points, in [0, 50].
    private static double RootOfLeastSquaresFitQuadraticCurve(List<double> x, List<double> y)
    {
        int n = x.Count;
        if (n < 4)
        {
            return 0;
        }

        double sumX = 0, sumX2 = 0, sumX3 = 0, sumX4 = 0;
        double sumY = 0, sumYX = 0, sumYX2 = 0;
        for (int i = 0; i < n; i++)
        {
            double xn = x[i];
            double yn = y[i];

            sumX += xn;
            sumX2 += xn * xn;
            sumX3 += xn * xn * xn;
            sumX4 += xn * xn * xn * xn;

            sumY += yn;
            sumYX += yn * xn;
            sumYX2 += yn * xn * xn;
        }

        IccMatrix m = new((n, sumX, sumX2), (sumX, sumX2, sumX3), (sumX2, sumX3, sumX4));
        if (!m.TryInvert(out IccMatrix inverse))
        {
            return 0;
        }

        (double c, double b, double a) = inverse.Transform(sumY, sumYX, sumYX2);

        if (Math.Abs(a) < 1e-10)
        {
            return Math.Abs(b) < 1e-10 ? 0 : Math.Max(0, Math.Min(50, -c / b));
        }

        double d = (b * b) - (4 * a * c);
        if (d <= 0)
        {
            return 0;
        }

        double root = (-b + Math.Sqrt(d)) / (2 * a);
        return Math.Max(0, Math.Min(50, root));
    }

    // CIE XYZ to Lab and back for the D50 PCS white, in double precision (cmsXYZ2Lab, cmsLab2XYZ).
    private static (double L, double A, double B) XyzToLab((double X, double Y, double Z) xyz)
    {
        double fx = F(xyz.X / IccConnectionSpace.D50X);
        double fy = F(xyz.Y / IccConnectionSpace.D50Y);
        double fz = F(xyz.Z / IccConnectionSpace.D50Z);
        return ((116 * fy) - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static (double X, double Y, double Z) LabToXyz(double l, double a, double b)
    {
        double fy = (l + 16) / 116;
        double fx = fy + (a / 500);
        double fz = fy - (b / 200);
        return (IccConnectionSpace.D50X * FInverse(fx), IccConnectionSpace.D50Y * FInverse(fy), IccConnectionSpace.D50Z * FInverse(fz));
    }

    private static double F(double t)
    {
        const double limit = 24D / 116D * (24D / 116D) * (24D / 116D);
        return t <= limit ? (841D / 108D * t) + (16D / 116D) : Math.Pow(t, 1D / 3D);
    }

    private static double FInverse(double t)
    {
        const double limit = 24D / 116D;
        return t <= limit ? 108D / 841D * (t - (16D / 116D)) : t * t * t;
    }
}
