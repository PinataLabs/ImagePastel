// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.ColorManagement.Icc;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.Formats.Png;

/// <summary>
/// Collects the <c>cICP</c>, <c>sRGB</c>, <c>cHRM</c> and <c>gAMA</c> chunks of a PNG file and works out
/// the pixel profile they describe, with the precedence of the PNG specification (third edition, section
/// 4.3): <c>cICP</c>, then <c>iCCP</c>, then <c>sRGB</c>, then <c>cHRM</c> and <c>gAMA</c>.
/// See: https://www.w3.org/TR/png-3/#color-chunk-precedence
/// </summary>
/// <remarks>
/// <para>
/// The spec allows one of each chunk, so only the first of each is kept; a malformed chunk is ignored.
/// No pixel profile means sRGB, so an <c>sRGB</c> chunk and a <c>cICP</c> chunk for sRGB give none.
/// </para>
/// <para>
/// Only a <c>cHRM</c> chunk together with a <c>gAMA</c> chunk builds a profile. A <c>gAMA</c> chunk
/// alone gives none: it does not say which primaries the image has (its value stays in
/// <see cref="PngMetadata.Gamma"/>). A <c>cHRM</c> chunk without a <c>gAMA</c> chunk gives none either:
/// there is no tone curve, and the spec notes that decoders can do little with it (section 12.2). A
/// <c>gAMA</c> of zero is ignored (section 13.13). An invalid <c>cHRM</c> chunk is ignored. Grayscale
/// images get a gray profile with the white point only.
/// </para>
/// <para>
/// A <c>cICP</c> chunk is used only when this decoder supports its code points (ITU-T H.273): BT.709,
/// BT.2020 or SMPTE EG 432-1 (Display P3) primaries; the BT.709 (also code points 6, 14 and 15), sRGB or
/// linear transfer function; RGB matrix coefficients (0); and full range. Other code points, such as
/// PQ or HLG, leave the chunk ignored, so a lower-precedence chunk describes the image. An RGB profile
/// built from a <c>cICP</c> chunk carries a <c>cicp</c> tag with the chunk's primaries and transfer
/// function; a gray one has none (the ICC tag is for RGB).
/// </para>
/// </remarks>
internal sealed class PngColorSpaceChunks
{
    private byte[] cicp;
    private bool hasSrgb;
    private uint gamma;
    private uint[] chromaticities;

    /// <summary>
    /// Reads a <c>cICP</c> chunk: colour primaries, transfer function, matrix coefficients and video full
    /// range flag, one byte each.
    /// </summary>
    /// <param name="data">The chunk data.</param>
    public void ReadCicp(ReadOnlySpan<byte> data)
    {
        if (this.cicp is null && data.Length >= 4)
        {
            this.cicp = data.Slice(0, 4).ToArray();
        }
    }

