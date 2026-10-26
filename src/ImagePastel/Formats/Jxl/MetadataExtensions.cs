// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Metadata;

namespace SixLabors.ImageSharp;

/// <summary>
/// Extension methods for the <see cref="ImageMetadata"/> type.
/// </summary>
public static partial class MetadataExtensions
{
    /// <summary>
    /// Gets the JPEG XL format specific metadata for the image.
    /// </summary>
    /// <param name="metadata">The metadata this method extends.</param>
    /// <returns>The <see cref="JxlMetadata"/>.</returns>
    public static JxlMetadata GetJxlMetadata(this ImageMetadata metadata) => metadata.GetFormatMetadata(JxlFormat.Instance);

    /// <summary>
    /// Gets the JPEG XL format specific metadata for the image frame.
    /// </summary>
    /// <param name="metadata">The metadata this method extends.</param>
    /// <returns>The <see cref="JxlFrameMetadata"/>.</returns>
    public static JxlFrameMetadata GetJxlFrameMetadata(this ImageFrameMetadata metadata) => metadata.GetFormatMetadata(JxlFormat.Instance);
}
