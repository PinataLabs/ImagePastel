// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Text;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Metadata.Profiles.Icc;

[Trait("Profile", "Icc")]
public class IccProfileTests
{
    [Theory]
    [MemberData(nameof(IccTestDataProfiles.ProfileIdTestData), MemberType = typeof(IccTestDataProfiles))]
    public void CalculateHash_WithByteArray_CalculatesProfileHash(byte[] data, IccProfileId expected)
    {
        IccProfileId result = IccProfile.CalculateHash(data);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalculateHash_WithByteArray_DoesNotModifyData()
    {
        byte[] data = IccTestDataProfiles.Profile_Random_Array;
        var copy = new byte[data.Length];
        Buffer.BlockCopy(data, 0, copy, 0, data.Length);

        IccProfile.CalculateHash(data);

        Assert.Equal(data, copy);
    }

    [Theory]
    [MemberData(nameof(IccTestDataProfiles.ProfileValidityTestData), MemberType = typeof(IccTestDataProfiles))]
    public void CheckIsValid_WithProfiles_ReturnsValidity(byte[] data, bool expected)
    {
        var profile = new IccProfile(data);

        bool result = profile.CheckIsValid();

        Assert.Equal(expected, result);
    }

    public static TheoryData<string, uint> MalformedTagSizes()
    {
        var data = new TheoryData<string, uint>();
        foreach (string type in new[] { "sf32", "uf32", "ui08", "ui16", "ui32", "ui64", "XYZ ", "text", "data", "zzzz" })
        {
            for (uint size = 0; size < 8; size++)
            {
                data.Add(type, size);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MalformedTagSizes))]
    public void Entries_WithTagSmallerThanHeader_SkipsTagWithoutLargeAllocations(string type, uint size)
    {
        byte[] data = CreateSingleTagProfile(type, size);

        long before = GC.GetAllocatedBytesForCurrentThread();
        IccTagDataEntry[] entries = new IccProfile(data).Entries;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Empty(entries);
        Assert.True(allocated < 100 * data.Length, $"Allocated {allocated} bytes reading a {data.Length} byte profile.");
    }

    [Fact]
    public void Entries_WithValidArrayTag_ReadsTag()
    {
        byte[] data = CreateSingleTagProfile("ui32", 16);

        IccTagDataEntry entry = Assert.Single(new IccProfile(data).Entries);

        var array = Assert.IsType<IccUInt32ArrayTagDataEntry>(entry);
        Assert.Equal(new uint[] { 0x00010000, 0xFFFE0000 }, array.Data);
    }

    [Fact]
    public void Entries_WithFix16ArrayTag_ReadsRealValues()
    {
        byte[] data = CreateSingleTagProfile("sf32", 16);

        IccTagDataEntry entry = Assert.Single(new IccProfile(data).Entries);

        var array = Assert.IsType<IccFix16ArrayTagDataEntry>(entry);
        Assert.Equal(new float[] { 1, -2 }, array.Data);
    }

    [Fact]
    public void ToByteArray_WithFix16ArrayTag_WritesRealValues()
    {
        var entry = new IccFix16ArrayTagDataEntry(new float[] { 1, -2 }, IccProfileTag.ChromaticAdaptation);
        var profile = new IccProfile(new IccProfileHeader(), new IccTagDataEntry[] { entry });

        byte[] output = profile.ToByteArray();

        byte[] expected = { 0x73, 0x66, 0x33, 0x32, 0, 0, 0, 0, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xFE, 0x00, 0x00 };
        Assert.Equal(expected, output.AsSpan(output.Length - 16).ToArray());
    }

    // A 160 byte profile with one tag at offset 144: the type signature, 4 reserved bytes,
    // and the 32-bit values 0x00010000 and 0xFFFE0000.
    private static byte[] CreateSingleTagProfile(string type, uint size)
    {
        byte[] data = new byte[160];
        BinaryPrimitives.WriteUInt32BigEndian(data, 160);
        Encoding.ASCII.GetBytes("RGB XYZ ").CopyTo(data, 16);
        Encoding.ASCII.GetBytes("acsp").CopyTo(data, 36);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(128), 1);
        Encoding.ASCII.GetBytes("wtpt").CopyTo(data, 132);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(136), 144);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(140), size);
        Encoding.ASCII.GetBytes(type).CopyTo(data, 144);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(152), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(156), 0xFFFE0000);
        return data;
    }
}
