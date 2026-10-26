// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace SixLabors.ImageSharp.Tests.Formats.Tiff.Utils;

/// <summary>
/// Builds little-endian TIFF files from explicit IFD entries, so tests can use tag combinations the encoder never writes.
/// </summary>
internal sealed class TiffTestFileBuilder
{
    private readonly List<Page> pages = new();

    /// <summary>
    /// Starts a new page (IFD). The entries added next belong to this page.
    /// </summary>
    /// <param name="strips">The strip data of the page. StripOffsets and StripByteCounts are added when the file is built.</param>
    /// <returns>The builder.</returns>
    public TiffTestFileBuilder AddPage(params byte[][] strips)
    {
        this.pages.Add(new Page(strips));
        return this;
    }

    /// <summary>
    /// Adds an entry of SHORT values to the current page.
    /// </summary>
    public TiffTestFileBuilder AddShort(ExifTagValue tag, params ushort[] values)
        => this.AddEntry(tag, ExifDataType.Short, (uint)values.Length, values.SelectMany(BitConverter.GetBytes).ToArray());

    /// <summary>
    /// Adds an entry of LONG values to the current page.
    /// </summary>
    public TiffTestFileBuilder AddLong(ExifTagValue tag, params uint[] values)
        => this.AddEntry(tag, ExifDataType.Long, (uint)values.Length, ToBytes(values));

    /// <summary>
    /// Adds an entry with raw little-endian value bytes to the current page.
    /// </summary>
    public TiffTestFileBuilder AddEntry(ExifTagValue tag, ExifDataType type, uint count, byte[] value)
    {
        this.pages[this.pages.Count - 1].Entries.Add(new Entry((ushort)tag, (ushort)type, count, value));
        return this;
    }

    /// <summary>
    /// Writes the pages to a TIFF file.
    /// </summary>
    public byte[] Build()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'I');
        writer.Write((byte)'I');
        writer.Write((ushort)42);
        long nextIfdPointer = stream.Position;
        writer.Write(0u);

        foreach (Page page in this.pages)
        {
            var stripOffsets = new uint[page.Strips.Length];
            var stripByteCounts = new uint[page.Strips.Length];
            for (int i = 0; i < page.Strips.Length; i++)
            {
                stripOffsets[i] = (uint)stream.Position;
                stripByteCounts[i] = (uint)page.Strips[i].Length;
                writer.Write(page.Strips[i]);
            }

            List<Entry> entries = page.Entries
                .Append(new Entry((ushort)ExifTagValue.StripOffsets, (ushort)ExifDataType.Long, (uint)stripOffsets.Length, ToBytes(stripOffsets)))
                .Append(new Entry((ushort)ExifTagValue.StripByteCounts, (ushort)ExifDataType.Long, (uint)stripByteCounts.Length, ToBytes(stripByteCounts)))
                .OrderBy(e => e.Tag)
                .ToList();

            // Values larger than 4 bytes are stored before the IFD, on word boundaries.
            var valueOffsets = new Dictionary<Entry, uint>();
            foreach (Entry entry in entries.Where(e => e.Value.Length > 4))
            {
                AlignToWord(writer);
                valueOffsets[entry] = (uint)stream.Position;
                writer.Write(entry.Value);
            }

            AlignToWord(writer);
            uint ifdOffset = (uint)stream.Position;
            stream.Position = nextIfdPointer;
            writer.Write(ifdOffset);
            stream.Position = ifdOffset;

            writer.Write((ushort)entries.Count);
            foreach (Entry entry in entries)
            {
                writer.Write(entry.Tag);
                writer.Write(entry.Type);
                writer.Write(entry.Count);
                if (valueOffsets.TryGetValue(entry, out uint valueOffset))
                {
                    writer.Write(valueOffset);
                }
                else
                {
                    var inline = new byte[4];
                    entry.Value.CopyTo(inline, 0);
                    writer.Write(inline);
                }
            }

            nextIfdPointer = stream.Position;
            writer.Write(0u);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] ToBytes(uint[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();

    private static void AlignToWord(BinaryWriter writer)
    {
        if (writer.BaseStream.Position % 2 != 0)
        {
            writer.Write((byte)0);
        }
    }

    private sealed record Entry(ushort Tag, ushort Type, uint Count, byte[] Value);

    private sealed class Page
    {
        public Page(byte[][] strips) => this.Strips = strips;

        public byte[][] Strips { get; }

        public List<Entry> Entries { get; } = new();
    }
}
