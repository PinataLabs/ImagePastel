// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;

namespace PinataLabs.ImagePastel.ColorManagement.Icc;

/// <summary>
/// A monochrome profile (ICC.1:2022 section 8.3.3): one tone curve, <c>kTRC</c>. With an XYZ PCS
/// the curve gives the luminance Y, and the PCS value is the D50 white scaled by it. With a Lab PCS
/// the curve gives L* / 100, and a* and b* are 0.
/// </summary>
/// <remarks>
/// Pixels reach the engine as RGB. On the way in, the gray value is the BT.709 luma of the RGB
/// values (as ImageSharp's gray pixel types compute it), which is the value itself for gray pixels.
/// On the way out, the gray value goes to all three RGB channels.
/// </remarks>
internal sealed class IccGrayTrcModel : IccProfileModel
{
    /// <summary>
    /// The gray value of RGB: the ITU-R BT.709 luma coefficients, as ColorNumerics.GetBT709Luminance
    /// uses them, in all three rows.
    /// </summary>
    internal static readonly IccMatrix Luma = new(
        (0.2126, 0.7152, 0.0722),
        (0.2126, 0.7152, 0.0722),
        (0.2126, 0.7152, 0.0722));

    private readonly IccTagDataEntry[] curves;
    private readonly IccColorSpaceType connectionSpace;

    private IccGrayTrcModel(IccTagDataEntry curve, IccColorSpaceType connectionSpace)
    {
        this.curves = [curve, curve, curve];
        this.connectionSpace = connectionSpace;
    }

    /// <inheritdoc/>
    public override IccColorSpaceType ConnectionSpace => this.connectionSpace;

    /// <summary>
    /// Reads a monochrome profile.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="connectionSpace">The profile's PCS encoding.</param>
    /// <param name="role">"source" or "target", for error messages.</param>
    /// <returns>The model.</returns>
    public static IccGrayTrcModel Read(IccProfile profile, IccColorSpaceType connectionSpace, string role)
        => new(ReadCurve(profile, IccProfileTag.GrayTrc, role), connectionSpace);

    /// <inheritdoc/>
    public override void AddDeviceToPcs(List<IccStage> stages)
    {
        stages.Add(new IccMatrixStage(Luma));
        stages.Add(new IccCurveStage(this.curves, inverse: false));

        stages.Add(new IccMatrixStage(this.connectionSpace == IccColorSpaceType.CieLab
            ? new IccMatrix((100, 0, 0), (0, 0, 0), (0, 0, 0))
            : IccMatrix.Diagonal(IccConnectionSpace.D50X, IccConnectionSpace.D50Y, IccConnectionSpace.D50Z)));
    }

    /// <inheritdoc/>
    public override void AddPcsToDevice(List<IccStage> stages)
    {
        // Pick Y from XYZ, or L* / 100 from Lab, into all three channels.
        stages.Add(new IccMatrixStage(this.connectionSpace == IccColorSpaceType.CieLab
            ? new IccMatrix((0.01, 0, 0), (0.01, 0, 0), (0.01, 0, 0))
            : new IccMatrix((0, 1, 0), (0, 1, 0), (0, 1, 0))));
        stages.Add(new IccCurveStage(this.curves, inverse: true));
    }
}
