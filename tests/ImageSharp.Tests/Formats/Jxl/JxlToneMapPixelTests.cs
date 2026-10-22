// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Icc;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Checks libjxl's <c>ToneMapPixel</c>, which gives the PCS Lab bytes of the 3D LUT in the profile of a PQ
/// or HLG image that a <c>cicp</c> tag can name, against the <c>A2B0</c> grid of the profile <c>djxl</c>
/// builds for the same file.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlToneMapPixelTests
{
    // The files are Rec2100HLG, Rec2100PQ and P3 primaries with HLG, all with a D65 white point (the
    // JxlPrimaries and JxlTransferFunction values are internal, so the rows give them as numbers). libjxl's
    // maths may use FMA on AVX2 and the CRT's pow, exp and cbrt, so one Lab step is allowed.
    // Measured: 0 for all three (every byte equal to djxl's).
    [Theory]
    [InlineData("hlg", 9, 18)]
    [InlineData("pq", 9, 16)]
    [InlineData("p3_hlg", 11, 18)]
    public void ToneMapPixel_OnTheLutGrid_GivesDjxlsLabBytes(string name, int primaries, int transferFunction)
    {
        JxlColorEncodingHeader c = JxlDjxlProfiles.ColourEncoding(name);
        Assert.Equal(JxlWhitePoint.D65, c.WhitePoint);
        Assert.Equal((JxlPrimaries)primaries, c.Primaries);
        Assert.Equal((JxlTransferFunction)transferFunction, c.Tf.TransferFunction);
        byte[] expected = JxlDjxlProfiles.A2B0Clut(JxlDjxlProfiles.Profile(name));

        const int n = JxlDjxlProfiles.LutGridPoints;
        int largestDifference = 0;
        int different = 0;
        Span<float> input = stackalloc float[3];
        Span<byte> lab = stackalloc byte[3];
        for (int ix = 0; ix < n; ix++)
        {
            for (int iy = 0; iy < n; iy++)
            {
                for (int ib = 0; ib < n; ib++)
                {
                    // libjxl: ix * (1.0f / (k3DLutDim - 1)), a float product.
                    input[0] = ix * (1.0f / (n - 1));
                    input[1] = iy * (1.0f / (n - 1));
                    input[2] = ib * (1.0f / (n - 1));

                    JxlIccBuilder.ToneMapPixel(c, input, lab);

                    int node = ((((ix * n) + iy) * n) + ib) * 3;
                    for (int k = 0; k < 3; k++)
                    {
                        int difference = Math.Abs(lab[k] - expected[node + k]);
                        largestDifference = Math.Max(largestDifference, difference);
                        different += difference == 0 ? 0 : 1;
                    }
                }
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{name}: largest Lab-byte difference {largestDifference}, {different} bytes differ");
        Assert.True(largestDifference <= 1, $"Largest Lab-byte difference {largestDifference}, {different} bytes differ.");
    }
}
