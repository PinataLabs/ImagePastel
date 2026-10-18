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
    /// <returns>The model.</returns>
    /// <exception cref="InvalidImageContentException">The profile is corrupt.</exception>
    /// <exception cref="NotSupportedException">The engine does not support this kind of profile yet.</exception>
    public static IccProfileModel Read(IccProfile profile, bool asSource)
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

        // With the perceptual intent, Little CMS takes the intent-0 table in the direction it needs:
        // the floating-point one (D2B0 or B2D0) first, then A2B0 or B2A0, and the matrix/TRC tags
        // (or the gray curve) when the profile has neither.
        IccProfileTag lutTag = asSource ? IccProfileTag.AToB0 : IccProfileTag.BToA0;
        IccProfileTag floatLutTag = asSource ? IccProfileTag.DToB0 : IccProfileTag.BToD0;
        foreach (IccTagDataEntry entry in entries)
        {
            if (entry.TagSignature == floatLutTag)
            {
                throw new NotSupportedException($"The {role} ICC profile uses a multi-process element table ({entry.TagSignature}), which is not supported.");
            }
        }

        if (profile.TryGetTag(lutTag, out IccTagDataEntry lut))
        {
            return IccLutModel.Read(lut, dataColorSpace, pcs, asSource, role);
        }

        return dataColorSpace switch
        {
            IccColorSpaceType.Rgb => IccMatrixTrcModel.Read(profile, pcs, role),
            IccColorSpaceType.Gray => IccGrayTrcModel.Read(profile, pcs, role),
            _ => throw Corrupt(role, $"it is a {dataColorSpace} profile without a {lutTag} table"),
        };
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
}
