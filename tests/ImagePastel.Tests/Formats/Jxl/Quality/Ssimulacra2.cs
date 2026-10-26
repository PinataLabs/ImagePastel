// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// tools/ssimulacra2.cc (ComputeSSIMULACRA2 and Msssim::Score), the alpha handling of
// tools/ssimulacra2_main.cc, tools/gauss_blur.cc (CreateRecursiveGaussian and FastGaussian),
// LinearRGBToXYB of lib/jxl/enc_xyb.cc, CubeRootAndAdd of lib/jxl/base/fast_math-inl.h,
// TF_SRGB::DisplayFromEncoded of lib/jxl/cms/transfer_functions-inl.h,
// Inv3x3Matrix and Mul3x3Vector of lib/jxl/base/matrix_ops.h, and the opsin constants of
// lib/jxl/cms/opsin_params.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Numerics;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Quality;

/// <summary>
/// SSIMULACRA2, the perceptual metric of libjxl's <c>ssimulacra2</c> tool: 100 for identical images,
/// lower for more visible distortion. A port of libjxl's code, for tests that judge lossy quality
/// without the native tool.
/// </summary>
/// <remarks>
/// <para>
/// The score is sensitive to rounding: computing the blur in double precision moves a score by as
/// much as 0.2. So the port follows the arithmetic of the x64 binary on AVX2 and AVX-512 where it
/// rounds: the four-lane horizontal blur, and the fused multiply-adds (Highway's <c>MulAdd</c>) of the
/// blur, the sRGB curve, the opsin absorbance and the cube root. libjxl's scalar code is ported as
/// plain arithmetic, as its baseline build does not fuse. Against the Linux x64 binary on AVX2 it
/// agrees to about 1e-5 on the same PNGs, on every platform; the Windows binary scores about 0.02 lower,
/// because its C runtime rounds the cube root of the opsin bias one ulp high.
/// </para>
/// <para>
/// Departures from libjxl, each of which moves the score by far less than the 0.5 that the
/// <c>jxl-oracle</c> check allows against the <c>ssimulacra2</c> binary:
/// </para>
/// <list type="bullet">
/// <item>The pixels are read as sRGB, the colour space of every calibration image. libjxl converts
/// from the image's own colour encoding with its colour management system, which for sRGB applies
/// the same rational polynomial as here.</item>
/// <item>An image has alpha when any of its samples is not opaque. libjxl asks whether the file has
/// an alpha channel; an opaque alpha channel blends to the same samples, so the two agree except
/// for an opaque original with a translucent distorted image.</item>
/// </list>
/// </remarks>
internal static class Ssimulacra2
{
    private const float C2 = 0.0009F;
    private const int NumScales = 6;

    // lib/jxl/cms/opsin_params.h, evaluated in float as there.
    private static readonly float M00 = 0.30F;
    private static readonly float M02 = 0.078F;
    private static readonly float M01 = (1.0F - M02) - M00;
    private static readonly float M10 = 0.23F;
    private static readonly float M12 = 0.078F;
    private static readonly float M11 = (1.0F - M12) - M10;
    private static readonly float M20 = 0.24342268924547819F;
    private static readonly float M21 = 0.20476744424496821F;
    private static readonly float M22 = (1.0F - M20) - M21;
    private static readonly float OpsinBias = 0.0037930732552754493F;

    // -cbrtf(OpsinBias), correctly rounded, as JxlXybEncoder has it. MathF.Cbrt calls the C runtime, and
    // the Windows one returns a value one ulp higher than glibc and macOS, which moves a score by about 0.02.
    private static readonly float NegBiasCbrt = -0.15595419704914093F;

    private static readonly RecursiveGaussian Gaussian = RecursiveGaussian.Create(1.5);

