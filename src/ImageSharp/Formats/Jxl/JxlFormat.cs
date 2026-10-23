// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Describes the JPEG XL format (ISO/IEC 18181): its name, mime types and file extensions.
/// </summary>
public sealed class JxlFormat : IImageFormat<JxlMetadata, JxlFrameMetadata>
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
