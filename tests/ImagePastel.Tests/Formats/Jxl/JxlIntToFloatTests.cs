// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Checks the conversion of stored float samples to binary32 against IEEE 754 values.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlIntToFloatTests
{
    [Theory]
    [InlineData(0x3C00, 1F)]
    [InlineData(0xC000, -2F)]
    [InlineData(0x3555, 0.333251953125F)]
    [InlineData(0x7BFF, 65504F)] // largest normal
    [InlineData(0x0001, 5.9604645E-08F)] // smallest subnormal, 2^-24
    [InlineData(0x03FF, 6.0975552E-05F)] // largest subnormal, 1023 * 2^-24
    [InlineData(0x7C00, float.PositiveInfinity)]
    [InlineData(0xFC00, float.NegativeInfinity)]
    public void HalfFloats_GiveTheirValues(int stored, float expected)
        => Assert.Equal(expected, Convert(stored, 16, 5));

    [Fact]
    public void HalfFloat_NaN_StaysNaN()
        => Assert.True(float.IsNaN(Convert(0x7E00, 16, 5)));

    [Fact]
    public void HalfFloat_NegativeZero_KeepsTheSign()
        => Assert.Equal(BitConverter.SingleToInt32Bits(-0F), BitConverter.SingleToInt32Bits(Convert(0x8000, 16, 5)));

    [Fact]
    public void Float24_WithEightExponentBits_IsTheTopOfABinary32()
        => Assert.Equal(1F, Convert(0x3F8000, 24, 8));

    [Fact]
    public void Float32_IsCopied()
        => Assert.Equal(-1.5F, Convert(BitConverter.SingleToInt32Bits(-1.5F), 32, 8));

    // The header bounds keep both of these out of a file; IntToFloat still rejects them, as libjxl does.
    [Fact]
    public void Float32_WithoutEightExponentBits_Throws()
        => Assert.Throws<InvalidImageContentException>(() => Convert(0, 32, 7));

    [Fact]
    public void ExponentBelowBinary32_Throws()
        => Assert.Throws<InvalidImageContentException>(() => Convert(1 << 6, 16, 9));

    private static float Convert(int stored, int bits, int expBits)
    {
        float[] output = new float[1];
        JxlModularFrameDecoder.IntToFloat(new[] { stored }, output, bits, expBits);
        return output[0];
    }
}
