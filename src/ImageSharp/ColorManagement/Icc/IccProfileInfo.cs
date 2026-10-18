// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// The media white point rules and the questions about a profile's tables follow
// _cmsReadMediaWhitePoint, cmsIsMatrixShaper, cmsIsCLUT and cmsIsIntentSupported in src/cmsio1.c of
// Little CMS (https://github.com/mm2/Little-CMS) at commit a57322d8 (Little CMS 2.20).
// Copyright (c) Marti Maria Saguer. MIT License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// What rendering intents and black-point compensation need to know about one profile of a
/// conversion: its version, class and tables, its media white point, and its model for any intent
/// and direction. A <see langword="null"/> profile stands for sRGB, which behaves as the version 4
/// matrix/TRC display profile that Little CMS builds for sRGB.
/// </summary>
internal sealed class IccProfileInfo
{
    private readonly IccProfile profile;
    private readonly string role;

    // The models read so far. Black-point detection asks for the same ones several times, and
    // reading a LUT model copies its tables.
    private readonly Dictionary<(bool AsSource, IccRenderingIntent Intent), IccProfileModel> models = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="IccProfileInfo"/> class.
    /// </summary>
    /// <param name="profile">The profile, or <see langword="null"/> for sRGB. It must have passed <see cref="IccProfileModel.Read"/>.</param>
    /// <param name="role">"source" or "target", for error messages.</param>
    private IccProfileInfo(IccProfile profile, string role)
    {
        this.profile = profile;
        this.role = role;
        if (profile is null)
        {
            this.IsVersion4 = true;
            this.Class = IccProfileClass.DisplayDevice;
            this.DataColorSpace = IccColorSpaceType.Rgb;
            this.ConnectionSpace = IccColorSpaceType.CieXyz;
            this.IsMatrixShaper = true;
            this.MediaWhitePoint = (IccConnectionSpace.D50X, IccConnectionSpace.D50Y, IccConnectionSpace.D50Z);
            return;
        }

        IccProfileHeader header = profile.Header;
        this.IsVersion4 = header.Version.Major >= 4;
        this.Class = header.Class;
        this.DataColorSpace = header.DataColorSpace;
        this.ConnectionSpace = header.ProfileConnectionSpace;
        this.IsMatrixShaper = header.DataColorSpace switch
        {
            IccColorSpaceType.Gray => this.HasTag(IccProfileTag.GrayTrc),
            IccColorSpaceType.Rgb => this.HasTag(IccProfileTag.RedMatrixColumn)
                && this.HasTag(IccProfileTag.GreenMatrixColumn)
                && this.HasTag(IccProfileTag.BlueMatrixColumn)
                && this.HasTag(IccProfileTag.RedTrc)
                && this.HasTag(IccProfileTag.GreenTrc)
                && this.HasTag(IccProfileTag.BlueTrc),
            _ => false,
        };

        // No white point means D50, and so does a version 2 display profile's, whatever it says.
        this.MediaWhitePoint = (IccConnectionSpace.D50X, IccConnectionSpace.D50Y, IccConnectionSpace.D50Z);
        if ((this.IsVersion4 || this.Class != IccProfileClass.DisplayDevice)
            && profile.TryGetTag(IccProfileTag.MediaWhitePoint, out IccXyzTagDataEntry white)
            && white.Data.Length > 0)
        {
            this.MediaWhitePoint = (white.Data[0].X, white.Data[0].Y, white.Data[0].Z);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the profile is version 4 or later.
    /// </summary>
    public bool IsVersion4 { get; }

    /// <summary>
    /// Gets the profile class.
    /// </summary>
    public IccProfileClass Class { get; }

    /// <summary>
    /// Gets the data color space: RGB, gray or CMYK.
    /// </summary>
    public IccColorSpaceType DataColorSpace { get; }

    /// <summary>
    /// Gets the PCS encoding.
    /// </summary>
    public IccColorSpaceType ConnectionSpace { get; }

    /// <summary>
    /// Gets a value indicating whether the profile has the matrix/TRC tags (or, for gray, the gray curve).
    /// </summary>
    public bool IsMatrixShaper { get; }

    /// <summary>
    /// Gets the media white point in PCS XYZ, as absolute colorimetric uses it.
    /// </summary>
    public (double X, double Y, double Z) MediaWhitePoint { get; }

    /// <summary>
    /// Gets a value indicating whether the profile has the LUT-based tag of an intent in a direction
    /// (no fallback to the perceptual tag).
    /// </summary>
    /// <param name="intent">The rendering intent.</param>
    /// <param name="asSource">The direction: device to PCS when <see langword="true"/>.</param>
    /// <returns>Whether the tag is there.</returns>
    public bool IsClut(IccRenderingIntent intent, bool asSource)
        => this.profile is not null && this.HasTag(IccProfileModel.LutTag(asSource, intent));

    /// <summary>
    /// Gets a value indicating whether the profile supports an intent in a direction: it has the
    /// intent's LUT-based tag or the matrix/TRC tags.
    /// </summary>
    /// <param name="intent">The rendering intent.</param>
    /// <param name="asSource">The direction: device to PCS when <see langword="true"/>.</param>
    /// <returns>Whether the intent is supported.</returns>
    public bool IsIntentSupported(IccRenderingIntent intent, bool asSource)
        => this.IsClut(intent, asSource) || this.IsMatrixShaper;

    /// <summary>
    /// Reads a profile's model for an intent and a direction, which also checks the profile, and
    /// then what else a conversion needs to know about it.
    /// </summary>
    /// <param name="profile">The profile, or <see langword="null"/> for sRGB.</param>
    /// <param name="role">"source" or "target", for error messages.</param>
    /// <param name="asSource">The direction: device to PCS when <see langword="true"/>.</param>
    /// <param name="intent">The rendering intent.</param>
    /// <param name="model">The model.</param>
    /// <returns>The profile information, which keeps the model.</returns>
    /// <exception cref="InvalidImageContentException">The profile is corrupt.</exception>
    /// <exception cref="System.NotSupportedException">The engine does not support the profile.</exception>
    public static IccProfileInfo Read(IccProfile profile, string role, bool asSource, IccRenderingIntent intent, out IccProfileModel model)
    {
        model = ReadModel(profile, role, asSource, intent);
        IccProfileInfo info = new(profile, role);
        info.models[(asSource, intent)] = model;
        return info;
    }

    /// <summary>
    /// Gets the profile's model for an intent and a direction. Each one is read once.
    /// </summary>
    /// <param name="asSource">The direction: device to PCS when <see langword="true"/>.</param>
    /// <param name="intent">The rendering intent.</param>
    /// <returns>The model.</returns>
    /// <exception cref="InvalidImageContentException">The profile is corrupt.</exception>
    /// <exception cref="System.NotSupportedException">The engine does not support the profile.</exception>
    public IccProfileModel ReadModel(bool asSource, IccRenderingIntent intent)
    {
        if (!this.models.TryGetValue((asSource, intent), out IccProfileModel model))
        {
            model = ReadModel(this.profile, this.role, asSource, intent);
            this.models[(asSource, intent)] = model;
        }

        return model;
    }

    private static IccProfileModel ReadModel(IccProfile profile, string role, bool asSource, IccRenderingIntent intent)
        => profile is null ? IccSrgb.Create(role) : IccProfileModel.Read(profile, asSource, intent);

    private bool HasTag(IccProfileTag tag)
    {
        foreach (IccTagDataEntry entry in this.profile.Entries)
        {
            if (entry.TagSignature == tag)
            {
                return true;
            }
        }

        return false;
    }
}
