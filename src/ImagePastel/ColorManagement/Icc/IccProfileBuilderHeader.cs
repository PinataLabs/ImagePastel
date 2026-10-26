// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;

namespace PinataLabs.ImagePastel.ColorManagement.Icc;

/// <summary>
/// What every profile <see cref="IccProfileBuilder"/> builds needs besides its colour transform: the white
/// point and primaries, the white they are adapted to, and the header and text fields.
/// </summary>
internal sealed class IccProfileBuilderHeader
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IccProfileBuilderHeader"/> class.
    /// </summary>
    /// <param name="white">The white point as CIE xy chromaticity.</param>
    /// <param name="primaries">
    /// The red, green and blue primaries as CIE xy chromaticity; null for gray, or to check only the
    /// white point of an RGB-like space.
    /// </param>
    /// <param name="adaptedWhite">
    /// The white that <c>chad</c> and the colorants adapt the white point to: <see cref="IccConnectionSpace.D50"/>,
    /// as the ICC specification and Little CMS do, or <see cref="IccProfileBuilder.LibjxlD50"/>, as libjxl does.
    /// </param>
    /// <param name="renderingIntent">The rendering intent of the header.</param>
    /// <param name="description">The text of the <c>desc</c> tag.</param>
    /// <param name="signature">The CMM type and creator signature of the header, or null for none.</param>
    /// <param name="cicp">
    /// The H.273 colour primaries and transfer characteristics that identify an RGB space, written to a
    /// <c>cicp</c> tag with matrix coefficients 0 and full range, as libjxl's <c>MaybeCreateICCCICPTag</c>
    /// does; null for no tag. Ignored for gray.
    /// </param>
    public IccProfileBuilderHeader(
        (double X, double Y) white,
        (double X, double Y)[] primaries,
        Vector3 adaptedWhite,
        IccRenderingIntent renderingIntent,
        string description,
        string signature,
        (byte ColorPrimaries, byte TransferCharacteristics)? cicp = null)
    {
        this.White = white;
        this.Primaries = primaries;
        this.AdaptedWhite = adaptedWhite;
        this.RenderingIntent = renderingIntent;
        this.Description = description;
        this.Signature = signature;
        this.Cicp = cicp;
    }

    /// <summary>Gets the white point as CIE xy chromaticity.</summary>
    public (double X, double Y) White { get; }

    /// <summary>Gets the red, green and blue primaries as CIE xy chromaticity, or null.</summary>
    public (double X, double Y)[] Primaries { get; }

    /// <summary>Gets the white that <c>chad</c> and the colorants adapt the white point to.</summary>
    public Vector3 AdaptedWhite { get; }

    /// <summary>Gets the rendering intent of the header.</summary>
    public IccRenderingIntent RenderingIntent { get; }

    /// <summary>Gets the text of the <c>desc</c> tag.</summary>
    public string Description { get; }

    /// <summary>Gets the CMM type and creator signature of the header, or null for none.</summary>
    public string Signature { get; }

    /// <summary>Gets the H.273 code points of the <c>cicp</c> tag, or null for no tag.</summary>
    public (byte ColorPrimaries, byte TransferCharacteristics)? Cicp { get; }
}
