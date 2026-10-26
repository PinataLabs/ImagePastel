// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// MaybeCreateProfileImpl (the parts specific to a JPEG XL colour encoding), MaybeCreateICCCICPTag,
// ColorEncodingDescriptionImpl, CanToneMap, CreateTableCurve, ToneMapPixel and the grid of
// CreateICCLutAtoBTagForHDR in lib/jxl/cms/jxl_cms_internal.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Globalization;
using PinataLabs.ImagePastel.ColorManagement;
using PinataLabs.ImagePastel.ColorManagement.Icc;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;

namespace PinataLabs.ImagePastel.Formats.Jxl.Icc;

/// <summary>
/// Builds the ICC profile that describes an enumerated colour encoding, with the tags libjxl's
/// <c>MaybeCreateProfile</c> writes, through <see cref="IccProfileBuilder"/>, the builder behind the public
/// <see cref="IccProfileFactory"/>.
/// </summary>
/// <remarks>
/// A PQ or HLG RGB encoding that <c>CanToneMap</c> accepts gets libjxl's 3D-LUT profile: a Lab connection
/// space and a 9 × 9 × 9 <c>A2B0</c> grid that tone-maps to PCS Lab (<see cref="ToneMapPixel"/>). Any other
/// PQ or HLG encoding (grey, or custom primaries or white point) gets libjxl's 64-entry table curve.
/// Unlike the public factory, it keeps libjxl's header signatures (<c>jxl </c>) and adapts to libjxl's
/// CIE D50 (<see cref="IccProfileBuilder.LibjxlD50"/>), so its profiles match <c>djxl</c>'s.
/// </remarks>
internal static class JxlIccBuilder
{
    // k3DLutDim: the grid points per input of the tone-mapping 3D LUT.
    private const int LutGridPoints = 9;

    /// <summary>
    /// Builds the profile for <paramref name="c"/>, checking it as libjxl's <c>CreateICC</c> does when it
    /// reads the header. Throws <see cref="InvalidImageContentException"/> for an encoding libjxl cannot
    /// build a profile for.
    /// </summary>
    /// <param name="c">The colour encoding, which does not signal an ICC profile.</param>
    /// <returns>
    /// The profile, or null for the XYB colour space, for which none is built.
    /// </returns>
    public static IccProfile Create(JxlColorEncodingHeader c)
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

    /// <summary>
    /// Tone-maps one encoded PQ or HLG sample of <paramref name="c"/> and gives its 8-bit PCS Lab value: a
    /// node of the 3D LUT (<c>A2B0</c>) that libjxl writes in the profile of an encoding <c>CanToneMap</c>
    /// accepts. PQ is tone-mapped from 0-10,000 nits to 0-250 nits (Rec. 2408), HLG with the OOTF from 300
    /// nits to 80; both are then gamut-mapped, adapted to D50 and converted to CIELAB.
    /// </summary>
    /// <param name="c">An RGB colour encoding with the PQ or HLG transfer function.</param>
    /// <param name="input">The encoded red, green and blue samples, from 0 to 1.</param>
    /// <param name="pcsLabOut">Receives L*, a* and b* as PCS Lab bytes: 255 L* / 100, and a* and b* plus 128.</param>
    /// <exception cref="ArgumentException">The white point or the primaries cannot describe a colour space.</exception>
    internal static void ToneMapPixel(JxlColorEncodingHeader c, ReadOnlySpan<float> input, Span<byte> pcsLabOut)
    {
        (float[,] primariesXyz, float[] luminances) = c.PrimariesToXyz();
        Span<float> linear = stackalloc float[3];
        bool pq = c.Tf.TransferFunction == JxlTransferFunction.Pq;
        for (int i = 0; i < 3; i++)
        {
            linear[i] = pq
                ? (float)JxlTfPqBase.DisplayFromEncoded(10000.0f, input[i])
                : (float)JxlTfHlgBase.DisplayFromEncoded(input[i]);
        }

        if (pq)
        {
            new JxlRec2408ToneMapperBase((0.0f, 10000.0f), (0.0f, 250.0f), luminances).ToneMap(linear);
        }
        else
        {
            new JxlHlgOotfBase(sourceLuminance: 300, targetLuminance: 80, luminances).Apply(linear);
        }

        JxlGamutMap.GamutMapScalar(linear, luminances, preserveSaturation: 0.3f);

        // The Bradford adaptation is to libjxl's D50 white (IccProfileBuilder.LibjxlD50); the Lab conversion
        // below uses a third D50 white, neither that one nor the wtpt tag's.
        (double wx, double wy) = c.WhitePointXy;
        float[,] chad = IccProfileBuilder.AdaptToXyzD50((float)wx, (float)wy, IccProfileBuilder.LibjxlD50);
        float[,] toXyzD50 = IccProfileBuilder.Mul3x3Matrix(chad, primariesXyz);

        Span<float> xyz = stackalloc float[3];
        for (int xyzC = 0; xyzC < 3; xyzC++)
        {
            for (int rgbC = 0; rgbC < 3; rgbC++)
            {
                xyz[xyzC] += linear[rgbC] * toXyzD50[xyzC, rgbC];
            }
        }

        const float xn = 0.964212f;
        const float yn = 1;
        const float zn = 0.825188f;

        float fX = LabF(xyz[0] / xn);
        float fY = LabF(xyz[1] / yn);
        float fZ = LabF(xyz[2] / zn);

        pcsLabOut[0] = (byte)MathF.Round(255.0f * JxlScalarMath.Clamp1((1.16f * fY) - .16f, 0.0f, 1.0f), MidpointRounding.AwayFromZero);
        pcsLabOut[1] = (byte)MathF.Round(128.0f + JxlScalarMath.Clamp1(500 * (fX - fY), -128.0f, 127.0f), MidpointRounding.AwayFromZero);
        pcsLabOut[2] = (byte)MathF.Round(128.0f + JxlScalarMath.Clamp1(200 * (fY - fZ), -128.0f, 127.0f), MidpointRounding.AwayFromZero);

        static float LabF(float x)
        {
            // static constexpr float kDelta = 6. / 29; the other constants are float expressions of it.
            const float delta = (float)(6.0 / 29);
            return x <= delta * delta * delta
                ? (x * (1 / (3 * delta * delta))) + (4.0f / 29)
                : MathF.Cbrt(x);
        }
    }

