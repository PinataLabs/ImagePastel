// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// JxlCmsSetFieldsFromICC, ApplyCICP, IsKnownTransferFunction, IsKnownColorPrimaries, UnadaptedWhitePoint and
// IdentifyPrimaries (the skcms build) and DetectTransferFunction and ProfileEquivalentToICC (the lcms build)
// in lib/jxl/cms/jxl_cms.cc; CustomTransferFunction::SetGamma, Customxy::SetValue, SetWhitePoint and
// SetPrimaries in lib/jxl/cms/color_encoding_cms.h; and ColorEncoding::DecideIfWantICC in
// lib/jxl/color_encoding_internal.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.
// cmsEstimateGamma is ported from src/cmsgamma.c of Little CMS (https://github.com/mm2/Little-CMS) at
// commit 5176347 (vendored in libjxl third_party/lcms).
// Copyright (c) Marti Maria Saguer. MIT License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Numerics;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Icc;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;

namespace SixLabors.ImageSharp.Formats.Jxl.Headers;

/// <content>
/// Recognition of an ICC profile as a colour encoding, which the lossy encoder writes in place of the
/// profile (#115).
/// </content>
internal sealed partial class JxlColorEncodingHeader
{
    // ApproxEq's default: sufficient for ICC's 15-bit fixed-point numbers.
    private const double SnapTolerance = 1e-3;

    // ProfileEquivalentToICC's tolerance, and its samples of a grey curve: from 1e-3 in steps of 0.2 / 8.
    private const double CurveTolerance = 2e-4;
    private const double CurveSampleStart = 1e-3;
    private const double CurveSampleStep = 0.2 / 8;

    // cmsEstimateGamma's precision, as DetectTransferFunction calls it, and lcms's MAX_NODES_IN_CURVE.
    private const double GammaPrecision = 1e-4;
    private const int GammaEstimateNodes = 4097;

    // H.273 ColourPrimaries 12: P3 with a D65 white (kColorPrimariesP3_D65).
    private const byte ColorPrimariesP3D65 = 12;

    // The transfer functions DetectTransferFunction tries, in the order of libjxl's enum (Values<TransferFunction>).
    private static readonly JxlTransferFunction[] TransferFunctions =
    {
        JxlTransferFunction.Bt709,
        JxlTransferFunction.Linear,
        JxlTransferFunction.Srgb,
        JxlTransferFunction.Pq,
        JxlTransferFunction.Dci,
        JxlTransferFunction.Hlg,
    };

    // IdentifyPrimaries without a chad tag: Bradford, from the D50 that skcms adapts to.
    private static readonly double[,] LmsFromXyz =
    {
        { 0.8951, 0.2664, -0.1614 },
        { -0.7502, 1.7135, 0.0367 },
        { 0.0389, -0.0685, 1.0296 },
    };

    private static readonly double[,] XyzFromLms =
    {
        { 0.9869929, -0.1470543, 0.1599627 },
        { 0.4323053, 0.5183603, 0.0492912 },
        { -0.0085287, 0.0400428, 0.9684867 },
    };

    private static readonly double[] WpD50Xyz = { 0.96420288, 1.0, 0.82490540 };

