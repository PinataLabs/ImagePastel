// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/icc_codec_common.h and lib/jxl/icc_codec_common.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace PinataLabs.ImagePastel.Formats.Jxl.Icc;

/// <summary>
/// The tags, commands and predictors shared by the compressed representation of ICC profiles.
/// </summary>
internal static class JxlIccCodecCommon
{
    public const int IccHeaderSize = 128;

    public const int NumIccContexts = 41;

    public const int CommandTagUnknown = 1;
    public const int CommandTagTrc = 2;
    public const int CommandTagXyz = 3;
    public const int CommandTagStringFirst = 4;

    public const int CommandInsert = 1;
    public const int CommandShuffle2 = 2;
    public const int CommandShuffle4 = 3;
    public const int CommandPredict = 4;
    public const int CommandXyz = 10;
    public const int CommandTypeStartFirst = 16;

    public const int FlagBitOffset = 64;
    public const int FlagBitSize = 128;

    // Tags, as their four bytes read big-endian.
    public const uint BkptTag = 0x626B7074; // bkpt
    public const uint BtrcTag = 0x62545243; // bTRC
    public const uint BxyzTag = 0x6258595A; // bXYZ
    public const uint CurvTag = 0x63757276; // curv
    public const uint GbdTag = 0x67626420; // "gbd "
    public const uint GtrcTag = 0x67545243; // gTRC
    public const uint GxyzTag = 0x6758595A; // gXYZ
    public const uint KxyzTag = 0x6B58595A; // kXYZ
    public const uint LumiTag = 0x6C756D69; // lumi
    public const uint MabTag = 0x6D414220; // "mAB "
    public const uint MbaTag = 0x6D424120; // "mBA "
    public const uint MlucTag = 0x6D6C7563; // mluc
    public const uint RtrcTag = 0x72545243; // rTRC
    public const uint RxyzTag = 0x7258595A; // rXYZ
    public const uint VcgtTag = 0x76636774; // vcgt
    public const uint WtptTag = 0x77747074; // wtpt
    public const uint XyzTypeTag = 0x58595A20; // "XYZ "

    // Tag names focused on RGB and GRAY monitor profiles: cprt, wtpt, bkpt, rXYZ, gXYZ, bXYZ, kXYZ,
    // rTRC, gTRC, bTRC, kTRC, chad, desc, chrm, dmnd, dmdd, lumi.
    public static readonly uint[] TagStrings =
    {
        0x63707274, WtptTag, BkptTag, RxyzTag, GxyzTag, BxyzTag, KxyzTag, RtrcTag, GtrcTag, BtrcTag,
        0x6B545243, 0x63686164, 0x64657363, 0x6368726D, 0x646D6E64, 0x646D6464, LumiTag,
    };

    // Tag types focused on RGB and GRAY monitor profiles: "XYZ ", desc, text, mluc, para, curv, sf32,
    // "gbd ".
    public static readonly uint[] TypeStrings =
    {
        XyzTypeTag, 0x64657363, 0x74657874, 0x6D6C7563, 0x70617261, 0x63757276, 0x73663332, 0x67626420,
    };

    private static readonly byte[] IccInitialHeaderPredictionTable =
    {
        0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0, 0, (byte)'m', (byte)'n', (byte)'t', (byte)'r',
        (byte)'R', (byte)'G', (byte)'B', (byte)' ', (byte)'X', (byte)'Y', (byte)'Z', (byte)' ', 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, (byte)'a', (byte)'c', (byte)'s', (byte)'p', 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 246, 214, 0, 1, 0, 0, 0, 0, 211, 45,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    };

    public static uint DecodeUint32(List<byte> data, int size, long pos)
        => pos + 4 > size ? 0 : (uint)((data[(int)pos] << 24) | (data[(int)pos + 1] << 16) | (data[(int)pos + 2] << 8) | data[(int)pos + 3]);

