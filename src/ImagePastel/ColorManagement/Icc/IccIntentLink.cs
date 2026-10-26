// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from Little CMS (https://github.com/mm2/Little-CMS) at commit a57322d8 (Little CMS 2.20):
// the black-point compensation rules of _cmsLinkProfiles, and ComputeConversion,
// ComputeBlackPointCompensation, ComputeAbsoluteIntent (with the default, full observer
// adaptation), IsEmptyLayer and AddConversion in src/cmscnvrt.c.
// Copyright (c) Marti Maria Saguer. MIT License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// Links a source and a target profile for a rendering intent: the source's device-to-PCS stages,
/// the conversion between the two PCS values that the intent and black-point compensation call
/// for, and the target's PCS-to-device stages.
/// </summary>
/// <remarks>
/// <para>
/// The conversion between the two halves is a scaling in PCS XYZ. For absolute colorimetric it
/// scales by the ratio of the media white points (the observer fully adapted, the ICC version 4
/// behavior). For the other intents with black-point compensation it maps the source black point
/// to the target black point and keeps the PCS white, as Adobe's published algorithm does.
/// </para>
/// <para>
/// As in Little CMS, black-point compensation never applies to absolute colorimetric, and always
/// applies to perceptual and saturation when the target profile is version 4, whose perceptual
/// tables have a fixed, non-zero black.
/// </para>
/// </remarks>
internal static class IccIntentLink
{
    // The largest PCS XYZ value of the 16-bit encoding, which scales offsets in Little CMS's empty-layer test.
    private const double MaxEncodableXyz = 1 + (32767D / 32768D);

    /// <summary>
    /// Builds the stages that convert from the source profile's device values to the target's.
    /// </summary>
    /// <param name="sourceProfile">The source profile, or <see langword="null"/> for sRGB.</param>
    /// <param name="targetProfile">The target profile, or <see langword="null"/> for sRGB.</param>
    /// <param name="intent">The rendering intent.</param>
    /// <param name="blackPointCompensation">Whether black-point compensation was asked for.</param>
    /// <returns>The stages, in order.</returns>
    /// <exception cref="InvalidImageContentException">A profile is corrupt.</exception>
    /// <exception cref="NotSupportedException">The engine does not support a profile.</exception>
    public static List<IccStage> Link(IccProfile sourceProfile, IccProfile targetProfile, IccRenderingIntent intent, bool blackPointCompensation)
    {
        // Reading the models first checks that the profiles are valid, which IccProfileInfo needs.
        IccProfileInfo source = IccProfileInfo.Read(sourceProfile, "source", asSource: true, intent, out IccProfileModel sourceModel);
        IccProfileInfo target = IccProfileInfo.Read(targetProfile, "target", asSource: false, intent, out IccProfileModel targetModel);

        if (intent == IccRenderingIntent.AbsoluteColorimetric)
        {
            blackPointCompensation = false;
        }
        else if (intent is IccRenderingIntent.Perceptual or IccRenderingIntent.Saturation && target.IsVersion4)
        {
            blackPointCompensation = true;
        }

        IccMatrix? conversion = null;
        if (intent == IccRenderingIntent.AbsoluteColorimetric)
        {
            conversion = AbsoluteIntent(source.MediaWhitePoint, target.MediaWhitePoint);
        }
        else if (blackPointCompensation)
        {
            (double X, double Y, double Z) blackIn = IccBlackPoint.Detect(source, intent);
            (double X, double Y, double Z) blackOut = IccBlackPoint.DetectDestination(target, intent);
            if (blackIn != blackOut)
            {
                conversion = BlackPointCompensation(blackIn, blackOut);
            }
        }

        List<IccStage> stages = [];
        sourceModel.AddDeviceToPcs(stages);
        AddConversion(stages, sourceModel.ConnectionSpace, targetModel.ConnectionSpace, conversion);
        targetModel.AddPcsToDevice(stages);
        return stages;
    }

