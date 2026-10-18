// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Provides JPEG XL specific metadata information for the image.
/// </summary>
internal sealed class JxlMetadata : IDeepCloneable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JxlMetadata"/> class.
    /// </summary>
    public JxlMetadata()
    {
    }

    private JxlMetadata(JxlMetadata other)
    {
        this.ColorEncoding = other.ColorEncoding?.Clone();
        this.IsXybEncoded = other.IsXybEncoded;
        this.OriginalIccProfile = other.OriginalIccProfile?.DeepClone();
    }

    /// <summary>
    /// Gets or sets the enumerated colour encoding of the image, or null when the stream signals an ICC
    /// profile instead.
    /// </summary>
    public JxlColorEncoding ColorEncoding { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the image is coded in the XYB colour space. Such images
    /// decode to the colour their encoding or profile describes, not to XYB.
    /// </summary>
    public bool IsXybEncoded { get; set; }

    /// <summary>
    /// Gets or sets the ICC profile embedded in the codestream, or null when the stream describes its
    /// colour with a colour encoding. It is set even where it does not describe the decoded pixels
    /// (XYB and CMYK images), so it is not always the pixel profile.
    /// </summary>
    public IccProfile OriginalIccProfile { get; set; }

    /// <inheritdoc/>
    public IDeepCloneable DeepClone() => new JxlMetadata(this);
}
