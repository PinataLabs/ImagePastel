// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Tests.TestUtilities.ReferenceCodecs;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;

/// <summary>
/// Compares <c>Image.Load</c> of every committed conformance input, through <see cref="Configuration.Default"/>,
/// with libjxl through Magick.NET
/// (<see cref="MagickReferenceDecoder.HighBitDepthInstance"/>), frame by frame. It needs no <c>.npy</c> data and
/// no libjxl tools, so it runs on every CI leg (#104). The fetched <c>bike</c>, <c>cafe</c> and <c>progressive</c>
/// inputs are left out, because the other legs do not have them.
/// </summary>
/// <remarks>
/// <para>
/// Magick.NET is Q16. libjxl gives it 8-bit samples for images of up to 8 bits, and 16-bit samples otherwise.
/// For 8-bit output libjxl adds an ordered blue-noise dither of less than 0.49219 of a step before it rounds
/// (<c>MakeUnsigned</c> in lib/jxl/render_pipeline/stage_write.cc). So where the decoded samples are not whole
/// steps, which is every lossy vector, Magick is up to 0.99219 of a step from the unrounded value. Lossy vectors
/// are therefore compared through <see cref="RgbaVector"/>, before this decoder rounds, in steps of Magick's
/// depth. When this dither was emulated on this decoder's samples, Magick differed on 0 to 147 samples per
/// vector, by one step each: libjxl uses FMA and this decoder does not (ADR 0004).
/// </para>
/// <para>
/// Lossless vectors are compared exactly, in the pixel type that <c>Image.Load</c> picks. Three of them, whose
/// samples above 8 bits are blended, may round one 16-bit step apart on arm64 only (see <see cref="Cases"/>).
/// </para>
/// </remarks>
[Trait("Format", "Jxl")]
public class JxlMagickCrossCheckTests
{
    // The difference at one pixel of frame f, row y and column x, whose Magick sample is expected.
    private delegate double PixelDifference(int f, int y, int x, Rgba64 expected);

    /// <summary>How this decoder's output relates to Magick's.</summary>
    public enum Comparison
    {
        /// <summary>Lossless: the pixels <c>Image.Load</c> picks equal Magick's, within the tolerance in 16-bit steps.</summary>
        Exact,

        /// <summary>The unrounded samples are within the tolerance, in steps of Magick's depth.</summary>
        Lossy,

        /// <summary>
        /// XYB with an ICC profile. libjxl gives Magick linear sRGB, which matches the linear sRGB reference
        /// planes; this decoder gives sRGB (ADR 0002). The colour is compared after the sRGB curve is undone.
        /// </summary>
        LinearSrgb,

        /// <summary>
        /// Premultiplied alpha. Both decoders divide by alpha, which magnifies their small differences without
        /// bound where alpha is small, so the colour is compared premultiplied again by each side's alpha.
        /// </summary>
        Premultiplied,

