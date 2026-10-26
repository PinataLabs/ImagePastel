// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using PinataLabs.ImagePastel.Advanced;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Quality;

/// <summary>
/// Judges one lossy encode against its row of the calibration table, by the rule of #107: the managed
/// SSIMULACRA2 of the decoded image is at least cjxl's score less <see cref="ScoreMargin"/> and a slack
/// per effort, the encoded size is at most <see cref="SizeFactor"/> times cjxl's, and alpha is exact.
/// </summary>
/// <remarks>
/// Score the image as decoded to an <c>Image&lt;TPixel&gt;</c> (clamped to 0 to 1), as the table scores
/// djxl's 16-bit PNG. <c>RgbaVector</c> keeps the most precision; an 8-bit pixel type rounds the
/// distorted image and lowers the score a little.
/// </remarks>
internal static class JxlQualityComparer
{
    /// <summary>The allowance for the managed SSIMULACRA2 against the binary's (#107).</summary>
    public const double ScoreMargin = 0.5;

    /// <summary>The size ceiling as a multiple of cjxl's: it guards only against gross regressions (#107).</summary>
    public const double SizeFactor = 2;

    /// <summary>
    /// Gets the slack below cjxl's score at each libjxl effort. #107 starts every level at 3; tighten a level
    /// here once its encoder slice has measured how close it comes.
    /// </summary>
    /// <param name="effort">The libjxl effort: 3, 5 or 7.</param>
    /// <returns>The slack.</returns>
    public static double SlackFor(int effort) => effort switch
    {
        3 => 3,
        5 => 3,
        7 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, "The calibration table has efforts 3, 5 and 7."),
    };

    /// <summary>Compares one encode with its calibration row.</summary>
    /// <typeparam name="TOriginal">The pixel type of the original.</typeparam>
    /// <typeparam name="TDecoded">The pixel type of the decoded image.</typeparam>
    /// <param name="row">The calibration row of the same image, effort and distance.</param>
    /// <param name="original">The image that was encoded: the row's image.</param>
    /// <param name="decoded">The encode, decoded.</param>
    /// <param name="encodedSize">The size of the encode in bytes.</param>
    /// <param name="slack">The slack, or <see langword="null"/> for <see cref="SlackFor"/> the row's effort.</param>
    /// <returns>The result.</returns>
    public static JxlQualityResult Compare<TOriginal, TDecoded>(JxlCalibrationRow row, Image<TOriginal> original, Image<TDecoded> decoded, long encodedSize, double? slack = null)
        where TOriginal : unmanaged, IPixel<TOriginal>
        where TDecoded : unmanaged, IPixel<TDecoded>
    {
        double score = Ssimulacra2.Compute(original, decoded);
        double minimumScore = row.Score - ScoreMargin - (slack ?? SlackFor(row.Effort));
        long maximumSize = (long)Math.Floor(SizeFactor * row.Size);
        return new JxlQualityResult(row, score, minimumScore, encodedSize, maximumSize, FindAlphaDifference(original, decoded));
    }

    /// <summary>Compares one encode with its calibration row, and fails the test with the reasons if it does not pass.</summary>
    /// <typeparam name="TOriginal">The pixel type of the original.</typeparam>
    /// <typeparam name="TDecoded">The pixel type of the decoded image.</typeparam>
    /// <param name="row">The calibration row of the same image, effort and distance.</param>
    /// <param name="original">The image that was encoded: the row's image.</param>
    /// <param name="decoded">The encode, decoded.</param>
    /// <param name="encodedSize">The size of the encode in bytes.</param>
    /// <param name="slack">The slack, or <see langword="null"/> for <see cref="SlackFor"/> the row's effort.</param>
    /// <returns>The result, which passed.</returns>
    public static JxlQualityResult Verify<TOriginal, TDecoded>(JxlCalibrationRow row, Image<TOriginal> original, Image<TDecoded> decoded, long encodedSize, double? slack = null)
        where TOriginal : unmanaged, IPixel<TOriginal>
        where TDecoded : unmanaged, IPixel<TDecoded>
    {
        JxlQualityResult result = Compare(row, original, decoded, encodedSize, slack);
        if (!result.Passed)
        {
            Assert.Fail(result.ToString());
        }

        return result;
    }

    // Alpha is coded losslessly, so it must come back exactly. Samples are compared at 16 bits, which is
    // exact for 8- and 16-bit alpha whatever the pixel types. Returns the first difference, or null.
    private static string FindAlphaDifference<TOriginal, TDecoded>(Image<TOriginal> original, Image<TDecoded> decoded)
        where TOriginal : unmanaged, IPixel<TOriginal>
        where TDecoded : unmanaged, IPixel<TDecoded>
    {
        if (original.Width != decoded.Width || original.Height != decoded.Height)
        {
            return $"the decoded image is {decoded.Width} x {decoded.Height}, not {original.Width} x {original.Height}";
        }

        Vector4[] expected = new Vector4[original.Width];
        Vector4[] actual = new Vector4[original.Width];
        for (int y = 0; y < original.Height; y++)
        {
            PixelOperations<TOriginal>.Instance.ToVector4(Configuration.Default, original.DangerousGetPixelRowMemory(y).Span, expected);
            PixelOperations<TDecoded>.Instance.ToVector4(Configuration.Default, decoded.DangerousGetPixelRowMemory(y).Span, actual);
            for (int x = 0; x < expected.Length; x++)
            {
                if (To16Bits(expected[x].W) != To16Bits(actual[x].W))
                {
                    return string.Create(CultureInfo.InvariantCulture, $"alpha at ({x}, {y}) is {actual[x].W}, not {expected[x].W}");
                }
            }
        }

        return null;
    }

    private static int To16Bits(float value) => (int)MathF.Round(Math.Clamp(value, 0F, 1F) * 65535F);
}

/// <summary>The outcome of <see cref="JxlQualityComparer.Compare"/>.</summary>
/// <param name="Row">The calibration row.</param>
/// <param name="Score">The managed SSIMULACRA2 of the decoded image.</param>
/// <param name="MinimumScore">The lowest score that passes.</param>
/// <param name="Size">The size of the encode in bytes.</param>
/// <param name="MaximumSize">The largest size that passes.</param>
/// <param name="AlphaDifference">The first alpha sample that differs, or <see langword="null"/>.</param>
internal sealed record JxlQualityResult(JxlCalibrationRow Row, double Score, double MinimumScore, long Size, long MaximumSize, string AlphaDifference)
{
    public bool Passed => this.Score >= this.MinimumScore && this.Size <= this.MaximumSize && this.AlphaDifference is null;

    public IEnumerable<string> Failures
    {
        get
        {
            if (this.Score < this.MinimumScore)
            {
                yield return string.Create(CultureInfo.InvariantCulture, $"SSIMULACRA2 {this.Score:F4} is below {this.MinimumScore:F4} (cjxl {this.Row.Score:F4})");
            }

            if (this.Size > this.MaximumSize)
            {
                yield return string.Create(CultureInfo.InvariantCulture, $"{this.Size} bytes is over {this.MaximumSize} (cjxl {this.Row.Size})");
            }

            if (this.AlphaDifference is not null)
            {
                yield return this.AlphaDifference;
            }
        }
    }

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.Row}: SSIMULACRA2 {this.Score:F4} (at least {this.MinimumScore:F4}; cjxl {this.Row.Score:F4}), {this.Size} bytes (at most {this.MaximumSize}; cjxl {this.Row.Size}){(this.Passed ? string.Empty : ": " + string.Join("; ", this.Failures))}");
}
