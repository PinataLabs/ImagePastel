// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Registers the image encoder, image decoder and image format detector for the JPEG XL format.
/// </summary>
public sealed class JxlConfigurationModule : IConfigurationModule
{
    /// <inheritdoc/>
    public void Configure(Configuration configuration)
    {
        configuration.ImageFormatsManager.SetEncoder(JxlFormat.Instance, new JxlEncoder());
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
    }
}
