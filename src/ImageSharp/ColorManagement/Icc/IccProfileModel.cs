// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// What the engine needs from one profile: its PCS encoding and the stages between its device
/// values and the PCS. Each kind of profile the engine supports is a subclass:
/// <see cref="IccMatrixTrcModel"/>, <see cref="IccGrayTrcModel"/> and <see cref="IccLutModel"/>.
/// </summary>
internal abstract class IccProfileModel
{
    /// <summary>
    /// Gets the PCS encoding: <see cref="IccColorSpaceType.CieXyz"/> or <see cref="IccColorSpaceType.CieLab"/>.
    /// </summary>
    public abstract IccColorSpaceType ConnectionSpace { get; }

    /// <summary>
    /// Adds the stages that take device values to the PCS.
    /// </summary>
    /// <param name="stages">The pipeline being built.</param>
    public abstract void AddDeviceToPcs(List<IccStage> stages);

    /// <summary>
    /// Adds the stages that take PCS values to device values.
    /// </summary>
    /// <param name="stages">The pipeline being built.</param>
    public abstract void AddPcsToDevice(List<IccStage> stages);

    /// <summary>
    /// Reads the model of a profile for one side of a conversion.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="asSource">
    /// <see langword="true"/> when the profile is the source (device to PCS);
    /// <see langword="false"/> when it is the target (PCS to device).
    /// </param>
    /// <param name="intent">The rendering intent, which chooses the profile's LUT-based tag.</param>
    /// <returns>The model.</returns>
    /// <exception cref="InvalidImageContentException">The profile is corrupt.</exception>
    /// <exception cref="NotSupportedException">The engine does not support this kind of profile yet.</exception>
    public static IccProfileModel Read(IccProfile profile, bool asSource, IccRenderingIntent intent)
    {
        string role = asSource ? "source" : "target";
        IccProfileHeader header;
        IccTagDataEntry[] entries;
        try
        {
            if (!profile.CheckIsValid())
            {
                throw Corrupt(role, "its header is invalid");
            }

            header = profile.Header;
            entries = profile.Entries;
        }
        catch (Exception ex) when (ex is not (InvalidImageContentException or OutOfMemoryException))
        {
            throw new InvalidImageContentException($"The {role} ICC profile is corrupt: {ex.Message}", ex);
        }

        if (header.Class is IccProfileClass.DeviceLink or IccProfileClass.Abstract or IccProfileClass.NamedColor)
        {
            throw new NotSupportedException($"The {role} ICC profile is a {header.Class} profile; only device and color space profiles are supported.");
        }

        IccColorSpaceType pcs = header.ProfileConnectionSpace;
        if (pcs is not (IccColorSpaceType.CieXyz or IccColorSpaceType.CieLab))
        {
            throw Corrupt(role, $"its connection space is {pcs}");
        }

        IccColorSpaceType dataColorSpace = header.DataColorSpace;
        if (dataColorSpace is not (IccColorSpaceType.Rgb or IccColorSpaceType.Gray or IccColorSpaceType.Cmyk))
        {
            throw new NotSupportedException($"The {role} ICC profile's color space is {dataColorSpace}; only RGB, gray and CMYK profiles are supported.");
        }

        // As Little CMS does (_cmsReadInputLUT, _cmsReadOutputLUT): the intent's floating-point table
        // (D2Bn or B2Dn) first, then its LUT-based table (A2Bn or B2An, where absolute colorimetric
        // uses the relative colorimetric table), then the perceptual one, and the matrix/TRC tags
        // (or the gray curve) when the profile has none of them.
        uint floatLutTag = FloatLutTag(asSource, intent);
        foreach (IccTagDataEntry entry in entries)
        {
            if ((uint)entry.TagSignature == floatLutTag)
            {
                throw new NotSupportedException($"The {role} ICC profile uses a multi-process element table ({FourCc(floatLutTag)}), which is not supported.");
            }
        }

        IccProfileTag lutTag = LutTag(asSource, intent);
        if (!profile.TryGetTag(lutTag, out IccTagDataEntry lut))
        {
            lutTag = LutTag(asSource, IccRenderingIntent.Perceptual);
            profile.TryGetTag(lutTag, out lut);
        }

        if (lut is not null)
        {
            return IccLutModel.Read(lut, lutTag, dataColorSpace, pcs, asSource, role);
        }

        return dataColorSpace switch
        {
            IccColorSpaceType.Rgb => IccMatrixTrcModel.Read(profile, pcs, role),
            IccColorSpaceType.Gray => IccGrayTrcModel.Read(profile, pcs, role),
            _ => throw Corrupt(role, $"it is a {dataColorSpace} profile without a {lutTag} table"),
        };
    }

    /// <summary>
    /// Gets the LUT-based tag that holds an intent's table in one direction: <c>A2B0</c>,
    /// <c>A2B1</c> or <c>A2B2</c> for a source, <c>B2A0</c>, <c>B2A1</c> or <c>B2A2</c> for a
    /// target. Absolute colorimetric uses the relative colorimetric table.
    /// </summary>
    /// <param name="asSource">Whether the profile is the source.</param>
    /// <param name="intent">The rendering intent.</param>
    /// <returns>The tag.</returns>
    public static IccProfileTag LutTag(bool asSource, IccRenderingIntent intent)
    {
        uint index = intent == IccRenderingIntent.AbsoluteColorimetric ? 1u : (uint)intent;
        return (IccProfileTag)((uint)(asSource ? IccProfileTag.AToB0 : IccProfileTag.BToA0) + index);
    }

    /// <summary>
    /// Reads a tone curve tag.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="signature">The tag.</param>
    /// <param name="role">"source" or "target", for the error message.</param>
    /// <returns>The curve.</returns>
    protected static IccTagDataEntry ReadCurve(IccProfile profile, IccProfileTag signature, string role)
        => profile.TryGetTag(signature, out IccTagDataEntry curve) && IccCurveEvaluator.IsCurve(curve)
            ? curve
            : throw Corrupt(role, $"it has no readable {signature} curve");

    /// <summary>
    /// Creates the exception for a corrupt profile.
    /// </summary>
    /// <param name="role">"source" or "target".</param>
    /// <param name="reason">What is wrong.</param>
    /// <returns>The exception.</returns>
    protected static InvalidImageContentException Corrupt(string role, string reason)
        => new($"The {role} ICC profile is corrupt: {reason}.");

    // D2B0 to D2B3, or B2D0 to B2D3, by intent. IccProfileTag.DToB1 to DToB3 carry the value of
    // DToB0, so the signatures are computed.
    private static uint FloatLutTag(bool asSource, IccRenderingIntent intent)
        => (asSource ? 0x44324230u : 0x42324430u) + (uint)intent;

    private static string FourCc(uint signature)
        => new(new[] { (char)(signature >> 24), (char)((signature >> 16) & 0xFF), (char)((signature >> 8) & 0xFF), (char)(signature & 0xFF) });
}
