// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Image encoder options for encoding a JPEG XL stream (ADR 0003).
/// </summary>
internal interface IJxlEncoderOptions
{
    /// <summary>
    /// Gets the target for lossy error, in butteraugli units: 0 means lossless, about 1 is visually
    /// lossless, and larger values allow more visible loss.
    /// </summary>
    float Distance { get; }

    /// <summary>Gets how much search the encoder spends to reach <see cref="Distance"/>.</summary>
    JxlEffort Effort { get; }

    /// <summary>
    /// Gets a value indicating whether a lossy frame keeps every coefficient order natural, instead of
    /// customising the DCT8 order as libjxl does. Not one of the options of ADR 0003: tests use it to
    /// compare an encode with the natural-order encode of the same image.
    /// </summary>
    bool NaturalCoeffOrders { get; }
}
