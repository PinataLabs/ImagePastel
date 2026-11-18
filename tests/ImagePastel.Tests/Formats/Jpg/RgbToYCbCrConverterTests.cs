// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using PinataLabs.ImagePastel.ColorSpaces;
using PinataLabs.ImagePastel.Formats.Jpeg.Components;
using PinataLabs.ImagePastel.Formats.Jpeg.Components.Encoder;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Colorspaces.Conversion;
using Xunit;

// ReSharper disable InconsistentNaming
namespace PinataLabs.ImagePastel.Tests.Formats.Jpg;

public class RgbToYCbCrConverterTests
{
    public RgbToYCbCrConverterTests(ITestOutputHelper output)
    {
        this.Output = output;
    }

    private ITestOutputHelper Output { get; }

    [Fact]
    public void TestConverterLut444()
    {
        int dataSize = 8 * 8;
        Rgb24[] data = CreateTestData(dataSize);
        var target = RgbToYCbCrConverterLut.Create();

        Block8x8F y = default;
        Block8x8F cb = default;
        Block8x8F cr = default;

        target.Convert444(data.AsSpan(), ref y, ref cb, ref cr);

        Verify444(data, ref y, ref cb, ref cr, new ApproximateColorSpaceComparer(1F));
    }

    [Fact]
    public void TestConverterVectorized444()
    {
        if (!RgbToYCbCrConverterVectorized.IsSupported)
        {
            this.Output.WriteLine("No AVX and/or FMA present, skipping test!");
            return;
        }

        int dataSize = 8 * 8;
        Rgb24[] data = CreateTestData(dataSize);

        Block8x8F y = default;
        Block8x8F cb = default;
        Block8x8F cr = default;

        RgbToYCbCrConverterVectorized.Convert444(data.AsSpan(), ref y, ref cb, ref cr);

        Verify444(data, ref y, ref cb, ref cr, new ApproximateColorSpaceComparer(0.0001F));
    }

    [Fact]
    public void TestConverterLut420()
    {
        int dataSize = 16 * 16;
        Span<Rgb24> data = CreateTestData(dataSize).AsSpan();
        var target = RgbToYCbCrConverterLut.Create();

        var yBlocks = new Block8x8F[4];
        var cb = default(Block8x8F);
        var cr = default(Block8x8F);

        target.Convert420(data, ref yBlocks[0], ref yBlocks[1], ref cb, ref cr, 0);
        target.Convert420(data.Slice(16 * 8), ref yBlocks[2], ref yBlocks[3], ref cb, ref cr, 1);

        Verify420(data, yBlocks, ref cb, ref cr, new ApproximateFloatComparer(1F));
    }

    [Fact]
    public void TestConverterVectorized420()
    {
        if (!RgbToYCbCrConverterVectorized.IsSupported)
        {
            this.Output.WriteLine("No AVX and/or FMA present, skipping test!");
            return;
        }

        int dataSize = 16 * 16;
        Span<Rgb24> data = CreateTestData(dataSize).AsSpan();

        var yBlocks = new Block8x8F[4];
        var cb = default(Block8x8F);
        var cr = default(Block8x8F);

        RgbToYCbCrConverterVectorized.Convert420(data, ref yBlocks[0], ref yBlocks[1], ref cb, ref cr, 0);
        RgbToYCbCrConverterVectorized.Convert420(data.Slice(16 * 8), ref yBlocks[2], ref yBlocks[3], ref cb, ref cr, 1);

        Verify420(data, yBlocks, ref cb, ref cr, new ApproximateFloatComparer(1F));
    }

    [Fact]
    public void Vectorized444_MatchesScalarExactly()
    {
        if (!RgbToYCbCrConverterVectorized.IsSupported)
        {
            return;
        }

        var scalar = RgbToYCbCrConverterLut.Create();
        var mismatches = new ConverterMismatches();

        // Every 24-bit colour, 64 at a time. The vectorized converter reads 8 bytes past the 64 pixels.
        Rgb24[] data = new Rgb24[64 + 3];
        for (int start = 0; start < 1 << 24; start += 64)
        {
            for (int i = 0; i < 64; i++)
            {
                int c = start + i;
                data[i] = new Rgb24((byte)(c >> 16), (byte)(c >> 8), (byte)c);
            }

            Block8x8F y = default, cb = default, cr = default;
            Block8x8F yVector = default, cbVector = default, crVector = default;
            scalar.Convert444(data.AsSpan(0, 64), ref y, ref cb, ref cr);
            RgbToYCbCrConverterVectorized.Convert444(data, ref yVector, ref cbVector, ref crVector);
            mismatches.Compare("Y", ref y, ref yVector, start);
            mismatches.Compare("Cb", ref cb, ref cbVector, start);
            mismatches.Compare("Cr", ref cr, ref crVector, start);
        }

        Assert.True(mismatches.Count == 0, mismatches.ToString());
    }

