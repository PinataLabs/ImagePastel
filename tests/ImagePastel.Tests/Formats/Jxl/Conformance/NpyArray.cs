// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;

/// <summary>
/// A float32 NumPy array in C order, as stored in the JPEG XL conformance references.
/// Conformance arrays have the shape (frames, height, width, channels).
/// </summary>
internal sealed partial class NpyArray
{
    private static readonly byte[] Magic = { 0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y' };

    public NpyArray(int[] shape, float[] data)
    {
        long length = shape.Aggregate(1L, (a, b) => a * b);
        if (length != data.Length)
        {
            throw new ArgumentException($"The shape ({string.Join(", ", shape)}) needs {length} values, but there are {data.Length}.", nameof(data));
        }

        this.Shape = shape;
        this.Data = data;
    }

    public int[] Shape { get; }

    public float[] Data { get; }

    public string ShapeText => $"({string.Join(", ", this.Shape)})";

    public static NpyArray Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Read(stream);
    }

    public static NpyArray Read(Stream stream)
    {
        byte[] prefix = ReadExactly(stream, 8);
        if (!prefix.AsSpan(0, 6).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a .npy file.");
        }

        byte major = prefix[6];
        int headerLength = major switch
        {
            1 => BinaryPrimitives.ReadUInt16LittleEndian(ReadExactly(stream, 2)),
            2 or 3 => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ReadExactly(stream, 4))),
            _ => throw new NotSupportedException($".npy version {major} is not supported.")
        };

        string header = Encoding.ASCII.GetString(ReadExactly(stream, headerLength));
        string descr = DescrRegex().Match(header).Groups[1].Value;
        if (descr != "<f4")
        {
            throw new NotSupportedException($"Only little-endian float32 ('<f4') arrays are supported, not '{descr}'.");
        }

        if (!header.Contains("'fortran_order': False", StringComparison.Ordinal))
        {
            throw new NotSupportedException("Only C-order arrays are supported.");
        }

        Match shapeMatch = ShapeRegex().Match(header);
        if (!shapeMatch.Success)
        {
            throw new InvalidDataException($"The .npy header has no shape: {header}");
        }

        int[] shape = shapeMatch.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToArray();

        long length = shape.Aggregate(1L, (a, b) => a * b);
        float[] data = new float[checked((int)length)];
        byte[] buffer = new byte[1 << 16];
        int index = 0;
        while (index < data.Length)
        {
            int count = Math.Min(buffer.Length / 4, data.Length - index);
            stream.ReadExactly(buffer, 0, count * 4);
            for (int i = 0; i < count; i++)
            {
                data[index + i] = BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(i * 4));
            }

            index += count;
        }

        return new NpyArray(shape, data);
    }

    public void Write(Stream stream)
    {
        string shapeText = this.Shape.Length == 1 ? $"({this.Shape[0]},)" : $"({string.Join(", ", this.Shape)})";
        string header = $"{{'descr': '<f4', 'fortran_order': False, 'shape': {shapeText}, }}";

        // Version 1.0: the magic, the version and the header length take 10 bytes,
        // and the header ends with a newline so the data starts on a 64-byte boundary.
        int padding = 64 - ((10 + header.Length + 1) % 64);
        if (padding == 64)
        {
            padding = 0;
        }

        header = header + new string(' ', padding) + "\n";

        stream.Write(Magic);
        stream.WriteByte(1);
        stream.WriteByte(0);
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(length, (ushort)header.Length);
        stream.Write(length);
        stream.Write(Encoding.ASCII.GetBytes(header));

        Span<byte> value = stackalloc byte[4];
        foreach (float f in this.Data)
        {
            BinaryPrimitives.WriteSingleLittleEndian(value, f);
            stream.Write(value);
        }
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        byte[] bytes = new byte[count];
        stream.ReadExactly(bytes);
        return bytes;
    }

    [GeneratedRegex(@"'descr':\s*'([^']*)'")]
    private static partial Regex DescrRegex();

    [GeneratedRegex(@"'shape':\s*\(([^)]*)\)")]
    private static partial Regex ShapeRegex();
}