        /// <summary>
        /// CMYK. Magick reads the coded C, M and Y planes into R, G and B and drops the Black channel, so
        /// there is nothing to compare <c>Image.Load</c>'s naive conversion (#111) with. The decoded C, M, Y
        /// and alpha planes are compared with Magick's R, G, B and A instead.
        /// </summary>
        CmykPlanes,
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The orientation is the codestream's: libjxl leaves it to Magick, which records it and does not apply it,
    // so Magick's output is oriented here before the comparison.
    //
    // Each tolerance is the largest difference measured on Windows x64 (in the comment), plus 0.01 for the
    // other legs' libjxl builds, rounded up to two decimals. Lossless vectors have none, except the three
    // above 8 bits whose samples are blended or have spot colours rendered (blendmodes, spot, sunset_logo).
    // Those samples fall between 16-bit steps, and thousands of them lie within one float ulp of a half
    // step. On arm64, where FMA is always available, Magick's libjxl can fuse a multiply and an add that this
    // decoder keeps apart (ADR 0004), so a few of them round the other way: one 16-bit step, measured on the
    // macOS arm64 legs. On Windows and Linux x64 they match exactly, so OnArm64 allows the step on arm64 only.
    public static TheoryData<string, Comparison, double, ushort> Cases { get; } = new()
    {
        { "alpha_nonpremultiplied", Comparison.Exact, 0, 1 },

        // 12-bit colour and 16-bit alpha, so libjxl gives Magick 16-bit samples. The measured 0.6279 of a
        // 16-bit step is Magick's rounding (0.5) and 0.13 (1.9e-6) of floating-point difference, under half
        // the vector's conformance peak error (3.815e-6). Other legs' libjxl builds may differ more, so this
        // one allows the floating-point difference to reach that peak error: 0.5 + 0.25.
        { "alpha_premultiplied", Comparison.Premultiplied, 0.75, 1 },
        { "alpha_triangles", Comparison.Exact, 0, 1 },
        { "animation_icos4d", Comparison.Lossy, 1.01, 1 }, // 0.9921, all 48 frames
        { "animation_newtons_cradle", Comparison.Exact, 0, 1 },
        { "animation_spline", Comparison.Lossy, 1.01, 1 }, // 0.9921, all 60 frames

        // A JPEG transcode, with orientation 5 (transposed).
        { "bench_oriented_brg", Comparison.Lossy, 1.01, 5 }, // 0.9923
        { "bicycles", Comparison.Lossy, 1.01, 1 }, // 0.9922
        { "blendmodes", Comparison.Exact, OnArm64(1), 1 }, // 12-bit; 0 on x64, 1 on arm64

        // Blending the layers gives samples between steps, so libjxl dithers them.
        { "cmyk_layers", Comparison.CmykPlanes, 1, 1 }, // 0.9882
        { "delta_palette", Comparison.Exact, 0, 1 },
        { "grayscale", Comparison.LinearSrgb, 1, 1 }, // 0.9878

        // Magick asks libjxl for RGB here, so each colour channel has its own dither.
        { "grayscale_jpeg", Comparison.Lossy, 1, 1 }, // 0.9898
        { "grayscale_public_university", Comparison.Lossy, 1.01, 1 }, // 0.9922

        // 32-bit float samples, which Magick rounds to 16 bits: a sample on a rounding boundary is half a step off.
        { "lossless_pfm", Comparison.Lossy, 0.51, 1 }, // 0.5000
        { "lz77_flower", Comparison.Exact, 0, 1 },

        // Lossless layers, but the mul blend gives samples between steps.
        { "mul_no_extra_channels", Comparison.Lossy, 0.76, 1 }, // 0.7490
        { "noise", Comparison.Lossy, 1.01, 1 }, // 0.9922
        { "opsin_inverse", Comparison.Lossy, 1.01, 1 }, // 0.9920
        { "patches", Comparison.LinearSrgb, 1.01, 1 }, // 0.9922
        { "patches_lossless", Comparison.Exact, 0, 1 },

        // Both decoders render the spot colours over the colour.
        { "spot", Comparison.Exact, OnArm64(1), 1 }, // 16-bit; 0 on x64, 1 on arm64

        // Orientation 7 (anti-transposed).
        { "sunset_logo", Comparison.Exact, OnArm64(1), 7 }, // 10-bit; 0 on x64, 1 on arm64
        { "upsampling", Comparison.Lossy, 1.01, 1 }, // 0.9922
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Load_MatchesMagick(string id, Comparison comparison, double tolerance, ushort orientation)
    {
        MagickReferenceDecoder.SkipUnlessJxlIsSupported();

        string path = JxlConformanceVector.Load(id).CommittedInputPath;
        using Image<Rgba64> reference = DecodeWithMagick(path, orientation);

        double worst = comparison switch
        {
            Comparison.Exact => CompareExact(path, reference),
            Comparison.CmykPlanes => CompareCmykPlanes(path, reference),
            _ => CompareUnrounded(path, reference, comparison),
        };

        TestContext.Current.TestOutputHelper?.WriteLine($"{id}: largest difference {worst:F6} of a step");
        Assert.True(worst <= tolerance, $"{id}: largest difference {worst:F4} of a step, more than {tolerance}");
    }

    [Fact]
    public void Cases_CoverEveryCommittedInput()
    {
        IEnumerable<string> committed = JxlConformanceVector.AllIds
            .Where(id => !id.EndsWith(JxlConformanceVector.Level5Suffix, StringComparison.Ordinal))
            .Where(id => !JxlConformanceVector.Load(id).IsInputFetched);

        Assert.Equal(committed, Cases.Select(row => row.Data.Item1));
    }

    private static Image<Rgba64> DecodeWithMagick(string path, ushort orientation)
    {
        using FileStream stream = File.OpenRead(path);
        Image<Rgba64> image = MagickReferenceDecoder.HighBitDepthInstance.Decode<Rgba64>(Configuration.Default, stream, CancellationToken);
        if (orientation != 1)
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation);
            image.Mutate(x => x.AutoOrient());
        }

        return image;
    }

    // The largest difference in 16-bit steps.
    private static double CompareExact(string path, Image<Rgba64> reference)
    {
        using Image image = Image.Load(Configuration.Default, path);
        Assert.True(image.PixelType.BitsPerPixel <= 64, $"{image.GetType()} holds more than Magick's 16 bits.");
        using Image<Rgba64> actual = image.CloneAs<Rgba64>();
        AssertSameFrames(reference, actual);

        return LargestDifference(reference, (f, y, x, q) =>
        {
            Rgba64 p = actual.Frames[f].PixelBuffer[x, y];
            return Math.Max(Math.Max(Math.Abs(p.R - q.R), Math.Abs(p.G - q.G)), Math.Max(Math.Abs(p.B - q.B), Math.Abs(p.A - q.A)));
        });
    }

