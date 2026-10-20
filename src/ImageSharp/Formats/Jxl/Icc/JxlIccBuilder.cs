// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// MaybeCreateProfileImpl (the parts specific to a JPEG XL colour encoding), MaybeCreateICCCICPTag,
// ColorEncodingDescriptionImpl, CanToneMap and CreateTableCurve in lib/jxl/cms/jxl_cms_internal.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Globalization;
using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.ColorManagement.Icc;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.Formats.Jxl.Icc;

/// <summary>
/// Builds the ICC profile that describes an enumerated colour encoding, with the tags libjxl's
/// <c>MaybeCreateProfile</c> writes, through <see cref="IccProfileBuilder"/>, the builder behind the public
/// <see cref="IccProfileFactory"/>.
/// </summary>
/// <remarks>
/// A PQ or HLG encoding gets libjxl's 64-entry table curve when <c>CanToneMap</c> is false (grey, or custom
/// primaries or white point). Where the built profile differs from libjxl's: an encoding that
/// <c>CanToneMap</c> accepts gets libjxl's 3D-LUT profile, which is not built yet (D11f); it is validated
/// but gets no profile.
/// Unlike the public factory, it keeps libjxl's header signatures (<c>jxl </c>) and adapts to libjxl's
/// CIE D50 (<see cref="IccProfileBuilder.LibjxlD50"/>), so its profiles match <c>djxl</c>'s.
/// </remarks>
internal static class JxlIccBuilder
{
    /// <summary>
    /// Builds the profile for <paramref name="c"/>, checking it as libjxl's <c>CreateICC</c> does when it
    /// reads the header. Throws <see cref="InvalidImageContentException"/> for an encoding libjxl cannot
    /// build a profile for.
    /// </summary>
    /// <param name="c">The colour encoding, which does not signal an ICC profile.</param>
    /// <returns>
    /// The profile, or null where none is built: the XYB colour space, and PQ and HLG RGB encodings that a
    /// cicp tag can name.
    /// </returns>
    public static IccProfile Create(JxlColorEncoding c)
    {
        if (c.ColorSpace == JxlColorSpace.Xyb && c.RenderingIntent != JxlRenderingIntent.Perceptual)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Only perceptual rendering intent implemented for XYB ICC profile.");
        }

