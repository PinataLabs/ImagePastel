// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using PinataLabs.ImagePastel.Metadata.Profiles.Icc;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Metadata.Profiles.Icc;

/// <summary>
/// Element counts read from a tag's own bytes, from crafted bytes. A count that the rest of the profile
/// data cannot hold must fail before the reader allocates an array of that size. The field layouts
/// come from ICC.1:2022 clause 10 (v2 types: ICC.1:2001-04, clause 6.5); every tag starts with an
/// 8-byte header (type signature and 4 reserved bytes).
/// </summary>
[Trait("Profile", "Icc")]
public class IccReaderElementCountTests
{
    private const int TagHeaderSize = 8;

    // 16 M elements: tens to hundreds of megabytes for most element types, in a tag of under 140 bytes.
    private const uint HugeCount = 0x0100_0000;

    // (field, type, offset of the count after the tag header, count width in bytes)
    public static TheoryData<string, uint, int, int> CountFields => new()
    {
        { "curv point count", (uint)IccTypeSignature.Curve, 0, 4 },
        { "clro colorant count", (uint)IccTypeSignature.ColorantOrder, 0, 4 },
        { "clrt colorant count", (uint)IccTypeSignature.ColorantTable, 0, 4 },
        { "mluc record count", (uint)IccTypeSignature.MultiLocalizedUnicode, 0, 4 },
        { "mpet element count", (uint)IccTypeSignature.MultiProcessElements, 4, 4 },
        { "ncl2 color count", (uint)IccTypeSignature.NamedColor2, 4, 4 },
        { "ncl2 device coordinate count", (uint)IccTypeSignature.NamedColor2, 8, 4 },
        { "pseq description count", (uint)IccTypeSignature.ProfileSequenceDesc, 0, 4 },
        { "psid identifier count", (uint)IccTypeSignature.ProfileSequenceIdentifier, 0, 4 },
        { "rcs2 measurement count", (uint)IccTypeSignature.ResponseCurveSet16, 2, 2 },
        { "scrn channel count", (uint)IccTypeSignature.Screening, 4, 4 },
        { "desc ASCII count", (uint)IccTypeSignature.TextDescription, 0, 4 },
        { "chrm channel count", (uint)IccTypeSignature.Chromaticity, 0, 2 },
    };

    [Theory]
    [MemberData(nameof(CountFields))]
    public void ReadTagDataEntry_CountPastEndOfData_ThrowsInvalidIccProfileException(string field, uint type, int offset, int width)
    {
        // 128 zero bytes after the header (enough for every fixed field, such as ncl2's 64-byte prefix
        // and suffix), then the count set as large as its field allows (up to HugeCount).
        byte[] content = new byte[128];
        if (width == 2)
        {
            BinaryPrimitives.WriteUInt16BigEndian(content.AsSpan(offset), ushort.MaxValue);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(offset), HugeCount);
        }

        // ncl2 with a huge coordinate count needs one color to read it.
        if (field == "ncl2 device coordinate count")
        {
            BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(4), 1);
        }

        byte[] data = CreateTag(type, content);
        IccDataReader reader = new(data);

        InvalidIccProfileException ex = Assert.Throws<InvalidIccProfileException>(
            () => reader.ReadTagDataEntry(new IccTagTableEntry(IccProfileTag.Unknown, 0, (uint)data.Length)));
        Assert.Contains("remain", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadTagDataEntry_ResponseCurveMeasurementCountPastEndOfData_ThrowsInvalidIccProfileException()
    {
        // rcs2 with one channel and one curve at offset 16 from the tag start; that curve claims
        // HugeCount measurements for its channel.
        byte[] content = new byte[64];
        BinaryPrimitives.WriteUInt16BigEndian(content.AsSpan(0), 1);
        BinaryPrimitives.WriteUInt16BigEndian(content.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(4), TagHeaderSize + 8);
        BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(12), HugeCount);
        byte[] data = CreateTag((uint)IccTypeSignature.ResponseCurveSet16, content);
        IccDataReader reader = new(data);

        InvalidIccProfileException ex = Assert.Throws<InvalidIccProfileException>(
            () => reader.ReadTagDataEntry(new IccTagTableEntry(IccProfileTag.Unknown, 0, (uint)data.Length)));
        Assert.Contains("remain", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadTagDataEntry_CurveWithCountThatFits_ReadsIt()
    {
        // curv with 3 points: 0, 0x8000, 0xFFFF.
        byte[] content = new byte[10];
        BinaryPrimitives.WriteUInt32BigEndian(content, 3);
        BinaryPrimitives.WriteUInt16BigEndian(content.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt16BigEndian(content.AsSpan(6), 0x8000);
        BinaryPrimitives.WriteUInt16BigEndian(content.AsSpan(8), 0xFFFF);
        byte[] data = CreateTag((uint)IccTypeSignature.Curve, content);
        IccDataReader reader = new(data);

        IccTagDataEntry entry = reader.ReadTagDataEntry(new IccTagTableEntry(IccProfileTag.Unknown, 0, (uint)data.Length));

        IccCurveTagDataEntry curve = Assert.IsType<IccCurveTagDataEntry>(entry);
        Assert.Equal(new[] { 0F, 0x8000 / 65535F, 1F }, curve.CurveData);
    }

    private static byte[] CreateTag(uint type, byte[] content)
    {
        byte[] tag = new byte[TagHeaderSize + content.Length];
        BinaryPrimitives.WriteUInt32BigEndian(tag, type);
        content.CopyTo(tag, TagHeaderSize);
        return tag;
    }
}
