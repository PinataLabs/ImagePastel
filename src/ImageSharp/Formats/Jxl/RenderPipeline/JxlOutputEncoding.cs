// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// OutputEncodingInfo (SetFromMetadata, SetColorEncoding) and CanOutputToColorEncoding in lib/jxl/dec_xyb.h
// and lib/jxl/dec_xyb.cc, OpsinParams and InitSIMDInverseMatrix in lib/jxl/opsin_params.h and
// lib/jxl/opsin_params.cc, for an XYB image whose output colour encoding the caller does not choose.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.ColorManagement.Icc;
using SixLabors.ImageSharp.Formats.Jxl.Headers;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// The colour encoding that the decoder renders an image to, and for an XYB image the opsin parameters
/// that turn XYB into linear samples of that encoding. libjxl's decoder API can ask for another output
/// encoding; this port always takes libjxl's default: the image's own encoding if the XYB stage can
/// output it, and linear sRGB otherwise (issue #105, ADR 0002).
/// </summary>
internal sealed class JxlOutputEncoding
{
    // kSRGBLuminances in SetColorEncoding.
    private static readonly float[] SrgbLuminances = { 0.2126f, 0.7152f, 0.0722f };

    private JxlOutputEncoding(bool isGray, bool colorEncodingIsOriginal, JxlCustomTransferFunction tf, bool linear)
    {
        this.IsGray = isGray;
        this.ColorEncodingIsOriginal = colorEncodingIsOriginal;
        this.Tf = tf;
        this.IsLinear = linear;
    }

    /// <summary>Gets a value indicating whether the output colour encoding is grey.</summary>
    public bool IsGray { get; }

    /// <summary>
    /// Gets a value indicating whether the output colour encoding is the image's own, libjxl's
    /// <c>color_encoding_is_original</c>. It is not for an XYB image with an ICC profile, or with an
    /// encoding the XYB stage cannot output: those render to linear sRGB.
    /// </summary>
    public bool ColorEncodingIsOriginal { get; }

    /// <summary>
    /// Gets the transfer function of the output colour encoding, or <see langword="null"/> when it is the
    /// linear one of the linear sRGB fallback.
    /// </summary>
    public JxlCustomTransferFunction Tf { get; }

    /// <summary>Gets a value indicating whether the output samples are linear.</summary>
    public bool IsLinear { get; }

    /// <summary>
    /// Gets the inverse opsin matrix, row-major, scaled so that 1.0 is the original intensity target: the
    /// image's matrix, adapted to the output primaries, and for grey output reduced to luminance. Only
    /// for an XYB image.
    /// </summary>
    public float[] InverseOpsinMatrix { get; } = new float[9];

    /// <summary>Gets the opsin biases, which are negative. Only for an XYB image.</summary>
    public float[] OpsinBiases { get; } = new float[3];

    /// <summary>Gets the cube roots of <see cref="OpsinBiases"/>. Only for an XYB image.</summary>
    public float[] OpsinBiasesCbrt { get; } = new float[3];

    /// <summary>
    /// Returns the output encoding of an image, as libjxl's <c>SetFromMetadata</c>. Throws
    /// <see cref="InvalidImageContentException"/> if the white point or primaries cannot be inverted.
    /// </summary>
    /// <param name="metadata">The image metadata.</param>
    /// <returns>The output encoding.</returns>
    public static JxlOutputEncoding Create(JxlCodecMetadata metadata)
    {
        JxlImageMetadata m = metadata.M;
        JxlColorEncoding orig = m.ColorEncoding;
        if (!m.XybEncoded)
        {
            // The samples are in the image's own encoding; only an XYB image needs the opsin parameters.
            return new JxlOutputEncoding(orig.IsGray, true, orig.Tf, IsLinearTf(orig.Tf));
        }

        bool origOk = CanOutputToColorEncoding(orig);
        JxlOutputEncoding output = origOk
            ? new JxlOutputEncoding(orig.IsGray, true, orig.Tf, IsLinearTf(orig.Tf))
            : new JxlOutputEncoding(orig.IsGray, false, null, true);

        JxlOpsinInverseMatrix im = metadata.TransformData.OpsinInverseMatrix;
        for (int i = 0; i < 3; ++i)
        {
            output.OpsinBiases[i] = im.OpsinBiases[i];
            output.OpsinBiasesCbrt[i] = MathF.Cbrt(im.OpsinBiases[i]);
        }

        float[,] inverseMatrix = new float[3, 3];
        for (int j = 0; j < 3; j++)
        {
            for (int i = 0; i < 3; i++)
            {
                inverseMatrix[j, i] = im.InverseMatrix[(j * 3) + i];
            }
        }

        // SetColorEncoding: the linear sRGB fallback has sRGB primaries and a D65 white point.
        if (origOk && (orig.Primaries != JxlPrimaries.Srgb || orig.WhitePoint != JxlWhitePoint.D65) && !orig.IsGray)
        {
            try
            {
                inverseMatrix = AdaptToPrimaries(orig, inverseMatrix);
            }
            catch (ArgumentException ex)
            {
                // libjxl's helpers fail with these messages; the frame cannot be rendered.
                throw new InvalidImageContentException(ex.Message, ex);
            }
        }

        if (output.IsGray)
        {
            float[,] srgbToLuma =
            {
                { SrgbLuminances[0], SrgbLuminances[1], SrgbLuminances[2] },
                { SrgbLuminances[0], SrgbLuminances[1], SrgbLuminances[2] },
                { SrgbLuminances[0], SrgbLuminances[1], SrgbLuminances[2] },
            };
            inverseMatrix = IccProfileBuilder.Mul3x3Matrix(srgbToLuma, inverseMatrix);
        }

        // The internal XYB color space uses absolute luminance, so we scale back the
        // opsin inverse matrix to relative luminance where 1.0 corresponds to the
        // original intensity target.
        float intensityTarget = m.ToneMapping.IntensityTarget;
        for (int j = 0; j < 3; ++j)
        {
            for (int i = 0; i < 3; ++i)
            {
                output.InverseOpsinMatrix[(j * 3) + i] = inverseMatrix[j, i] * (255.0f / intensityTarget);
            }
        }

        return output;
    }

