// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Numerics;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// An RGB matrix/TRC profile (ICC.1:2022 section 8.3.4): three tone curves (<c>rTRC</c>,
/// <c>gTRC</c>, <c>bTRC</c>) and the colorant matrix (<c>rXYZ</c>, <c>gXYZ</c>, <c>bXYZ</c>).
/// </summary>
/// <remarks>
/// The colorants are relative to the D50 PCS white in both ICC v2 and v4 profiles, so for the
/// relative colorimetric result the media white point (<c>wtpt</c>) and the adaptation matrix
/// (<c>chad</c>) are not needed. They matter for the absolute colorimetric intent.
/// </remarks>
internal sealed class IccMatrixTrcModel : IccProfileModel
{
    private readonly IccMatrix colorants;
    private readonly IccTagDataEntry[] curves;
    private readonly IccColorSpaceType connectionSpace;
    private readonly string role;

    /// <summary>
    /// Initializes a new instance of the <see cref="IccMatrixTrcModel"/> class.
    /// </summary>
    /// <param name="colorants">The matrix from linear RGB to PCS XYZ, with the colorants as columns.</param>
    /// <param name="curves">The red, green and blue tone curves.</param>
    /// <param name="connectionSpace">The PCS encoding.</param>
    /// <param name="role">"source" or "target", for error messages.</param>
    public IccMatrixTrcModel(IccMatrix colorants, IccTagDataEntry[] curves, IccColorSpaceType connectionSpace, string role)
    {
        this.colorants = colorants;
        this.curves = curves;
        this.connectionSpace = connectionSpace;
        this.role = role;
    }

    /// <inheritdoc/>
    public override IccColorSpaceType ConnectionSpace => this.connectionSpace;

    /// <summary>
    /// Reads an RGB matrix/TRC profile.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="connectionSpace">The profile's PCS encoding.</param>
    /// <param name="role">"source" or "target", for error messages.</param>
    /// <returns>The model.</returns>
    public static IccMatrixTrcModel Read(IccProfile profile, IccColorSpaceType connectionSpace, string role)
    {
        IccMatrix colorants = IccMatrix.FromColumns(
            ReadColorant(profile, IccProfileTag.RedMatrixColumn, role),
            ReadColorant(profile, IccProfileTag.GreenMatrixColumn, role),
            ReadColorant(profile, IccProfileTag.BlueMatrixColumn, role));

        IccTagDataEntry[] curves =
        [
            ReadCurve(profile, IccProfileTag.RedTrc, role),
            ReadCurve(profile, IccProfileTag.GreenTrc, role),
            ReadCurve(profile, IccProfileTag.BlueTrc, role),
        ];

        return new IccMatrixTrcModel(colorants, curves, connectionSpace, role);
    }

    /// <inheritdoc/>
    public override void AddDeviceToPcs(List<IccStage> stages)
    {
        stages.Add(new IccCurveStage(this.curves, inverse: false));
        stages.Add(new IccMatrixStage(this.colorants));

        // Little CMS accepts a Lab PCS here too (the specification allows only XYZ), and so do we.
        if (this.connectionSpace == IccColorSpaceType.CieLab)
        {
            stages.Add(IccXyzToLabStage.Instance);
        }
    }

    /// <inheritdoc/>
    public override void AddPcsToDevice(List<IccStage> stages)
    {
        if (!this.colorants.TryInvert(out IccMatrix inverse))
        {
            throw Corrupt(this.role, "its colorant matrix cannot be inverted");
        }

        if (this.connectionSpace == IccColorSpaceType.CieLab)
        {
            stages.Add(IccLabToXyzStage.Instance);
        }

        stages.Add(new IccMatrixStage(inverse));
        stages.Add(new IccCurveStage(this.curves, inverse: true));
    }

    private static Vector3 ReadColorant(IccProfile profile, IccProfileTag signature, string role)
        => profile.TryGetTag(signature, out IccXyzTagDataEntry entry) && entry.Data.Length > 0
            ? entry.Data[0]
            : throw Corrupt(role, $"it has no readable {signature} colorant");
}
