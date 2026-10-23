// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl.Quality;

/// <summary>
/// E3's done-check (#270) and E4's (#306): every Fastest (libjxl effort 3) and Default (effort 5) row of the
/// calibration table, encoded by our encoder at the row's distance and effort, meets the quality bar of #107
/// against cjxl's row. Our decoder checks it on every leg, Magick.NET on every leg but coverage, and djxl in
/// <c>jxl-oracle</c>. E5 (#318) logs how many Thorough (effort 7) rows pass, as its progress meter.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlLossyCalibrationTests
{
    /// <summary>
    /// The slack below cjxl's score at Fastest, on top of the comparer's 0.5: #107's starting value, kept.
    /// Every row passes it with about 3.3 points to spare (<see cref="FastestRows"/>), so a regression of
    /// a few tenths would still pass: the SHA-256 pins catch any change to the bytes.
    /// </summary>
    private const double FastestSlack = 3;

    /// <summary>
    /// The slack below cjxl's score at Default, on top of the comparer's 0.5: #107's starting value, kept
    /// for every row (#297). No row needs a larger one: each passes with about 3.4 points to spare
    /// (<see cref="DefaultRows"/>), calliphora d4 among them, where cjxl itself scores only 38.14.
    /// </summary>
    private const double DefaultSlack = 3;

    /// <summary>
    /// The slack below cjxl's score at Thorough, on top of the comparer's 0.5: #107's starting value.
    /// </summary>
    private const double ThoroughSlack = 3;

    /// <summary>
    /// How many of the 18 Thorough rows (cjxl v0.12.0 <c>-e 7</c>) pass at <see cref="ThoroughSlack"/>
    /// through our decoder, as the last E5 ticket that changed it recorded it.
    /// </summary>
    /// <remarks>
    /// E5a (net10.0, x64): 14 of 18. The tracer runs Hare's VarDCT choices, so each row without alpha is
    /// the Default encode (bike_alpha differs only in its alpha tree). The photos and the gradient pass
    /// (smallest margins: calliphora d4 +1.20, text d4 +1.16, bike_alpha d1 +2.23, text d1 +2.21; text is
    /// 1.11x to 1.17x cjxl's size, which has patches). text d2 fails (-0.99), and line_art fails at every
    /// distance: d1 -2.25, d2 -9.52, d4 -9.33, at 0.98x to 1.00x cjxl's size. cjxl's e5 to e7 jump on
    /// line_art is not from patches (#318), so it comes from a stage still in its Hare form.
    /// <para>
    /// E5c (AFV at the 8x8 level) alone: 14 of 18, text d2 -0.30. E5b (DCT4X8 and DCT8X4) alone: 15 of 18,
    /// text d2 +0.03. With both (E5b merged after E5c): 14 of 18. text d2 fails again, at -0.15. The
    /// thinnest passing margins: text d4 +1.20, calliphora d4 +1.22, bike_alpha d1 +2.00, text d1 +2.17.
    /// line_art still fails: d1 -2.62, d2 -6.41, d4 -6.42.
    /// </para>
    /// <para>
    /// E5e (the CfL pre-pass and Newton fit): 16 of 18. text d2 passes (+0.46) and line_art d1 passes
    /// (+2.68); line_art still fails at d2 (-1.13) and d4 (-3.63). Smallest passing margins: text d2 +0.46,
    /// calliphora d4 +1.10, text d4 +1.78. E5e merged with E5c (AFV): 18 of 18. Smallest margins:
    /// line_art d2 +0.31, text d2 +0.34, line_art d4 +0.48, calliphora d4 +1.46.
    /// E5h (dynamic EPF sharpness), on E5a alone: still 14 of 18; line_art rises from 61.19 to 61.79 at d2
    /// and from 51.64 to 53.75 at d4. Its sharpness histogram is within a few percent of cjxl -e 7's (d2:
    /// ours 240 / 44 / 740 blocks at 0 / 2 / 7, cjxl's 205 / 43 / 776), but cjxl's B quantisation scale
    /// there is 5, not 2, which is E5f's. With E5c (AFV) merged: 15 of 18, as text d2 passes by +0.21;
    /// line_art scores 61.97 at d2 and 55.01 at d4 (margins d1 -2.61, d2 -8.75, d4 -5.96). Smallest
    /// passing margins: text d2 +0.21, text d4 +1.13, text d1 +2.30, bike_alpha d1 +2.39.
    /// E5h merged with E5c and E5e: 18 of 18. line_art scores 71.65 at d2 and 63.45 at d4 (margins +0.94 and
    /// +2.48, against +0.31 and +0.48 without E5h). Smallest margins: text d2 +0.88, line_art d2 +0.94,
    /// bike_alpha d1 +2.42, line_art d4 +2.48.
    /// </para>
    /// <para>
    /// E5b merged after E5c and E5e: 17 of 18, lowered on purpose. DCT4X8 and DCT8X4 match libjxl bit for
    /// bit (the transforms and EstimateEntropy against the harnesses), so the loss is not a port error. It
    /// is a real Squirrel stage that moves line_art d2 from +0.31 to -0.11 (70.60 against a floor of
    /// 70.71). Other margins: text d2 +0.31, line_art d4 +0.74, calliphora d4 +1.22, text d4 +1.39. The
    /// stages still in Hare form (E5d merges, E5f qm scales, E5h EPF sharpness) are expected to win it back.
    /// </para>
    /// <para>
    /// E5f (the pixel-statistics X and B scales, cjxl -e 7's on every row: B 5 on line_art, X 4 and B 3 on
    /// text, X 5 and B 4 on bike_alpha; the other rows keep Default's) alone: 16 of 18. text d2 passes
    /// (+0.81) and line_art d1 passes (+2.30); line_art d2 (-1.52) and d4 (-4.10) fail. E5f merged with E5c
    /// and E5g: 17 of 18 (line_art d2 +0.07, d4 -2.53). E5f merged with E5c, E5e and E5g: 18 of 18.
    /// Smallest margins: line_art d4 +0.23 (E5e's tip had +0.48), text d2 +1.28, calliphora d4 +1.46,
    /// text d4 +2.02; line_art d2 +2.83 and d1 +3.48.
    /// </para>
    /// <para>
    /// E5h merged with E5c, E5e and E5f: 18 of 18. line_art scores 74.29 at d2 and 63.01 at d4 (margins
    /// +3.57 and +2.05, against +2.83 and +0.23 without E5h; E5a's were -9.52 and -9.33). Its d2 sharpness
    /// histogram is 212 / 43 / 769 blocks at 0 / 2 / 7, cjxl -e 7's 205 / 43 / 776. Smallest margins:
    /// line_art d4 +2.05, text d2 +2.17, text d4 +2.71, bike d4 +3.10. With E5d (off-grid merges) too:
    /// 18 of 18; line_art d2 +3.57 and d4 +2.00 (E5d's tip without E5h had d4 +0.14). Smallest margins:
    /// line_art d4 +2.00, text d2 +2.15, text d4 +2.70, bike d4 +3.03.
    /// </para>
    /// <para>
    /// E5b (DCT4X8 and DCT8X4) merged last, after E5c, E5d, E5e, E5f, E5g and E5h: 18 of 18. Every
    /// Squirrel stage is now ported. Smallest margins: text d2 +2.04, text d4 +2.33, text d1 +2.95, bike_alpha
    /// d4 +3.22; line_art d4 is +3.51 (it was +2.00 without E5b). The photos, the gradient and line_art are
    /// 0.99x to 1.02x cjxl's size, and text is 1.17x to 1.18x, as it has no patches.
    /// </para>
    /// </remarks>
    private const int ThoroughRowsPassing = 18;

    /// <summary>
    /// Each Fastest row of the table, and our encode of it as measured through our decoder (net10.0, x64,
    /// feat/jxl-e3f): the managed SSIMULACRA2 and the size in bytes. The margin is the score less the floor
    /// (cjxl's score - 0.5 - <see cref="FastestSlack"/>), and the size is against a ceiling of 2x cjxl's.
    /// The values are logged, not asserted: the comparer is the check.
    /// </summary>
    /// <remarks>
    /// Through our decoder our scores are within 0.04 of cjxl's, and our sizes within 0.4 %: djxl decodes
    /// our files and cjxl's to almost the same pixels (E3a). The smallest margin is bike_alpha d2's 3.4631
    /// (0.037 below cjxl), and the largest size bike_alpha d4's 1.0031x. Through djxl's 16-bit PNG, which is
    /// what the table scores, the smallest margin is 3.4912 (gradient d1); through Magick.NET, whose
    /// libjxl delegate gives slightly different samples, it is 3.3276 (line_art d1).
    /// </remarks>
    private static readonly MeasuredRow[] FastestRows =
    {
        // image, distance, our score, our size; then cjxl's score and size, our score margin, our size over cjxl's
        new("bike", 1, 88.2668, 48376),           // cjxl 88.2425,  48399: margin 3.5243, size x0.9995
        new("bike", 2, 81.0685, 31428),           // cjxl 81.0863,  31445: margin 3.4822, size x0.9995
        new("bike", 4, 71.5711, 21083),           // cjxl 71.5663,  21101: margin 3.5048, size x0.9991
        new("calliphora", 1, 84.7354, 65989),     // cjxl 84.7576,  65958: margin 3.4778, size x1.0005
        new("calliphora", 2, 73.6994, 45291),     // cjxl 73.6808,  45309: margin 3.5186, size x0.9996
        new("calliphora", 4, 57.8323, 30364),     // cjxl 57.8343,  30312: margin 3.4980, size x1.0017
        new("bike_alpha", 1, 87.7651, 27659),     // cjxl 87.7864,  27585: margin 3.4787, size x1.0027
        new("bike_alpha", 2, 79.9347, 21646),     // cjxl 79.9716,  21586: margin 3.4631, size x1.0028
        new("bike_alpha", 4, 69.3022, 17638),     // cjxl 69.3149,  17583: margin 3.4873, size x1.0031
        new("gradient", 1, 92.9796, 2604),        // cjxl 92.9802,   2625: margin 3.4994, size x0.9920
        new("gradient", 2, 87.3145, 1818),        // cjxl 87.2994,   1839: margin 3.5151, size x0.9886
        new("gradient", 4, 84.2080, 1243),        // cjxl 84.2113,   1265: margin 3.4967, size x0.9826
        new("text", 1, 85.0518, 15681),           // cjxl 85.0655,  15698: margin 3.4863, size x0.9989
        new("text", 2, 74.1556, 11954),           // cjxl 74.1813,  11973: margin 3.4743, size x0.9984
        new("text", 4, 62.7802, 9096),            // cjxl 62.7604,   9112: margin 3.5198, size x0.9982
        new("line_art", 1, 79.8843, 27026),       // cjxl 79.8649,  27044: margin 3.5194, size x0.9993
        new("line_art", 2, 67.4650, 19096),       // cjxl 67.4817,  19116: margin 3.4833, size x0.9990
        new("line_art", 4, 51.2793, 13233),       // cjxl 51.2692,  13255: margin 3.5101, size x0.9983
    };

    /// <summary>
    /// Each Default row of the table, and our encode of it as measured through our decoder (net10.0, x64,
    /// e4/e4i): the managed SSIMULACRA2 and the size in bytes. The margin is the score less the floor
    /// (cjxl's score - 0.5 - <see cref="DefaultSlack"/>), and the size is against a ceiling of 2x cjxl's.
    /// The values are logged, not asserted: the comparer is the check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Through our decoder every score is within 0.09 of cjxl's and every size within 0.98x to 1.02x: E4h's
    /// strategy map is cjxl's block for block on Bike and Calliphora. The smallest margin is line_art d4's
    /// 3.4123 (0.088 below cjxl), and the largest size the same row's 1.0198x. Through djxl's 16-bit PNG the
    /// smallest margin is 3.4038 (line_art d4); through Magick.NET it is 3.2337 (gradient d1).
    /// </para>
    /// <para>
    /// How the rows passed as E4 was built (slack 3, our decoder): E4a's tracer (Fastest's field and DCT8
    /// everywhere) 16 of 18; E4c 17; E4b's global scale of 0.39 / d on E4c, E4d and E4e 13, as the tracer
    /// no longer over-spent on text, line art and the gradient; E4g's 8x8 search 17; E4h's merges 18.
    /// </para>
    /// </remarks>
    private static readonly MeasuredRow[] DefaultRows =
    {
        // image, distance, our score, our size; then cjxl's score and size, our score margin, our size over cjxl's
        new("bike", 1, 86.6131, 44687),           // cjxl 86.5820,  44661: margin 3.5311, size x1.0006
        new("bike", 2, 79.7840, 29296),           // cjxl 79.7795,  29272: margin 3.5044, size x1.0008
        new("bike", 4, 68.9696, 17859),           // cjxl 68.9480,  17833: margin 3.5216, size x1.0015
        new("calliphora", 1, 80.1160, 56238),     // cjxl 80.1204,  56305: margin 3.4956, size x0.9988
        new("calliphora", 2, 65.3133, 36445),     // cjxl 65.3027,  36415: margin 3.5106, size x1.0008
        new("calliphora", 4, 38.1381, 22307),     // cjxl 38.1394,  22130: margin 3.4987, size x1.0080
        new("bike_alpha", 1, 86.2216, 26529),     // cjxl 86.1951,  26390: margin 3.5265, size x1.0053
        new("bike_alpha", 2, 78.0109, 20961),     // cjxl 78.0106,  20809: margin 3.5003, size x1.0073
        new("bike_alpha", 4, 63.5843, 16824),     // cjxl 63.5816,  16730: margin 3.5027, size x1.0056
        new("gradient", 1, 94.0487, 2187),        // cjxl 94.0537,   2187: margin 3.4950, size x1.0000
        new("gradient", 2, 92.1156, 1683),        // cjxl 92.0942,   1706: margin 3.5215, size x0.9865
        new("gradient", 4, 90.0266, 1258),        // cjxl 89.9831,   1278: margin 3.5435, size x0.9844
        new("text", 1, 91.3358, 14375),           // cjxl 91.2677,  14388: margin 3.5681, size x0.9991
        new("text", 2, 85.4831, 10779),           // cjxl 85.4606,  10797: margin 3.5225, size x0.9983
        new("text", 4, 80.5499, 8533),            // cjxl 80.5571,   8549: margin 3.4928, size x0.9981
        new("line_art", 1, 78.0567, 24273),       // cjxl 78.0722,  24227: margin 3.4845, size x1.0019
        new("line_art", 2, 61.1895, 18538),       // cjxl 61.1594,  18295: margin 3.5301, size x1.0133
        new("line_art", 4, 51.6336, 13285),       // cjxl 51.7213,  13027: margin 3.4123, size x1.0198
    };

    // Each row is encoded once, whichever readers check it.
    private static readonly ConcurrentDictionary<(string Image, int Effort, double Distance), Lazy<byte[]>> Encodes = new();

    public static TheoryData<string, string, double> ReaderRows => CreateReaderRows(FastestRows);

    public static TheoryData<string, string, double> DefaultReaderRows => CreateReaderRows(DefaultRows);

    // The theory covers the whole table at Fastest: a row added to the table must be added here too.
    [Fact]
    public void FastestRows_AreEveryFastestRowOfTheTable()
        => Assert.Equal(RowNames(JxlEffort.Fastest), FastestRows.Select(Name).Order(StringComparer.Ordinal).ToArray());

    // The Default theory covers the whole table at Default: a row added to the table must be added here too.
    [Fact]
    public void DefaultRows_AreEveryDefaultRowOfTheTable()
        => Assert.Equal(RowNames(JxlEffort.Default), DefaultRows.Select(Name).Order(StringComparer.Ordinal).ToArray());

    // #107's rule: SSIMULACRA2 >= cjxl's - 0.5 - the slack, size <= 2x cjxl's, and alpha exact (the
    // comparer checks alpha on every row; bike_alpha is the row with alpha). Each reader's decode is
    // scored: djxl's is the 16-bit PNG the table itself scores.
    [Theory]
    [MemberData(nameof(ReaderRows))]
    public Task Encode_FastestCalibrationRow_MeetsTheQualityBar(string reader, string image, double distance)
        => AssertRowMeetsTheQualityBarAsync(JxlEffort.Fastest, FastestRows, FastestSlack, reader, image, distance);

    // E4's done-check: the same rule at Default against cjxl -e 5, with the Default slack for every row.
    [Theory]
    [MemberData(nameof(DefaultReaderRows))]
    public Task Encode_DefaultCalibrationRow_MeetsTheQualityBar(string reader, string image, double distance)
        => AssertRowMeetsTheQualityBarAsync(JxlEffort.Default, DefaultRows, DefaultSlack, reader, image, distance);

    // E3d's 16-bit and float input against the 8-bit rows: the table has no such rows, but the image widened
    // to 16 bits (x 257) or to floats is the same colour, so its XYB is the same and it meets the same bar.
    // Alpha stays exact. Float input is opaque Bike: float alpha is coded losslessly as 32-bit floats, so
    // bike_alpha as floats is about twice the size of the 8-bit row (64,664 bytes at d1), which is no
    // fault. Through our decoder only, and not under coverage: the 8-bit rows run the same encoder code.
    [Theory]
    [InlineData(nameof(Rgb48), "bike", 1.0)]
    [InlineData(nameof(Rgb48), "bike", 4.0)]
    [InlineData(nameof(Rgba64), "bike_alpha", 1.0)]
    [InlineData(nameof(RgbaVector), "bike", 1.0)]
    [InlineData(nameof(RgbaVector), "bike", 4.0)]
    public void Encode_FastestCalibrationRowAtAHigherDepth_MeetsTheQualityBar(string pixelType, string image, double distance)
        => AssertWidenedRowMeetsTheQualityBar(JxlEffort.Fastest, FastestRows, FastestSlack, pixelType, image, distance);

    // The same widened inputs at Default, against the 8-bit Default rows.
    [Theory]
    [InlineData(nameof(Rgb48), "bike", 1.0)]
    [InlineData(nameof(Rgb48), "bike", 4.0)]
    [InlineData(nameof(Rgba64), "bike_alpha", 1.0)]
    [InlineData(nameof(RgbaVector), "bike", 1.0)]
    [InlineData(nameof(RgbaVector), "bike", 4.0)]
    public void Encode_DefaultCalibrationRowAtAHigherDepth_MeetsTheQualityBar(string pixelType, string image, double distance)
        => AssertWidenedRowMeetsTheQualityBar(JxlEffort.Default, DefaultRows, DefaultSlack, pixelType, image, distance);

    // E5's progress meter (#318): every Thorough (libjxl effort 7) row of the table, encoded at the row's
    // distance and decoded by our decoder, against the comparer at the Thorough slack. Only E5i must pass
    // them all; until then each ticket logs how many pass, and this asserts that no ticket passes fewer than
    // ThoroughRowsPassing, the count recorded by the last ticket that changed it.
    [Fact]
    public void Encode_ThoroughCalibrationRows_PassAtLeastTheRecordedCount()
    {
        Assert.SkipWhen(TestEnvironment.RunsWithCodeCoverage, "Adds no coverage: JxlLossyThoroughEncoderTests runs the same encoder code.");
        JxlCalibrationRow[] rows = JxlCalibrationTable.Instance.Rows
            .Where(r => r.Effort == JxlCalibrationTable.LibjxlEffort(JxlEffort.Thorough))
            .ToArray();

        int passed = 0;
        foreach (JxlCalibrationRow row in rows)
        {
            using Image<Rgba32> original = Image.Load<Rgba32>(row.Image.FullPath);
            byte[] encoded = Encode(row, JxlEffort.Thorough);
            using Image<Rgba64> decoded = Image.Load<Rgba64>(JxlEncoderRoundTrip.CreateConfiguration(), encoded);
            JxlQualityResult result = JxlQualityComparer.Compare(row, original, decoded, encoded.Length, ThoroughSlack);
            passed += result.Passed ? 1 : 0;
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{(result.Passed ? "pass" : "FAIL")} {result}; score margin {result.Score - result.MinimumScore:F4}, size ratio {(double)result.Size / row.Size:F4}"));
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{passed} of {rows.Length} Thorough rows pass (recorded {ThoroughRowsPassing}).");
        Assert.Equal(18, rows.Length);
        Assert.True(passed >= ThoroughRowsPassing, $"{passed} of {rows.Length} Thorough rows pass; {ThoroughRowsPassing} did before.");
    }

    private static async Task AssertRowMeetsTheQualityBarAsync(JxlEffort effort, MeasuredRow[] measuredRows, double slack, string reader, string image, double distance)
    {
        JxlCalibrationRow row = JxlCalibrationTable.Instance.Find(image, effort, distance);
        MeasuredRow measured = measuredRows.Single(r => r.Image == image && r.Distance == distance);
        JxlEncoderRoundTrip.Reader readerKind = Enum.Parse<JxlEncoderRoundTrip.Reader>(reader);
        JxlEncoderRoundTrip.SkipUnlessReaderRuns(readerKind);
        using Image<Rgba32> original = Image.Load<Rgba32>(row.Image.FullPath);

        byte[] encoded = Encode(row, effort);

        using Image<Rgba64> decoded = await DecodeAsync(readerKind, row, encoded);
        JxlQualityResult result = JxlQualityComparer.Compare(row, original, decoded, encoded.Length, slack);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{reader}: {result}; score margin {result.Score - result.MinimumScore:F4} (recorded {measured.Score - result.MinimumScore:F4}), size ratio {(double)result.Size / row.Size:F4} (recorded {(double)measured.Size / row.Size:F4})"));
        Assert.True(result.Passed, result.ToString());
    }

    private static void AssertWidenedRowMeetsTheQualityBar(JxlEffort effort, MeasuredRow[] measuredRows, double slack, string pixelType, string image, double distance)
    {
        Assert.SkipWhen(TestEnvironment.RunsWithCodeCoverage, "Adds no coverage: the 8-bit rows run the same encoder code.");
        JxlCalibrationRow row = JxlCalibrationTable.Instance.Find(image, effort, distance);
        MeasuredRow measured = measuredRows.Single(r => r.Image == image && r.Distance == distance);
        using Image<Rgba32> original = Image.Load<Rgba32>(row.Image.FullPath);

        byte[] encoded = JxlPixelTypes.Call(pixelType, new EncodeWidened(original, (float)distance, effort));

        using Image<Rgba64> decoded = Image.Load<Rgba64>(JxlEncoderRoundTrip.CreateConfiguration(), encoded);
        JxlQualityResult result = JxlQualityComparer.Compare(row, original, decoded, encoded.Length, slack);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{pixelType}: {result}; score {result.Score:F4} (8-bit {measured.Score:F4}), size {result.Size} (8-bit {measured.Size})"));
        Assert.True(result.Passed, result.ToString());
    }

    private static TheoryData<string, string, double> CreateReaderRows(MeasuredRow[] rows)
    {
        TheoryData<string, string, double> data = new();
        foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
        {
            foreach (MeasuredRow row in rows)
            {
                data.Add(reader.ToString(), row.Image, row.Distance);
            }
        }

        return data;
    }

    private static string[] RowNames(JxlEffort effort)
        => JxlCalibrationTable.Instance.Rows
            .Where(r => r.Effort == JxlCalibrationTable.LibjxlEffort(effort))
            .Select(r => $"{r.Image.Name} d{r.Distance.ToString(CultureInfo.InvariantCulture)}")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string Name(MeasuredRow row) => $"{row.Image} d{row.Distance.ToString(CultureInfo.InvariantCulture)}";

    // The table's images are 8-bit sRGB with no colour chunks; only bike_alpha has alpha. Each is encoded
    // as the pixel type it is stored in, as cjxl reads it: RGB without an alpha channel, or RGBA.
    private static byte[] Encode(JxlCalibrationRow row, JxlEffort effort)
        => Encodes.GetOrAdd((row.Image.Name, row.Effort, row.Distance), _ => new Lazy<byte[]>(() =>
        {
            JxlEncoder encoder = new() { Distance = (float)row.Distance, Effort = effort };
            if (Image.Identify(row.Image.FullPath).Metadata.GetPngMetadata().ColorType is PngColorType.RgbWithAlpha)
            {
                using Image<Rgba32> rgba = Image.Load<Rgba32>(row.Image.FullPath);
                return JxlEncoderRoundTrip.Encode(rgba, encoder);
            }

            using Image<Rgb24> rgb = Image.Load<Rgb24>(row.Image.FullPath);
            return JxlEncoderRoundTrip.Encode(rgb, encoder);
        })).Value;

    // Decodes to 16 bits per sample, at least the precision of djxl's PNG, which the table scores.
    private static Task<Image<Rgba64>> DecodeAsync(JxlEncoderRoundTrip.Reader reader, JxlCalibrationRow row, byte[] encoded)
    {
        string name = $"{row.Image.Name}_e{row.Effort}_d{row.Distance.ToString(CultureInfo.InvariantCulture)}";
        return JxlEncoderRoundTrip.DecodeAsync<Rgba64>(reader, encoded, "LossyCalibration", name, highBitDepth: true, [.. JxlCalibrationTable.Instance.DjxlArguments]);
    }

    // Encodes the 8-bit original converted to the pixel type at the effort.
    private sealed record EncodeWidened(Image<Rgba32> Original, float Distance, JxlEffort Effort) : IJxlPixelTypeCall<byte[]>
    {
        public byte[] Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> image = this.Original.CloneAs<TPixel>();
            return JxlEncoderRoundTrip.Encode(image, new JxlEncoder { Distance = this.Distance, Effort = this.Effort });
        }
    }

    /// <summary>One row of the table and our encode of it, as measured.</summary>
    /// <param name="Image">The calibration image.</param>
    /// <param name="Distance">The distance.</param>
    /// <param name="Score">The managed SSIMULACRA2 of our encode through our decoder.</param>
    /// <param name="Size">The size of our encode in bytes.</param>
    private sealed record MeasuredRow(string Image, double Distance, double Score, long Size);
}
