// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// How much search the JPEG XL encoder spends to reach a given <see cref="JxlEncoder.Distance"/>. It
/// changes encode time and file size, not the target error.
/// </summary>
public enum JxlEffort
{
    /// <summary>
    /// The quickest encode and the largest files, as libjxl's effort 3. Lossy encoding codes every block
    /// the same way, with no search.
    /// </summary>
    Fastest,

    /// <summary>
    /// The usual trade-off between encode time and file size, as libjxl's effort 5. Lossy encoding adapts
    /// the quantisation and the block sizes to the image.
    /// </summary>
    Default,

    /// <summary>
    /// The slowest encode and the smallest files, as libjxl's effort 7. Lossy encoding searches more block
    /// shapes and, where the loop filter is on, tunes its strength for each block.
    /// </summary>
    Thorough,
}
