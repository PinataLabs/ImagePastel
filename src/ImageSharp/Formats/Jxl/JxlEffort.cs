// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// How much search the JPEG XL encoder spends to reach a given <see cref="JxlEncoder.Distance"/>. It
/// changes encode time and file size, not the target error.
/// </summary>
internal enum JxlEffort
{
    /// <summary>The least search, as libjxl's effort 3 (<c>SpeedTier::kFalcon</c>).</summary>
    Fastest,

    /// <summary>The usual trade-off between encode time and file size.</summary>
    Default,

    /// <summary>More search for smaller files.</summary>
    Thorough,
}
