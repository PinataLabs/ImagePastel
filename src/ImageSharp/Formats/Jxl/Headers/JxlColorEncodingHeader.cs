// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/color_encoding_internal.h and lib/jxl/color_encoding_internal.cc (Customxy,
// CustomTransferFunction, ColorEncoding fields), with the storage defaults and gamma limits of
// lib/jxl/cms/color_encoding_cms.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.ColorManagement.Icc;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Headers;

/// <summary>
/// The colour encoding field bundle of the image header: either an enumerated encoding, or a flag that an
/// ICC profile follows. <see cref="JxlColorEncoding"/> is its public description.
/// </summary>
/// <remarks>
/// libjxl builds an ICC profile from the enumerated fields as it reads them (<c>CreateICC</c>) and
/// rejects the header when that fails. Here <see cref="Icc.JxlIccBuilder"/> does that once the headers
/// are read.
/// </remarks>
internal sealed partial class JxlColorEncodingHeader : JxlFields
{
    public JxlColorEncodingHeader() => JxlBundle.Init(this);

    /// <summary>Gets or sets a value indicating whether an ICC profile follows the headers instead of the enumerated fields.</summary>
    public bool WantIcc { get; set; }

    /// <summary>Gets or sets the colour space.</summary>
    public JxlColorSpace ColorSpace { get; set; } = JxlColorSpace.Rgb;

    /// <summary>Gets or sets the white point.</summary>
    public JxlWhitePoint WhitePoint { get; set; } = JxlWhitePoint.D65;

    /// <summary>Gets the custom white point, used if <see cref="WhitePoint"/> is <see cref="JxlWhitePoint.Custom"/>.</summary>
    public JxlCustomXy White { get; } = new();

    /// <summary>Gets or sets the primaries. Only valid if <see cref="HasPrimaries"/>.</summary>
    public JxlPrimaries Primaries { get; set; } = JxlPrimaries.Srgb;

    /// <summary>Gets the custom red primary.</summary>
    public JxlCustomXy Red { get; } = new();

    /// <summary>Gets the custom green primary.</summary>
    public JxlCustomXy Green { get; } = new();

    /// <summary>Gets the custom blue primary.</summary>
    public JxlCustomXy Blue { get; } = new();

    /// <summary>Gets the transfer function.</summary>
    public JxlCustomTransferFunction Tf { get; } = new();

    /// <summary>Gets or sets the rendering intent.</summary>
    public JxlRenderingIntent RenderingIntent { get; set; } = JxlRenderingIntent.Relative;

    /// <summary>Gets a value indicating whether the image is greyscale.</summary>
    public bool IsGray => this.ColorSpace == JxlColorSpace.Gray;

    /// <summary>Gets a value indicating whether the encoding has primaries, which grey and XYB do not.</summary>
    public bool HasPrimaries => this.ColorSpace != JxlColorSpace.Gray && this.ColorSpace != JxlColorSpace.Xyb;

    /// <summary>Gets the white point as CIE xy (GetWhitePoint in lib/jxl/cms/color_encoding_cms.h).</summary>
    public (double X, double Y) WhitePointXy => this.WhitePoint switch
    {
        JxlWhitePoint.Custom => this.White.Value,
        JxlWhitePoint.D65 => (0.3127, 0.3290),

        // From https://ieeexplore.ieee.org/document/7290729 C.2 page 11
        JxlWhitePoint.Dci => (0.314, 0.351),
        _ => (1.0 / 3, 1.0 / 3), // E
    };

    /// <summary>Gets the red, green and blue primaries as CIE xy (GetPrimaries). Only valid if <see cref="HasPrimaries"/>.</summary>
    public ((double X, double Y) R, (double X, double Y) G, (double X, double Y) B) PrimariesXy => this.Primaries switch
    {
        JxlPrimaries.Custom => (this.Red.Value, this.Green.Value, this.Blue.Value),
        JxlPrimaries.Srgb => ((0.639998686, 0.330010138), (0.300003784, 0.600003357), (0.150002046, 0.059997204)),
        JxlPrimaries.Bt2100 => ((0.708, 0.292), (0.170, 0.797), (0.131, 0.046)),
        _ => ((0.680, 0.320), (0.265, 0.690), (0.150, 0.060)), // P3
    };

