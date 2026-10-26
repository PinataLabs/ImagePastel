// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// OutputEncodingInfo (SetFromMetadata, SetColorEncoding, with inverse_gamma, luminances and the original and
// desired intensity targets) and CanOutputToColorEncoding
// in lib/jxl/dec_xyb.h and lib/jxl/dec_xyb.cc, OpsinParams and InitSIMDInverseMatrix in lib/jxl/opsin_params.h and
// lib/jxl/opsin_params.cc, for an XYB image whose output colour encoding the caller does not choose.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.ColorManagement.Icc;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// The colour encoding that the decoder renders an image to, and for an XYB image the opsin parameters
/// that turn XYB into linear samples of that encoding. libjxl's decoder API can ask for another output
/// encoding; this port takes the image's own encoding if the XYB stage can output it, and otherwise
/// libjxl's default, linear sRGB, or sRGB for a sink that takes display-encoded samples
/// (<see cref="IJxlRenderSink.DisplayEncoded"/>; issue #105, ADR 0002).
/// </summary>
internal sealed class JxlOutputEncoding
{
    // kSRGBLuminances in SetColorEncoding.
    private static readonly float[] SrgbLuminances = { 0.2126f, 0.7152f, 0.0722f };

    private JxlOutputEncoding(bool isGray, bool colorEncodingIsOriginal, JxlCustomTransferFunction tf, bool linear, float intensityTarget)
    {
        this.IsGray = isGray;
        this.ColorEncodingIsOriginal = colorEncodingIsOriginal;
        this.Tf = tf;
        this.IsLinear = linear;
        this.OrigIntensityTarget = intensityTarget;

        // This port has no caller-chosen intensity target, so the desired one is the image's own.
        this.DesiredIntensityTarget = intensityTarget;

        // Set the inverse gamma based on color space transfer function.
        this.InverseGamma = tf is null ? 1.0f
            : tf.HaveGamma ? (float)(tf.Gamma * (1.0 / JxlCustomTransferFunction.GammaMul))
            : tf.IsDci ? 1.0f / 2.6f
            : 1.0f;
    }

    /// <summary>Gets a value indicating whether the output colour encoding is grey.</summary>
    public bool IsGray { get; }

    /// <summary>
    /// Gets a value indicating whether the output colour encoding is the image's own, libjxl's
    /// <c>color_encoding_is_original</c>. It is not for an XYB image with an ICC profile, or with an
    /// encoding the XYB stage cannot output: those render to linear sRGB, or sRGB.
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
    /// Gets the exponent that encodes a linear sample, libjxl's <c>inverse_gamma</c>: the gamma of the
    /// transfer function if it has one (0.45455 for a 2.2 gamma), 1 / 2.6 for DCI, and 1 otherwise.
    /// </summary>
    public float InverseGamma { get; }

    /// <summary>
    /// Gets the image's intensity target, in nits, libjxl's <c>orig_intensity_target</c>: the luminance
    /// of a linear sample of 1.0. The PQ curve scales by it.
    /// </summary>
    public float OrigIntensityTarget { get; }

    /// <summary>
    /// Gets the peak luminance, in nits, of the display the output is for, libjxl's
    /// <c>desired_intensity_target</c>. The HLG OOTF takes display light on such a display back to scene
    /// light. libjxl's decoder API can choose it; in this port it is always <see cref="OrigIntensityTarget"/>.
    /// </summary>
    public float DesiredIntensityTarget { get; }

