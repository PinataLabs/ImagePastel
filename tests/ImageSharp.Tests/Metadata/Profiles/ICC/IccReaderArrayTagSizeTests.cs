// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Metadata.Profiles.Icc;

/// <summary>
/// Tag sizes from crafted bytes. Every ICC tag starts with an 8-byte header: a 4-byte type signature
/// and 4 reserved bytes (ICC.1:2022, clause 10.1). The minimum sizes and element sizes below come from
/// the type layouts in ICC.1:2022 clause 10 (v2 types: ICC.1:2001-04, clause 6.5).
/// </summary>
[Trait("Profile", "Icc")]
public class IccReaderArrayTagSizeTests
{
    private const int TagHeaderSize = 8;

    public static TheoryData<uint, uint> TagSizeBelowMinimum => new()
    {
        // Array types and XYZType: anything shorter than the header.
        { (uint)IccTypeSignature.S15Fixed16Array, 0 },
        { (uint)IccTypeSignature.S15Fixed16Array, 7 },
        { (uint)IccTypeSignature.U16Fixed16Array, 0 },
        { (uint)IccTypeSignature.U16Fixed16Array, 7 },
        { (uint)IccTypeSignature.UInt16Array, 0 },
        { (uint)IccTypeSignature.UInt16Array, 7 },
        { (uint)IccTypeSignature.UInt32Array, 0 },
        { (uint)IccTypeSignature.UInt32Array, 7 },
        { (uint)IccTypeSignature.UInt64Array, 0 },
        { (uint)IccTypeSignature.UInt64Array, 7 },
        { (uint)IccTypeSignature.UInt8Array, 0 },
        { (uint)IccTypeSignature.UInt8Array, 7 },
        { (uint)IccTypeSignature.Xyz, 0 },
        { (uint)IccTypeSignature.Xyz, 7 },

        // Byte-run types sized by the tag size.
        { (uint)IccTypeSignature.Text, 0 },
        { (uint)IccTypeSignature.Text, 7 },
        { (uint)IccTypeSignature.DeviceSettings, 0 },
        { (uint)IccTypeSignature.DeviceSettings, 7 },

        // dataType has a 4-byte flag after the header.
        { (uint)IccTypeSignature.Data, 7 },
        { (uint)IccTypeSignature.Data, 11 },

        // ucrbgType has two 4-byte counts after the header.
        { (uint)IccTypeSignature.UcrBg, 7 },
        { (uint)IccTypeSignature.UcrBg, 15 },

        // cicpType has four 1-byte code points after the header.
        { (uint)IccTypeSignature.Cicp, 8 },
        { (uint)IccTypeSignature.Cicp, 11 },
    };

    public static TheoryData<uint> SizedTypes => new()
    {
        (uint)IccTypeSignature.S15Fixed16Array,
        (uint)IccTypeSignature.U16Fixed16Array,
        (uint)IccTypeSignature.UInt16Array,
        (uint)IccTypeSignature.UInt32Array,
        (uint)IccTypeSignature.UInt64Array,
        (uint)IccTypeSignature.UInt8Array,
        (uint)IccTypeSignature.Xyz,
        (uint)IccTypeSignature.Text,
        (uint)IccTypeSignature.DeviceSettings,
        (uint)IccTypeSignature.Data,
        (uint)IccTypeSignature.Cicp,
    };

    [Theory]
    [MemberData(nameof(TagSizeBelowMinimum))]
    public void ReadTagDataEntry_SizeBelowTypeMinimum_ThrowsInvalidIccProfileException(uint type, uint size)
    {
        // Plenty of bytes follow the header, so only the declared size is wrong.
        byte[] data = CreateTag(type, new byte[64]);
        IccDataReader reader = new(data);

        Assert.Throws<InvalidIccProfileException>(
            () => reader.ReadTagDataEntry(new IccTagTableEntry(IccProfileTag.Unknown, 0, size)));
    }