        // The builder rejects what libjxl rejects (an invalid white point, degenerate primaries, values an
        // s15Fixed16Number cannot hold) with an ArgumentException; in a decoder that is bad image content.
        try
        {
            return Build(c);
        }
        catch (ArgumentException e)
        {
            throw new InvalidImageContentException(e.Message, e);
        }
    }

    private static IccProfile Build(JxlColorEncoding c)
    {
        IccColorSpaceType dataColorSpace = c.ColorSpace == JxlColorSpace.Gray ? IccColorSpaceType.Gray : IccColorSpaceType.Rgb;
        (double X, double Y)[] primaries = null;
        if (c.ColorSpace == JxlColorSpace.Rgb)
        {
            ((double rx, double ry), (double gx, double gy), (double bx, double by)) = c.PrimariesXy;
            primaries = new[] { (rx, ry), (gx, gy), (bx, by) };
        }

        // A PQ or HLG encoding that libjxl cannot tone map gets a table curve, which the TRC tags share.
        bool pqOrHlg = c.ColorSpace != JxlColorSpace.Xyb && !c.Tf.HaveGamma && c.Tf.TransferFunction is (JxlTransferFunction.Pq or JxlTransferFunction.Hlg);
        if (pqOrHlg && !CanToneMap(c))
        {
            return IccProfileBuilder.Build(
                dataColorSpace,
                c.WhitePointXy,
                primaries,
                CreateTableCurve(c.Tf.TransferFunction == JxlTransferFunction.Hlg),
                IccProfileBuilder.LibjxlD50,
                (IccRenderingIntent)(uint)c.RenderingIntent,
                Description(c),
                "jxl ",
                Cicp(c));
        }

        // XYB gets a LUT-based profile, and the PQ and HLG encodings that libjxl can tone map a 3D-LUT one;
        // neither is built here, but the white point and primaries are still checked.
        IccParametricCurve[] curves = null;
        if (c.ColorSpace != JxlColorSpace.Xyb && !pqOrHlg)
        {
            IccParametricCurve curve = Curve(c.Tf).Curve;
            curves = c.ColorSpace == JxlColorSpace.Gray ? new[] { curve } : new[] { curve, curve, curve };
        }

        return IccProfileBuilder.Build(
            dataColorSpace,
            c.WhitePointXy,
            primaries,
            curves,
            IccProfileBuilder.LibjxlD50,
            (IccRenderingIntent)(uint)c.RenderingIntent,
            Description(c),
            "jxl ",
            Cicp(c));
    }

    // CanToneMap: if the color space cannot be represented by a CICP tag in the ICC profile then the rest
    // of the profile must unambiguously identify it; we have less freedom to do use it for tone mapping.
    // libjxl's default build (JXL_ENABLE_3D_ICC_TONEMAPPING) gives these encodings a 3D-LUT profile.
    private static bool CanToneMap(JxlColorEncoding c)
        => c.ColorSpace == JxlColorSpace.Rgb
        && !c.Tf.HaveGamma
        && c.Tf.TransferFunction is (JxlTransferFunction.Pq or JxlTransferFunction.Hlg)
        && ((c.Primaries == JxlPrimaries.P3 && c.WhitePoint is (JxlWhitePoint.D65 or JxlWhitePoint.Dci))
            || (c.Primaries != JxlPrimaries.Custom && c.WhitePoint == JxlWhitePoint.D65));

    // CreateTableCurve<64, kPQ or kHLG>(tone_map: false). The tone-mapping flag is CanToneMap(c), which is
    // false on this path because libjxl's default build gives the encodings it can tone map the 3D LUT, so
    // the tone-mapping block is not ported.
    private static ushort[] CreateTableCurve(bool hlg)
    {
        const int n = 64;

        // The generated PQ curve will make room for highlights up to this luminance.
        const float pqIntensityTarget = 10000;

        // No point using float - LCMS converts to 16-bit for A2B/MFT.
        ushort[] table = new ushort[n];
        for (int i = 0; i < n; i++)
        {
            float x = (float)i / (n - 1); // 1.0 at index N - 1.
            double dx = x;

            // LCMS requires EOTF (e.g. 2.4 exponent).
            double y = hlg ? JxlTfHlgBase.DisplayFromEncoded(dx) : JxlTfPqBase.DisplayFromEncoded(pqIntensityTarget, dx);

            // Clamp to table range - necessary for HLG.
            y = Math.Clamp(y, 0.0, 1.0);

            // 1.0 corresponds to table value 0xFFFF. roundf takes the product as a float.
            table[i] = (ushort)MathF.Round((float)(y * 65535.0), MidpointRounding.AwayFromZero);
        }

        return table;
    }

    // MaybeCreateICCCICPTag: the H.273 code points of an RGB encoding with enumerated primaries and white
    // point and an enumerated transfer function. The JPEG XL enums use the H.273 values, except that P3
    // primaries are 12 with a D65 white and 11 with the DCI white.
    private static (byte ColorPrimaries, byte TransferCharacteristics)? Cicp(JxlColorEncoding c)
    {
        if (c.ColorSpace != JxlColorSpace.Rgb)
        {
            return null;
        }

        byte primaries;
        if (c.Primaries == JxlPrimaries.P3)
        {
            if (c.WhitePoint == JxlWhitePoint.D65)
            {
                primaries = 12;
            }
            else if (c.WhitePoint == JxlWhitePoint.Dci)
            {
                primaries = 11;
            }
            else
            {
                return null;
            }
        }
        else if (c.Primaries != JxlPrimaries.Custom && c.WhitePoint == JxlWhitePoint.D65)
        {
            primaries = (byte)c.Primaries;
        }
        else
        {
            return null;
        }

        if (c.Tf.HaveGamma || c.Tf.TransferFunction == JxlTransferFunction.Unknown)
        {
            return null;
        }

        return (primaries, (byte)c.Tf.TransferFunction);
    }

    // The parametric curve of a gamma or one of the transfer functions a parametric curve can express.
    private static IccToneCurve Curve(JxlCustomTransferFunction tf)
    {
        if (tf.HaveGamma)
        {
            return IccToneCurve.Gamma((float)(1.0 / (tf.Gamma * (1.0 / JxlCustomTransferFunction.GammaMul))));
        }

        return tf.TransferFunction switch
        {
            JxlTransferFunction.Srgb => IccToneCurve.Srgb,
            JxlTransferFunction.Bt709 => IccToneCurve.Rec709,
            JxlTransferFunction.Linear => IccToneCurve.Linear,
            JxlTransferFunction.Dci => IccToneCurve.Parametric(2.6F, 1, 0, 1, 0),
            _ => throw new InvalidOperationException($"unknown TF {tf.TransferFunction}"),
        };
    }

    // ColorEncodingDescriptionImpl(c, uniquename: false): a short name for the common spaces, otherwise
    // the fields, as in "RGB_D65_SRG_Rel_Lin". libjxl formats the custom values with its own ToString;
    // this uses the shortest round-trip form.
    private static string Description(JxlColorEncoding c)
    {
        // Return short names for the most common color spaces. These names are returned regardless of
        // rendering intent, and also there is some tolerance regarding primaries and transfer function,
        // so different ColorEncodings can return the same short name.
        JxlTransferFunction tf = c.Tf.TransferFunction;
        double gamma = c.Tf.Gamma * (1.0 / JxlCustomTransferFunction.GammaMul);
        if (c.ColorSpace == JxlColorSpace.Rgb)
        {
            ((double rx, double ry), (double gx, double gy), (double bx, double by)) = c.PrimariesXy;
            bool custom = c.Primaries == JxlPrimaries.Custom;
            if (c.WhitePoint == JxlWhitePoint.D65)
            {
                if (!c.Tf.HaveGamma && tf == JxlTransferFunction.Srgb)
                {
                    if (c.Primaries == JxlPrimaries.Srgb)
                    {
                        return "sRGB";
                    }

                    if (c.Primaries == JxlPrimaries.P3)
                    {
                        return "DisplayP3";
                    }
                }

                if (c.Primaries == JxlPrimaries.Bt2100 && !c.Tf.HaveGamma)
                {
                    if (tf == JxlTransferFunction.Pq)
                    {
                        return "Rec2100PQ";
                    }

                    if (tf == JxlTransferFunction.Hlg)
                    {
                        return "Rec2100HLG";
                    }
                }

                if (custom && CloseEnough(rx, 0.6400) && CloseEnough(ry, 0.3300) && CloseEnough(gx, 0.2100) && CloseEnough(gy, 0.7100)
                    && CloseEnough(bx, 0.1500) && CloseEnough(by, 0.0600) && c.Tf.HaveGamma && CloseEnough(gamma, 256.0 / 563.0))
                {
                    return "Adobe98";
                }
            }

            // Apple's ROMM, Adobe's ProPhoto and the CSS prophoto-rgb differ slightly; this matches the
            // CSS definition. Adobe's ProPhoto profile uses a simple gamma curve (g0.555315); others
            // have a small linear segment near black.
            (double wx, double wy) = c.WhitePointXy;
            if (c.WhitePoint == JxlWhitePoint.Custom && CloseEnough(wx, 0.345669) && CloseEnough(wy, 0.358496) && custom
                && CloseEnough(rx, 0.734699) && CloseEnough(ry, 0.265301) && CloseEnough(gx, 0.159597) && CloseEnough(gy, 0.840403)
                && CloseEnough(bx, 0.036598) && CloseEnough(by, 0.000105) && c.Tf.HaveGamma && CloseEnough(gamma, 1.0 / 1.8))
            {
                return "ProPhoto";
            }
        }

        static string Number(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        string d = c.ColorSpace switch
        {
            JxlColorSpace.Rgb => "RGB",
            JxlColorSpace.Gray => "Gra",
            JxlColorSpace.Xyb => "XYB",
            _ => "CS?",
        };

        bool explicitWpTf = c.ColorSpace != JxlColorSpace.Xyb;
        if (explicitWpTf)
        {
            (double wx, double wy) = c.WhitePointXy;
            d += '_' + c.WhitePoint switch
            {
                JxlWhitePoint.Custom => Number(wx) + ';' + Number(wy),
                JxlWhitePoint.D65 => "D65",
                JxlWhitePoint.E => "EER",
                _ => "DCI",
            };
        }

        if (c.HasPrimaries)
        {
            ((double rx, double ry), (double gx, double gy), (double bx, double by)) = c.PrimariesXy;
            d += '_' + c.Primaries switch
            {
                JxlPrimaries.Custom => string.Join(";", Number(rx), Number(ry), Number(gx), Number(gy), Number(bx), Number(by)),
                JxlPrimaries.Srgb => "SRG",
                JxlPrimaries.Bt2100 => "202",
                _ => "DCI",
            };
        }

        d += '_' + c.RenderingIntent switch
        {
            JxlRenderingIntent.Perceptual => "Per",
            JxlRenderingIntent.Relative => "Rel",
            JxlRenderingIntent.Saturation => "Sat",
            _ => "Abs",
        };

        if (explicitWpTf)
        {
            d += '_' + (c.Tf.HaveGamma
                ? "g" + Number(gamma)
                : tf switch
                {
                    JxlTransferFunction.Srgb => "SRG",
                    JxlTransferFunction.Linear => "Lin",
                    JxlTransferFunction.Bt709 => "709",
                    JxlTransferFunction.Pq => "PeQ",
                    JxlTransferFunction.Hlg => "HLG",
                    JxlTransferFunction.Dci => "DCI",
                    _ => "TF?",
                });
        }

        return d;
    }

    private static bool CloseEnough(double a, double b) => Math.Abs(a - b) < 3e-5;
}
