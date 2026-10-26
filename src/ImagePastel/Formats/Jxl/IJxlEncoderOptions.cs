// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>
/// Image encoder options for encoding a JPEG XL stream (ADR 0003).
/// </summary>
internal interface IJxlEncoderOptions
{
    /// <inheritdoc cref="JxlEncoder.Distance"/>
    float Distance { get; }

    /// <inheritdoc cref="JxlEncoder.Effort"/>
    JxlEffort Effort { get; }

    /// <summary>
    /// Gets the encoder's hooks for tests, or <see langword="null"/> for none. Not one of the options of
    /// ADR 0003.
    /// </summary>
    JxlEncoderTestHooks TestHooks { get; }
}
