// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Linq;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Quality;

[Trait("Format", "Jxl")]
public class JxlCalibrationTableTests
{
    [Fact]
    public void Table_HasEveryImageAtEveryEffortAndDistance()
    {
        JxlCalibrationTable table = JxlCalibrationTable.Instance;

        Assert.Equal(6, table.Images.Count);
        Assert.Equal(54, table.Rows.Count);
        foreach (JxlCalibrationImage image in table.Images)
        {
            Assert.True(File.Exists(image.FullPath), image.FullPath);
            foreach (int effort in new[] { 3, 5, 7 })
            {
                foreach (int distance in new[] { 1, 2, 4 })
                {
                    JxlCalibrationRow row = table.Find(image.Name, effort, distance);
                    Assert.Same(image, row.Image);
                    Assert.InRange(row.Size, 1, 1 << 20);
                    Assert.InRange(row.Score, 0, 100);
                }
            }
        }
    }

    [Fact]
    public void Find_ReturnsTheRecordedRow()
    {
        // The first row of calibration.json.
        JxlCalibrationRow row = JxlCalibrationTable.Instance.Find("bike", 3, 1);

        Assert.Equal(48399, row.Size);
        Assert.Equal(88.24250327, row.Score);
        Assert.Equal("Png/Bike.png", row.Image.Path);
    }

    [Fact]
    public void CjxlArguments_SubstituteDistanceAndEffort()
    {
        JxlCalibrationRow row = JxlCalibrationTable.Instance.Find("text", 7, 2);

        Assert.Equal(new[] { "-d", "2", "-e", "7" }, JxlCalibrationTable.Instance.CjxlArguments(row).ToArray());
        Assert.Equal(new[] { "--bits_per_sample=16" }, JxlCalibrationTable.Instance.DjxlArguments);
    }
}
