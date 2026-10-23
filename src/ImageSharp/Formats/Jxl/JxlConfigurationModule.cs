// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Registers the image decoder and the format detector for the JPEG XL format. JPEG XL is decode only
/// for now: no encoder is registered, so saving an image as JPEG XL throws a <see cref="System.NotSupportedException"/>.
/// </summary>
public sealed class JxlConfigurationModule : IConfigurationModule
{
    /// <inheritdoc/>
    public void Configure(Configuration configuration)
    {
        // The encoder stays internal until it is released (slice E6 of docs/jxl/spec.md), so it is not registered.
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
    }
}
