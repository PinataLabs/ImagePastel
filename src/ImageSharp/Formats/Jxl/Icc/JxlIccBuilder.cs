// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// MaybeCreateProfileImpl and its helpers (PrimariesToXYZ, AdaptToXYZD50, CIEXYZFromWhiteCIExy,
// WriteICCS15Fixed16, CreateICCHeader, ColorEncodingDescriptionImpl) in lib/jxl/cms/jxl_cms_internal.h,
// and Mul3x3Matrix, Mul3x3Vector and Inv3x3Matrix in lib/jxl/base/matrix_ops.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.Formats.Jxl.Icc;

/// <summary>
/// Builds the ICC profile that describes an enumerated colour encoding, with the tags libjxl's
/// <c>MaybeCreateProfile</c> writes, through ImageSharp's ICC object model (issue #105) rather than
/// libjxl's byte writer.
/// </summary>
/// <remarks>
/// Where the built profile differs from libjxl's:
/// <list type="bullet">
/// <item>It has no <c>cicp</c> tag, which the ICC object model has no type for. The tag is optional and
/// only repeats the colour encoding.</item>
/// <item>Its profile ID is zero (not computed), which ICC allows, where libjxl writes an MD5.</item>
/// <item>PQ and HLG need curve tables (<c>CreateTableCurve</c>), which come with slice D11; until then
/// those encodings are validated but get no profile.</item>
/// </list>
/// </remarks>
internal static class JxlIccBuilder
{
    // Chromatic adaptation matrices.
    private static readonly float[,] Bradford =
    {
        { 0.8951f, 0.2664f, -0.1614f },
        { -0.7502f, 1.7135f, 0.0367f },
        { 0.0389f, -0.0685f, 1.0296f },
    };

    private static readonly float[,] BradfordInv =
    {
        { 0.9869929f, -0.1470543f, 0.1599627f },
        { 0.4323053f, 0.5183603f, 0.0492912f },
        { -0.0085287f, 0.0400428f, 0.9684867f },
    };

    // libjxl writes its text tags in en-US. Under invariant globalization that culture can be missing,
    // and the text is then written with no language.
    private static readonly CultureInfo EnUs = GetEnUs();

    /// <summary>
    /// Builds the profile for <paramref name="c"/>, checking it as libjxl's <c>CreateICC</c> does when it
    /// reads the header. Throws <see cref="InvalidImageContentException"/> for an encoding libjxl cannot
    /// build a profile for.
    /// </summary>
    /// <param name="c">The colour encoding, which does not signal an ICC profile.</param>
    /// <returns>The profile, or null where none is built: the XYB colour space, PQ and HLG.</returns>
    public static IccProfile Create(JxlColorEncoding c)
    {
        if (c.ColorSpace == JxlColorSpace.Xyb && c.RenderingIntent != JxlRenderingIntent.Perceptual)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Only perceptual rendering intent implemented for XYB ICC profile.");
        }

        (double wx, double wy) = c.WhitePointXy;
        List<IccTagDataEntry> tags = new()
        {
            new IccMultiLocalizedUnicodeTagDataEntry(new[] { new IccLocalizedString(EnUs, Description(c)) }, IccProfileTag.ProfileDescription),
            new IccMultiLocalizedUnicodeTagDataEntry(new[] { new IccLocalizedString(EnUs, "CC0") }, IccProfileTag.Copyright),
        };

        // TODO(eustas): isn't it the other way round: gray image has d50 WhitePoint?
        if (c.ColorSpace == JxlColorSpace.Gray)
        {
            tags.Add(Xyz(CieXyzFromWhiteCieXy(wx, wy), IccProfileTag.MediaWhitePoint));
        }
        else
        {
            tags.Add(Xyz(new[] { 0.964203, 1.0, 0.824905 }, IccProfileTag.MediaWhitePoint));
        }

        if (c.ColorSpace != JxlColorSpace.Gray)
        {
            // Chromatic adaptation matrix (CreateICCChadMatrix).
            if (wy == 0)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid WhitePoint"); // WhitePoint can not be pitch-black.
            }

            float[,] chad = AdaptToXyzD50((float)wx, (float)wy);
            float[] values = new float[9];
            for (int j = 0; j < 3; j++)
            {
                for (int i = 0; i < 3; i++)
                {
                    values[(j * 3) + i] = S15Fixed16(chad[j, i]);
                }
            }

