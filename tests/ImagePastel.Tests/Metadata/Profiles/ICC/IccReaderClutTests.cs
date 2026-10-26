// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Metadata.Profiles.Icc;

/// <summary>
/// CLUTs whose declared dimensions do not match the data, read through <see cref="IccProfile.Entries"/>.
/// The profiles hold one AToB0 tag of multiProcessElementsType with one CLUT element. The layouts are those
/// this library's reader and writer use: the element's signature, its 16-bit input and output channel counts,
/// a 16-byte grid-point field and then the 32-bit float values.
/// </summary>
[Trait("Profile", "Icc")]
public class IccReaderClutTests
{
    private const int HeaderSize = 128;
    private const int TagTableSize = 4 + 12;

    [Fact]
    public void Entries_ClutLargerThanItsData_DropsTheTagWithoutAllocatingForIt()
    {
        // 15 inputs and 15 outputs with 3 grid points each declare 3^15 * 15 floats, about 860 MB, and the
        // profile ends after the grid-point field (GHSA-gwg2-r3hj-4w44).
        byte[] gridPoints = new byte[16];
        gridPoints.AsSpan(0, 15).Fill(3);
        byte[] profile = CreateProfile(15, 15, gridPoints, Array.Empty<byte>());

        long before = GC.GetAllocatedBytesForCurrentThread();
        IccProfile output = new(profile);
        int entryCount = output.Entries.Length;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, entryCount);
        Assert.InRange(allocated, 0, 1024 * 1024);
    }

    [Fact]
    public void Entries_ClutWithMoreThanSixteenInputs_DropsTheTag()
    {
        // The grid-point field holds 16 counts, one for each input, so a 17th input has no count of its own and
        // would take the first byte of the values as one. With every count 1, one grid point of one float fits
        // the data, so only the channel count is wrong.
        byte[] gridPoints = new byte[16];
        gridPoints.AsSpan().Fill(1);
        byte[] values = { 1, 0, 0, 0 };
        byte[] profile = CreateProfile(17, 1, gridPoints, values);

        IccProfile output = new(profile);

        Assert.Empty(output.Entries);
    }

    [Fact]
    public void Entries_ClutThatFitsItsData_ReadsIt()
    {
        byte[] gridPoints = new byte[16];
        gridPoints.AsSpan(0, 2).Fill(2);
        byte[] values = new byte[2 * 2 * 3 * 4];
        byte[] profile = CreateProfile(2, 3, gridPoints, values);

        IccProfile output = new(profile);

        IccTagDataEntry entry = Assert.Single(output.Entries);
        IccMultiProcessElementsTagDataEntry mpet = Assert.IsType<IccMultiProcessElementsTagDataEntry>(entry);
        IccClutProcessElement clut = Assert.IsType<IccClutProcessElement>(Assert.Single(mpet.Data));
        Assert.Equal(4, clut.ClutValue.Values.Length);
    }

    private static byte[] CreateProfile(ushort inChannels, ushort outChannels, byte[] gridPoints, byte[] values)
    {
        // The tag: its type header, the channel counts, one position-table entry, then the element.
        const int elementOffset = 8 + 4 + 4 + 8;
        int elementSize = 4 + 2 + 2 + gridPoints.Length + values.Length;
        byte[] tag = new byte[elementOffset + elementSize];
        Span<byte> span = tag;
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)IccTypeSignature.MultiProcessElements);
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], inChannels);
        BinaryPrimitives.WriteUInt16BigEndian(span[10..], outChannels);
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], 1);
        BinaryPrimitives.WriteUInt32BigEndian(span[16..], elementOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span[20..], (uint)elementSize);

        Span<byte> element = span[elementOffset..];
        BinaryPrimitives.WriteUInt32BigEndian(element, (uint)IccMultiProcessElementSignature.Clut);
        BinaryPrimitives.WriteUInt16BigEndian(element[4..], inChannels);
        BinaryPrimitives.WriteUInt16BigEndian(element[6..], outChannels);
        gridPoints.CopyTo(element[8..]);
        values.CopyTo(element[(8 + gridPoints.Length)..]);

        const int tagOffset = HeaderSize + TagTableSize;
        byte[] profile = new byte[tagOffset + tag.Length];
        BinaryPrimitives.WriteUInt32BigEndian(profile, (uint)profile.Length);
        Span<byte> table = profile.AsSpan(HeaderSize);
        BinaryPrimitives.WriteUInt32BigEndian(table, 1);
        BinaryPrimitives.WriteUInt32BigEndian(table[4..], (uint)IccProfileTag.AToB0);
        BinaryPrimitives.WriteUInt32BigEndian(table[8..], tagOffset);
        BinaryPrimitives.WriteUInt32BigEndian(table[12..], (uint)tag.Length);
        tag.CopyTo(profile, tagOffset);
        return profile;
    }
}