    // The largest difference in steps of Magick's depth.
    private static double CompareUnrounded(string path, Image<Rgba64> reference, Comparison comparison)
    {
        using Image<RgbaVector> actual = Image.Load<RgbaVector>(Configuration.Default, path);
        AssertSameFrames(reference, actual);
        double steps = MagickSteps(actual.Metadata.GetFormatMetadata(JxlFormat.Instance).BitsPerSample);

        return LargestDifference(reference, (f, y, x, q) =>
        {
            RgbaVector p = actual.Frames[f].PixelBuffer[x, y];
            double alpha = p.A;
            double expectedAlpha = q.A / 65535.0;
            double worst = Math.Abs(alpha - expectedAlpha);
            worst = Math.Max(worst, ColourDifference(p.R, q.R, alpha, expectedAlpha, comparison));
            worst = Math.Max(worst, ColourDifference(p.G, q.G, alpha, expectedAlpha, comparison));
            worst = Math.Max(worst, ColourDifference(p.B, q.B, alpha, expectedAlpha, comparison));
            return worst * steps;
        });
    }

    private static double ColourDifference(float actual, ushort expected, double alpha, double expectedAlpha, Comparison comparison)
    {
        double e = expected / 65535.0;
        return comparison switch
        {
            Comparison.LinearSrgb => Math.Abs(SrgbToLinear(actual) - e),
            Comparison.Premultiplied => Math.Abs((actual * alpha) - (e * expectedAlpha)),
            _ => Math.Abs(actual - e),
        };
    }

    // The largest difference in 8-bit steps: cmyk_layers has 8 bits per sample.
    private static double CompareCmykPlanes(string path, Image<Rgba64> reference)
    {
        using FileStream stream = File.OpenRead(path);
        using JxlDecodedImage planes = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);

        // C, M and Y, then the extra channels in stream order: Black, then alpha.
        Assert.Equal(reference.Frames.Count, planes.Frames.Count);
        Assert.Equal(5, planes.Frames[0].Planes.Count);
        for (int f = 0; f < planes.Frames.Count; f++)
        {
            Assert.Equal(reference.Frames[f].Width, planes.Frames[f].Planes[0].Width);
            Assert.Equal(reference.Frames[f].Height, planes.Frames[f].Planes[0].Height);
        }

        double steps = MagickSteps(8);
        return LargestDifference(reference, (f, y, x, q) =>
        {
            IReadOnlyList<Buffer2D<float>> plane = planes.Frames[f].Planes;
            double worst = PlaneDifference(plane[0][x, y], q.R);
            worst = Math.Max(worst, PlaneDifference(plane[1][x, y], q.G));
            worst = Math.Max(worst, PlaneDifference(plane[2][x, y], q.B));
            worst = Math.Max(worst, PlaneDifference(plane[4][x, y], q.A));
            return worst * steps;
        });
    }

    private static double PlaneDifference(float actual, ushort expected) => Math.Abs(Math.Clamp(actual, 0F, 1F) - (expected / 65535.0));

    // The largest difference over every pixel of every frame of the reference.
    private static double LargestDifference(Image<Rgba64> reference, PixelDifference difference)
    {
        double worst = 0;
        for (int f = 0; f < reference.Frames.Count; f++)
        {
            Buffer2D<Rgba64> e = reference.Frames[f].PixelBuffer;
            for (int y = 0; y < e.Height; y++)
            {
                Span<Rgba64> expectedRow = e.DangerousGetRowSpan(y);
                for (int x = 0; x < expectedRow.Length; x++)
                {
                    worst = Math.Max(worst, difference(f, y, x, expectedRow[x]));
                }
            }
        }

        return worst;
    }

    private static void AssertSameFrames<TPixel>(Image<Rgba64> expected, Image<TPixel> actual)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Assert.Equal(expected.Frames.Count, actual.Frames.Count);
        for (int f = 0; f < expected.Frames.Count; f++)
        {
            Assert.Equal(expected.Frames[f].Size(), actual.Frames[f].Size());
        }
    }

    // libjxl gives Magick 8-bit samples for images of up to 8 bits, and 16-bit samples otherwise.
    private static double MagickSteps(int bitsPerSample) => bitsPerSample <= 8 ? 255 : 65535;

    // A tolerance that applies on arm64 only; on x64 the row is exact.
    private static double OnArm64(double tolerance) => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? tolerance : 0;

    // The sRGB decoding of IEC 61966-2-1, in double precision.
    private static double SrgbToLinear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
}
