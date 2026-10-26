// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.ColorManagement;

namespace PinataLabs.ImagePastel.Formats.Jpeg.Components.Decoder;

/// <summary>
/// Represents decompressed, unprocessed jpeg data with spectral space <see cref="IJpegComponent" />-s.
/// </summary>
internal interface IRawJpegData : IDisposable
{
    /// <summary>
    /// Gets the color space
    /// </summary>
    JpegColorSpace ColorSpace { get; }

    /// <summary>
    /// Gets the components.
    /// </summary>
    IJpegComponent[] Components { get; }

    /// <summary>
    /// Gets the quantization tables, in natural order.
    /// </summary>
    Block8x8F[] QuantizationTables { get; }

    /// <summary>
    /// Gets the transform from the embedded CMYK profile for <see cref="JpegColorSpace.Cmyk"/> and
    /// <see cref="JpegColorSpace.Ycck"/> samples, or <see langword="null"/> to convert them with the
    /// CMYK-to-RGB formula.
    /// </summary>
    IccColorTransform CmykTransform { get; }
}