    [Theory]
    [MemberData(nameof(SizedTypes))]
    public void ReadTagDataEntry_SizePastEndOfData_ThrowsInvalidIccProfileException(uint type)
    {
        // The size claims 64 content bytes; the data holds only 4.
        byte[] data = CreateTag(type, new byte[4]);
        IccDataReader reader = new(data);

        Assert.Throws<InvalidIccProfileException>(
            () => reader.ReadTagDataEntry(new IccTagTableEntry(IccProfileTag.Unknown, 0, TagHeaderSize + 64)));
    }

    [Fact]
    public void ReadTagDataEntry_UcrBgCountsPastTagSize_ThrowsInvalidIccProfileException()
    {
        // ucrCount = 0x10000 claims 128 KiB of curve data in a 16-byte tag.
        byte[] content = new byte[64];
        BinaryPrimitives.WriteUInt32BigEndian(content, 0x10000);
        byte[] data = CreateTag((uint)IccTypeSignature.UcrBg, content);
        IccDataReader reader = new(data);

        Assert.Throws<InvalidIccProfileException>(
            () => reader.ReadTagDataEntry(new IccTagTableEntry(IccProfileTag.Unknown, 0, TagHeaderSize + 8)));
    }

    [Fact]
    public void ReadTagDataEntry_ArrayTagOfHeaderSize_ReadsEmptyArray()
    {
        byte[] data = CreateTag((uint)IccTypeSignature.UInt32Array, Array.Empty<byte>());
        IccDataReader reader = new(data);

        IccTagDataEntry entry = reader.ReadTagDataEntry(new IccTagTableEntry(IccProfileTag.Unknown, 0, TagHeaderSize));

        IccUInt32ArrayTagDataEntry array = Assert.IsType<IccUInt32ArrayTagDataEntry>(entry);
        Assert.Empty(array.Data);
    }

    [Fact]
    public void Read_ProfileWithArrayTagPastEndOfData_DropsThatTagAndKeepsTheOthers()
    {
        // Tag 1: wtpt, a well-formed XYZType holding D50 (0.9642, 1.0, 0.8249 as s15Fixed16).
        byte[] xyz = new byte[12];
        BinaryPrimitives.WriteInt32BigEndian(xyz.AsSpan(0), 0x0000F6D6);
        BinaryPrimitives.WriteInt32BigEndian(xyz.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteInt32BigEndian(xyz.AsSpan(8), 0x0000D32D);
        byte[] wtpt = CreateTag((uint)IccTypeSignature.Xyz, xyz);

        // Tag 2: a uint32Array whose size claims 8 elements, with only one present.
        byte[] array = CreateTag((uint)IccTypeSignature.UInt32Array, new byte[4]);

        const int tagTableSize = 4 + (2 * 12);
        int wtptOffset = 128 + tagTableSize;
        int arrayOffset = wtptOffset + wtpt.Length;
        byte[] profile = new byte[arrayOffset + array.Length];
        BinaryPrimitives.WriteUInt32BigEndian(profile, (uint)profile.Length);

        Span<byte> table = profile.AsSpan(128);
        BinaryPrimitives.WriteUInt32BigEndian(table, 2);
        WriteTagTableEntry(table[4..], (uint)IccProfileTag.MediaWhitePoint, wtptOffset, wtpt.Length);
        WriteTagTableEntry(table[16..], (uint)IccProfileTag.Unknown, arrayOffset, TagHeaderSize + (8 * 4));
        wtpt.CopyTo(profile, wtptOffset);
        array.CopyTo(profile, arrayOffset);

        IccProfile output = new IccReader().Read(profile);

        IccTagDataEntry entry = Assert.Single(output.Entries);
        Assert.Equal(IccProfileTag.MediaWhitePoint, entry.TagSignature);
        Assert.IsType<IccXyzTagDataEntry>(entry);
    }

    private static byte[] CreateTag(uint type, byte[] content)
    {
        byte[] tag = new byte[TagHeaderSize + content.Length];
        BinaryPrimitives.WriteUInt32BigEndian(tag, type);
        content.CopyTo(tag, TagHeaderSize);
        return tag;
    }

    private static void WriteTagTableEntry(Span<byte> destination, uint signature, int offset, int size)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, signature);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], (uint)offset);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..], (uint)size);
    }
}
