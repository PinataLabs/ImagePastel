// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// The order of the elements of each table type, the matrix of lut8Type and lut16Type tables
// applying only to three inputs, and the version 2 Lab encoding of lut16Type tables follow
// Type_LUT8_Read, Type_LUT16_Read, Type_LUTA2B_Read and Type_LUTB2A_Read in src/cmstypes.c,
// _cmsReadInputLUT, _cmsReadOutputLUT and ChangeInterpolationToTrilinear in src/cmsio1.c, and
// _cmsStageAllocLabV2ToV4 in src/cmslut.c, of Little CMS (https://github.com/mm2/Little-CMS) at
// commit 5176347 (vendored in libjxl third_party/lcms).
// Copyright (c) Marti Maria Saguer. MIT License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Numerics;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// A profile used through one of its LUT-based tags (ICC.1:2022 sections 10.10, 10.11, 10.12 and
/// 10.13): an <c>A2Bn</c> tag as a source, a <c>B2An</c> tag as a target, chosen by the rendering
/// intent (see <see cref="IccProfileModel.Read"/>). The tag may be a <c>lut8Type</c>,
/// <c>lut16Type</c>, <c>lutAToBType</c> or <c>lutBToAType</c>, and the profile may be RGB, gray
/// or CMYK.
/// </summary>
/// <remarks>
/// <para>
/// The table works on values encoded in [0, 1]. The model adds the stages that turn its PCS side
/// into the unencoded PCS of the pipeline: XYZ with 1.0 at 0x8000 of 0xFFFF; Lab with L* = 100 at
/// 1.0 and a*, b* = 0 at 128 / 255, except for <c>lut16Type</c> tables, which use the version 2
/// Lab encoding (L* = 100 at 0xFF00 of 0xFFFF).
/// </para>
/// <para>
/// Pixels reach the engine as RGBA. Gray profiles take the BT.709 luma of the RGB values and give
/// their gray value in all three RGB channels, as <see cref="IccGrayTrcModel"/> does. CMYK values
/// fill all four channels: a CMYK source gives opaque colors, and a CMYK target writes its black
/// over the alpha channel.
/// </para>
/// </remarks>
internal sealed class IccLutModel : IccProfileModel
{
    // Gray in all three channels.
    private static readonly IccMatrix Replicate = new((1, 0, 0), (1, 0, 0), (1, 0, 0));

    private readonly IccColorSpaceType connectionSpace;
    private readonly IccStage[] stages;
    private readonly bool isSource;

    private IccLutModel(IccColorSpaceType connectionSpace, IccStage[] stages, bool isSource)
    {
        this.connectionSpace = connectionSpace;
        this.stages = stages;
        this.isSource = isSource;
    }

    /// <inheritdoc/>
    public override IccColorSpaceType ConnectionSpace => this.connectionSpace;

