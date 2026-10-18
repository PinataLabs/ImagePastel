// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// MaybeCreateProfileImpl and its helpers (PrimariesToXYZ, AdaptToXYZD50, CIEXYZFromWhiteCIExy,
// WriteICCS15Fixed16, CreateICCHeader, MaybeCreateICCCICPTag) in lib/jxl/cms/jxl_cms_internal.h,
// and Mul3x3Matrix, Mul3x3Vector and Inv3x3Matrix in lib/jxl/base/matrix_ops.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// Builds a matrix/TRC display profile from a white point, primaries and tone curves, with the tags and
/// the math of libjxl's <c>MaybeCreateProfile</c>: <c>desc</c>, <c>cprt</c>, <c>wtpt</c>, <c>chad</c>,
/// <c>cicp</c> (when the caller gives code points), <c>rXYZ</c>/<c>gXYZ</c>/<c>bXYZ</c> and the TRCs, with
/// Bradford adaptation to D50 and the colorants computed in single precision with double intermediates.
/// </summary>
/// <remarks>
/// Invalid input throws <see cref="ArgumentException"/> with libjxl's message. The public
/// <see cref="IccProfileFactory"/> lets it through; the JPEG XL decoder turns it into
/// <see cref="InvalidImageContentException"/>.
/// <para>
/// Where the built profile differs from libjxl's: table curves (PQ, HLG) are not built.
/// </para>
/// </remarks>
internal static class IccProfileBuilder
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

    /// <summary>
    /// The CIE D50 white libjxl adapts to. It is not quite the ICC PCS illuminant
    /// (<see cref="IccConnectionSpace.D50"/>), so the colorants of a libjxl profile add up to a white
    /// about 4e-4 bluer than the one the PCS defines.
    /// </summary>
    public static readonly Vector3 LibjxlD50 = new(0.96422f, 1.0f, 0.82521f);

    // libjxl writes its text tags in en-US. Under invariant globalization that culture can be missing,
    // and the text is then written with no language.
    private static readonly CultureInfo EnUs = GetEnUs();

    /// <summary>
    /// Builds the profile.
    /// </summary>
    /// <param name="dataColorSpace"><see cref="IccColorSpaceType.Rgb"/> or <see cref="IccColorSpaceType.Gray"/>.</param>
    /// <param name="white">The white point as CIE xy chromaticity.</param>
    /// <param name="primaries">
    /// The red, green and blue primaries as CIE xy chromaticity; null for gray, or to check only the
    /// white point of an RGB-like space.
    /// </param>
    /// <param name="curves">
    /// The tone curves: one for gray, red, green and blue for RGB. Null checks the other values and
    /// builds nothing.
    /// </param>
    /// <param name="adaptedWhite">
    /// The white that <c>chad</c> and the colorants adapt the white point to: <see cref="IccConnectionSpace.D50"/>,
    /// as the ICC specification and Little CMS do, or <see cref="LibjxlD50"/>, as libjxl does.
    /// </param>
    /// <param name="renderingIntent">The rendering intent of the header.</param>
    /// <param name="description">The text of the <c>desc</c> tag.</param>
    /// <param name="signature">The CMM type and creator signature of the header, or null for none.</param>
    /// <param name="cicp">
    /// The H.273 colour primaries and transfer characteristics that identify an RGB space, written to a
    /// <c>cicp</c> tag with matrix coefficients 0 and full range, as libjxl's <c>MaybeCreateICCCICPTag</c>
    /// does; null for no tag. Ignored for gray.
    /// </param>
    /// <returns>The profile, written and read back; null when <paramref name="curves"/> is null.</returns>
    /// <exception cref="ArgumentException">The white point or the primaries cannot describe a colour space.</exception>
    public static IccProfile Build(
        IccColorSpaceType dataColorSpace,
        (double X, double Y) white,
        (double X, double Y)[] primaries,
        IccParametricCurve[] curves,
        Vector3 adaptedWhite,
        IccRenderingIntent renderingIntent,
        string description,
        string signature,
        (byte ColorPrimaries, byte TransferCharacteristics)? cicp = null)
    {
        bool gray = dataColorSpace == IccColorSpaceType.Gray;
        (double wx, double wy) = white;
        List<IccTagDataEntry> tags = new()
        {
            new IccMultiLocalizedUnicodeTagDataEntry(new[] { new IccLocalizedString(EnUs, description) }, IccProfileTag.ProfileDescription),
            new IccMultiLocalizedUnicodeTagDataEntry(new[] { new IccLocalizedString(EnUs, "CC0") }, IccProfileTag.Copyright),
        };

        // TODO(eustas): isn't it the other way round: gray image has d50 WhitePoint?
        if (gray)
        {
            tags.Add(Xyz(CieXyzFromWhiteCieXy(wx, wy), IccProfileTag.MediaWhitePoint));
        }
        else
        {
            tags.Add(Xyz(new[] { 0.964203, 1.0, 0.824905 }, IccProfileTag.MediaWhitePoint));

            // Chromatic adaptation matrix (CreateICCChadMatrix).
            if (wy == 0)
            {
                throw new ArgumentException("Invalid WhitePoint", nameof(white)); // WhitePoint can not be pitch-black.
            }

            float[,] chad = AdaptToXyzD50((float)wx, (float)wy, adaptedWhite);
            float[] values = new float[9];
            for (int j = 0; j < 3; j++)
            {
                for (int i = 0; i < 3; i++)
                {
                    values[(j * 3) + i] = S15Fixed16(chad[j, i]);
                }
            }

            tags.Add(new IccFix16ArrayTagDataEntry(values, IccProfileTag.ChromaticAdaptation));

            // MaybeCreateICCCICPTag: matrix coefficients 0 (identity, RGB) and full range.
            if (cicp is (byte colorPrimaries, byte transferCharacteristics))
            {
                tags.Add(new IccCicpTagDataEntry(colorPrimaries, transferCharacteristics, 0, 1, IccProfileTag.Cicp));
            }

            if (primaries is not null)
            {
                float[,] m = PrimariesToXyzD50(
                    (float)primaries[0].X,
                    (float)primaries[0].Y,
                    (float)primaries[1].X,
                    (float)primaries[1].Y,
                    (float)primaries[2].X,
                    (float)primaries[2].Y,
                    (float)wx,
                    (float)wy,
                    adaptedWhite);
                tags.Add(Xyz(new double[] { m[0, 0], m[1, 0], m[2, 0] }, IccProfileTag.RedMatrixColumn));
                tags.Add(Xyz(new double[] { m[0, 1], m[1, 1], m[2, 1] }, IccProfileTag.GreenMatrixColumn));
                tags.Add(Xyz(new double[] { m[0, 2], m[1, 2], m[2, 2] }, IccProfileTag.BlueMatrixColumn));
            }
        }

        if (curves is null)
        {
            return null;
        }

        if (gray)
        {
            tags.Add(new IccParametricCurveTagDataEntry(curves[0], IccProfileTag.GrayTrc));
        }
        else
        {
            tags.Add(new IccParametricCurveTagDataEntry(curves[0], IccProfileTag.RedTrc));
            tags.Add(new IccParametricCurveTagDataEntry(curves[1], IccProfileTag.GreenTrc));
            tags.Add(new IccParametricCurveTagDataEntry(curves[2], IccProfileTag.BlueTrc));
        }

        // Written and read back, so the header carries its size and the profile passes CheckIsValid,
        // which the encoders check before they write a profile.
        return new IccProfile(new IccProfile(Header(dataColorSpace, renderingIntent, signature), tags.ToArray()).ToByteArray());
    }

    /// <summary>
    /// Rounds <paramref name="value"/> to the nearest s15Fixed16Number, as libjxl's <c>WriteICCS15Fixed16</c>
    /// does, so the profile writer's own rounding keeps it.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The rounded value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is NaN or out of the s15Fixed16Number range.</exception>
    public static float S15Fixed16(float value)
    {
        // "nextafterf" for 32768.0f towards zero are:
        // 32767.998046875, 32767.99609375, 32767.994140625
        // Even the first value works well,...
        bool ok = value is >= -32767.995f and <= 32767.995f;
        if (!ok)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "ICC value is out of range / NaN");
        }

        return MathF.Round(value * 65536.0f, MidpointRounding.AwayFromZero) / 65536.0f;
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
    private static IccProfileHeader Header(IccColorSpaceType dataColorSpace, IccRenderingIntent renderingIntent, string signature) => new()
    {
        CmmType = signature,
        Version = new IccVersion(4, 4, 0),
        Class = IccProfileClass.DisplayDevice,
        DataColorSpace = dataColorSpace,
        ProfileConnectionSpace = IccColorSpaceType.CieXyz,

        // TODO(lode): encode actual date and time, this is a placeholder
        CreationDate = new DateTime(2019, 12, 1, 0, 0, 0, DateTimeKind.Utc),
        FileSignature = "acsp",
        PrimaryPlatformSignature = IccPrimaryPlatformType.AppleComputerInc,
        RenderingIntent = renderingIntent,

        // Mandatory D50 white point of profile connection space
        PcsIlluminant = new Vector3(0x0000f6d6 / 65536f, 1, 0x0000d32d / 65536f),
        CreatorSignature = signature,
    };

    private static IccXyzTagDataEntry Xyz(double[] xyz, IccProfileTag tag)
        => new(new[] { new Vector3(S15Fixed16((float)xyz[0]), S15Fixed16((float)xyz[1]), S15Fixed16((float)xyz[2])) }, tag);

    private static double[] CieXyzFromWhiteCieXy(double wx, double wy)
    {
        // Target Y = 1.
        if (Math.Abs(wy) < 1e-12)
        {
            throw new ArgumentException("Y value is too small", "white");
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
            throw new ArgumentException("Invalid white point", "white");
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
    private static float[,] AdaptToXyzD50(float wx, float wy, Vector3 d50)
    {
        // Out of range values can cause division through zero further down with the bradford
        // adaptation too.
        CheckWhitePoint(wx, wy);
        float[] w = { wx / wy, 1.0f, (1.0f - wx - wy) / wy };

        // 1 / tiny float can still overflow
        if (!float.IsFinite(w[0]) || !float.IsFinite(w[2]))
        {
            throw new ArgumentException("Invalid white point", "white");
        }

        float[] w50 = { d50.X, d50.Y, d50.Z };
        float[] lms = Mul3x3Vector(Bradford, w);
        float[] lms50 = Mul3x3Vector(Bradford, w50);
        if (lms[0] == 0 || lms[1] == 0 || lms[2] == 0)
        {
            throw new ArgumentException("Invalid white point", "white");
        }

        float[,] a =
        {
            { lms50[0] / lms[0], 0, 0 },
            { 0, lms50[1] / lms[1], 0 },
            { 0, 0, lms50[2] / lms[2] },
        };
        if (!float.IsFinite(a[0, 0]) || !float.IsFinite(a[1, 1]) || !float.IsFinite(a[2, 2]))
        {
            throw new ArgumentException("Invalid white point", "white");
        }

        float[,] b = Mul3x3Matrix(a, Bradford);
        return Mul3x3Matrix(BradfordInv, b);
    }

    private static float[,] PrimariesToXyzD50(float rx, float ry, float gx, float gy, float bx, float by, float wx, float wy, Vector3 adaptedWhite)
    {
        float[,] toXyz = PrimariesToXyz(rx, ry, gx, gy, bx, by, wx, wy);
        float[,] d50 = AdaptToXyzD50(wx, wy, adaptedWhite);
        return Mul3x3Matrix(d50, toXyz);
    }

    private static void CheckWhitePoint(float wx, float wy)
    {
        bool ok = wx >= 0 && wx <= 1 && wy > 0 && wy <= 1;
        if (!ok)
        {
            throw new ArgumentException("Invalid white point", "white");
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
            throw new ArgumentException("Matrix determinant is too close to 0", "primaries");
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
}
