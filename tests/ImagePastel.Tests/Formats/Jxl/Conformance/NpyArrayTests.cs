// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Text;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;

[Trait("Format", "Jxl")]
public class NpyArrayTests
{
    [Fact]
    public void WriteThenRead_RoundTripsShapeAndValues()
    {
        float[] values = { 0F, 1F, -0.25F, 1.5F, float.Epsilon, 0.5F };
        NpyArray array = new(new[] { 1, 1, 2, 3 }, values);

        using MemoryStream stream = new();
        array.Write(stream);

        // Version 1.0 headers pad the data to a 64-byte boundary.
        Assert.Equal(0, (stream.Length - (values.Length * 4)) % 64);

        stream.Position = 0;
        NpyArray read = NpyArray.Read(stream);

        Assert.Equal(array.Shape, read.Shape);
        Assert.Equal(values, read.Data);
    }

    [Fact]
    public void Read_CommittedConformanceReference()
    {
        // The only reference stored in the conformance repository itself.
        string path = Path.Combine(JxlConformanceVector.RootDirectory, "mul_no_extra_channels", "reference_image.npy");

        NpyArray array = NpyArray.Load(path);

        Assert.Equal(new[] { 1, 8, 8, 3 }, array.Shape);
        Assert.All(array.Data, v => Assert.InRange(v, 0F, 1F));
    }

    [Fact]
    public void Read_Version2Header()
    {
        string header = "{'descr': '<f4', 'fortran_order': False, 'shape': (2,), }\n";
        using MemoryStream stream = new();
        stream.Write(new byte[] { 0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y', 2, 0 });
        stream.Write(BitConverter.GetBytes((uint)header.Length));
        stream.Write(Encoding.ASCII.GetBytes(header));
        stream.Write(BitConverter.GetBytes(0.75F));
        stream.Write(BitConverter.GetBytes(-2F));
        stream.Position = 0;

        NpyArray array = NpyArray.Read(stream);

        Assert.Equal(new[] { 2 }, array.Shape);
        Assert.Equal(new[] { 0.75F, -2F }, array.Data);
    }

    [Theory]
    [InlineData("{'descr': '<f8', 'fortran_order': False, 'shape': (1,), }")]
    [InlineData("{'descr': '>f4', 'fortran_order': False, 'shape': (1,), }")]
    [InlineData("{'descr': '<f4', 'fortran_order': True, 'shape': (1,), }")]
    public void Read_UnsupportedLayout_Throws(string header)
    {
        using MemoryStream stream = new();
        stream.Write(new byte[] { 0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y', 1, 0 });
        stream.Write(BitConverter.GetBytes((ushort)header.Length));
        stream.Write(Encoding.ASCII.GetBytes(header));
        stream.Write(new byte[8]);
        stream.Position = 0;

        Assert.Throws<NotSupportedException>(() => NpyArray.Read(stream));
    }

    [Fact]
    public void Read_NotNpy_Throws()
    {
        using MemoryStream stream = new(Encoding.ASCII.GetBytes("not a numpy file"));

        Assert.Throws<InvalidDataException>(() => NpyArray.Read(stream));
    }
}
