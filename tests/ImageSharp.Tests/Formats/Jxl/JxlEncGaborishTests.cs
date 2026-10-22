// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using SixLabors.ImageSharp.Memory;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The encoder's 5x5 symmetric convolution (libjxl's <c>Symmetric5</c>) and the inverse Gaborish filter
/// built on it (<c>GaborishInverse</c>), against libjxl's own values on a fixed 37x29 plane. The values
/// come from a harness that builds libjxl <c>ef67fde2</c>'s <c>convolve_symmetric5.cc</c> and
/// <c>enc_gaborish.cc</c> on Highway's scalar target with <c>-ffp-contract=off</c>
/// (<c>e4-notes/e4a-scratch/symmetric5.cc</c>). Neither has a lane sum or a fused multiply-add, so they
/// match bit for bit: each plane's SHA-256 is over its float bits, row by row, little-endian.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncGaborishTests
{
    private const int Width = 37;
    private const int Height = 29;

    // Positions whose bits each case also checks, so that a failure shows where: the four corners, a
    // pixel one in from two borders, and the centre.
    private static readonly (int X, int Y)[] SamplePositions = { (0, 0), (1, 0), (36, 0), (18, 14), (0, 28), (36, 28), (2, 1), (35, 27) };

    [Fact]
    public void Symmetric5_GivesLibjxlsValuesBitForBit()
    {
        using Buffer2D<float> input = CreatePlane(0);
        using Buffer2D<float> output = Configuration.Default.MemoryAllocator.Allocate2D<float>(Width, Height);
        JxlWeightsSymmetric5 weights = new(0.36f, 0.11f, -0.07f, 0.05f, 0.013f, -0.021f);

        JxlConvolve.Symmetric5(input, new JxlRect(0, 0, Width, Height), weights, output, new JxlRect(0, 0, Width, Height));

        AssertPlane(
            output,
            "D13AD72ED3CF747AE582C56B9B54DA69714EC1B4C50DC76AA69059B108251D71",
            new uint[] { 0xBF069FBE, 0xBB9F3B60, 0x3E643D72, 0xBD82872B, 0xBEBAF3B6, 0x3E146666, 0x3E5F8520, 0x3DB94FE2 });
    }

    [Fact]
    public void GaborishInverse_GivesLibjxlsValuesBitForBit()
    {
        Buffer2D<float>[] planes = { CreatePlane(0), CreatePlane(1), CreatePlane(2) };
        try
        {
            JxlEncGaborish.GaborishInverse(planes, new[] { 1.0f, 1.0f, 1.0f }, Configuration.Default.MemoryAllocator);

            AssertPlane(
                planes[0],
                "61BAC7F8C3795ABC5E0D10E886B1D49F7163BC8CF34C4231ADA3C46AE9580B91",
                new uint[] { 0xBF5B065C, 0xBE83FCAA, 0x3F0B2DAA, 0x3EEE57BC, 0xBF1A868D, 0x3E498EF4, 0x3FA4EAB3, 0x3EED70AE });
            AssertPlane(
                planes[1],
                "20B09C7E7E048B0BF231F8754BBE71CF2E1BB2DA9BB7D8160205589F2EBB1A04",
                new uint[] { 0xBCF27659, 0x3F9C6C37, 0xBF380308, 0xBF816096, 0x3EF31B38, 0xBF392796, 0x3C834A07, 0xBF7DA427 });
            AssertPlane(
                planes[2],
                "8B29AB6A71E668FD84A410BD24DC43D43595D86F1698B9A75A81B08B9D15F09C",
                new uint[] { 0xBF3A8780, 0xBCBE578E, 0x3F36AA54, 0x3F6BCEFA, 0xBF049C0E, 0x3F2706D7, 0xBFA79DA7, 0x3F534FD9 });
        }
        finally
        {
            JxlPlanes.DisposeAll(planes);
        }
    }

    // The harness's input: an integer over 64, exact in float.
    private static Buffer2D<float> CreatePlane(int c)
    {
        Buffer2D<float> plane = Configuration.Default.MemoryAllocator.Allocate2D<float>(Width, Height);
        for (int y = 0; y < Height; y++)
        {
            Span<float> row = plane.DangerousGetRowSpan(y);
            for (int x = 0; x < Width; x++)
            {
                row[x] = ((((x * 37) + (y * 101) + (x * y * 7) + (c * 53)) % 97) - 48) / 64.0f;
            }
        }

        return plane;
    }

    private static void AssertPlane(Buffer2D<float> plane, string expectedSha256, uint[] expectedSamples)
    {
        for (int i = 0; i < SamplePositions.Length; i++)
        {
            (int x, int y) = SamplePositions[i];
            uint bits = BitConverter.SingleToUInt32Bits(plane[x, y]);
            Assert.True(expectedSamples[i] == bits, $"({x}, {y}): 0x{bits:X8}, libjxl 0x{expectedSamples[i]:X8}");
        }

        byte[] bytes = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(((y * Width) + x) * 4, 4), BitConverter.SingleToUInt32Bits(plane[x, y]));
            }
        }

        Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