    /// <summary>
    /// Recognises <paramref name="profile"/> as a colour encoding, as libjxl's <c>DecideIfWantICC</c> does
    /// with its CMS (<c>JxlCmsSetFieldsFromICC</c>), in this order:
    /// <list type="number">
    /// <item><description>A <c>cicp</c> tag with an RGB matrix (0), full range, and primaries and a transfer function the enums name (<c>ApplyCICP</c>).</description></item>
    /// <item><description>
    /// A matrix/TRC profile with an XYZ connection space: RGB with <c>rXYZ</c>/<c>gXYZ</c>/<c>bXYZ</c> and
    /// <c>rTRC</c>/<c>gTRC</c>/<c>bTRC</c>, or grey with <c>kTRC</c>. The white point is the raw <c>wtpt</c>
    /// un-adapted through <c>chad</c>, and the primaries the colorants un-adapted the same way; both snap to a
    /// named enum within 1e-3, or are custom xy. The curve matches a gamma, or else the first of BT.709,
    /// linear, sRGB, PQ, DCI and HLG whose curve is within 2e-4 of it.
    /// </description></item>
    /// </list>
    /// The rendering intent comes from the header. The encoding must also be one a profile can be built for
    /// (<c>MaybeCreateProfile</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// libjxl has two CMS builds that recognise slightly differently. This follows the skcms build (libjxl's
    /// default) for the white point and primaries, and the lcms build for the curve: the gamma comes from
    /// <c>cmsEstimateGamma</c>, and each curve is sampled against the one a profile of the candidate encoding
    /// has (<see cref="JxlIccBuilder.CreateTrc"/>), the rule #115 adopted.
    /// </para>
    /// <para>
    /// Departure (ADR 0001): this library has no CMS (#105). The curves are compared, not the whole profile
    /// through a CMS, so the snapped primaries do not have to give the same XYZ. A profile with lookup tables
    /// (<c>AToB</c>, <c>BToA</c>, <c>DToB</c> or <c>BToD</c>) is not recognised as matrix/TRC, because a CMS
    /// would use the tables, which this library cannot compare. A <c>cicp</c> tag in a grey profile is
    /// ignored: libjxl would give such a profile an RGB encoding. libjxl reads the rendering intent from the
    /// header bytes and also accepts it little-endian; <see cref="IccProfile.CheckIsValid"/> refuses such a
    /// profile, so it is not recognised (and the encoder refuses it before it gets here).
    /// </para>
    /// </remarks>
    /// <param name="profile">The profile.</param>
    /// <param name="encoding">The colour encoding, when the method returns <see langword="true"/>; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the profile is recognised.</returns>
    public static bool TryFromIccProfile(IccProfile profile, out JxlColorEncodingHeader encoding)
    {
        encoding = null;
        if (profile is null || !profile.CheckIsValid())
        {
            return false;
        }

        // ICC and RenderingIntent have the same values (0..3), which CheckIsValid requires.
        JxlColorEncodingHeader c = new() { RenderingIntent = (JxlRenderingIntent)(uint)profile.Header.RenderingIntent };
        IccColorSpaceType dataColorSpace = profile.Header.DataColorSpace;
        if (dataColorSpace == IccColorSpaceType.Rgb
            && profile.TryGetTag(IccProfileTag.Cicp, out IccCicpTagDataEntry cicp)
            && c.ApplyCicp(cicp))
        {
            return c.TryAccept(out encoding);
        }

        if (dataColorSpace is not (IccColorSpaceType.Rgb or IccColorSpaceType.Gray)
            || profile.Header.ProfileConnectionSpace != IccColorSpaceType.CieXyz
            || HasLookupTables(profile))
        {
            return false;
        }

        c.ColorSpace = dataColorSpace == IccColorSpaceType.Gray ? JxlColorSpace.Gray : JxlColorSpace.Rgb;
        c.Tf.NonserializedColorSpace = c.ColorSpace;
        IccTagDataEntry[] curves = c.IsGray
            ? new[] { GetCurve(profile, IccProfileTag.GrayTrc) }
            : new[] { GetCurve(profile, IccProfileTag.RedTrc), GetCurve(profile, IccProfileTag.GreenTrc), GetCurve(profile, IccProfileTag.BlueTrc) };
        if (Array.Exists(curves, curve => curve is null))
        {
            return false;
        }

        double[,] chad = GetChad(profile);
        if (!TryGetUnadaptedWhitePoint(profile, chad, out (double X, double Y) white)
            || !c.SetWhitePoint(white)
            || (c.HasPrimaries && !c.TryIdentifyPrimaries(profile, chad, white))
            || !c.DetectTransferFunction(curves))
        {
            return false;
        }

        return c.TryAccept(out encoding);
    }

    // AToBn, BToAn, DToBn and BToDn: the first three bytes of the signature, whatever the intent digit.
    private static bool HasLookupTables(IccProfile profile)
    {
        foreach (IccTagDataEntry entry in profile.Entries)
        {
            if (((uint)entry.TagSignature >> 8) is 0x413242 or 0x423241 or 0x443242 or 0x423244)
            {
                return true;
            }
        }

        return false;
    }

    private static IccTagDataEntry GetCurve(IccProfile profile, IccProfileTag signature)
        => profile.TryGetTag(signature, out IccTagDataEntry curve) && IccCurveEvaluator.IsCurve(curve) ? curve : null;