    /// <summary>
    /// Reads an <c>sRGB</c> chunk: one byte of rendering intent, 0 to 3.
    /// </summary>
    /// <param name="data">The chunk data.</param>
    public void ReadSrgb(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 1 && data[0] <= 3)
        {
            this.hasSrgb = true;
        }
    }

    /// <summary>
    /// Reads a <c>gAMA</c> chunk: the gamma times 100000.
    /// </summary>
    /// <param name="data">The chunk data.</param>
    public void ReadGamma(ReadOnlySpan<byte> data)
    {
        if (this.gamma == 0 && data.Length >= 4)
        {
            this.gamma = BinaryPrimitives.ReadUInt32BigEndian(data);
        }
    }

    /// <summary>
    /// Reads a <c>cHRM</c> chunk: white point x, y, then red, green and blue x, y, each times 100000.
    /// </summary>
    /// <param name="data">The chunk data.</param>
    public void ReadChromaticities(ReadOnlySpan<byte> data)
    {
        if (this.chromaticities is not null || data.Length < 32)
        {
            return;
        }

        uint[] values = new uint[8];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(i * 4));
        }

        this.chromaticities = values;
    }

    /// <summary>
    /// Gets the pixel profile of the image.
    /// </summary>
    /// <param name="embeddedProfile">The profile of the <c>iCCP</c> chunk, or <see langword="null"/>.</param>
    /// <param name="gray">Whether the image is grayscale.</param>
    /// <returns>The pixel profile, or <see langword="null"/> for sRGB.</returns>
    public IccProfile GetPixelProfile(IccProfile embeddedProfile, bool gray)
    {
        if (this.TryCreateCicpProfile(gray, out IccProfile profile))
        {
            return profile;
        }

        if (embeddedProfile is not null)
        {
            return embeddedProfile;
        }

        // gAMA alone does not say which primaries the image has, so only cHRM with gAMA builds a profile.
        if (this.hasSrgb || this.gamma == 0 || this.chromaticities is null)
        {
            return null;
        }

        IccToneCurve curve;
        try
        {
            curve = IccToneCurve.Gamma(100_000F / this.gamma);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Too small a gAMA for an ICC curve.
            return null;
        }

        CieXyChromaticityCoordinates white = this.Chromaticity(0);
        RgbPrimariesChromaticityCoordinates primaries = new(this.Chromaticity(1), this.Chromaticity(2), this.Chromaticity(3));
        try
        {
            return gray ? IccProfileFactory.CreateGray(white, curve) : IccProfileFactory.CreateRgb(primaries, white, curve);
        }
        catch (ArgumentException)
        {
            // An invalid cHRM chunk is ignored, which leaves gAMA alone.
            return null;
        }
    }

    private CieXyChromaticityCoordinates Chromaticity(int index)
        => new(this.chromaticities[index * 2] * 1e-5F, this.chromaticities[(index * 2) + 1] * 1e-5F);

    private bool TryCreateCicpProfile(bool gray, out IccProfile profile)
    {
        profile = null;
        if (this.cicp is null)
        {
            return false;
        }

        byte primariesCode = this.cicp[0];
        byte transferCode = this.cicp[1];
        IccToneCurve curve = IccStandardRgb.GetToneCurve(transferCode);

        // PNG holds RGB samples (matrix coefficients 0); narrow-range samples would need rescaling.
        if (!IccStandardRgb.TryGetPrimaries(primariesCode, out (double X, double Y)[] primaries)
            || curve is null
            || this.cicp[2] != 0
            || this.cicp[3] != 1)
        {
            return false;
        }

        if (transferCode == IccStandardRgb.TransferSrgb && (gray || primariesCode == IccStandardRgb.PrimariesBt709))
        {
            // sRGB: no profile.
            return true;
        }

        // All the supported primaries have a D65 white.
        if (gray)
        {
            profile = IccProfileFactory.CreateGray(curve);
            return true;
        }

        profile = (primariesCode, transferCode) switch
        {
            (IccStandardRgb.PrimariesBt709, IccStandardRgb.TransferLinear) => IccProfileFactory.CreateLinearSrgb(),
            (IccStandardRgb.PrimariesP3D65, IccStandardRgb.TransferSrgb) => IccProfileFactory.CreateDisplayP3(),
            (IccStandardRgb.PrimariesBt2020, IccStandardRgb.TransferBt709) => IccProfileFactory.CreateRec2020(),

            // The cicp tag holds the chunk's own code points, as the standard factory profiles hold theirs.
            _ => IccStandardRgb.Create(primaries, curve, $"{PrimariesName(primariesCode)} primaries, {TransferName(transferCode)} transfer", (primariesCode, transferCode)),
        };

        return true;
    }

    private static string PrimariesName(byte code) => code switch
    {
        IccStandardRgb.PrimariesBt709 => "BT.709",
        IccStandardRgb.PrimariesBt2020 => "BT.2020",
        _ => "Display P3",
    };

    private static string TransferName(byte code) => code switch
    {
        IccStandardRgb.TransferSrgb => "sRGB",
        IccStandardRgb.TransferLinear => "linear",
        _ => "BT.709",
    };
}
