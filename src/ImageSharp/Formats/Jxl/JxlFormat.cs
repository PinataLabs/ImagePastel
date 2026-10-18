// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Registers the image encoders, decoders and mime type detectors for the JPEG XL format.
/// </summary>
internal sealed class JxlFormat : IImageFormat<JxlMetadata, JxlFrameMetadata>
{
    private JxlFormat()
    {
    }

    /// <summary>
    /// Gets the current instance.
    /// </summary>
    public static JxlFormat Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "JPEG XL";

    /// <inheritdoc/>
    public string DefaultMimeType => "image/jxl";

    /// <inheritdoc/>
    public IEnumerable<string> MimeTypes => JxlConstants.MimeTypes;

    /// <inheritdoc/>
    public IEnumerable<string> FileExtensions => JxlConstants.FileExtensions;

    /// <inheritdoc/>
    public JxlMetadata CreateDefaultFormatMetadata() => new();

    /// <inheritdoc/>
    public JxlFrameMetadata CreateDefaultFormatFrameMetadata() => new();
}
