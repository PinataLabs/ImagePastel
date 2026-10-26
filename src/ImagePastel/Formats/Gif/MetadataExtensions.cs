// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Formats.Gif;
using PinataLabs.ImagePastel.Metadata;

namespace PinataLabs.ImagePastel;

/// <summary>
/// Extension methods for the <see cref="ImageMetadata"/> type.
/// </summary>
public static partial class MetadataExtensions
{
    /// <summary>
    /// Gets the gif format specific metadata for the image.
    /// </summary>
    /// <param name="metadata">The metadata this method extends.</param>
    /// <returns>The <see cref="GifMetadata"/>.</returns>
    public static GifMetadata GetGifMetadata(this ImageMetadata metadata) => metadata.GetFormatMetadata(GifFormat.Instance);

    /// <summary>
    /// Gets the gif format specific metadata for the image frame.
    /// </summary>
    /// <param name="metadata">The metadata this method extends.</param>
    /// <returns>The <see cref="GifFrameMetadata"/>.</returns>
    public static GifFrameMetadata GetGifMetadata(this ImageFrameMetadata metadata) => metadata.GetFormatMetadata(GifFormat.Instance);
}
