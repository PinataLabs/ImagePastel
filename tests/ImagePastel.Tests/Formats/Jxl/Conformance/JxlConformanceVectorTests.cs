// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Linq;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;

[Trait("Format", "Jxl")]
public class JxlConformanceVectorTests
{
    public static TheoryData<string> AllIds => new(JxlConformanceVector.AllIds);

    [Fact]
    public void EveryListedVectorHasADirectory()
    {
        string[] ids = JxlConformanceVector.AllIds.ToArray();

        Assert.Equal(27, JxlConformanceVector.Level10Ids.Count);
        Assert.Equal(24, JxlConformanceVector.Level5Ids.Count);
        Assert.All(JxlConformanceVector.Level10Ids.Concat(JxlConformanceVector.Level5Ids), id => Assert.Contains(id, ids));
    }

    [Theory]
    [MemberData(nameof(AllIds))]
    public void TestJson_HasThresholdsAndReference(string id)
    {
        JxlConformanceVector vector = JxlConformanceVector.Load(id);

        Assert.NotEmpty(vector.Frames);
        Assert.All(vector.Frames, frame =>
        {
            Assert.InRange(frame.RmsError, 0, 0.1);
            Assert.InRange(frame.PeakError, 0, 0.1);
        });

        // A reference is either downloadable by sha256 or committed (only mul_no_extra_channels).
        Assert.True(
            vector.Sha256Sums.ContainsKey("reference_image.npy")
            || File.Exists(Path.Combine(vector.DirectoryPath, "reference_image.npy")),
            $"{id} has no reference image.");
    }

    [Theory]
    [MemberData(nameof(AllIds))]
    public void InputIsCommittedUnlessFetched(string id)
    {
        JxlConformanceVector vector = JxlConformanceVector.Load(id);

        Assert.Equal(!vector.IsInputFetched, File.Exists(vector.CommittedInputPath));
    }

    [Fact]
    public void Level5Variant_UsesBaseInputAndLooserThresholds()
    {
        JxlConformanceVector level10 = JxlConformanceVector.Load("bench_oriented_brg");
        JxlConformanceVector level5 = JxlConformanceVector.Load("bench_oriented_brg_5");

        Assert.Equal("bench_oriented_brg", level5.InputId);
        Assert.Equal(level10.CommittedInputPath, level5.CommittedInputPath);
        Assert.Equal(0.02, level5.Frames[0].RmsError);
        Assert.Equal(0.06, level5.Frames[0].PeakError);
    }

    [Fact]
    public void RestrictedInputs_AreFetched()
    {
        Assert.Equal(new[] { "bike", "cafe", "progressive" }, JxlConformanceVector.Source.FetchedInputs.Keys.OrderBy(k => k));
        Assert.True(JxlConformanceVector.Load("cafe_5").IsInputFetched);
    }

    [Theory]
    [InlineData("bike")]
    [InlineData("cafe_5")]
    public void FetchedInputCachePath_IsNamedByPinnedHash(string id)
    {
        // A cached input from an earlier pin has a different name, so it can never be reused.
        JxlConformanceVector vector = JxlConformanceVector.Load(id);

        string path = JxlConformanceData.GetFetchedInputCachePath(vector);

        Assert.Equal(JxlConformanceVector.Source.FetchedInputs[vector.InputId] + ".jxl", Path.GetFileName(path));
    }

    [Fact]
    public void AnimationThresholds_ArePerFrame()
    {
        JxlConformanceVector vector = JxlConformanceVector.Load("animation_icos4d");

        Assert.Equal(48, vector.Frames.Count);
    }
}
