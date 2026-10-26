// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// What <see cref="JxlContainer"/> reads from a JPEG XL container.
/// </summary>
internal sealed class JxlContainerContents
{
    /// <summary>
    /// Gets or sets the codestream, joined from its boxes.
    /// </summary>
    public byte[] Codestream { get; set; }

    /// <summary>
    /// Gets or sets the Exif data from the TIFF header on, or null when there is none.
    /// </summary>
    public byte[] Exif { get; set; }

    /// <summary>
    /// Gets or sets the XMP data, or null when there is none.
    /// </summary>
    public byte[] Xmp { get; set; }
}
