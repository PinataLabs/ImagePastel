// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Metadata.Profiles.Icc;

[Trait("Profile", "Icc")]
public class IccWriterTests
{
    [Fact]
    public void WriteProfile_NoEntries()
    {
        IccWriter writer = this.CreateWriter();

        var profile = new IccProfile
        {
            Header = IccTestDataProfiles.Header_Random_Write
        };
        byte[] output = writer.Write(profile);

        Assert.Equal(IccTestDataProfiles.Header_Random_Array, output);
    }

    [Fact]
    public void WriteProfile_DuplicateEntry()
    {
        IccWriter writer = this.CreateWriter();

        byte[] output = writer.Write(IccTestDataProfiles.Profile_Random_Val);

        Assert.Equal(IccTestDataProfiles.Profile_Random_Array, output);
    }

    [Fact]
    public void WriteProfile_ReadProfileWithSharedTagData_KeepsEveryTagAndSharesItsData()
    {
        IccWriter writer = this.CreateWriter();
        IccProfile profile = new IccReader().Read(IccTestDataProfiles.Profile_SharedTrc_Array);

        byte[] output = writer.Write(profile);

        AssertSameTagTableAndData(IccTestDataProfiles.Profile_SharedTrc_Array, output);
    }

    [Fact]
    public void WriteProfile_EqualTagDataInSeparateEntries_WritesTheDataOnce()
    {
        IccWriter writer = this.CreateWriter();
        IccProfile profile = new(
            IccTestDataProfiles.Header_Random_Write,
            [
                new IccCurveTagDataEntry(IccTestDataProfiles.Profile_SharedTrc_Gamma, IccProfileTag.RedTrc),
                new IccCurveTagDataEntry(IccTestDataProfiles.Profile_SharedTrc_Gamma, IccProfileTag.GreenTrc),
                new IccCurveTagDataEntry(IccTestDataProfiles.Profile_SharedTrc_Gamma, IccProfileTag.BlueTrc),
            ]);

        byte[] output = writer.Write(profile);

        AssertSameTagTableAndData(IccTestDataProfiles.Profile_SharedTrc_Array, output);
    }

    private static void AssertSameTagTableAndData(byte[] expected, byte[] actual)
    {
        // The header differs in its date, ID and so on; compare from the tag table on.
        int start = IccTestDataProfiles.TagTableOffset;
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected.AsSpan(start).ToArray(), actual.AsSpan(start).ToArray());
    }

    private IccWriter CreateWriter()
    {
        return new IccWriter();
    }
}