    // Msssim::Score.
    private static readonly double[] Weights =
    {
        0.0, 0.0007376606707406586, 0.0, 0.0, 0.0007793481682867309, 0.0, 0.0, 0.0004371155730107379, 0.0,
        1.1041726426657346, 0.00066284834129271, 0.00015231632783718752, 0.0, 0.0016406437456599754, 0.0,
        1.8422455520539298, 11.441172603757666, 0.0, 0.0007989109436015163, 0.000176816438078653, 0.0,
        1.8787594979546387, 10.94906990605142, 0.0, 0.0007289346991508072, 0.9677937080626833, 0.0,
        0.00014003424285435884, 0.9981766977854967, 0.00031949755934435053, 0.0004550992113792063, 0.0, 0.0,
        0.0013648766163243398, 0.0, 0.0, 0.0, 0.0, 0.0, 7.466890328078848, 0.0, 17.445833984131262,
        0.0006235601634041466, 0.0, 0.0, 6.683678146179332, 0.00037724407979611296, 1.027889937768264,
        225.20515300849274, 0.0, 0.0, 19.213238186143016, 0.0011401524586618361, 0.001237755635509985,
        176.39317598450694, 0.0, 0.0, 24.43300999870476, 0.28520802612117757, 0.0004485436923833408, 0.0, 0.0,
        0.0, 34.77906344483772, 44.835625328877896, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
        0.0008680556573291698, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0005313191874358747, 0.0, 0.00016533814161379112, 0.0,
        0.0, 0.0, 0.0, 0.0, 0.0004179171803251336, 0.0017290828234722833, 0.0, 0.0020827005846636437, 0.0, 0.0,
        8.826982764996862, 23.19243343998926, 0.0, 95.1080498811086, 0.9863978034400682, 0.9834382792465353,
        0.0012286405048278493, 171.2667255897307, 0.9807858872435379, 0.0, 0.0, 0.0, 0.0005130064588990679, 0.0,
        0.00010854057858411537,
    };

    /// <summary>
    /// Computes the SSIMULACRA2 score of <paramref name="distorted"/> against <paramref name="original"/>,
    /// as the <c>ssimulacra2</c> tool prints it. Both are read as sRGB. When the original has alpha, the
    /// images are blended on a dark and a bright background and the worse score is returned.
    /// </summary>
    /// <typeparam name="TOriginal">The pixel type of the original.</typeparam>
    /// <typeparam name="TDistorted">The pixel type of the distorted image.</typeparam>
    /// <param name="original">The original image.</param>
    /// <param name="distorted">The distorted image, the same size.</param>
    /// <returns>The score: 100 for identical images, lower for more visible distortion.</returns>
    public static double Compute<TOriginal, TDistorted>(Image<TOriginal> original, Image<TDistorted> distorted)
        where TOriginal : unmanaged, IPixel<TOriginal>
        where TDistorted : unmanaged, IPixel<TDistorted>
    {
        // ssimulacra2_main.cc.
        if (original.Width != distorted.Width || original.Height != distorted.Height)
        {
            throw new ArgumentException("Image size mismatch.", nameof(distorted));
        }

        if (original.Width < 8 || original.Height < 8)
        {
            throw new ArgumentException("Minimum image size is 8x8 pixels.", nameof(original));
        }

        Rgba orig = Rgba.Read(original);
        Rgba dist = Rgba.Read(distorted);
        if (orig.Alpha is null)
        {
            return Score(Compute(orig, dist, 0.5F));
        }

        // In case of alpha transparency: blend against dark and bright backgrounds and return the
        // worst of both scores.
        return Math.Min(Score(Compute(orig, dist, 0.1F)), Score(Compute(orig, dist, 0.9F)));
    }

    // ComputeSSIMULACRA2.
    private static List<MsssimScale> Compute(Rgba orig, Rgba dist, float bg)
    {
        Image3 linear1 = orig.BlendToLinear(bg);
        Image3 linear2 = dist.BlendToLinear(bg);
        Image3 img1 = ToPositiveXyb(linear1);
        Image3 img2 = ToPositiveXyb(linear2);

        List<MsssimScale> scales = new();
        for (int scale = 0; scale < NumScales; scale++)
        {
            if (img1.Width < 8 || img1.Height < 8)
            {
                break;
            }

            if (scale > 0)
            {
                linear1 = Downsample(linear1, 2, 2);
                linear2 = Downsample(linear2, 2, 2);
                img1 = ToPositiveXyb(linear1);
                img2 = ToPositiveXyb(linear2);
            }

            Image3 sigma1Sq = Blur(Multiply(img1, img1));
            Image3 sigma2Sq = Blur(Multiply(img2, img2));
            Image3 sigma12 = Blur(Multiply(img1, img2));
            Image3 mu1 = Blur(img1);
            Image3 mu2 = Blur(img2);

            MsssimScale sscale = new();
            SsimMap(mu1, mu2, sigma1Sq, sigma2Sq, sigma12, sscale.AvgSsim);
            EdgeDiffMap(img1, mu1, img2, mu2, sscale.AvgEdgeDiff);
            scales.Add(sscale);
        }

        return scales;
    }

