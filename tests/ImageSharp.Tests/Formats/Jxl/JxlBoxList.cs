// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Lists the boxes of a JPEG XL container, for the tests that check the encoder's box layout. It reads
/// the 8-byte box header and the 16-byte large form, and an open-ended last box (size 0).
/// </summary>
internal static class JxlBoxList
{
    /// <summary>Lists the boxes of <paramref name="file"/>, which has to be a container.</summary>
    /// <param name="file">The file.</param>
    /// <returns>Each box's type and contents, in file order.</returns>
    public static IReadOnlyList<JxlBox> Read(byte[] file)
    {
        List<JxlBox> boxes = new();
        int pos = 0;
        while (pos < file.Length)
        {
            Assert.True(file.Length - pos >= 8, $"A box header at byte {pos} is cut short.");
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(pos));
            string type = Encoding.ASCII.GetString(file, pos + 4, 4);
            int headerSize = 8;
            if (size == 1)
            {
                size = BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan(pos + 8));
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = (ulong)(file.Length - pos);
            }

            Assert.True(size >= (ulong)headerSize && size <= (ulong)(file.Length - pos), $"The {type} box at byte {pos} has size {size}.");
            boxes.Add(new JxlBox(type, file.AsSpan(pos + headerSize, (int)size - headerSize).ToArray(), headerSize));
            pos += (int)size;
        }

        return boxes;
    }

    /// <summary>
    /// Gives the codestream of <paramref name="file"/>: the file itself when it is a bare codestream, else
    /// the codestream that <see cref="JxlContainer.Read"/> joins. Codestream pins hash this, so they hold
    /// when an output gains a container.
    /// </summary>
    /// <param name="file">A bare codestream or a container.</param>
    /// <returns>The codestream.</returns>
    public static byte[] Codestream(byte[] file)
        => file.AsSpan().StartsWith(JxlConstants.CodestreamSignature)
            ? file
            : JxlContainer.Read(file, readMetadata: false, CancellationToken.None).Codestream;

    /// <summary>Gives the types of <paramref name="boxes"/>, in order.</summary>
    /// <param name="boxes">The boxes.</param>
    /// <returns>The types.</returns>
    public static string[] Types(IEnumerable<JxlBox> boxes) => boxes.Select(b => b.Type).ToArray();

    /// <summary>Gives the index of a <c>jxlp</c> box: its first 4 bytes, big-endian.</summary>
    /// <param name="box">The box.</param>
    /// <returns>The index, with bit 31 set on the last.</returns>
    public static uint JxlpIndex(JxlBox box)
    {
        Assert.Equal("jxlp", box.Type);
        return BinaryPrimitives.ReadUInt32BigEndian(box.Contents);
    }
}

/// <summary>A box of a JPEG XL container.</summary>
/// <param name="Type">The 4-character type.</param>
/// <param name="Contents">The contents, after the header.</param>
/// <param name="HeaderSize">The size of the header: 8, or 16 for the large form.</param>
internal sealed record JxlBox(string Type, byte[] Contents, int HeaderSize);
