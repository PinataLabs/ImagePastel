// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Processing;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Quality;

[Trait("Format", "Jxl")]
public class JxlQualityComparerTests
{
    // gradient e3 d1: 2625 bytes, score 92.98022117. Small, so these tests stay fast.
    private static JxlCalibrationRow Row => JxlCalibrationTable.Instance.Find("gradient", JxlEffort.Fastest, 1);

    [Fact]
    public void Rule_IsHalfAPointAndTheSlackBelowCjxl_AndTwiceItsSize()
    {
        using Image<Rgba32> original = LoadOriginal(Row);

        JxlQualityResult result = JxlQualityComparer.Compare(Row, original, original, Row.Size);

        // #107: the score at least cjxl's - 0.5 - the slack (3 to start with), the size at most 2x cjxl's.
        Assert.Equal(92.98022117 - 0.5 - 3, result.MinimumScore, 1e-9);
        Assert.Equal(2 * 2625, result.MaximumSize);
    }

    [Fact]
    public void IdenticalImage_AtCjxlsSize_Passes()
    {
        using Image<Rgba32> original = LoadOriginal(Row);

        JxlQualityResult result = JxlQualityComparer.Compare(Row, original, original, Row.Size);

        Assert.True(result.Passed, result.ToString());
        Assert.Equal(100, result.Score);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void TwiceCjxlsSize_Passes_OneByteMoreFails()
    {
        using Image<Rgba32> original = LoadOriginal(Row);

        Assert.True(JxlQualityComparer.Compare(Row, original, original, 2 * Row.Size).Passed);

        JxlQualityResult result = JxlQualityComparer.Compare(Row, original, original, (2 * Row.Size) + 1);
        Assert.False(result.Passed);
        Assert.Contains("bytes", Assert.Single(result.Failures));
    }

    [Fact]
    public void DegradedImage_Fails()
    {
        using Image<Rgba32> original = LoadOriginal(Row);
        using Image<Rgba32> degraded = original.Clone(x => x.GaussianBlur(1.5F));

        JxlQualityResult result = JxlQualityComparer.Compare(Row, original, degraded, Row.Size);

        Assert.False(result.Passed);
        Assert.True(result.Score < result.MinimumScore);
        Assert.Contains("SSIMULACRA2", Assert.Single(result.Failures));
    }

    [Fact]
    public void ChangedAlpha_Fails()
    {
        JxlCalibrationRow row = JxlCalibrationTable.Instance.Find("bike_alpha", JxlEffort.Fastest, 1);
        using Image<Rgba32> original = LoadOriginal(row);
        using Image<Rgba32> decoded = original.Clone();
        Rgba32 pixel = decoded[100, 80];
        pixel.A = (byte)(pixel.A ^ 1);
        decoded[100, 80] = pixel;

        JxlQualityResult result = JxlQualityComparer.Compare(row, original, decoded, row.Size);

        Assert.False(result.Passed);
        Assert.Contains("alpha", Assert.Single(result.Failures));
    }

    [Fact]
    public void Slack_CanBeGivenPerTest()
    {
        using Image<Rgba32> original = LoadOriginal(Row);

        JxlQualityResult result = JxlQualityComparer.Compare(Row, original, original, Row.Size, slack: 1);

        Assert.Equal(92.98022117 - 0.5 - 1, result.MinimumScore, 1e-9);
    }

    [Fact]
    public void Verify_FailsWithTheReasons()
    {
        using Image<Rgba32> original = LoadOriginal(Row);

        Xunit.Sdk.XunitException exception = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => JxlQualityComparer.Verify(Row, original, original, 3 * Row.Size));

        Assert.Contains("gradient e3 d1", exception.Message);
        Assert.Contains("bytes", exception.Message);
    }

    private static Image<Rgba32> LoadOriginal(JxlCalibrationRow row) => Image.Load<Rgba32>(row.Image.FullPath);
}