    // The chad tag as a row-major 3x3 matrix, or null without one.
    private static double[,] GetChad(IccProfile profile)
    {
        if (!profile.TryGetTag(IccProfileTag.ChromaticAdaptation, out IccFix16ArrayTagDataEntry entry) || entry.Data.Length != 9)
        {
            return null;
        }

        double[,] chad = new double[3, 3];
        for (int j = 0; j < 3; j++)
        {
            for (int i = 0; i < 3; i++)
            {
                chad[j, i] = entry.Data[(j * 3) + i];
            }
        }

        return chad;
    }

    // UnadaptedWhitePoint: the white point the profile was created with. Without chad the wtpt tag is
    // already unadapted; otherwise it was adapted to the PCS white with chad, which is undone.
    private static bool TryGetUnadaptedWhitePoint(IccProfile profile, double[,] chad, out (double X, double Y) white)
    {
        white = default;
        if (!profile.TryGetTag(IccProfileTag.MediaWhitePoint, out IccXyzTagDataEntry wtpt) || wtpt.Data.Length == 0)
        {
            return false;
        }

        Vector3 v = wtpt.Data[0];
        double[] xyz = { v.X, v.Y, v.Z };
        if (chad is not null)
        {
            if (!JxlMatrix3x3.TryInvert(chad, out double[,] inverseChad))
            {
                return false;
            }

            xyz = JxlMatrix3x3.Multiply(inverseChad, xyz);
        }

        white = XyFromXyz(xyz);
        return true;
    }

    // IdentifyPrimaries: each colorant, the profile's XYZ (D50) of a unit primary, un-adapted with the
    // inverse of chad, or without chad with a Bradford adaptation from D50 to the white point.
    private bool TryIdentifyPrimaries(IccProfile profile, double[,] chad, (double X, double Y) white)
    {
        double[,] inverseChad;
        if (chad is not null)
        {
            if (!JxlMatrix3x3.TryInvert(chad, out inverseChad))
            {
                return false;
            }
        }
        else
        {
            // CIEXYZFromWhiteCIExy.
            if (Math.Abs(white.Y) < 1e-12)
            {
                return false;
            }

            double factor = 1 / white.Y;
            double[] wpUnadaptedXyz = { white.X * factor, 1, (1 - white.X - white.Y) * factor };
            double[] wpD50Lms = JxlMatrix3x3.Multiply(LmsFromXyz, WpD50Xyz);
            double[] wpUnadaptedLms = JxlMatrix3x3.Multiply(LmsFromXyz, wpUnadaptedXyz);
            double[,] scale =
            {
                { wpUnadaptedLms[0] / wpD50Lms[0], 0, 0 },
                { 0, wpUnadaptedLms[1] / wpD50Lms[1], 0 },
                { 0, 0, wpUnadaptedLms[2] / wpD50Lms[2] },
            };
            inverseChad = JxlMatrix3x3.Multiply(JxlMatrix3x3.Multiply(XyzFromLms, scale), LmsFromXyz);
        }

        IccProfileTag[] colorants = { IccProfileTag.RedMatrixColumn, IccProfileTag.GreenMatrixColumn, IccProfileTag.BlueMatrixColumn };
        (double X, double Y)[] xy = new (double X, double Y)[3];
        for (int i = 0; i < 3; i++)
        {
            if (!profile.TryGetTag(colorants[i], out IccXyzTagDataEntry colorant) || colorant.Data.Length == 0)
            {
                return false;
            }

            Vector3 v = colorant.Data[0];
            xy[i] = XyFromXyz(JxlMatrix3x3.Multiply(inverseChad, new double[] { v.X, v.Y, v.Z }));
        }

        return this.SetPrimaries(xy[0], xy[1], xy[2]);
    }

