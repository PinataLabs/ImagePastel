// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace PinataLabs.ImagePastel.Metadata.Profiles.Icc;

/// <summary>
/// Contains methods for writing ICC profiles.
/// </summary>
internal sealed class IccWriter
{
    /// <summary>
    /// Writes the ICC profile into a byte array
    /// </summary>
    /// <param name="profile">The ICC profile to write</param>
    /// <returns>The ICC profile as a byte array</returns>
    public byte[] Write(IccProfile profile)
    {
        Guard.NotNull(profile, nameof(profile));

        using (var writer = new IccDataWriter())
        {
            IccTagTableEntry[] tagTable = this.WriteTagData(writer, profile.Entries);
            this.WriteTagTable(writer, tagTable);
            this.WriteHeader(writer, profile.Header);
            return writer.GetData();
        }
    }

    private static byte[] Encode(IccTagDataEntry entry)
    {
        using var writer = new IccDataWriter();
        writer.WriteTagDataEntry(entry);
        return writer.GetData();
    }

    private void WriteHeader(IccDataWriter writer, IccProfileHeader header)
    {
        writer.SetIndex(0);

        writer.WriteUInt32(writer.Length);
        writer.WriteAsciiString(header.CmmType, 4, false);
        writer.WriteVersionNumber(header.Version);
        writer.WriteUInt32((uint)header.Class);
        writer.WriteUInt32((uint)header.DataColorSpace);
        writer.WriteUInt32((uint)header.ProfileConnectionSpace);
        writer.WriteDateTime(header.CreationDate);
        writer.WriteAsciiString("acsp");
        writer.WriteUInt32((uint)header.PrimaryPlatformSignature);
        writer.WriteInt32((int)header.Flags);
        writer.WriteUInt32(header.DeviceManufacturer);
        writer.WriteUInt32(header.DeviceModel);
        writer.WriteInt64((long)header.DeviceAttributes);
        writer.WriteUInt32((uint)header.RenderingIntent);
        writer.WriteXyzNumber(header.PcsIlluminant);
        writer.WriteAsciiString(header.CreatorSignature, 4, false);

        IccProfileId id = IccProfile.CalculateHash(writer.GetData());
        writer.WriteProfileId(id);
    }

    private void WriteTagTable(IccDataWriter writer, IccTagTableEntry[] table)
    {
        // 128 = size of ICC header
        writer.SetIndex(128);

        writer.WriteUInt32((uint)table.Length);
        foreach (IccTagTableEntry entry in table)
        {
            writer.WriteUInt32((uint)entry.Signature);
            writer.WriteUInt32(entry.Offset);
            writer.WriteUInt32(entry.DataSize);
        }
    }

    private IccTagTableEntry[] WriteTagData(IccDataWriter writer, IccTagDataEntry[] entries)
    {
        // (Header size) + (entry count) + (nr of entries) * (size of table entry)
        uint offset = (uint)(128 + 4 + (entries.Length * 12));
        writer.SetIndex((int)offset);

        // Tags whose data is equal share one copy of it, as the ICC specification allows.
        // Compare the encoded bytes, so that equal data in separate entries is written once too.
        var written = new List<(byte[] Data, uint Offset)>(entries.Length);
        var table = new IccTagTableEntry[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            IccTagDataEntry entry = entries[i];
            byte[] data = Encode(entry);

            int index = written.FindIndex(w => w.Data.AsSpan().SequenceEqual(data));
            uint dataOffset;
            if (index >= 0)
            {
                dataOffset = written[index].Offset;
            }
            else
            {
                dataOffset = offset;
                written.Add((data, dataOffset));
                offset += (uint)writer.WriteArray(data);
                offset += (uint)writer.WritePadding();
            }

            table[i] = new IccTagTableEntry(entry.TagSignature, dataOffset, (uint)data.Length);
        }

        return table;
    }
}