    /// <summary>
    /// Reads the model of a profile from its LUT-based tag.
    /// </summary>
    /// <param name="lut">The table: an <c>A2Bn</c> tag for a source, a <c>B2An</c> tag for a target.</param>
    /// <param name="tag">The tag the table comes from, for error messages.</param>
    /// <param name="dataColorSpace">The profile's data color space: RGB, gray or CMYK.</param>
    /// <param name="connectionSpace">The profile's PCS encoding.</param>
    /// <param name="asSource">Whether the profile is the source.</param>
    /// <param name="role">"source" or "target", for error messages.</param>
    /// <returns>The model.</returns>
    /// <exception cref="InvalidImageContentException">The table does not fit the profile.</exception>
    public static IccLutModel Read(IccTagDataEntry lut, IccProfileTag tag, IccColorSpaceType dataColorSpace, IccColorSpaceType connectionSpace, bool asSource, string role)
    {
        int deviceChannels = dataColorSpace switch
        {
            IccColorSpaceType.Gray => 1,
            IccColorSpaceType.Rgb => 3,
            _ => 4,
        };

        Builder table = new(asSource ? deviceChannels : 3, role, tag.ToString());

        // Little CMS interpolates three-input tables indexed by Lab trilinearly.
        bool trilinear = !asSource && connectionSpace == IccColorSpaceType.CieLab;
        bool versionTwoLab = false;
        switch (lut)
        {
            case IccLut8TagDataEntry lut8:
                table.AddLegacy(lut8.Matrix, lut8.InputValues, lut8.ClutValues, lut8.OutputValues, trilinear);
                break;
            case IccLut16TagDataEntry lut16:
                table.AddLegacy(lut16.Matrix, lut16.InputValues, lut16.ClutValues, lut16.OutputValues, trilinear);
                versionTwoLab = connectionSpace == IccColorSpaceType.CieLab;
                break;
            case IccLutAToBTagDataEntry aToB when asSource:
                table.AddCurves(aToB.CurveA);
                table.AddClut(aToB.ClutValues, trilinear);
                table.AddCurves(aToB.CurveM);
                table.AddMatrix(aToB.Matrix3x3, aToB.Matrix3x1);
                table.AddCurves(aToB.CurveB);
                break;
            case IccLutBToATagDataEntry bToA when !asSource:
                table.AddCurves(bToA.CurveB);
                table.AddMatrix(bToA.Matrix3x3, bToA.Matrix3x1);
                table.AddCurves(bToA.CurveM);
                table.AddClut(bToA.ClutValues, trilinear);
                table.AddCurves(bToA.CurveA);
                break;
            default:
                throw Corrupt(role, $"its {tag} tag is a {lut.Signature} table");
        }

        table.CheckEnd(asSource ? 3 : deviceChannels);

        IccMatrix decode = Decoding(connectionSpace, versionTwoLab);
        List<IccStage> stages = [];
        if (asSource)
        {
            if (deviceChannels == 1)
            {
                stages.Add(new IccMatrixStage(IccGrayTrcModel.Luma));
            }

            stages.AddRange(table.Stages);
            stages.Add(new IccMatrixStage(decode));
            if (deviceChannels == 4)
            {
                stages.Add(IccOpaqueStage.Instance);
            }
        }
        else
        {
            decode.TryInvert(out IccMatrix encode);
            stages.Add(new IccMatrixStage(encode));
            stages.AddRange(table.Stages);
            if (deviceChannels == 1)
            {
                stages.Add(new IccMatrixStage(Replicate));
            }
        }

        return new IccLutModel(connectionSpace, [.. stages], asSource);
    }

    /// <inheritdoc/>
    public override void AddDeviceToPcs(List<IccStage> stages)
    {
        if (!this.isSource)
        {
            throw new InvalidOperationException("The model was read for the target side.");
        }

        stages.AddRange(this.stages);
    }

    /// <inheritdoc/>
    public override void AddPcsToDevice(List<IccStage> stages)
    {
        if (this.isSource)
        {
            throw new InvalidOperationException("The model was read for the source side.");
        }

        stages.AddRange(this.stages);
    }

    // From the [0, 1] encoding of a table's PCS side to the unencoded PCS.
    private static IccMatrix Decoding(IccColorSpaceType connectionSpace, bool versionTwoLab)
    {
        if (connectionSpace == IccColorSpaceType.CieXyz)
        {
            // u1Fixed15Number: 1.0 is 0x8000.
            const double xyz = 65535D / 32768D;
            return IccMatrix.Diagonal(xyz, xyz, xyz);
        }

        // Version 2 Lab puts L* = 100 at 0xFF00 rather than 0xFFFF, and a* = b* = 0 at 0x8000 rather than 0x8080.
        double scale = versionTwoLab ? 65535D / 65280D : 1;
        return new IccMatrix((100 * scale, 0, 0), (0, 255 * scale, 0), (0, 0, 255 * scale), (0, -128, -128));
    }

