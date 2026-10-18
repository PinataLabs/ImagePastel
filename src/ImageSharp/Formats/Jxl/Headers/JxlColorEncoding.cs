// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/color_encoding_internal.h and lib/jxl/color_encoding_internal.cc (Customxy,
// CustomTransferFunction, ColorEncoding fields), with the storage defaults and gamma limits of
// lib/jxl/cms/color_encoding_cms.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.Headers;

/// <summary>
/// The colour encoding of the image: either an enumerated encoding, or a flag that an ICC profile follows.
/// </summary>
/// <remarks>
/// Only the fields are ported. libjxl also builds an ICC profile from the enumerated fields
/// (<c>CreateICC</c>); that comes with the colour slice, so the "Empty ICC" check that depends on it is
/// left out here.
/// </remarks>
internal sealed class JxlColorEncoding : JxlFields
{
    public JxlColorEncoding() => JxlBundle.Init(this);

    /// <summary>Gets a value indicating whether an ICC profile follows the headers instead of the enumerated fields.</summary>
    public bool WantIcc { get; private set; }

    /// <summary>Gets the colour space.</summary>
    public JxlColorSpace ColorSpace { get; private set; } = JxlColorSpace.Rgb;

    /// <summary>Gets the white point.</summary>
    public JxlWhitePoint WhitePoint { get; private set; } = JxlWhitePoint.D65;

    /// <summary>Gets the custom white point, used if <see cref="WhitePoint"/> is <see cref="JxlWhitePoint.Custom"/>.</summary>
    public JxlCustomXy White { get; } = new();

    /// <summary>Gets the primaries. Only valid if <see cref="HasPrimaries"/>.</summary>
    public JxlPrimaries Primaries { get; private set; } = JxlPrimaries.Srgb;

    /// <summary>Gets the custom red primary.</summary>
    public JxlCustomXy Red { get; } = new();

    /// <summary>Gets the custom green primary.</summary>
    public JxlCustomXy Green { get; } = new();

    /// <summary>Gets the custom blue primary.</summary>
    public JxlCustomXy Blue { get; } = new();

    /// <summary>Gets the transfer function.</summary>
    public JxlCustomTransferFunction Tf { get; } = new();

    /// <summary>Gets the rendering intent.</summary>
    public JxlRenderingIntent RenderingIntent { get; private set; } = JxlRenderingIntent.Relative;

    /// <summary>Gets a value indicating whether the image is greyscale.</summary>
    public bool IsGray => this.ColorSpace == JxlColorSpace.Gray;

    /// <summary>Gets a value indicating whether the encoding has primaries, which grey and XYB do not.</summary>
    public bool HasPrimaries => this.ColorSpace != JxlColorSpace.Gray && this.ColorSpace != JxlColorSpace.Xyb;

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