    // ApplyCICP: an RGB matrix (0), full range, and primaries and a transfer function the enums name. P3
    // primaries (11) come with the DCI white point, and code 12 is P3 with D65.
    private bool ApplyCicp(IccCicpTagDataEntry cicp)
    {
        if (cicp.MatrixCoefficients != 0 || cicp.VideoFullRangeFlag != 1)
        {
            return false;
        }

        JxlTransferFunction tf = (JxlTransferFunction)cicp.TransferCharacteristics;
        if (Array.IndexOf(TransferFunctions, tf) < 0)
        {
            return false;
        }

        JxlPrimaries primaries = (JxlPrimaries)cicp.ColorPrimaries;
        bool p3D65 = cicp.ColorPrimaries == ColorPrimariesP3D65;
        if (!p3D65 && primaries is not (JxlPrimaries.Srgb or JxlPrimaries.Bt2100 or JxlPrimaries.P3))
        {
            return false;
        }

        this.ColorSpace = JxlColorSpace.Rgb;
        this.Tf.NonserializedColorSpace = JxlColorSpace.Rgb;
        this.Tf.HaveGamma = false;
        this.Tf.TransferFunction = tf;
        this.WhitePoint = primaries == JxlPrimaries.P3 ? JxlWhitePoint.Dci : JxlWhitePoint.D65;
        this.Primaries = p3D65 ? JxlPrimaries.P3 : primaries;
        return true;
    }

    // SetWhitePoint: D65, E or DCI within 1e-3, otherwise custom.
    private bool SetWhitePoint((double X, double Y) xy)
    {
        if (xy.X == 0.0 || xy.Y == 0.0)
        {
            return false;
        }

        if (ApproxEq(xy.X, 0.3127) && ApproxEq(xy.Y, 0.3290))
        {
            this.WhitePoint = JxlWhitePoint.D65;
            return true;
        }

        if (ApproxEq(xy.X, 1.0 / 3) && ApproxEq(xy.Y, 1.0 / 3))
        {
            this.WhitePoint = JxlWhitePoint.E;
            return true;
        }

        if (ApproxEq(xy.X, 0.314) && ApproxEq(xy.Y, 0.351))
        {
            this.WhitePoint = JxlWhitePoint.Dci;
            return true;
        }

        this.WhitePoint = JxlWhitePoint.Custom;
        return SetValue(this.White, xy);
    }

    // SetPrimaries: sRGB, BT.2100 or P3 within 1e-3, otherwise custom.
    private bool SetPrimaries((double X, double Y) r, (double X, double Y) g, (double X, double Y) b)
    {
        if (r.X == 0.0 || r.Y == 0.0 || g.X == 0.0 || g.Y == 0.0 || b.X == 0.0 || b.Y == 0.0)
        {
            return false;
        }

        if (Matches(0.64, 0.33, 0.30, 0.60, 0.15, 0.06))
        {
            this.Primaries = JxlPrimaries.Srgb;
            return true;
        }

        if (Matches(0.708, 0.292, 0.170, 0.797, 0.131, 0.046))
        {
            this.Primaries = JxlPrimaries.Bt2100;
            return true;
        }

        if (Matches(0.680, 0.320, 0.265, 0.690, 0.150, 0.060))
        {
            this.Primaries = JxlPrimaries.P3;
            return true;
        }

        this.Primaries = JxlPrimaries.Custom;
        return SetValue(this.Red, r) && SetValue(this.Green, g) && SetValue(this.Blue, b);

        bool Matches(double rx, double ry, double gx, double gy, double bx, double by)
            => ApproxEq(r.X, rx) && ApproxEq(r.Y, ry) && ApproxEq(g.X, gx) && ApproxEq(g.Y, gy) && ApproxEq(b.X, bx) && ApproxEq(b.Y, by);
    }

    // Customxy::SetValue: the values times 10^6, rounded, within the range the header can code.
    private static bool SetValue(JxlCustomXy target, (double X, double Y) xy)
    {
        const double roughLimit = 4;
        const int min = -0x200000;
        const int max = 0x1FFFFF;
        if (!(Math.Abs(xy.X) < roughLimit) || !(Math.Abs(xy.Y) < roughLimit))
        {
            return false;
        }

        // roundf of the double product, as a float.
        int x = (int)MathF.Round((float)(xy.X * 1000000), MidpointRounding.AwayFromZero);
        int y = (int)MathF.Round((float)(xy.Y * 1000000), MidpointRounding.AwayFromZero);
        if (x < min || x > max || y < min || y > max)
        {
            return false;
        }

        target.X = x;
        target.Y = y;
        return true;
    }

