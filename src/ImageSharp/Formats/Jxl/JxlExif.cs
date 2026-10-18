// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// ResetExifOrientation in lib/extras/exif.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers.Binary;

namespace SixLabors.ImageSharp.Formats.Jxl;

/// <summary>
/// Edits the Exif of a JPEG XL file as djxl does.
/// </summary>
internal static class JxlExif
{
    private const ushort ExifOrientationTag = 274;

    /// <summary>
    /// Sets the orientation tag of IFD0 to 1, in place, when it is a single SHORT. The decoder undoes the
    /// codestream orientation, so this keeps the image from being oriented twice. Any other Exif is left
    /// as it is.
    /// </summary>
    /// <param name="exif">The Exif, starting at its TIFF header.</param>
    public static void ResetOrientation(Span<byte> exif)
    {
        if (exif.Length < 12)
        {
            return; // not enough bytes for a valid exif blob
        }

        bool bigEndian;
        uint header = BinaryPrimitives.ReadUInt32LittleEndian(exif);
        if (header == 0x2A004D4D)
        {
            bigEndian = true;
        }
        else if (header == 0x002A4949)
        {
            bigEndian = false;
        }
        else
        {
            return; // not a valid tiff header
        }

        long t = 4;
        ulong offset = ReadUInt32(exif, t, bigEndian);
        if ((ulong)exif.Length < 12 + offset + 2 || offset < 8)
        {
            return;
        }

        t += (long)offset - 4;
        ushort numTags = ReadUInt16(exif, t, bigEndian);
        t += 2;
        while (numTags > 0)
        {
            if (t + 12 >= exif.Length)
            {
                return;
            }

            ushort tag = ReadUInt16(exif, t, bigEndian);
            t += 2;
            if (tag == ExifOrientationTag)
            {
                ushort type = ReadUInt16(exif, t, bigEndian);
                t += 2;
                uint count = ReadUInt32(exif, t, bigEndian);
                t += 4;
                if (type == 3 && count == 1)
                {
                    if (bigEndian)
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(exif.Slice((int)t), 1);
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(exif.Slice((int)t), 1);
                    }
                }

                return;
            }

            t += 10;
            numTags--;
        }
    }

    private static ushort ReadUInt16(Span<byte> data, long position, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data.Slice((int)position)) : BinaryPrimitives.ReadUInt16LittleEndian(data.Slice((int)position));

    private static uint ReadUInt32(Span<byte> data, long position, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(data.Slice((int)position)) : BinaryPrimitives.ReadUInt32LittleEndian(data.Slice((int)position));
}
