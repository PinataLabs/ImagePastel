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
/// E3's done-check (#270): every Fastest (libjxl effort 3) row of the calibration table, encoded by our
/// encoder at the row's distance, meets the quality bar of #107 against cjxl's row. Our decoder checks it
/// on every leg, Magick.NET on every leg but coverage, and djxl in <c>jxl-oracle</c>. The Default (effort
/// 5) rows are E4's progress meter until its done-check.
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
    /// How many of the 18 Default rows pass at <see cref="JxlQualityComparer.SlackFor"/> Default's slack,
    /// as E4a measured them through our decoder (see <see cref="Encode_DefaultCalibrationRows_PassAtLeastTheRecordedCount"/>).
    /// Each E4 ticket that changes it records its new count here.
    /// </summary>
    /// <remarks>
    /// E4a (net10.0, x64): 16 of 18. Its tracer quantises with Fastest's constant field (0.79 / d) and DCT8
    /// everywhere, plus Gaborish: it scores above cjxl on the photos, at 1.2x to 1.9x cjxl's size
    /// (calliphora d4 1.88x). It fails gradient d4 (margin -0.35) and text d4 (-2.29), where cjxl's
    /// effort 5 gains from its strategy search (E4g, E4h).
    /// E4c (net10.0, x64): 17 of 18. AdjustQuantBlockAC raises the quant of most blocks (flat X and B
    /// channels, lone high frequencies), so scores rise and files grow: gradient d4 (margin +1.29) and
    /// text d4 (+0.41) now pass, and calliphora d4 fails on size alone (2.04x cjxl, margin +36.12). E4b's
    /// global scale from 0.39 / d should bring the sizes down.
    /// E4d (net10.0, x64): 17 of 18. The fitted chroma-from-luma map lifts text d4 to a margin of +2.85;
    /// gradient d4 still fails (-0.29). The photos keep their margins (bike d1 +7.54, calliphora d4 +34.25).
    /// E4c + E4d + E4e merged: 17 of 18. Gradient d4 (+1.52) and text d4 (+3.86) pass; calliphora d4 fails
    /// on size alone (2.04x cjxl, margin +37.25), until E4b's global scale from 0.39 / d.
    /// E4b on E4c + E4d + E4e: 13 of 18. The adaptive field at libjxl's global scale of 0.39 / d brings
    /// the photos to cjxl's size (bike d1 1.00x, calliphora d1 1.00x, bike_alpha 0.99x to 1.00x), with
    /// margins of +2.4 to +4.7, and calliphora d4 passes (1.16x, +7.40). The rows where cjxl's effort 5
    /// gains most from its strategy search now fail on score, as the tracer no longer over-spends there:
    /// gradient d4 (-1.88), text d1 (-0.20), d2 (-1.06) and d4 (-2.81), and line_art d1 (-1.24). E4g and
    /// E4h port that search. (E4b alone, on E4a: 8 of 18, without AdjustQuantBlockAC.)
    /// </remarks>
    private const int DefaultRowsPassing = 13;

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

    // Each row is encoded once, whichever readers check it.
    private static readonly ConcurrentDictionary<(string Image, int Effort, double Distance), Lazy<byte[]>> Encodes = new();

    public static TheoryData<string, string, double> ReaderRows
    {
        get
        {
            TheoryData<string, string, double> data = new();
            foreach (JxlEncoderRoundTrip.Reader reader in Enum.GetValues<JxlEncoderRoundTrip.Reader>())
            {
                foreach (MeasuredRow row in FastestRows)
                {
                    data.Add(reader.ToString(), row.Image, row.Distance);
                }
            }

            return data;
        }
    }

    // The theory covers the whole table at Fastest: a row added to the table must be added here too.
    [Fact]
    public void FastestRows_AreEveryFastestRowOfTheTable()
    {
        string[] expected = JxlCalibrationTable.Instance.Rows
            .Where(r => r.Effort == JxlCalibrationTable.LibjxlEffort(JxlEffort.Fastest))
            .Select(r => $"{r.Image.Name} d{r.Distance.ToString(CultureInfo.InvariantCulture)}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] actual = FastestRows
            .Select(r => $"{r.Image} d{r.Distance.ToString(CultureInfo.InvariantCulture)}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    // #107's rule: SSIMULACRA2 >= cjxl's - 0.5 - the slack, size <= 2x cjxl's, and alpha exact (the
    // comparer checks alpha on every row; bike_alpha is the row with alpha). Each reader's decode is
    // scored: djxl's is the 16-bit PNG the table itself scores.
    [Theory]
    [MemberData(nameof(ReaderRows))]
    public async Task Encode_FastestCalibrationRow_MeetsTheQualityBar(string reader, string image, double distance)
    {
        JxlCalibrationRow row = JxlCalibrationTable.Instance.Find(image, JxlEffort.Fastest, distance);
        MeasuredRow measured = FastestRows.Single(r => r.Image == image && r.Distance == distance);
        JxlEncoderRoundTrip.Reader readerKind = Enum.Parse<JxlEncoderRoundTrip.Reader>(reader);
        JxlEncoderRoundTrip.SkipUnlessReaderRuns(readerKind);
        using Image<Rgba32> original = Image.Load<Rgba32>(row.Image.FullPath);

        byte[] encoded = Encode(row);

        using Image<Rgba64> decoded = await DecodeAsync(readerKind, row, encoded);
        JxlQualityResult result = JxlQualityComparer.Compare(row, original, decoded, encoded.Length, FastestSlack);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{reader}: {result}; score margin {result.Score - result.MinimumScore:F4} (recorded {measured.Score - result.MinimumScore:F4}), size ratio {(double)result.Size / row.Size:F4} (recorded {(double)measured.Size / row.Size:F4})"));
        Assert.True(result.Passed, result.ToString());
    }

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
    {
        Assert.SkipWhen(TestEnvironment.RunsWithCodeCoverage, "Adds no coverage: the 8-bit rows run the same encoder code.");
        JxlCalibrationRow row = JxlCalibrationTable.Instance.Find(image, JxlEffort.Fastest, distance);
        MeasuredRow measured = FastestRows.Single(r => r.Image == image && r.Distance == distance);
        using Image<Rgba32> original = Image.Load<Rgba32>(row.Image.FullPath);

        byte[] encoded = JxlPixelTypes.Call(pixelType, new EncodeWidened(original, (float)distance));

        using Image<Rgba64> decoded = Image.Load<Rgba64>(JxlEncoderRoundTrip.CreateConfiguration(), encoded);
        JxlQualityResult result = JxlQualityComparer.Compare(row, original, decoded, encoded.Length, FastestSlack);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{pixelType}: {result}; score {result.Score:F4} (8-bit {measured.Score:F4}), size {result.Size} (8-bit {measured.Size})"));
        Assert.True(result.Passed, result.ToString());
    }

    // E4's progress meter (#297): every Default (libjxl effort 5) row of the table, encoded at the row's
    // distance and decoded by our decoder, against the comparer at the Default slack. Only E4i must pass
    // them all; until then each ticket logs how many pass, and this asserts that no ticket passes fewer than
    // DefaultRowsPassing, the count recorded by the last ticket that changed it.
    [Fact]
    public void Encode_DefaultCalibrationRows_PassAtLeastTheRecordedCount()
    {
        Assert.SkipWhen(TestEnvironment.RunsWithCodeCoverage, "Adds no coverage: JxlLossyDefaultEncoderTests runs the same encoder code.");
        JxlCalibrationRow[] rows = JxlCalibrationTable.Instance.Rows
            .Where(r => r.Effort == JxlCalibrationTable.LibjxlEffort(JxlEffort.Default))
            .ToArray();

        int passed = 0;
        foreach (JxlCalibrationRow row in rows)
        {
            using Image<Rgba32> original = Image.Load<Rgba32>(row.Image.FullPath);
            byte[] encoded = Encode(row);
            using Image<Rgba64> decoded = Image.Load<Rgba64>(JxlEncoderRoundTrip.CreateConfiguration(), encoded);
            JxlQualityResult result = JxlQualityComparer.Compare(row, original, decoded, encoded.Length);
            passed += result.Passed ? 1 : 0;
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{(result.Passed ? "pass" : "FAIL")} {result}; score margin {result.Score - result.MinimumScore:F4}, size ratio {(double)result.Size / row.Size:F4}"));
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{passed} of {rows.Length} Default rows pass (recorded {DefaultRowsPassing}).");
        Assert.Equal(18, rows.Length);
        Assert.True(passed >= DefaultRowsPassing, $"{passed} of {rows.Length} Default rows pass; {DefaultRowsPassing} did before.");
    }

    // The table's images are 8-bit sRGB with no colour chunks; only bike_alpha has alpha. Each is encoded
    // as the pixel type it is stored in, as cjxl reads it: RGB without an alpha channel, or RGBA.
    private static byte[] Encode(JxlCalibrationRow row)
        => Encodes.GetOrAdd((row.Image.Name, row.Effort, row.Distance), _ => new Lazy<byte[]>(() =>
        {
            JxlEffort effort = row.Effort == JxlCalibrationTable.LibjxlEffort(JxlEffort.Default) ? JxlEffort.Default : JxlEffort.Fastest;
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

    // Encodes the 8-bit original converted to the pixel type at Fastest.
    private sealed record EncodeWidened(Image<Rgba32> Original, float Distance) : IJxlPixelTypeCall<byte[]>
    {
        public byte[] Invoke<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> image = this.Original.CloneAs<TPixel>();
            return JxlEncoderRoundTrip.Encode(image, new JxlEncoder { Distance = this.Distance, Effort = JxlEffort.Fastest });
        }
    }

    /// <summary>One Fastest row of the table and our encode of it, as measured.</summary>
    /// <param name="Image">The calibration image.</param>
    /// <param name="Distance">The distance.</param>
    /// <param name="Score">The managed SSIMULACRA2 of our encode through our decoder.</param>
    /// <param name="Size">The size of our encode in bytes.</param>
    private sealed record MeasuredRow(string Image, double Distance, double Score, long Size);
}
