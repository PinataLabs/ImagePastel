// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;

namespace PinataLabs.ImagePastel.Formats.Tiff.PhotometricInterpretation;

/// <summary>
/// Converts ink amounts to RGB with the simple CMYK-to-RGB formula that the JPEG decoder also uses:
/// R = (1 - C) * (1 - K), G = (1 - M) * (1 - K) and B = (1 - Y) * (1 - K).
/// It ignores any embedded CMYK profile.
/// </summary>
internal static class TiffCmykFormula
{
    /// <summary>
    /// Converts a row in place.
    /// </summary>
    /// <param name="row">
    /// On input, the ink amounts from <see cref="TiffCmykSamples"/>.
    /// On output, the RGB colours as scaled vectors, with an opaque alpha.
    /// </param>
    public static void ToRgb(Span<Vector4> row)
    {
        for (int x = 0; x < row.Length; x++)
        {
            Vector4 cmyk = row[x];
            float white = 1F - cmyk.W;
            row[x] = new Vector4((1F - cmyk.X) * white, (1F - cmyk.Y) * white, (1F - cmyk.Z) * white, 1F);
        }
    }
}
