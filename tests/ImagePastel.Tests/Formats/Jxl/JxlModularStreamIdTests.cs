// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Formats.Jxl.Frame;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The Modular stream ids, numbered as libjxl's <c>ModularStreamId::ID</c>: the global stream, then
/// per DC group the VarDCT DC, Modular DC and AC metadata streams, then the 17 quant tables, then the
/// Modular AC stream of each group in each pass.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlModularStreamIdTests
{
    // A 4096x2048 VarDCT frame with 256x256 groups: 512x256 blocks, so 2 DC groups and 16x8 = 128 groups.
    private static readonly JxlFrameDimensions FrameDim = new(4096, 2048, 1, 0, 0, false, 1);

    [Fact]
    public void FrameDim_HasTwoDcGroupsAndOneHundredTwentyEightGroups()
    {
        Assert.Equal(2, FrameDim.NumDcGroups);
        Assert.Equal(128, FrameDim.NumGroups);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    public void VarDctDc_FollowsTheGlobalStream(long groupId, long expected)
        => Assert.Equal(expected, JxlModularStreamId.VarDctDc(FrameDim, groupId));

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 4)]
    public void ModularDc_FollowsTheVarDctDcStreams(long groupId, long expected)
        => Assert.Equal(expected, JxlModularStreamId.ModularDc(FrameDim, groupId));

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 6)]
    public void AcMetadata_FollowsTheModularDcStreams(long groupId, long expected)
        => Assert.Equal(expected, JxlModularStreamId.AcMetadata(FrameDim, groupId));

    [Theory]
    [InlineData(0, 7)]
    [InlineData(16, 23)]
    public void QuantTable_FollowsTheAcMetadataStreams(int quantTableId, long expected)
        => Assert.Equal(expected, JxlModularStreamId.QuantTable(FrameDim, quantTableId));

    [Theory]
    [InlineData(0, 0, 24)]
    [InlineData(127, 0, 151)]
    [InlineData(5, 1, 157)]
    public void ModularAc_FollowsTheQuantTables(long groupId, long passId, long expected)
        => Assert.Equal(expected, JxlModularStreamId.ModularAc(FrameDim, groupId, passId));

    // libjxl's JXL_ENSURE(quant_table_id < kNumQuantTables).
    [Theory]
    [InlineData(-1)]
    [InlineData(17)]
    public void QuantTable_OutOfRange_Throws(int quantTableId)
        => Assert.Throws<InvalidImageContentException>(() => JxlModularStreamId.QuantTable(FrameDim, quantTableId));
}