    /// <summary>
    /// Returns the matrix that takes linear samples with these primaries to CIE XYZ, not adapted to D50
    /// (libjxl's <c>PrimariesToXYZ</c> of <see cref="PrimariesXy"/> and <see cref="WhitePointXy"/>), and its Y
    /// row: the luminances of the red, green and blue primaries, which libjxl's tone mapping and HLG OOTF
    /// weight the samples with (<c>luminances</c>, <c>primaries_luminances</c>). Only valid if
    /// <see cref="HasPrimaries"/>.
    /// </summary>
    /// <returns>The matrix, and a new array with its Y row.</returns>
    /// <exception cref="ArgumentException">The white point or the primaries cannot describe a colour space.</exception>
    public (float[,] ToXyz, float[] Luminances) PrimariesToXyz()
    {
        ((double rx, double ry), (double gx, double gy), (double bx, double by)) = this.PrimariesXy;
        (double wx, double wy) = this.WhitePointXy;
        float[,] toXyz = IccProfileBuilder.PrimariesToXyz((float)rx, (float)ry, (float)gx, (float)gy, (float)bx, (float)by, (float)wx, (float)wy);
        return (toXyz, new[] { toXyz[1, 0], toXyz[1, 1], toXyz[1, 2] });
    }

    /// <summary>Copies the fields of <paramref name="other"/> into this instance.</summary>
    /// <param name="other">The encoding to copy.</param>
    public void CopyFrom(JxlColorEncodingHeader other)
    {
        this.WantIcc = other.WantIcc;
        this.ColorSpace = other.ColorSpace;
        this.WhitePoint = other.WhitePoint;
        this.Primaries = other.Primaries;
        this.RenderingIntent = other.RenderingIntent;
        this.White.CopyFrom(other.White);
        this.Red.CopyFrom(other.Red);
        this.Green.CopyFrom(other.Green);
        this.Blue.CopyFrom(other.Blue);
        this.Tf.CopyFrom(other.Tf);
    }

    public override void VisitFields(JxlVisitor visitor)
    {
        if (visitor.AllDefault(this))
        {
            visitor.SetDefault(this);
            return;
        }

        this.WantIcc = visitor.Bool(false, this.WantIcc);

        // Always send even if want_icc_ because this affects decoding.
        this.ColorSpace = visitor.Enum(JxlColorSpace.Rgb, this.ColorSpace);

        if (visitor.Conditional(!this.WantIcc))
        {
            if (visitor.Conditional(!this.ImplicitWhitePoint()))
            {
                this.WhitePoint = visitor.Enum(JxlWhitePoint.D65, this.WhitePoint);
                if (visitor.Conditional(this.WhitePoint == JxlWhitePoint.Custom))
                {
                    visitor.VisitNested(this.White);
                }
            }

            if (visitor.Conditional(this.HasPrimaries))
            {
                this.Primaries = visitor.Enum(JxlPrimaries.Srgb, this.Primaries);
                if (visitor.Conditional(this.Primaries == JxlPrimaries.Custom))
                {
                    visitor.VisitNested(this.Red);
                    visitor.VisitNested(this.Green);
                    visitor.VisitNested(this.Blue);
                }
            }

            this.Tf.NonserializedColorSpace = this.ColorSpace;
            visitor.VisitNested(this.Tf);

            this.RenderingIntent = visitor.Enum(JxlRenderingIntent.Relative, this.RenderingIntent);

            // We didn't have ICC, so all fields should be known.
            if (this.ColorSpace == JxlColorSpace.Unknown || this.Tf.IsUnknown)
            {
                JxlThrowHelper.ThrowInvalidImageContentException($"No ICC but cs {this.ColorSpace} and tf {(this.Tf.HaveGamma ? "(gamma)" : this.Tf.TransferFunction.ToString())}");
            }
        }
    }

    private bool ImplicitWhitePoint()
    {
        if (this.ColorSpace == JxlColorSpace.Xyb)
        {
            this.WhitePoint = JxlWhitePoint.D65;
            return true;
        }

        return false;
    }
}
