// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;

namespace PinataLabs.ImagePastel.Metadata.Profiles.Icc;

/// <summary>
/// The cicpType (ICC v4.4): the coding-independent code points of ITU-T H.273 that identify the color
/// space a profile describes. A profile carries it in its <see cref="IccProfileTag.Cicp"/> tag.
/// </summary>
internal sealed class IccCicpTagDataEntry : IccTagDataEntry, IEquatable<IccCicpTagDataEntry>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IccCicpTagDataEntry"/> class.
    /// </summary>
    /// <param name="colorPrimaries">The H.273 ColourPrimaries code point.</param>
    /// <param name="transferCharacteristics">The H.273 TransferCharacteristics code point.</param>
    /// <param name="matrixCoefficients">The H.273 MatrixCoefficients code point.</param>
    /// <param name="videoFullRangeFlag">The H.273 VideoFullRangeFlag: 1 for full range, 0 for narrow range.</param>
    public IccCicpTagDataEntry(byte colorPrimaries, byte transferCharacteristics, byte matrixCoefficients, byte videoFullRangeFlag)
        : this(colorPrimaries, transferCharacteristics, matrixCoefficients, videoFullRangeFlag, IccProfileTag.Unknown)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IccCicpTagDataEntry"/> class.
    /// </summary>
    /// <param name="colorPrimaries">The H.273 ColourPrimaries code point.</param>
    /// <param name="transferCharacteristics">The H.273 TransferCharacteristics code point.</param>
    /// <param name="matrixCoefficients">The H.273 MatrixCoefficients code point.</param>
    /// <param name="videoFullRangeFlag">The H.273 VideoFullRangeFlag: 1 for full range, 0 for narrow range.</param>
    /// <param name="tagSignature">Tag Signature</param>
    public IccCicpTagDataEntry(byte colorPrimaries, byte transferCharacteristics, byte matrixCoefficients, byte videoFullRangeFlag, IccProfileTag tagSignature)
        : base(IccTypeSignature.Cicp, tagSignature)
    {
        this.ColorPrimaries = colorPrimaries;
        this.TransferCharacteristics = transferCharacteristics;
        this.MatrixCoefficients = matrixCoefficients;
        this.VideoFullRangeFlag = videoFullRangeFlag;
    }

    /// <summary>
    /// Gets the H.273 ColourPrimaries code point, for example 1 for BT.709 or 12 for Display P3.
    /// </summary>
    public byte ColorPrimaries { get; }

    /// <summary>
    /// Gets the H.273 TransferCharacteristics code point, for example 13 for sRGB or 8 for linear.
    /// </summary>
    public byte TransferCharacteristics { get; }

    /// <summary>
    /// Gets the H.273 MatrixCoefficients code point; 0 (identity) for RGB.
    /// </summary>
    public byte MatrixCoefficients { get; }

    /// <summary>
    /// Gets the H.273 VideoFullRangeFlag: 1 for full range, 0 for narrow range.
    /// </summary>
    public byte VideoFullRangeFlag { get; }

    /// <inheritdoc/>
    public override bool Equals(IccTagDataEntry other) => other is IccCicpTagDataEntry entry && this.Equals(entry);

    /// <inheritdoc/>
    public bool Equals(IccCicpTagDataEntry other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return base.Equals(other)
            && this.ColorPrimaries == other.ColorPrimaries
            && this.TransferCharacteristics == other.TransferCharacteristics
            && this.MatrixCoefficients == other.MatrixCoefficients
            && this.VideoFullRangeFlag == other.VideoFullRangeFlag;
    }

    /// <inheritdoc/>
    public override bool Equals(object obj) => obj is IccCicpTagDataEntry other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(this.Signature, this.ColorPrimaries, this.TransferCharacteristics, this.MatrixCoefficients, this.VideoFullRangeFlag);
}