    // Msssim::Score. With fewer than six scales the weights are taken in order all the same, as in libjxl.
    private static double Score(List<MsssimScale> scales)
    {
        double ssim = 0.0;
        int i = 0;
        for (int c = 0; c < 3; ++c)
        {
            for (int scale = 0; scale < scales.Count; ++scale)
            {
                for (int n = 0; n < 2; n++)
                {
                    ssim += Weights[i++] * Math.Abs(scales[scale].AvgSsim[(c * 2) + n]);
                    ssim += Weights[i++] * Math.Abs(scales[scale].AvgEdgeDiff[(c * 4) + n]);
                    ssim += Weights[i++] * Math.Abs(scales[scale].AvgEdgeDiff[(c * 4) + n + 2]);
                }
            }
        }

        ssim *= 0.9562382616834844;
        ssim = (2.326765642916932 * ssim) - (0.020884521182843837 * ssim * ssim) + (6.248496625763138e-05 * ssim * ssim * ssim);
        return ssim > 0 ? 100.0 - (10.0 * Math.Pow(ssim, 0.6276336467831387)) : 100.0;
    }

    private static Image3 Downsample(Image3 input, int fx, int fy)
    {
        int outWidth = (input.Width + fx - 1) / fx;
        int outHeight = (input.Height + fy - 1) / fy;
        Image3 output = new(outWidth, outHeight);
        float normalize = 1.0F / (fx * fy);
        for (int c = 0; c < 3; ++c)
        {
            float[] inPlane = input.Planes[c];
            float[] outPlane = output.Planes[c];
            for (int oy = 0; oy < outHeight; ++oy)
            {
                for (int ox = 0; ox < outWidth; ++ox)
                {
                    float sum = 0.0F;
                    for (int iy = 0; iy < fy; ++iy)
                    {
                        for (int ix = 0; ix < fx; ++ix)
                        {
                            int x = Math.Min((ox * fx) + ix, input.Width - 1);
                            int y = Math.Min((oy * fy) + iy, input.Height - 1);
                            sum += inPlane[(y * input.Width) + x];
                        }
                    }

                    outPlane[(oy * outWidth) + ox] = sum * normalize;
                }
            }
        }

        return output;
    }

    private static Image3 Multiply(Image3 a, Image3 b)
    {
        Image3 mul = new(a.Width, a.Height);
        for (int c = 0; c < 3; ++c)
        {
            float[] in1 = a.Planes[c];
            float[] in2 = b.Planes[c];
            float[] output = mul.Planes[c];
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = in1[i] * in2[i];
            }
        }