            tags.Add(new IccFix16ArrayTagDataEntry(values, IccProfileTag.ChromaticAdaptation));
        }

        if (c.ColorSpace == JxlColorSpace.Rgb)
        {
            ((double rx, double ry), (double gx, double gy), (double bx, double by)) = c.PrimariesXy;
            float[,] m = PrimariesToXyzD50((float)rx, (float)ry, (float)gx, (float)gy, (float)bx, (float)by, (float)wx, (float)wy);
            tags.Add(Xyz(new double[] { m[0, 0], m[1, 0], m[2, 0] }, IccProfileTag.RedMatrixColumn));
            tags.Add(Xyz(new double[] { m[0, 1], m[1, 1], m[2, 1] }, IccProfileTag.GreenMatrixColumn));
            tags.Add(Xyz(new double[] { m[0, 2], m[1, 2], m[2, 2] }, IccProfileTag.BlueMatrixColumn));
        }

        // XYB gets a LUT-based profile, and PQ and HLG table curves; none of them is built here.
        if (c.ColorSpace == JxlColorSpace.Xyb || (!c.Tf.HaveGamma && c.Tf.TransferFunction is JxlTransferFunction.Pq or JxlTransferFunction.Hlg))
        {
            return null;
        }

        IccParametricCurve curve = Curve(c.Tf);
        if (c.ColorSpace == JxlColorSpace.Gray)
        {
            tags.Add(new IccParametricCurveTagDataEntry(curve, IccProfileTag.GrayTrc));
        }
        else
        {
            tags.Add(new IccParametricCurveTagDataEntry(curve, IccProfileTag.RedTrc));
            tags.Add(new IccParametricCurveTagDataEntry(curve, IccProfileTag.GreenTrc));
            tags.Add(new IccParametricCurveTagDataEntry(curve, IccProfileTag.BlueTrc));
        }

        return new IccProfile(Header(c), tags.ToArray());
    }

    private static CultureInfo GetEnUs()
    {
        try
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    // CreateICCHeader. The size is filled in when the profile is written.
    private static IccProfileHeader Header(JxlColorEncoding c) => new()
    {
        CmmType = "jxl ",
        Version = new IccVersion(4, 4, 0),
        Class = IccProfileClass.DisplayDevice,
        DataColorSpace = c.ColorSpace == JxlColorSpace.Gray ? IccColorSpaceType.Gray : IccColorSpaceType.Rgb,
        ProfileConnectionSpace = IccColorSpaceType.CieXyz,

        // TODO(lode): encode actual date and time, this is a placeholder
        CreationDate = new DateTime(2019, 12, 1, 0, 0, 0, DateTimeKind.Utc),
        FileSignature = "acsp",
        PrimaryPlatformSignature = IccPrimaryPlatformType.AppleComputerInc,
        RenderingIntent = (IccRenderingIntent)(uint)c.RenderingIntent,

        // Mandatory D50 white point of profile connection space
        PcsIlluminant = new Vector3(0x0000f6d6 / 65536f, 1, 0x0000d32d / 65536f),
        CreatorSignature = "jxl ",
    };

    // The parametric curve of a gamma or one of the transfer functions a parametric curve can express.
    private static IccParametricCurve Curve(JxlCustomTransferFunction tf)
    {
        if (tf.HaveGamma)
        {
            float gamma = (float)(1.0 / (tf.Gamma * (1.0 / JxlCustomTransferFunction.GammaMul)));
            return new IccParametricCurve(S15Fixed16(gamma));
        }

        return tf.TransferFunction switch
        {
            JxlTransferFunction.Srgb => SrgbCurve(2.4, 1.0 / 1.055, 0.055 / 1.055, 1.0 / 12.92, 0.04045),
            JxlTransferFunction.Bt709 => SrgbCurve(1.0 / 0.45, 1.0 / 1.099, 0.099 / 1.099, 1.0 / 4.5, 0.081),
            JxlTransferFunction.Linear => SrgbCurve(1.0, 1.0, 0.0, 1.0, 0.0),
            JxlTransferFunction.Dci => SrgbCurve(2.6, 1.0, 0.0, 1.0, 0.0),
            _ => throw new InvalidOperationException($"unknown TF {tf.TransferFunction}"),
        };
    }

    // A type 3 curve: for X >= d, Y = (a * X + b)^g; for X < d, Y = c * X.
    private static IccParametricCurve SrgbCurve(double g, double a, double b, double c, double d)
        => new(S15Fixed16((float)g), S15Fixed16((float)a), S15Fixed16((float)b), S15Fixed16((float)c), S15Fixed16((float)d));

    private static IccXyzTagDataEntry Xyz(double[] xyz, IccProfileTag tag)
        => new(new[] { new Vector3(S15Fixed16((float)xyz[0]), S15Fixed16((float)xyz[1]), S15Fixed16((float)xyz[2])) }, tag);

    // WriteICCS15Fixed16: checks the value fits, and rounds it as libjxl does, so the profile writer's
    // own rounding keeps it.
    private static float S15Fixed16(float value)
    {
        // "nextafterf" for 32768.0f towards zero are:
        // 32767.998046875, 32767.99609375, 32767.994140625
        // Even the first value works well,...
        bool ok = value is >= -32767.995f and <= 32767.995f;
        if (!ok)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("ICC value is out of range / NaN");
        }

        return MathF.Round(value * 65536.0f, MidpointRounding.AwayFromZero) / 65536.0f;
    }

    private static double[] CieXyzFromWhiteCieXy(double wx, double wy)
    {
        // Target Y = 1.
        if (Math.Abs(wy) < 1e-12)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Y value is too small");
        }

        float factor = (float)(1 / wy);
        return new[] { wx * factor, 1, (1 - wx - wy) * factor };
    }

    private static float[,] PrimariesToXyz(float rx, float ry, float gx, float gy, float bx, float by, float wx, float wy)
    {
        CheckWhitePoint(wx, wy);

        // TODO(lode): also require rx, ry, gx, gy, bx, to be in range 0-1? ICC profiles in theory forbid
        // negative XYZ values, but in practice the ACES P0 color space uses a negative y for the blue
        // primary.
        float[,] primaries =
        {
            { rx, gx, bx },
            { ry, gy, by },
            { 1.0f - rx - ry, 1.0f - gx - gy, 1.0f - bx - by },
        };
        float[,] primariesInv = Inv3x3Matrix(primaries);

        float[] w = { wx / wy, 1.0f, (1.0f - wx - wy) / wy };

        // 1 / tiny float can still overflow
        if (!float.IsFinite(w[0]) || !float.IsFinite(w[2]))
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid white point");
        }

        float[] xyz = Mul3x3Vector(primariesInv, w);
        float[,] a =
        {
            { xyz[0], 0, 0 },
            { 0, xyz[1], 0 },
            { 0, 0, xyz[2] },
        };

        return Mul3x3Matrix(primaries, a);
    }

    // Adapts white point x, y to D50
    private static float[,] AdaptToXyzD50(float wx, float wy)
    {
        // Out of range values can cause division through zero further down with the bradford
        // adaptation too.
        CheckWhitePoint(wx, wy);
        float[] w = { wx / wy, 1.0f, (1.0f - wx - wy) / wy };

        // 1 / tiny float can still overflow
        if (!float.IsFinite(w[0]) || !float.IsFinite(w[2]))
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid white point");
        }

        float[] w50 = { 0.96422f, 1.0f, 0.82521f };
        float[] lms = Mul3x3Vector(Bradford, w);
        float[] lms50 = Mul3x3Vector(Bradford, w50);
        if (lms[0] == 0 || lms[1] == 0 || lms[2] == 0)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid white point");
        }

        float[,] a =
        {
            { lms50[0] / lms[0], 0, 0 },
            { 0, lms50[1] / lms[1], 0 },
            { 0, 0, lms50[2] / lms[2] },
        };
        if (!float.IsFinite(a[0, 0]) || !float.IsFinite(a[1, 1]) || !float.IsFinite(a[2, 2]))
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid white point");
        }

        float[,] b = Mul3x3Matrix(a, Bradford);
        return Mul3x3Matrix(BradfordInv, b);
    }

    private static float[,] PrimariesToXyzD50(float rx, float ry, float gx, float gy, float bx, float by, float wx, float wy)
    {
        float[,] toXyz = PrimariesToXyz(rx, ry, gx, gy, bx, by, wx, wy);
        float[,] d50 = AdaptToXyzD50(wx, wy);
        return Mul3x3Matrix(d50, toXyz);
    }

    private static void CheckWhitePoint(float wx, float wy)
    {
        bool ok = wx >= 0 && wx <= 1 && wy > 0 && wy <= 1;
        if (!ok)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Invalid white point");
        }
    }

    // Computes C = A * B, where A, B, C are 3x3 matrices.
    private static float[,] Mul3x3Matrix(float[,] a, float[,] b)
    {
        float[,] c = new float[3, 3];
        for (int x = 0; x < 3; x++)
        {
            double t0 = b[0, x];
            double t1 = b[1, x];
            double t2 = b[2, x];
            for (int y = 0; y < 3; y++)
            {
                c[y, x] = (float)((a[y, 0] * t0) + (a[y, 1] * t1) + (a[y, 2] * t2));
            }
        }

        return c;
    }

    // Computes C = A * B, where A is 3x3 matrix and B is vector.
    private static float[] Mul3x3Vector(float[,] a, float[] b)
    {
        float[] c = new float[3];
        for (int y = 0; y < 3; y++)
        {
            double e = 0;
            for (int x = 0; x < 3; x++)
            {
                e += (double)a[y, x] * b[x];
            }

            c[y] = (float)e;
        }

        return c;
    }

    // Inverts a 3x3 matrix. Intermediate computation is done in double precision.
    private static float[,] Inv3x3Matrix(float[,] m)
    {
        double[,] temp = new double[3, 3];
        temp[0, 0] = ((double)m[1, 1] * m[2, 2]) - ((double)m[1, 2] * m[2, 1]);
        temp[0, 1] = ((double)m[0, 2] * m[2, 1]) - ((double)m[0, 1] * m[2, 2]);
        temp[0, 2] = ((double)m[0, 1] * m[1, 2]) - ((double)m[0, 2] * m[1, 1]);
        temp[1, 0] = ((double)m[1, 2] * m[2, 0]) - ((double)m[1, 0] * m[2, 2]);
        temp[1, 1] = ((double)m[0, 0] * m[2, 2]) - ((double)m[0, 2] * m[2, 0]);
        temp[1, 2] = ((double)m[0, 2] * m[1, 0]) - ((double)m[0, 0] * m[1, 2]);
        temp[2, 0] = ((double)m[1, 0] * m[2, 1]) - ((double)m[1, 1] * m[2, 0]);
        temp[2, 1] = ((double)m[0, 1] * m[2, 0]) - ((double)m[0, 0] * m[2, 1]);
        temp[2, 2] = ((double)m[0, 0] * m[1, 1]) - ((double)m[0, 1] * m[1, 0]);
        double det = (m[0, 0] * temp[0, 0]) + (m[0, 1] * temp[1, 0]) + (m[0, 2] * temp[2, 0]);
        if (Math.Abs(det) < 1e-10)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Matrix determinant is too close to 0");
        }

        double idet = 1.0 / det;
        float[,] result = new float[3, 3];
        for (int j = 0; j < 3; j++)
        {
            for (int i = 0; i < 3; i++)
            {
                result[j, i] = (float)(temp[j, i] * idet);
            }
        }

        return result;
    }

    // ColorEncodingDescriptionImpl(c, uniquename: false): a short name for the common spaces, otherwise
    // the fields, as in "RGB_D65_SRG_Rel_Lin". libjxl formats the custom values with its own ToString;
    // this uses the shortest round-trip form.
    private static string Description(JxlColorEncoding c)
    {
        JxlTransferFunction tf = c.Tf.TransferFunction;
        if (c.ColorSpace == JxlColorSpace.Rgb && c.WhitePoint == JxlWhitePoint.D65 && !c.Tf.HaveGamma)
        {
            if (tf == JxlTransferFunction.Srgb && c.Primaries == JxlPrimaries.Srgb)
            {
                return "sRGB";
            }

            if (tf == JxlTransferFunction.Srgb && c.Primaries == JxlPrimaries.P3)
            {
                return "DisplayP3";
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
                ? "g" + Number(c.Tf.Gamma * (1.0 / JxlCustomTransferFunction.GammaMul))
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
}