    private static IccProfile Build(JxlColorEncodingHeader c)
    {
        IccColorSpaceType dataColorSpace = c.ColorSpace == JxlColorSpace.Gray ? IccColorSpaceType.Gray : IccColorSpaceType.Rgb;
        (double X, double Y)[] primaries = null;
        if (c.ColorSpace == JxlColorSpace.Rgb)
        {
            ((double rx, double ry), (double gx, double gy), (double bx, double by)) = c.PrimariesXy;
            primaries = new[] { (rx, ry), (gx, gy), (bx, by) };
        }

        // libjxl's header signatures, and its D50 white.
        IccProfileBuilderHeader header = new(
            c.WhitePointXy,
            primaries,
            IccProfileBuilder.LibjxlD50,
            (IccRenderingIntent)(uint)c.RenderingIntent,
            Description(c),
            "jxl ",
            Cicp(c));

        // A PQ or HLG encoding that libjxl can tone map gets a 3D LUT to PCS Lab (kEnable3DToneMapping, which
        // libjxl's default build sets).
        if (CanToneMap(c))
        {
            return IccProfileBuilder.BuildLabLut(header, LutGridPoints, CreateLutAtoBGridForHdr(c));
        }

        // A PQ or HLG encoding that libjxl cannot tone map gets a table curve, which the TRC tags share.
        bool pqOrHlg = c.ColorSpace != JxlColorSpace.Xyb && (c.Tf.IsPq || c.Tf.IsHlg);
        if (pqOrHlg)
        {
            return IccProfileBuilder.Build(dataColorSpace, header, CreateTableCurve(c.Tf.IsHlg));
        }

        // XYB gets a LUT-based profile, which is not built here, but the white point and primaries are
        // still checked.
        IccParametricCurve[] curves = null;
        if (c.ColorSpace != JxlColorSpace.Xyb)
        {
            IccParametricCurve curve = Curve(c.Tf).Curve;
            curves = c.ColorSpace == JxlColorSpace.Gray ? new[] { curve } : new[] { curve, curve, curve };
        }

        return IccProfileBuilder.Build(dataColorSpace, header, curves);
    }

    // CreateICCLutAtoBTagForHDR: the grid of the mft1 tag, k3DLutDim nodes per input, each the PCS Lab bytes
    // ToneMapPixel gives for the node's encoded samples. ix (red) varies slowest and ib (blue) fastest.
    private static byte[] CreateLutAtoBGridForHdr(JxlColorEncodingHeader c)
    {
        const int k3DLutDim = LutGridPoints;
        byte[] grid = new byte[k3DLutDim * k3DLutDim * k3DLutDim * 3];
        Span<float> f = stackalloc float[3];
        int offset = 0;
        for (int ix = 0; ix < k3DLutDim; ++ix)
        {
            for (int iy = 0; iy < k3DLutDim; ++iy)
            {
                for (int ib = 0; ib < k3DLutDim; ++ib)
                {
                    f[0] = ix * (1.0f / (k3DLutDim - 1));
                    f[1] = iy * (1.0f / (k3DLutDim - 1));
                    f[2] = ib * (1.0f / (k3DLutDim - 1));
                    ToneMapPixel(c, f, grid.AsSpan(offset, 3));
                    offset += 3;
                }
            }
        }

        return grid;
    }

    // CanToneMap: if the color space cannot be represented by a CICP tag in the ICC profile then the rest
    // of the profile must unambiguously identify it; we have less freedom to do use it for tone mapping.
    // libjxl's default build (JXL_ENABLE_3D_ICC_TONEMAPPING) gives these encodings a 3D-LUT profile.
    private static bool CanToneMap(JxlColorEncodingHeader c)
        => c.ColorSpace == JxlColorSpace.Rgb
        && (c.Tf.IsPq || c.Tf.IsHlg)
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
    private static (byte ColorPrimaries, byte TransferCharacteristics)? Cicp(JxlColorEncodingHeader c)
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

    /// <summary>
    /// Returns the tone curve that a profile built for <paramref name="tf"/> has in its TRC tags: a
    /// parametric curve, or for PQ and HLG the 64-entry table curve. A PQ or HLG RGB encoding that
    /// <c>CanToneMap</c> accepts gets a 3D LUT instead, but its curve is still this one.
    /// </summary>
    /// <param name="tf">A gamma or a known transfer function.</param>
    /// <returns>The curve, as a <c>para</c> or <c>curv</c> tag entry.</returns>
    internal static IccTagDataEntry CreateTrc(JxlCustomTransferFunction tf)
    {
        if (tf.IsPq || tf.IsHlg)
        {
            // As IccProfileBuilder stores the table: 65535 is 1.
            ushort[] table = CreateTableCurve(tf.IsHlg);
            float[] values = new float[table.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = table[i] / 65535F;
            }

            return new IccCurveTagDataEntry(values);
        }

        return new IccParametricCurveTagDataEntry(Curve(tf).Curve);
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
    private static string Description(JxlColorEncodingHeader c)
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