        return mul;
    }

    private static double Quartic(double x)
    {
        x *= x;
        x *= x;
        return x;
    }

    private static void SsimMap(Image3 m1, Image3 m2, Image3 s11, Image3 s22, Image3 s12, double[] planeAverages)
    {
        double onePerPixels = 1.0 / ((long)m1.Height * m1.Width);
        for (int c = 0; c < 3; ++c)
        {
            double sum0 = 0.0;
            double sum1 = 0.0;
            float[] rowM1 = m1.Planes[c];
            float[] rowM2 = m2.Planes[c];
            float[] rowS11 = s11.Planes[c];
            float[] rowS22 = s22.Planes[c];
            float[] rowS12 = s12.Planes[c];
            for (int i = 0; i < rowM1.Length; i++)
            {
                float mu1 = rowM1[i];
                float mu2 = rowM2[i];
                float mu11 = mu1 * mu1;
                float mu22 = mu2 * mu2;
                float mu12 = mu1 * mu2;

                // libjxl drops the denominator of SSIM's luma term, since the values are already
                // perceptually uniform; see the comment in ssimulacra2.cc.
                float numM = (float)(1.0 - ((mu1 - mu2) * (mu1 - mu2)));
                float numS = (2 * (rowS12[i] - mu12)) + C2;
                float denomS = (rowS11[i] - mu11) + (rowS22[i] - mu22) + C2;

                // 1 - SSIM', an error score, so that an L4 norm makes sense.
                double d = 1.0 - (numM * numS / denomS);
                d = Math.Max(d, 0.0);
                sum0 += d;
                sum1 += Quartic(d);
            }

            planeAverages[c * 2] = onePerPixels * sum0;
            planeAverages[(c * 2) + 1] = Math.Sqrt(Math.Sqrt(onePerPixels * sum1));
        }
    }

    private static void EdgeDiffMap(Image3 img1, Image3 mu1, Image3 img2, Image3 mu2, double[] planeAverages)
    {
        double onePerPixels = 1.0 / ((long)img1.Height * img1.Width);
        for (int c = 0; c < 3; ++c)
        {
            double sum0 = 0.0;
            double sum1 = 0.0;
            double sum2 = 0.0;
            double sum3 = 0.0;
            float[] row1 = img1.Planes[c];
            float[] row2 = img2.Planes[c];
            float[] rowM1 = mu1.Planes[c];
            float[] rowM2 = mu2.Planes[c];
            for (int i = 0; i < row1.Length; i++)
            {
                double d1 = ((1.0 + MathF.Abs(row2[i] - rowM2[i])) / (1.0 + MathF.Abs(row1[i] - rowM1[i]))) - 1.0;

                // d1 > 0: the distorted image has an edge where the original is smooth (ringing,
                // colour banding, blockiness).
                double artifact = Math.Max(d1, 0.0);
                sum0 += artifact;
                sum1 += Quartic(artifact);

                // d1 < 0: the original has an edge where the distorted image is smooth (smoothing,
                // blurring, smearing).
                double detailLost = Math.Max(-d1, 0.0);
                sum2 += detailLost;
                sum3 += Quartic(detailLost);
            }

            planeAverages[c * 4] = onePerPixels * sum0;
            planeAverages[(c * 4) + 1] = Math.Sqrt(Math.Sqrt(onePerPixels * sum1));
            planeAverages[(c * 4) + 2] = onePerPixels * sum2;
            planeAverages[(c * 4) + 3] = Math.Sqrt(Math.Sqrt(onePerPixels * sum3));
        }
    }

    // ToXYB on linear sRGB (LinearSRGBToXYB with an intensity target of 255), then MakePositiveXYB,
    // which brings every component into about 0 to 1.
    private static Image3 ToPositiveXyb(Image3 linear)
    {
        Image3 xyb = new(linear.Width, linear.Height);
        float[] r = linear.Planes[0];
        float[] g = linear.Planes[1];
        float[] b = linear.Planes[2];
        float[] outX = xyb.Planes[0];
        float[] outY = xyb.Planes[1];
        float[] outB = xyb.Planes[2];
        for (int i = 0; i < r.Length; i++)
        {
            // OpsinAbsorbance: MulAdd(m0, r, MulAdd(m1, g, MulAdd(m2, b, bias))).
            float mixed0 = MathF.FusedMultiplyAdd(M00, r[i], MathF.FusedMultiplyAdd(M01, g[i], MathF.FusedMultiplyAdd(M02, b[i], OpsinBias)));
            float mixed1 = MathF.FusedMultiplyAdd(M10, r[i], MathF.FusedMultiplyAdd(M11, g[i], MathF.FusedMultiplyAdd(M12, b[i], OpsinBias)));
            float mixed2 = MathF.FusedMultiplyAdd(M20, r[i], MathF.FusedMultiplyAdd(M21, g[i], MathF.FusedMultiplyAdd(M22, b[i], OpsinBias)));

            // The mixed values should be non-negative even for wide gamut, so clamp to zero.
            mixed0 = CubeRootAndAdd(MathF.Max(mixed0, 0), NegBiasCbrt);
            mixed1 = CubeRootAndAdd(MathF.Max(mixed1, 0), NegBiasCbrt);
            mixed2 = CubeRootAndAdd(MathF.Max(mixed2, 0), NegBiasCbrt);

            float x = 0.5F * (mixed0 - mixed1);
            float y = 0.5F * (mixed0 + mixed1);

            // MakePositiveXYB.
            outB[i] = (mixed2 - y) + 0.55F;
            outX[i] = (x * 14.0F) + 0.42F;
            outY[i] = y + 0.01F;
        }

        return xyb;
    }

    // Returns cbrt(x) + add, with libjxl's initial guess and Newton-Raphson steps.
    private static float CubeRootAndAdd(float x, float add)
    {
        const float k13 = 1.0F / 3;
        const float k43 = 4.0F / 3;
        float xa3 = k13 * x;

        // Multiply the exponent by -1/3. Zero has an exponent of 0 and would give the wrong result.
        int m1 = BitConverter.SingleToInt32Bits(x);
        int m2 = m1 == 0 ? 0 : 0x54800000 - ((m1 >> 23) * 0x002AAAAA);
        float r = BitConverter.Int32BitsToSingle(m2);

        float r2;
        for (int i = 0; i < 3; i++)
        {
            r2 = r * r;
            r = MathF.FusedMultiplyAdd(-xa3, r2 * r2, k43 * r);
        }

        // Final iteration.
        r2 = r * r;
        r = MathF.FusedMultiplyAdd(k13, MathF.FusedMultiplyAdd(-x, r2 * r2, r), r);
        r2 = r * r;
        return MathF.FusedMultiplyAdd(r2, x, add);
    }

    // Blur::operator(): FastGaussian on each plane, horizontally into a temporary plane and then vertically.
    private static Image3 Blur(Image3 input)
    {
        Image3 output = new(input.Width, input.Height);
        float[] temp = new float[input.Width * input.Height];
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < input.Height; y++)
            {
                Gaussian.Horizontal(input.Planes[c].AsSpan(y * input.Width, input.Width), temp.AsSpan(y * input.Width, input.Width));
            }

            Gaussian.Vertical(temp, output.Planes[c], input.Width, input.Height);
        }

        return output;
    }

    private sealed class MsssimScale
    {
        public double[] AvgSsim { get; } = new double[3 * 2];

        public double[] AvgEdgeDiff { get; } = new double[3 * 4];
    }

    /// <summary>Three float planes of one size, in rows.</summary>
    private sealed class Image3
    {
        public Image3(int width, int height)
        {
            this.Width = width;
            this.Height = height;
            this.Planes = new[] { new float[width * height], new float[width * height], new float[width * height] };
        }

        public int Width { get; }

        public int Height { get; }

        public float[][] Planes { get; }
    }

    /// <summary>The samples of an image as sRGB planes, with alpha when any sample is not opaque.</summary>
    private sealed class Rgba
    {
        private Rgba(Image3 color, float[] alpha)
        {
            this.Color = color;
            this.Alpha = alpha;
        }

        public Image3 Color { get; }

        public float[] Alpha { get; }

        public static Rgba Read<TPixel>(Image<TPixel> image)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            int width = image.Width;
            Image3 color = new(width, image.Height);
            float[] alpha = new float[width * image.Height];
            bool translucent = false;
            Vector4[] row = new Vector4[width];
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    PixelOperations<TPixel>.Instance.ToVector4(Configuration.Default, accessor.GetRowSpan(y), row);
                    for (int x = 0; x < width; x++)
                    {
                        int i = (y * width) + x;
                        color.Planes[0][i] = row[x].X;
                        color.Planes[1][i] = row[x].Y;
                        color.Planes[2][i] = row[x].Z;
                        alpha[i] = row[x].W;
                        translucent |= row[x].W != 1F;
                    }
                }
            });

            return new Rgba(color, translucent ? alpha : null);
        }

        // AlphaBlend on the sRGB samples, then the transform to linear sRGB.
        public Image3 BlendToLinear(float bg)
        {
            Image3 linear = new(this.Color.Width, this.Color.Height);
            for (int c = 0; c < 3; c++)
            {
                float[] input = this.Color.Planes[c];
                float[] output = linear.Planes[c];
                for (int i = 0; i < input.Length; i++)
                {
                    float v = input[i];
                    if (this.Alpha is not null)
                    {
                        float a = this.Alpha[i];
                        v = (a * v) + ((1.0F - a) * bg);
                    }

                    output[i] = LinearFromSrgb(v);
                }
            }

            return linear;
        }

        // TF_SRGB::DisplayFromEncoded of lib/jxl/cms/transfer_functions-inl.h, which the colour
        // management system applies before it skips the transform from sRGB to linear sRGB: a rational
        // polynomial (Horner's scheme), mirrored for negative values.
        private static float LinearFromSrgb(float encoded)
        {
            const float lowDivInv = 1.0F / 12.92F;
            float x = MathF.Abs(encoded);
            float magnitude;
            if (x > 0.04045F)
            {
                float yp = MathF.FusedMultiplyAdd(8.210152774e-01F, x, 7.961564959e-01F);
                float yq = MathF.FusedMultiplyAdd(6.521209011e-03F, x, -5.512498495e-02F);
                yp = MathF.FusedMultiplyAdd(yp, x, 1.624820318e-01F);
                yq = MathF.FusedMultiplyAdd(yq, x, 4.987528350e-01F);
                yp = MathF.FusedMultiplyAdd(yp, x, 1.043637593e-02F);
                yq = MathF.FusedMultiplyAdd(yq, x, 1.076976492e+00F);
                yp = MathF.FusedMultiplyAdd(yp, x, 2.200248328e-04F);
                yq = MathF.FusedMultiplyAdd(yq, x, 2.631846970e-01F);
                magnitude = yp / yq;
            }
            else
            {
                magnitude = x * lowDivInv;
            }

            return encoded < 0 ? -magnitude : magnitude;
        }
    }

    /// <summary>
    /// The recursive Gaussian of tools/gauss_blur.cc: "Recursive Implementation of the Gaussian
    /// Filter Using Truncated Cosine Functions" by Charalampidis [2016], with zero padding.
    /// </summary>
    private sealed class RecursiveGaussian
    {
        private const int Lanes = 4;

        // For k = 1, 3 and 5: the vertical constants, and the horizontal multipliers for each of
        // four lanes.
        private readonly float[] n2 = new float[3];
        private readonly float[] d1 = new float[3];
        private readonly float[] mulPrev = new float[3 * Lanes];
        private readonly float[] mulPrev2 = new float[3 * Lanes];
        private readonly float[] mulIn = new float[3 * Lanes];
        private int radius;

        public static RecursiveGaussian Create(double sigma)
        {
            RecursiveGaussian rg = new();
            const double kPi = 3.141592653589793238;

            double radius = Math.Round((3.2795 * sigma) + 0.2546, MidpointRounding.AwayFromZero); // (57), "N"

            // Table I, first row.
            double piDiv2r = kPi / (2.0 * radius);
            double[] omega = { piDiv2r, 3.0 * piDiv2r, 5.0 * piDiv2r };

            // (37), k = {1, 3, 5}.
            double p1 = +1.0 / Math.Tan(0.5 * omega[0]);
            double p3 = -1.0 / Math.Tan(0.5 * omega[1]);
            double p5 = +1.0 / Math.Tan(0.5 * omega[2]);

            // (44), k = {1, 3, 5}.
            double r1 = +p1 * p1 / Math.Sin(omega[0]);
            double r3 = -p3 * p3 / Math.Sin(omega[1]);
            double r5 = +p5 * p5 / Math.Sin(omega[2]);

            // (50), k = {1, 3, 5}.
            double negHalfSigma2 = -0.5 * sigma * sigma;
            double recipRadius = 1.0 / radius;
            double[] rho = new double[3];
            for (int i = 0; i < 3; ++i)
            {
                rho[i] = Math.Exp(negHalfSigma2 * omega[i] * omega[i]) * recipRadius;
            }

            // Second part of (52), k1, k2 = 1, 3; 3, 5; 5, 1.
            double d13 = (p1 * r3) - (r1 * p3);
            double d35 = (p3 * r5) - (r3 * p5);
            double d51 = (p5 * r1) - (r5 * p1);

            // (52), k = 5.
            double recipD13 = 1.0 / d13;
            double zeta15 = d35 * recipD13;
            double zeta35 = d51 * recipD13;

            // (56)
            double[,] a =
            {
                { p1, p3, p5 },
                { r1, r3, r5 },
                { zeta15, zeta35, 1 },
            };
            Inv3x3Matrix(a);
            double[] gamma = { 1, (radius * radius) - (sigma * sigma), (zeta15 * rho[0]) + (zeta35 * rho[1]) + rho[2] }; // (55)
            double[] beta = new double[3];
            for (int y = 0; y < 3; y++)
            {
                // (53), Mul3x3Vector.
                double e = 0;
                for (int x = 0; x < 3; x++)
                {
                    e += a[y, x] * gamma[x];
                }

                beta[y] = e;
            }

            rg.radius = (int)radius;
            for (int i = 0; i < 3; ++i)
            {
                double n2 = -beta[i] * Math.Cos(omega[i] * (radius + 1.0)); // (33)
                double d1 = -2.0 * Math.Cos(omega[i]); // (33)
                rg.n2[i] = (float)n2;
                rg.d1[i] = (float)d1;

                // Obtained by expanding (35) for four consecutive outputs; see gauss_blur.cc.
                double d2 = d1 * d1;
                rg.mulPrev[(4 * i) + 0] = (float)-d1;
                rg.mulPrev[(4 * i) + 1] = (float)(d2 - 1.0);
                rg.mulPrev[(4 * i) + 2] = (float)((-d2 * d1) + (2.0 * d1));
                rg.mulPrev[(4 * i) + 3] = (float)((d2 * d2) - (3.0 * d2) + 1.0);
                rg.mulPrev2[(4 * i) + 0] = -1.0F;
                rg.mulPrev2[(4 * i) + 1] = (float)d1;
                rg.mulPrev2[(4 * i) + 2] = (float)(-d2 + 1.0);
                rg.mulPrev2[(4 * i) + 3] = (float)((d2 * d1) - (2.0 * d1));
                rg.mulIn[(4 * i) + 0] = (float)n2;
                rg.mulIn[(4 * i) + 1] = (float)(-d1 * n2);
                rg.mulIn[(4 * i) + 2] = (float)((d2 * n2) - n2);
                rg.mulIn[(4 * i) + 3] = (float)((-d2 * d1 * n2) + (2.0 * d1 * n2));
            }

            return rg;
        }

        // FastGaussian1D with four lanes, as on AVX2 and AVX-512 (HWY_CAPPED(float, 4)): one sample at a
        // time at the left, four at a time in the middle, then one at a time to the end.
        public void Horizontal(ReadOnlySpan<float> input, Span<float> output)
        {
            int xsize = input.Length;
            int bigN = this.radius;
            Span<float> prev = stackalloc float[3];
            Span<float> prev2 = stackalloc float[3];
            Span<float> sums = stackalloc float[Lanes];
            Span<float> outK = stackalloc float[3 * Lanes];

            int n = -bigN + 1;
            int firstAligned = (bigN + 1 + Lanes - 1) / Lanes * Lanes;
            for (; n < Math.Min(firstAligned, xsize); ++n)
            {
                this.HorizontalOne(input, output, n, prev, prev2);
            }

            // Unrolled, no bounds checks needed. Lane j of the outputs takes inputs 0 to j through the
            // shifted multipliers, and the previous two outputs, which are broadcast.
            for (; n < xsize - bigN + 1 - (Lanes - 1); n += Lanes)
            {
                for (int i = 0; i < Lanes; i++)
                {
                    sums[i] = input[n - bigN - 1 + i] + input[n + bigN - 1 + i];
                }

                for (int k = 0; k < 3; k++)
                {
                    for (int j = 0; j < Lanes; j++)
                    {
                        float o = sums[0] * this.mulIn[(4 * k) + j];
                        for (int i = 1; i < Lanes; i++)
                        {
                            // ShiftLeftLanes<i>: lanes below i multiply by zero.
                            float shifted = j >= i ? this.mulIn[(4 * k) + j - i] : 0F;
                            o = MathF.FusedMultiplyAdd(shifted, sums[i], o);
                        }

                        o = MathF.FusedMultiplyAdd(this.mulPrev2[(4 * k) + j], prev2[k], o);
                        o = MathF.FusedMultiplyAdd(this.mulPrev[(4 * k) + j], prev[k], o);
                        outK[(4 * k) + j] = o;
                    }

                    prev2[k] = outK[(4 * k) + Lanes - 2];
                    prev[k] = outK[(4 * k) + Lanes - 1];
                }

                for (int j = 0; j < Lanes; j++)
                {
                    output[n + j] = outK[j] + (outK[4 + j] + outK[8 + j]);
                }
            }

            // Remainder handling with bounds checks.
            for (; n < xsize; ++n)
            {
                this.HorizontalOne(input, output, n, prev, prev2);
            }
        }

        // FastGaussianVertical, one column at a time: each column's ring buffer of the last three
        // outputs per k, zero padding above and below. Each lane of libjxl's vectors is one column,
        // so the lane count does not change the result.
        public void Vertical(float[] input, float[] output, int xsize, int ysize)
        {
            int radius = this.radius;
            float[] y1 = new float[4];
            float[] y3 = new float[4];
            float[] y5 = new float[4];
            for (int x = 0; x < xsize; x++)
            {
                Array.Clear(y1);
                Array.Clear(y3);
                Array.Clear(y5);
                for (int n = -radius + 1; n < ysize; ++n)
                {
                    // The top is out of bounds until n > radius; the bottom past the last row.
                    int top = n - radius - 1;
                    int bottom = n + radius - 1;
                    float sum = bottom < ysize ? input[(bottom * xsize) + x] : 0.0F;
                    if (top >= 0)
                    {
                        sum = input[(top * xsize) + x] + sum;
                    }

                    int n0 = n & 3;
                    int n1 = (n - 1) & 3;
                    int n2 = (n - 2) & 3;

                    // (35): MulAdd(n2, sum, NegMulSub(d1, y[n - 1], y[n - 2])).
                    float v1 = MathF.FusedMultiplyAdd(this.n2[0], sum, MathF.FusedMultiplyAdd(-this.d1[0], y1[n1], -y1[n2]));
                    float v3 = MathF.FusedMultiplyAdd(this.n2[1], sum, MathF.FusedMultiplyAdd(-this.d1[1], y3[n1], -y3[n2]));
                    float v5 = MathF.FusedMultiplyAdd(this.n2[2], sum, MathF.FusedMultiplyAdd(-this.d1[2], y5[n1], -y5[n2]));
                    y1[n0] = v1;
                    y3[n0] = v3;
                    y5[n0] = v5;
                    if (n >= 0)
                    {
                        output[(n * xsize) + x] = v1 + (v3 + v5);
                    }
                }
            }
        }

        // One output of FastGaussian1D's bounds-checked loops: lane 0 of its vectors.
        private void HorizontalOne(ReadOnlySpan<float> input, Span<float> output, int n, Span<float> prev, Span<float> prev2)
        {
            int left = n - this.radius - 1;
            int right = n + this.radius - 1;
            float leftVal = left >= 0 ? input[left] : 0.0F;
            float rightVal = right < input.Length ? input[right] : 0.0F;
            float sum = leftVal + rightVal;
            for (int k = 0; k < 3; k++)
            {
                float o = sum * this.mulIn[4 * k];
                o = MathF.FusedMultiplyAdd(this.mulPrev2[4 * k], prev2[k], o);
                prev2[k] = prev[k];
                o = MathF.FusedMultiplyAdd(this.mulPrev[4 * k], prev[k], o);
                prev[k] = o;
            }

            if (n >= 0)
            {
                output[n] = prev[0] + (prev[1] + prev[2]);
            }
        }

        // Inv3x3Matrix of lib/jxl/base/matrix_ops.h, in double.
        private static void Inv3x3Matrix(double[,] m)
        {
            double[,] temp = new double[3, 3];
            temp[0, 0] = (m[1, 1] * m[2, 2]) - (m[1, 2] * m[2, 1]);
            temp[0, 1] = (m[0, 2] * m[2, 1]) - (m[0, 1] * m[2, 2]);
            temp[0, 2] = (m[0, 1] * m[1, 2]) - (m[0, 2] * m[1, 1]);
            temp[1, 0] = (m[1, 2] * m[2, 0]) - (m[1, 0] * m[2, 2]);
            temp[1, 1] = (m[0, 0] * m[2, 2]) - (m[0, 2] * m[2, 0]);
            temp[1, 2] = (m[0, 2] * m[1, 0]) - (m[0, 0] * m[1, 2]);
            temp[2, 0] = (m[1, 0] * m[2, 1]) - (m[1, 1] * m[2, 0]);
            temp[2, 1] = (m[0, 1] * m[2, 0]) - (m[0, 0] * m[2, 1]);
            temp[2, 2] = (m[0, 0] * m[1, 1]) - (m[0, 1] * m[1, 0]);
            double det = (m[0, 0] * temp[0, 0]) + (m[0, 1] * temp[1, 0]) + (m[0, 2] * temp[2, 0]);
            double idet = 1.0 / det;
            for (int j = 0; j < 3; j++)
            {
                for (int i = 0; i < 3; i++)
                {
                    m[j, i] = temp[j, i] * idet;
                }
            }
        }
    }
}
