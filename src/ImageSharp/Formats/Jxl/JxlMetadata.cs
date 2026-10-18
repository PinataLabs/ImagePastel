// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

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

    private JxlMetadata(JxlMetadata other) => this.OriginalIccProfile = other.OriginalIccProfile?.DeepClone();

    /// <summary>
    /// Gets or sets the ICC profile embedded in the codestream, or null when the stream describes its
    /// colour with a colour encoding. It is set even where it does not describe the decoded pixels
    /// (XYB and CMYK images), so it is not always the pixel profile.
    /// </summary>
    public IccProfile OriginalIccProfile { get; set; }

    /// <inheritdoc/>
    public IDeepCloneable DeepClone() => new JxlMetadata(this);
}