    /// <summary>
    /// Builds the stages of a table, checking that each element takes as many channels as the one
    /// before gives.
    /// </summary>
    private sealed class Builder
    {
        private readonly string role;
        private readonly string tag;
        private int channels;

        public Builder(int channels, string role, string tag)
        {
            this.channels = channels;
            this.role = role;
            this.tag = tag;
        }

        public List<IccStage> Stages { get; } = [];

        // A lut8Type or lut16Type table: matrix (three inputs only), input tables, CLUT, output tables.
        public void AddLegacy(Matrix4x4 matrix, IccLut[] inputTables, IccClut clut, IccLut[] outputTables, bool trilinear)
        {
            if (this.channels == 3 && !matrix.IsIdentity)
            {
                this.AddMatrix(matrix, null);
            }

            this.AddTables(inputTables);
            this.AddClut(clut, trilinear);
            this.AddTables(outputTables);
        }

        public void AddCurves(IccTagDataEntry[] curves)
        {
            if (curves is null)
            {
                return;
            }

            this.Expect(curves.Length, "curves");
            foreach (IccTagDataEntry curve in curves)
            {
                if (!IccCurveEvaluator.IsCurve(curve))
                {
                    throw this.Corrupt("a curve of an unknown type");
                }
            }

            this.Stages.Add(new IccCurveStage(curves, inverse: false));
        }

        public void AddClut(IccClut clut, bool trilinear)
        {
            if (clut is null)
            {
                return;
            }

            this.Expect(clut.InputChannelCount, "CLUT");
            if (clut.OutputChannelCount > 4)
            {
                throw this.Corrupt($"a CLUT with {clut.OutputChannelCount} outputs");
            }

            for (int i = 0; i < clut.InputChannelCount; i++)
            {
                if (clut.GridPointCount[i] < 2)
                {
                    throw this.Corrupt($"a CLUT with {clut.GridPointCount[i]} grid points in a dimension");
                }
            }

            this.Stages.Add(new IccClutStage(clut, trilinear));
            this.channels = clut.OutputChannelCount;
        }

        public void AddMatrix(Matrix4x4? matrix, Vector3? offset)
        {
            if (matrix is null)
            {
                return;
            }

            this.Expect(3, "matrix");

            // The tag's matrix is stored transposed (see IccLutAToBTagDataEntry).
            Matrix4x4 m = matrix.Value;
            Vector3 o = offset ?? Vector3.Zero;
            this.Stages.Add(new IccMatrixStage(new IccMatrix(
                (m.M11, m.M21, m.M31),
                (m.M12, m.M22, m.M32),
                (m.M13, m.M23, m.M33),
                (o.X, o.Y, o.Z))));
        }

        public void CheckEnd(int channels)
        {
            if (this.channels != channels)
            {
                throw this.Corrupt($"{this.channels} output channels where {channels} are expected");
            }
        }

        private void AddTables(IccLut[] tables)
        {
            // A lut16Type table may have no entries (a Little CMS extension): no tables.
            if (tables.Length == 0 || tables[0].Values.Length == 0)
            {
                return;
            }

            IccTagDataEntry[] curves = new IccTagDataEntry[tables.Length];
            for (int i = 0; i < tables.Length; i++)
            {
                // One entry would read as a gamma; a table needs two at least.
                if (tables[i].Values.Length < 2)
                {
                    throw this.Corrupt("a one-dimensional table with fewer than two entries");
                }

                curves[i] = new IccCurveTagDataEntry(tables[i].Values);
            }

            this.AddCurves(curves);
        }

        private void Expect(int channels, string element)
        {
            if (channels != this.channels || channels is < 1 or > 4)
            {
                throw this.Corrupt($"{channels} channels at its {element} where {this.channels} are expected");
            }
        }

        private InvalidImageContentException Corrupt(string reason)
            => IccProfileModel.Corrupt(this.role, $"its {this.tag} table has {reason}");
    }
}
