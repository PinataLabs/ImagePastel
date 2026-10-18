// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.Formats.Tiff;

/// <summary>
/// Provides Tiff specific metadata information for the image.
/// </summary>
public class TiffMetadata : IDeepCloneable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TiffMetadata"/> class.
    /// </summary>
    public TiffMetadata()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TiffMetadata"/> class.
    /// </summary>
    /// <param name="other">The metadata to create an instance from.</param>
    private TiffMetadata(TiffMetadata other)
    {
        this.ByteOrder = other.ByteOrder;
        this.EmbeddedIccProfile = other.EmbeddedIccProfile?.DeepClone();
    }

    /// <summary>
    /// Gets or sets the byte order.
    /// </summary>
    public ByteOrder ByteOrder { get; set; }

    /// <summary>
    /// Gets or sets the format type.
    /// </summary>
    public TiffFormatType FormatType { get; set; }

    /// <summary>
    /// Gets or sets the embedded ICC profile of the first frame, exactly as the file stores it, or
    /// <see langword="null"/> when there is none or metadata is ignored. It is not always the pixel
    /// profile (<c>ImageMetadata.IccProfile</c>): a CMYK profile describes the file's CMYK samples,
    /// not the decoded RGB pixels, so a CMYK image keeps it only here, and a converted image has the
    /// target profile (or none) as its pixel profile. Each frame's is in
    /// <see cref="TiffFrameMetadata.EmbeddedIccProfile"/>.
    /// </summary>
    public IccProfile EmbeddedIccProfile { get; set; }

    /// <inheritdoc/>
    public IDeepCloneable DeepClone() => new TiffMetadata(this);
}
