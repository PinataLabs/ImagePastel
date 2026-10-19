// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Formats.Jxl.Frame;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The DC quantization factors of a frame, which <see cref="JxlDequantMatrices.DecodeDc"/> reads from
/// the DC global section and keeps for the Modular XYB conversion and the VarDCT DC.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlDequantMatricesTests
{
    [Fact]
    public void New_HasLibjxlsDefaultDcQuant()
    {
        JxlDequantMatrices matrices = new();

        AssertDcQuant(matrices, new[] { 1f / 4096, 1f / 512, 1f / 256 }, new[] { 4096f, 512f, 256f });
    }

    [Fact]
    public void DecodeDc_AllDefault_KeepsTheDefaults()
    {
        JxlDequantMatrices matrices = new();

        matrices.DecodeDc(new JxlTestBitWriter().Bit(true).ZeroPadToByte().ToReader());

        AssertDcQuant(matrices, new[] { 1f / 4096, 1f / 512, 1f / 256 }, new[] { 4096f, 512f, 256f });
    }

    // Each factor is an F16 divided by 128: 1, 2 and 4 give 1/128, 1/64 and 1/32.
    [Fact]
    public void DecodeDc_Custom_KeepsEachFactorOver128()
    {
        JxlDequantMatrices matrices = new();

        matrices.DecodeDc(new JxlTestBitWriter().Bit(false).Write(16, 0x3C00).Write(16, 0x4000).Write(16, 0x4400).ZeroPadToByte().ToReader());

        AssertDcQuant(matrices, new[] { 1f / 128, 1f / 64, 1f / 32 }, new[] { 128f, 64f, 32f });
    }

    [Fact]
    public void DecodeDc_ZeroFactor_Throws()
    {
        JxlDequantMatrices matrices = new();

        Assert.Throws<InvalidImageContentException>(() => matrices.DecodeDc(new JxlTestBitWriter().Bit(false).Write(16, 0x3C00).Write(16, 0).Write(16, 0x3C00).ZeroPadToByte().ToReader()));
    }

    private static void AssertDcQuant(JxlDequantMatrices matrices, float[] dcQuant, float[] invDcQuant)
    {
        for (int c = 0; c < 3; c++)
        {
            Assert.Equal(dcQuant[c], matrices.DcQuant(c));
            Assert.Equal(invDcQuant[c], matrices.InvDcQuant(c));
        }
    }
}
