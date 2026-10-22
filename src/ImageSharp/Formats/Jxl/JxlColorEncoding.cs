// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.Formats.Jxl.Headers;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Describes the enumerated colour encoding of a JPEG XL image: the colour space, white point, primaries,
/// transfer function and rendering intent that its header codes in place of an ICC profile.
/// </summary>
public sealed class JxlColorEncoding
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JxlColorEncoding"/> class from the colour encoding of an
    /// image header that signals no ICC profile.
    /// </summary>
    /// <param name="header">The colour encoding of the image header.</param>
    internal JxlColorEncoding(JxlColorEncodingHeader header)
    {
        this.ColorSpace = header.ColorSpace;
        this.WhitePoint = header.WhitePoint;
        this.Primaries = header.Primaries;
        this.RenderingIntent = header.RenderingIntent;

        JxlCustomTransferFunction tf = header.Tf;
        if (tf.HaveGamma)
        {
            this.TransferFunction = JxlTransferFunction.Gamma;
            this.Gamma = tf.Gamma / (double)JxlCustomTransferFunction.GammaMul;
        }
        else
        {
            this.TransferFunction = tf.TransferFunction;
        }

        if (header.WhitePoint == JxlWhitePoint.Custom)
        {
            this.CustomWhitePoint = ToXy(header.White);
        }

        if (header.HasPrimaries && header.Primaries == JxlPrimaries.Custom)
        {
            this.CustomRedPrimary = ToXy(header.Red);
            this.CustomGreenPrimary = ToXy(header.Green);
            this.CustomBluePrimary = ToXy(header.Blue);
        }
    }

    /// <summary>
    /// Gets the colour space of the colour channels.
    /// </summary>
    public JxlColorSpace ColorSpace { get; }

    /// <summary>
    /// Gets the white point. When it is <see cref="JxlWhitePoint.Custom"/>, <see cref="CustomWhitePoint"/>
    /// gives its chromaticity.
    /// </summary>
    public JxlWhitePoint WhitePoint { get; }

    /// <summary>
    /// Gets the primaries. When they are <see cref="JxlPrimaries.Custom"/>, <see cref="CustomRedPrimary"/>,
    /// <see cref="CustomGreenPrimary"/> and <see cref="CustomBluePrimary"/> give their chromaticities.
    /// A grey or XYB colour space has no primaries, and then this is <see cref="JxlPrimaries.Srgb"/>, as in libjxl.
    /// </summary>
    public JxlPrimaries Primaries { get; }

    /// <summary>
    /// Gets the transfer function. When it is <see cref="JxlTransferFunction.Gamma"/>, <see cref="Gamma"/>
    /// gives the exponent.
    /// </summary>
    public JxlTransferFunction TransferFunction { get; }

    /// <summary>
    /// Gets the rendering intent.
    /// </summary>
    public JxlRenderingIntent RenderingIntent { get; }

    /// <summary>
    /// Gets the exponent of the gamma curve when <see cref="TransferFunction"/> is
    /// <see cref="JxlTransferFunction.Gamma"/>, otherwise null. It is the encoding exponent, in (0, 1]: the
    /// reciprocal of the display gamma, so about 0.4545 for a display gamma of 2.2.
    /// </summary>
    public double? Gamma { get; }

    /// <summary>
    /// Gets the chromaticity of the white point when <see cref="WhitePoint"/> is
    /// <see cref="JxlWhitePoint.Custom"/>, otherwise null.
    /// </summary>
    public CieXyChromaticityCoordinates? CustomWhitePoint { get; }

    /// <summary>
    /// Gets the chromaticity of the red primary when <see cref="Primaries"/> is
    /// <see cref="JxlPrimaries.Custom"/>, otherwise null.
    /// </summary>
    public CieXyChromaticityCoordinates? CustomRedPrimary { get; }

    /// <summary>
    /// Gets the chromaticity of the green primary when <see cref="Primaries"/> is
    /// <see cref="JxlPrimaries.Custom"/>, otherwise null.
    /// </summary>
    public CieXyChromaticityCoordinates? CustomGreenPrimary { get; }

    /// <summary>
    /// Gets the chromaticity of the blue primary when <see cref="Primaries"/> is
    /// <see cref="JxlPrimaries.Custom"/>, otherwise null.
    /// </summary>
    public CieXyChromaticityCoordinates? CustomBluePrimary { get; }

    private static CieXyChromaticityCoordinates ToXy(JxlCustomXy xy)
    {
        (double x, double y) = xy.Value;
        return new CieXyChromaticityCoordinates((float)x, (float)y);
    }
}