    // DetectTransferFunction (the lcms build): a gamma, if every curve has the same estimated gamma and the
    // curve of that gamma matches; otherwise the first known transfer function whose curve matches. No match
    // leaves the transfer function unknown, which libjxl cannot build a profile for, so it keeps the ICC.
    private bool DetectTransferFunction(IccTagDataEntry[] curves)
    {
        float gamma = 0;
        float[] gammas = new float[curves.Length];
        for (int i = 0; i < curves.Length; i++)
        {
            double estimatedGamma = EstimateGamma(curves[i]);
            if (estimatedGamma > 0)
            {
                gammas[i] = (float)(1.0 / estimatedGamma);
            }
        }

        if (curves.Length == 1)
        {
            gamma = gammas[0];
        }
        else if (gammas[0] != 0 && MathF.Abs(gammas[0] - gammas[1]) < 1e-4f && MathF.Abs(gammas[1] - gammas[2]) < 1e-4f)
        {
            gamma = gammas[0];
        }

        if (gamma != 0 && this.Tf.SetGamma(gamma) && CurvesMatch(curves, this.Tf))
        {
            return true;
        }

        this.Tf.HaveGamma = false;
        foreach (JxlTransferFunction tf in TransferFunctions)
        {
            this.Tf.TransferFunction = tf;
            if (CurvesMatch(curves, this.Tf))
            {
                return true;
            }
        }

        this.Tf.TransferFunction = JxlTransferFunction.Unknown;
        return false;
    }

    // ProfileEquivalentToICC, for the curves only: each curve within 2e-4 of the curve a profile of the
    // candidate has, sampled as libjxl samples a grey profile.
    private static bool CurvesMatch(IccTagDataEntry[] curves, JxlCustomTransferFunction tf)
    {
        IccTagDataEntry candidate = JxlIccBuilder.CreateTrc(tf);
        foreach (IccTagDataEntry curve in curves)
        {
            for (double x = CurveSampleStart; x < 1.0; x += CurveSampleStep)
            {
                IccCurveEvaluator.TryEvaluate(curve, (float)x, out float actual);
                IccCurveEvaluator.TryEvaluate(candidate, (float)x, out float expected);
                if (!(Math.Abs(actual - expected) <= CurveTolerance))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // cmsEstimateGamma(curve, 1e-4): the mean of log(y) / log(x) over the curve's nodes above x = 0.07 with
    // y in (0, 1), or -1 when there are too few of them or they spread by more than the precision, that is
    // when the curve is not a power function.
    private static double EstimateGamma(IccTagDataEntry curve)
    {
        double sum = 0;
        double sum2 = 0;
        double n = 0;

        // Excluding endpoints.
        for (int i = 1; i < GammaEstimateNodes - 1; i++)
        {
            double x = (double)i / (GammaEstimateNodes - 1);
            IccCurveEvaluator.TryEvaluate(curve, (float)x, out float yf);
            double y = yf;

            // Avoid 7% on lower part to prevent artifacts due to linear ramps.
            if (y > 0.0 && y < 1.0 && x > 0.07)
            {
                double gamma = Math.Log(y) / Math.Log(x);
                sum += gamma;
                sum2 += gamma * gamma;
                n++;
            }
        }

        // We need enough valid samples.
        if (n <= 1)
        {
            return -1.0;
        }

        // Take a look on SD to see if gamma isn't exponential at all.
        double std = Math.Sqrt(((n * sum2) - (sum * sum)) / (n * (n - 1)));
        return std > GammaPrecision ? -1.0 : sum / n;
    }

    // DecideIfWantICC: the encoding is written in place of the profile only if a profile can be built for
    // it (MaybeCreateProfile).
    private bool TryAccept(out JxlColorEncodingHeader encoding)
    {
        encoding = null;
        try
        {
            JxlIccBuilder.Create(this);
        }
        catch (InvalidImageContentException)
        {
            return false;
        }

        this.WantIcc = false;
        encoding = this;
        return true;
    }

    private static bool ApproxEq(double a, double b) => Math.Abs(a - b) <= SnapTolerance;

    private static (double X, double Y) XyFromXyz(double[] xyz)
    {
        double sum = xyz[0] + xyz[1] + xyz[2];
        return (xyz[0] / sum, xyz[1] / sum);
    }
}