    /// <summary>
    /// Gets the scaling in PCS XYZ that takes one black point to another and keeps the PCS white.
    /// </summary>
    /// <param name="blackIn">The source black point.</param>
    /// <param name="blackOut">The target black point.</param>
    /// <returns>The scaling, with an offset.</returns>
    public static IccMatrix BlackPointCompensation((double X, double Y, double Z) blackIn, (double X, double Y, double Z) blackOut)
    {
        // Solve m * blackIn + offset = blackOut and m * D50 + offset = D50 per component:
        // m = (blackOut - D50) / (blackIn - D50), offset = -D50 * (blackOut - blackIn) / (blackIn - D50).
        double tx = blackIn.X - IccConnectionSpace.D50X;
        double ty = blackIn.Y - IccConnectionSpace.D50Y;
        double tz = blackIn.Z - IccConnectionSpace.D50Z;

        double ax = (blackOut.X - IccConnectionSpace.D50X) / tx;
        double ay = (blackOut.Y - IccConnectionSpace.D50Y) / ty;
        double az = (blackOut.Z - IccConnectionSpace.D50Z) / tz;

        double bx = -IccConnectionSpace.D50X * (blackOut.X - blackIn.X) / tx;
        double by = -IccConnectionSpace.D50Y * (blackOut.Y - blackIn.Y) / ty;
        double bz = -IccConnectionSpace.D50Z * (blackOut.Z - blackIn.Z) / tz;

        return new IccMatrix((ax, 0, 0), (0, ay, 0), (0, 0, az), (bx, by, bz));
    }

    /// <summary>
    /// Adds the stages between two halves of a pipeline: the conversion between their PCS encodings
    /// and, unless it is close to the identity, a scaling in PCS XYZ.
    /// </summary>
    /// <param name="stages">The pipeline being built.</param>
    /// <param name="from">The PCS encoding of the first half.</param>
    /// <param name="to">The PCS encoding of the second half.</param>
    /// <param name="conversion">The scaling in PCS XYZ, or <see langword="null"/> for none.</param>
    public static void AddConversion(List<IccStage> stages, IccColorSpaceType from, IccColorSpaceType to, IccMatrix? conversion)
    {
        bool scale = conversion is not null && !IsEmptyLayer(conversion.Value);
        if (!scale)
        {
            if (from != to)
            {
                stages.Add(from == IccColorSpaceType.CieLab ? IccLabToXyzStage.Instance : IccXyzToLabStage.Instance);
            }

            return;
        }

        if (from == IccColorSpaceType.CieLab)
        {
            stages.Add(IccLabToXyzStage.Instance);
        }

        stages.Add(new IccMatrixStage(conversion.Value));

        if (to == IccColorSpaceType.CieLab)
        {
            stages.Add(IccXyzToLabStage.Instance);
        }
    }

    // Full adaptation: scale by the ratio of the media white points.
    private static IccMatrix AbsoluteIntent((double X, double Y, double Z) whiteIn, (double X, double Y, double Z) whiteOut)
        => IccMatrix.Diagonal(whiteIn.X / whiteOut.X, whiteIn.Y / whiteOut.Y, whiteIn.Z / whiteOut.Z);

    // Little CMS skips a scaling whose distance from the identity (offset in its normalized encoding) is below 0.002.
    private static bool IsEmptyLayer(IccMatrix m)
    {
        double difference = Math.Abs(m.Row1.X - 1) + Math.Abs(m.Row1.Y) + Math.Abs(m.Row1.Z)
            + Math.Abs(m.Row2.X) + Math.Abs(m.Row2.Y - 1) + Math.Abs(m.Row2.Z)
            + Math.Abs(m.Row3.X) + Math.Abs(m.Row3.Y) + Math.Abs(m.Row3.Z - 1)
            + ((Math.Abs(m.Offset.X) + Math.Abs(m.Offset.Y) + Math.Abs(m.Offset.Z)) / MaxEncodableXyz);

        return difference < 0.002;
    }
}