    /// <summary>
    /// Returns whether the XYB stage can output to <paramref name="c"/>: it has fields rather than an ICC
    /// profile, a known transfer function, and if it is grey a D65 white point.
    /// </summary>
    /// <param name="c">The colour encoding.</param>
    /// <returns><see langword="true"/> if the XYB stage can output to it.</returns>
    public static bool CanOutputToColorEncoding(JxlColorEncoding c)
    {
        if (c.WantIcc)
        {
            return false;
        }

        // TODO(veluca): keep in sync with dec_reconstruct.cc
        JxlCustomTransferFunction tf = c.Tf;
        if (tf.IsUnknown)
        {
            return false;
        }

        if (c.IsGray && c.WhitePoint != JxlWhitePoint.D65)
        {
            // TODO(veluca): figure out what should happen here.
            return false;
        }

        return true;
    }

    private static bool IsLinearTf(JxlCustomTransferFunction tf) => !tf.HaveGamma && tf.TransferFunction == JxlTransferFunction.Linear;

    // The primaries part of SetColorEncoding for an XYB image: the matrix from linear sRGB to the output
    // primaries, through XYZ adapted to D50 with Bradford, is folded into the inverse opsin matrix.
    // libjxl also takes the luminances from these primaries, but only grey output uses them, and grey
    // has no primaries.
    private static float[,] AdaptToPrimaries(JxlColorEncoding c, float[,] origInverseMatrix)
    {
        // The default colour encoding is sRGB.
        JxlColorEncoding srgbEncoding = new();
        ((double X, double Y) R, (double X, double Y) G, (double X, double Y) B) srgb = srgbEncoding.PrimariesXy;
        (double X, double Y) d65 = srgbEncoding.WhitePointXy;
        float[,] srgbToXyzD50 = IccProfileBuilder.PrimariesToXyzD50(
            (float)srgb.R.X, (float)srgb.R.Y, (float)srgb.G.X, (float)srgb.G.Y, (float)srgb.B.X, (float)srgb.B.Y, (float)d65.X, (float)d65.Y, IccProfileBuilder.LibjxlD50);

        ((double X, double Y) R, (double X, double Y) G, (double X, double Y) B) p = c.PrimariesXy;
        (double X, double Y) w = c.WhitePointXy;
        float[,] originalToXyz = IccProfileBuilder.PrimariesToXyz(
            (float)p.R.X, (float)p.R.Y, (float)p.G.X, (float)p.G.Y, (float)p.B.X, (float)p.B.Y, (float)w.X, (float)w.Y);

        float[,] adaptToD50 = IccProfileBuilder.AdaptToXyzD50((float)w.X, (float)w.Y, IccProfileBuilder.LibjxlD50);
        float[,] xyzd50ToOriginal = IccProfileBuilder.Inv3x3Matrix(IccProfileBuilder.Mul3x3Matrix(adaptToD50, originalToXyz));
        float[,] srgbToOriginal = IccProfileBuilder.Mul3x3Matrix(xyzd50ToOriginal, srgbToXyzD50);
        return IccProfileBuilder.Mul3x3Matrix(srgbToOriginal, origInverseMatrix);
    }
}