    public static void AppendUint32(uint value, List<byte> data)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        foreach (byte b in bytes)
        {
            data.Add(b);
        }
    }

    public static uint DecodeKeyword(byte[] data, long size, long pos)
        => pos + 4 > size ? 0x20202020 : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)pos));

    public static void AppendKeyword(uint keyword, List<byte> data) => AppendUint32(keyword, data);

    // Checks if a + b > size, taking possible integer overflow into account.
    public static void CheckOutOfBounds(ulong a, ulong b, ulong size)
    {
        ulong pos = unchecked(a + b);
        if (pos > size)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Out of bounds");
        }

        if (pos < a)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Out of bounds"); // overflow happened
        }
    }

    public static void CheckIs32Bit(ulong v)
    {
        if ((v & ~0xFFFFFFFFUL) != 0)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("32-bit value expected");
        }
    }

    public static byte[] IccInitialHeaderPrediction(uint size)
    {
        byte[] copy = (byte[])IccInitialHeaderPredictionTable.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(copy, size);
        return copy;
    }

    public static void IccPredictHeader(List<byte> icc, int size, byte[] header, int pos)
    {
        if (pos == 8 && size >= 8)
        {
            header[80] = icc[4];
            header[81] = icc[5];
            header[82] = icc[6];
            header[83] = icc[7];
        }

        if (pos == 41 && size >= 41)
        {
            if (icc[40] == 'A')
            {
                header[41] = (byte)'P';
                header[42] = (byte)'P';
                header[43] = (byte)'L';
            }

            if (icc[40] == 'M')
            {
                header[41] = (byte)'S';
                header[42] = (byte)'F';
                header[43] = (byte)'T';
            }
        }

        if (pos == 42 && size >= 42)
        {
            if (icc[40] == 'S' && icc[41] == 'G')
            {
                header[42] = (byte)'I';
                header[43] = (byte)' ';
            }

            if (icc[40] == 'S' && icc[41] == 'U')
            {
                header[42] = (byte)'N';
                header[43] = (byte)'W';
            }
        }
    }

    /// <summary>
    /// Predicts a value with linear prediction of given order (0-2), for integers with width bytes and
    /// given stride in bytes between values. The start position is at start + i, and the relevant
    /// modulus of i describes which byte of the multi-byte integer is being handled. The value
    /// start + i must be at least stride * 4.
    /// </summary>
    /// <param name="data">The profile so far.</param>
    /// <param name="start">The start of the predicted run.</param>
    /// <param name="i">The index in the run.</param>
    /// <param name="stride">The stride in bytes.</param>
    /// <param name="width">The integer width in bytes.</param>
    /// <param name="order">The prediction order.</param>
    /// <returns>The predicted byte.</returns>
    public static byte LinearPredictIccValue(List<byte> data, int start, int i, int stride, int width, int order)
    {
        int pos = start + i;
        if (width == 1)
        {
            byte p1 = data[pos - stride];
            byte p2 = data[pos - (stride * 2)];
            byte p3 = data[pos - (stride * 3)];
            return (byte)PredictValue(p1, p2, p3, order);
        }
        else if (width == 2)
        {
            int p = start + (i & ~1);
            ushort p1 = (ushort)((data[p - stride] << 8) + data[p - stride + 1]);
            ushort p2 = (ushort)((data[p - (stride * 2)] << 8) + data[p - (stride * 2) + 1]);
            ushort p3 = (ushort)((data[p - (stride * 3)] << 8) + data[p - (stride * 3) + 1]);
            ushort pred = (ushort)PredictValue(p1, p2, p3, order);
            return (byte)((i & 1) != 0 ? (pred & 255) : ((pred >> 8) & 255));
        }
        else
        {
            int p = start + (i & ~3);
            uint p1 = DecodeUint32(data, pos, p - stride);
            uint p2 = DecodeUint32(data, pos, p - ((long)stride * 2));
            uint p3 = DecodeUint32(data, pos, p - ((long)stride * 3));
            uint pred = (uint)PredictValue(p1, p2, p3, order);
            int shiftbytes = 3 - (i & 3);
            return (byte)((pred >> (shiftbytes * 8)) & 255);
        }
    }

    public static int IccAnsContext(int i, int b1, int b2)
    {
        if (i <= 128)
        {
            return 0;
        }

        return 1 + ByteKind1(b1) + (ByteKind2(b2) * 8);
    }

    // libjxl computes in the integer type of the values and truncates to it; the callers truncate.
    private static long PredictValue(long p1, long p2, long p3, int order)
    {
        if (order == 0)
        {
            return p1;
        }

        if (order == 1)
        {
            return (2 * p1) - p2;
        }

        if (order == 2)
        {
            return (3 * p1) - (3 * p2) + p3;
        }

        return 0;
    }

    private static int ByteKind1(int b)
    {
        if (b is >= 'a' and <= 'z')
        {
            return 0;
        }

        if (b is >= 'A' and <= 'Z')
        {
            return 0;
        }

        if (b is >= '0' and <= '9')
        {
            return 1;
        }

        if (b is '.' or ',')
        {
            return 1;
        }

        if (b == 0)
        {
            return 2;
        }

        if (b == 1)
        {
            return 3;
        }

        if (b < 16)
        {
            return 4;
        }

        if (b == 255)
        {
            return 6;
        }

        if (b > 240)
        {
            return 5;
        }

        return 7;
    }

    private static int ByteKind2(int b)
    {
        if (b is >= 'a' and <= 'z')
        {
            return 0;
        }

        if (b is >= 'A' and <= 'Z')
        {
            return 0;
        }

        if (b is >= '0' and <= '9')
        {
            return 1;
        }

        if (b is '.' or ',')
        {
            return 1;
        }

        if (b < 16)
        {
            return 2;
        }

        if (b > 240)
        {
            return 3;
        }

        return 4;
    }
}