    [Fact]
    public void Vectorized420_MatchesScalarExactly()
    {
        if (!RgbToYCbCrConverterVectorized.IsSupported)
        {
            return;
        }

        var scalar = RgbToYCbCrConverterLut.Create();
        var mismatches = new ConverterMismatches();
        var random = new Random(42);

        // A 16x8 strip per call. The vectorized converter reads 8 bytes past the 128 pixels.
        byte[] bytes = new byte[(16 * 16 * 3) + 8];
        for (int n = 0; n < 20000; n++)
        {
            random.NextBytes(bytes);
            Span<Rgb24> data = MemoryMarshal.Cast<byte, Rgb24>(bytes.AsSpan());
            Block8x8F cb = default, cr = default, cbVector = default, crVector = default;
            for (int row = 0; row < 2; row++)
            {
                Span<Rgb24> strip = data.Slice(row * 16 * 8);
                Block8x8F yLeft = default, yRight = default, yLeftVector = default, yRightVector = default;
                scalar.Convert420(strip, ref yLeft, ref yRight, ref cb, ref cr, row);
                RgbToYCbCrConverterVectorized.Convert420(strip, ref yLeftVector, ref yRightVector, ref cbVector, ref crVector, row);
                mismatches.Compare("Y left", ref yLeft, ref yLeftVector, n);
                mismatches.Compare("Y right", ref yRight, ref yRightVector, n);
            }

            mismatches.Compare("Cb", ref cb, ref cbVector, n);
            mismatches.Compare("Cr", ref cr, ref crVector, n);
        }

        Assert.True(mismatches.Count == 0, mismatches.ToString());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Scale16x2_8x1(int seed)
    {
        if (!Avx2.IsSupported)
        {
            return;
        }

        Span<float> data = new Random(seed).GenerateRandomFloatArray(Vector256<float>.Count * 4, -1000, 1000);

        // Act:
        Vector256<float> resultVector = RgbToYCbCrConverterVectorized.Scale16x2_8x1(MemoryMarshal.Cast<float, Vector256<float>>(data));
        ref float result = ref Unsafe.As<Vector256<float>, float>(ref resultVector);

        // Assert:
        // Comparison epsilon is tricky but 10^(-4) is good enough (?)
        var comparer = new ApproximateFloatComparer(0.0001f);
        for (int i = 0; i < Vector256<float>.Count; i++)
        {
            float actual = Unsafe.Add(ref result, i);
            float expected = CalculateAverage16x2_8x1(data, i);

            Assert.True(comparer.Equals(actual, expected), $"Pos {i}, Expected: {expected}, Actual: {actual}");
        }

        static float CalculateAverage16x2_8x1(Span<float> data, int index)
        {
            int upIdx = index * 2;
            int lowIdx = (index + 8) * 2;
            return 0.25f * (data[upIdx] + data[upIdx + 1] + data[lowIdx] + data[lowIdx + 1]);
        }
    }

    private static void Verify444(
        ReadOnlySpan<Rgb24> data,
        ref Block8x8F yResult,
        ref Block8x8F cbResult,
        ref Block8x8F crResult,
        ApproximateColorSpaceComparer comparer)
    {
        Block8x8F y = default;
        Block8x8F cb = default;
        Block8x8F cr = default;

        RgbToYCbCr(data, ref y, ref cb, ref cr);

        for (int i = 0; i < Block8x8F.Size; i++)
        {
            Assert.True(comparer.Equals(new YCbCr(y[i], cb[i], cr[i]), new YCbCr(yResult[i], cbResult[i], crResult[i])), $"Pos {i}, Expected {y[i]} == {yResult[i]}, {cb[i]} == {cbResult[i]}, {cr[i]} == {crResult[i]}");
        }
    }

    private static void Verify420(
        ReadOnlySpan<Rgb24> data,
        Block8x8F[] yResult,
        ref Block8x8F cbResult,
        ref Block8x8F crResult,
        ApproximateFloatComparer comparer)
    {
        var trueBlock = default(Block8x8F);
        var cbTrue = new Block8x8F[4];
        var crTrue = new Block8x8F[4];

        Span<Rgb24> tempData = new Rgb24[8 * 8].AsSpan();

        // top left
        Copy8x8(data, tempData);
        RgbToYCbCr(tempData, ref trueBlock, ref cbTrue[0], ref crTrue[0]);
        VerifyBlock(ref yResult[0], ref trueBlock, comparer);

        // top right
        Copy8x8(data.Slice(8), tempData);
        RgbToYCbCr(tempData, ref trueBlock, ref cbTrue[1], ref crTrue[1]);
        VerifyBlock(ref yResult[1], ref trueBlock, comparer);

        // bottom left
        Copy8x8(data.Slice(8 * 16), tempData);
        RgbToYCbCr(tempData, ref trueBlock, ref cbTrue[2], ref crTrue[2]);
        VerifyBlock(ref yResult[2], ref trueBlock, comparer);

        // bottom right
        Copy8x8(data.Slice((8 * 16) + 8), tempData);
        RgbToYCbCr(tempData, ref trueBlock, ref cbTrue[3], ref crTrue[3]);
        VerifyBlock(ref yResult[3], ref trueBlock, comparer);

        // verify Cb
        Scale16X16To8X8(ref trueBlock, cbTrue);
        VerifyBlock(ref cbResult, ref trueBlock, comparer);

        // verify Cr
        Scale16X16To8X8(ref trueBlock, crTrue);
        VerifyBlock(ref crResult, ref trueBlock, comparer);

        // extracts 8x8 blocks from 16x8 memory region
        static void Copy8x8(ReadOnlySpan<Rgb24> source, Span<Rgb24> dest)
        {
            for (int i = 0; i < 8; i++)
            {
                source.Slice(i * 16, 8).CopyTo(dest.Slice(i * 8));
            }
        }

        // scales 16x16 to 8x8, used in chroma subsampling tests
        static void Scale16X16To8X8(ref Block8x8F dest, ReadOnlySpan<Block8x8F> source)
        {
            for (int i = 0; i < 4; i++)
            {
                int dstOff = ((i & 2) << 4) | ((i & 1) << 2);
                Block8x8F iSource = source[i];

                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        int j = (16 * y) + (2 * x);
                        float sum = iSource[j] + iSource[j + 1] + iSource[j + 8] + iSource[j + 9];
                        dest[(8 * y) + x + dstOff] = (sum + 2) * .25F;
                    }
                }
            }
        }
    }

    private static void RgbToYCbCr(ReadOnlySpan<Rgb24> data, ref Block8x8F y, ref Block8x8F cb, ref Block8x8F cr)
    {
        for (int i = 0; i < data.Length; i++)
        {
            int r = data[i].R;
            int g = data[i].G;
            int b = data[i].B;

            y[i] = (0.299F * r) + (0.587F * g) + (0.114F * b);
            cb[i] = 128F + ((-0.168736F * r) - (0.331264F * g) + (0.5F * b));
            cr[i] = 128F + ((0.5F * r) - (0.418688F * g) - (0.081312F * b));
        }
    }

    private static void VerifyBlock(ref Block8x8F res, ref Block8x8F target, ApproximateFloatComparer comparer)
    {
        for (int i = 0; i < Block8x8F.Size; i++)
        {
            Assert.True(comparer.Equals(res[i], target[i]), $"Pos {i}, Expected: {target[i]}, Got: {res[i]}");
        }
    }

    private sealed class ConverterMismatches
    {
        private string first;

        public int Count { get; private set; }

        public void Compare(string plane, ref Block8x8F scalar, ref Block8x8F vectorized, int block)
        {
            for (int i = 0; i < Block8x8F.Size; i++)
            {
                if (scalar[i] != vectorized[i])
                {
                    this.Count++;
                    this.first ??= $"{plane} in block {block} at {i}: scalar {scalar[i]:R}, vectorized {vectorized[i]:R}";
                }
            }
        }

        public override string ToString() => $"{this.Count} values differ, first {this.first}";
    }

    private static Rgb24[] CreateTestData(int size)
    {
        var data = new Rgb24[size];
        var r = new Random();

        var random = new byte[3];
        for (int i = 0; i < data.Length; i++)
        {
            r.NextBytes(random);
            data[i] = new Rgb24(random[0], random[1], random[2]);
        }

        return data;
    }
}
