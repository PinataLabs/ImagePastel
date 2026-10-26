// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Quality;

/// <summary>
/// The <c>jxl-oracle</c> checks of the calibration table and the managed SSIMULACRA2: each row's cjxl
/// and djxl commands are run again, and the output is scored by the <c>ssimulacra2</c> binary and by
/// <see cref="Ssimulacra2"/>. They skip when <c>JXL_TOOLS_DIR</c> is unset, and fail when it is set but a
/// tool is missing.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlCalibrationOracleTests
{
    /// <summary>The largest difference allowed between the managed score and the binary's (#107).</summary>
    private const double ManagedTolerance = 0.5;

    /// <summary>
    /// How far a regenerated score may be from the table's. The table was made with the Windows x64 tools
    /// (Clang 22). The Linux x64 tools (Clang 18) round differently: on the same AVX2 machine their scores
    /// were up to 0.074 away.
    /// </summary>
    private const double ScoreReproduction = 0.15;

    /// <summary>
    /// How far a regenerated size may be from the table's, as a fraction. The Linux x64 cjxl made files up
    /// to 11 bytes (0.05 %) away from the Windows one at efforts 5 and 7.
    /// </summary>
    private const double SizeReproduction = 0.0025;

    private static readonly ConcurrentDictionary<JxlCalibrationRow, Lazy<Task<CjxlOutput>>> Outputs = new();

    public static TheoryData<string, int, double> Rows
    {
        get
        {
            TheoryData<string, int, double> data = new();
            foreach (JxlCalibrationRow row in JxlCalibrationTable.Instance.Rows)
            {
                data.Add(row.Image.Name, row.Effort, row.Distance);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task ManagedScore_IsWithinHalfOfTheBinary(string image, int effort, double distance)
    {
        JxlCalibrationRow row = JxlCalibrationTable.Instance.Find(image, effort, distance);
        CjxlOutput output = await EncodeAsync(row, TestContext.Current.CancellationToken);

        // The way the lossy tests score: our decoder's output of the codestream. And the binary's own
        // input, djxl's PNG, which checks the port on its own.
        using Image<Rgba32> original = Image.Load<Rgba32>(row.Image.FullPath);
        using Image<RgbaVector> decoded = Image.Load<RgbaVector>(JxlEncoderRoundTrip.CreateConfiguration(), output.JxlPath);
        using Image<Rgba64> djxl = Image.Load<Rgba64>(output.PngPath);
        double managed = Ssimulacra2.Compute(original, decoded);
        double managedPng = Ssimulacra2.Compute(original, djxl);

        TestContext.Current.TestOutputHelper?.WriteLine($"{row}: binary {output.Score:F8}; managed on our decode {managed:F8} ({managed - output.Score:+0.00000000;-0.00000000}), on djxl's PNG {managedPng:F8} ({managedPng - output.Score:+0.00000000;-0.00000000})");
        Assert.True(Math.Abs(managed - output.Score) <= ManagedTolerance, $"{row}: the managed score {managed:F8} of our decode is not within {ManagedTolerance} of the binary's {output.Score:F8}.");
        Assert.True(Math.Abs(managedPng - output.Score) <= ManagedTolerance, $"{row}: the managed score {managedPng:F8} of djxl's PNG is not within {ManagedTolerance} of the binary's {output.Score:F8}.");
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Table_IsReproduced(string image, int effort, double distance)
    {
        JxlCalibrationRow row = JxlCalibrationTable.Instance.Find(image, effort, distance);
        CjxlOutput output = await EncodeAsync(row, TestContext.Current.CancellationToken);

        TestContext.Current.TestOutputHelper?.WriteLine($"{row}: size {output.Size} (table {row.Size}), score {output.Score:F8} (table {row.Score:F8})");
        Assert.True(Math.Abs(output.Size - row.Size) <= SizeReproduction * row.Size, $"{row}: cjxl wrote {output.Size} bytes; the table has {row.Size}.");
        Assert.True(Math.Abs(output.Score - row.Score) <= ScoreReproduction, $"{row}: ssimulacra2 gave {output.Score:F8}; the table has {row.Score:F8}.");
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Comparer_PassesCjxlsOwnOutput(string image, int effort, double distance)
    {
        JxlCalibrationRow row = JxlCalibrationTable.Instance.Find(image, effort, distance);
        CjxlOutput output = await EncodeAsync(row, TestContext.Current.CancellationToken);

        using Image<Rgba32> original = Image.Load<Rgba32>(row.Image.FullPath);
        using Image<RgbaVector> decoded = Image.Load<RgbaVector>(JxlEncoderRoundTrip.CreateConfiguration(), output.JxlPath);

        JxlQualityResult result = JxlQualityComparer.Verify(row, original, decoded, output.Size);
        TestContext.Current.TestOutputHelper?.WriteLine(result.ToString());
    }

    /// <summary>
    /// Deliberately degraded encodes at effort 3: cjxl at distance 4 fails the quality bar of the distance 1
    /// row, and cjxl at distance 0.25 fails the size bar of the distance 4 row.
    /// </summary>
    /// <param name="image">The calibration image.</param>
    [Theory]
    [InlineData("bike")]
    [InlineData("calliphora")]
    [InlineData("bike_alpha")]
    [InlineData("gradient")]
    [InlineData("text")]
    [InlineData("line_art")]
    public async Task Comparer_FailsADegradedEncode(string image)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        JxlCalibrationRow d1 = JxlCalibrationTable.Instance.Find(image, 3, 1);
        JxlCalibrationRow d4 = JxlCalibrationTable.Instance.Find(image, 3, 4);
        using Image<Rgba32> original = Image.Load<Rgba32>(d1.Image.FullPath);
        JxlQualityResult lowQuality = await CompareCjxlAsync(d1, original, "4", cancellationToken);
        JxlQualityResult tooLarge = await CompareCjxlAsync(d4, original, "0.25", cancellationToken);

        TestContext.Current.TestOutputHelper?.WriteLine(lowQuality.ToString());
        TestContext.Current.TestOutputHelper?.WriteLine(tooLarge.ToString());
        Assert.False(lowQuality.Passed, lowQuality.ToString());
        Assert.Contains("SSIMULACRA2", Assert.Single(lowQuality.Failures));
        Assert.False(tooLarge.Passed, tooLarge.ToString());
        Assert.Contains("bytes", Assert.Single(tooLarge.Failures));
    }

    // Encodes the row's image with cjxl at effort 3 and another distance, and compares it with the row.
    private static async Task<JxlQualityResult> CompareCjxlAsync(JxlCalibrationRow row, Image<Rgba32> original, string distance, CancellationToken cancellationToken)
    {
        string path = Path.Combine(TestEnvironment.CreateOutputDirectory("Jxl", "Calibration"), $"{row.Image.Name}_e3_d{distance}_against_d{row.Distance.ToString(CultureInfo.InvariantCulture)}.jxl");
        File.Delete(path);
        await JxlTools.RunAsync("cjxl", cancellationToken, row.Image.FullPath, path, "-d", distance, "-e", "3");
        using Image<RgbaVector> decoded = Image.Load<RgbaVector>(JxlEncoderRoundTrip.CreateConfiguration(), path);
        return JxlQualityComparer.Compare(row, original, decoded, new FileInfo(path).Length);
    }

    private static Task<CjxlOutput> EncodeAsync(JxlCalibrationRow row, CancellationToken cancellationToken)
    {
        // Resolve the tools first, so the test skips before it writes anything.
        JxlTools.GetToolPath("cjxl");
        JxlTools.GetToolPath("djxl");
        JxlTools.GetToolPath("ssimulacra2");
        return Outputs.GetOrAdd(row, r => new Lazy<Task<CjxlOutput>>(() => RunToolsAsync(r, cancellationToken))).Value;
    }

    private static async Task<CjxlOutput> RunToolsAsync(JxlCalibrationRow row, CancellationToken cancellationToken)
    {
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", "Calibration");
        string name = $"{row.Image.Name}_e{row.Effort}_d{row.Distance.ToString(CultureInfo.InvariantCulture)}";
        string jxlPath = Path.Combine(directory, name + ".jxl");
        string pngPath = Path.Combine(directory, name + ".png");

        // Write to fresh files: the output folder may hold longer files from an earlier run.
        File.Delete(jxlPath);
        File.Delete(pngPath);
        JxlCalibrationTable table = JxlCalibrationTable.Instance;
        await JxlTools.RunAsync("cjxl", cancellationToken, new[] { row.Image.FullPath, jxlPath }.Concat(table.CjxlArguments(row)).ToArray());
        await JxlTools.RunAsync("djxl", cancellationToken, new[] { jxlPath, pngPath }.Concat(table.DjxlArguments).ToArray());

        (int exitCode, string text) = await JxlTools.RunForExitCodeAsync("ssimulacra2", cancellationToken, row.Image.FullPath, pngPath);
        Assert.True(exitCode == 0, $"ssimulacra2 exited with code {exitCode}.{Environment.NewLine}{text}");
        double score = double.Parse(text.Split('\n')[0].Trim(), CultureInfo.InvariantCulture);
        return new CjxlOutput(jxlPath, pngPath, new FileInfo(jxlPath).Length, score);
    }

    /// <summary>One row's files, made again: cjxl's codestream and djxl's 16-bit PNG of it, scored.</summary>
    /// <param name="JxlPath">cjxl's output.</param>
    /// <param name="PngPath">djxl's 16-bit PNG of it.</param>
    /// <param name="Size">The size of cjxl's output.</param>
    /// <param name="Score">The binary's score of the PNG.</param>
    private sealed record CjxlOutput(string JxlPath, string PngPath, long Size, double Score);
}