    /// <summary>
    /// Gets the luminances of the output primaries, libjxl's <c>luminances</c>: the Y row of the
    /// primaries' XYZ matrix (not adapted to D50), or the sRGB luminances for sRGB primaries with a D65
    /// white point, for grey, and for the linear sRGB fallback. Only for an XYB image.
    /// </summary>
    public float[] Luminances { get; private set; } = (float[])SrgbLuminances.Clone();

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
    /// Returns the output encoding of an image, as libjxl's <c>SetFromMetadata</c>, then for an XYB
    /// image that cannot render to its own encoding and a sink that takes display-encoded samples, as
    /// libjxl's <c>SetColorEncoding</c> asked for sRGB. Throws <see cref="InvalidImageContentException"/>
    /// if the white point or primaries cannot be inverted.
    /// </summary>
    /// <param name="metadata">The image metadata.</param>
    /// <param name="displayEncoded">Whether the sink takes display-encoded samples (<see cref="IJxlRenderSink.DisplayEncoded"/>).</param>
    /// <returns>The output encoding.</returns>
    public static JxlOutputEncoding Create(JxlCodecMetadata metadata, bool displayEncoded)
    {
        JxlImageMetadata m = metadata.M;
        JxlColorEncodingHeader orig = m.ColorEncoding;
        float intensityTarget = m.ToneMapping.IntensityTarget;
        if (!m.XybEncoded)
        {
            // The samples are in the image's own encoding; only an XYB image needs the opsin parameters.
            return new JxlOutputEncoding(orig.IsGray, true, orig.Tf, orig.Tf.IsLinear, intensityTarget);
        }

        // The fallback has sRGB primaries and a D65 white point, with the sRGB curve or none.
        bool origOk = IsOriginalOutput(m);
        JxlOutputEncoding output = origOk
            ? new JxlOutputEncoding(orig.IsGray, true, orig.Tf, orig.Tf.IsLinear, intensityTarget)
            : displayEncoded
                ? new JxlOutputEncoding(orig.IsGray, false, new JxlCustomTransferFunction(), false, intensityTarget)
                : new JxlOutputEncoding(orig.IsGray, false, null, true, intensityTarget);

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

        // SetColorEncoding: the fallback has sRGB primaries and a D65 white point.
        if (origOk && (orig.Primaries != JxlPrimaries.Srgb || orig.WhitePoint != JxlWhitePoint.D65) && !orig.IsGray)
        {
            try
            {
                // The luminances are the Y row of the primaries' XYZ matrix (original_to_xyz[1]).
                (float[,] originalToXyz, float[] luminances) = orig.PrimariesToXyz();
                output.Luminances = luminances;
                inverseMatrix = AdaptToPrimaries(orig.WhitePointXy, originalToXyz, inverseMatrix);
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
    /// Returns whether the image renders to its own colour encoding: it is not XYB, or the XYB stage can
    /// output that encoding. Otherwise it renders to linear sRGB, or sRGB.
    /// </summary>
    /// <param name="m">The image metadata.</param>
    /// <returns><see langword="true"/> if the output colour encoding is the image's own.</returns>
    public static bool IsOriginalOutput(JxlImageMetadata m) => !m.XybEncoded || CanOutputToColorEncoding(m.ColorEncoding);

    /// <summary>
    /// Returns whether the XYB stage can output to <paramref name="c"/>: it has fields rather than an ICC
    /// profile, a known transfer function, and if it is grey a D65 white point.
    /// </summary>
    /// <param name="c">The colour encoding.</param>
    /// <returns><see langword="true"/> if the XYB stage can output to it.</returns>
    public static bool CanOutputToColorEncoding(JxlColorEncodingHeader c)
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

    // The primaries part of SetColorEncoding for an XYB image: the matrix from linear sRGB to the output
    // primaries, through XYZ adapted to D50 with Bradford, is folded into the inverse opsin matrix.
    // originalToXyz is the output primaries' XYZ matrix (original_to_xyz), and w their white point.
    private static float[,] AdaptToPrimaries((double X, double Y) w, float[,] originalToXyz, float[,] origInverseMatrix)
    {
        float[,] xyzd50ToOriginal = IccProfileBuilder.Inv3x3Matrix(JxlMatrix3x3.ToXyzD50(w, originalToXyz));
        float[,] srgbToXyzD50 = JxlMatrix3x3.SrgbToXyzD50();
        float[,] srgbToOriginal = IccProfileBuilder.Mul3x3Matrix(xyzd50ToOriginal, srgbToXyzD50);
        return IccProfileBuilder.Mul3x3Matrix(srgbToOriginal, origInverseMatrix);
    }
}
