// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Memory;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The pixel statistics behind the X and B quantisation scales from Squirrel down (libjxl's
/// <c>PixelStatsForChromacityAdjustment</c>), on fixed planes.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlPixelStatsForChromacityAdjustmentTests
{
    // From a harness of lib/jxl/enc_frame.cc:597-670 at ef67fde2, built with -ffp-contract=off
    // (e5-notes/e5f-scratch/pixel_stats.cc, which compiles libjxl's struct verbatim). The statistics are
    // plain maxima and differences with no lane sums, so they are exact: dx, db and exposed_blue as bits,
    // then HowMuchIsXChannelPixelized and HowMuchIsBChannelPixelized.
    //
    // "x" and "b": 2x2 zero planes but X or B at (1,1) = the float of the argument's bits, so dx or db is
    // that float; the three floats around each threshold of the ladders (0.015, 0.022, 0.026 for X, compared
    // with >=; 0.28, 0.33, 0.38 for B, compared with >) land on either side. The thresholds are doubles, so
    // 0.015f (below 0.015) gives 0 and 0.026f (above 0.026) gives 3. "eb": exposed_blue = k 2^-23 around
    // 0.13 with db = 2, so the B ladder's add shows. "dprod": exposed_b = cur_b - cur_y * 1.2 is a double
    // product; these Y values round differently as a float product. "rand": XYB-like random planes, the
    // whole plane as the rect (as libjxl passes the padded image), and one sub-rect.
    [Theory]
    [InlineData("x", "3C75C28E", 0x3C75C28Eu, 0u, 0u, 0, 0)]
    [InlineData("x", "3C75C28F", 0x3C75C28Fu, 0u, 0u, 0, 0)]
    [InlineData("x", "3C75C290", 0x3C75C290u, 0u, 0u, 1, 0)]
    [InlineData("x", "3CB43957", 0x3CB43957u, 0u, 0u, 1, 0)]
    [InlineData("x", "3CB43958", 0x3CB43958u, 0u, 0u, 1, 0)]
    [InlineData("x", "3CB43959", 0x3CB43959u, 0u, 0u, 2, 0)]
    [InlineData("x", "3CD4FDF3", 0x3CD4FDF3u, 0u, 0u, 2, 0)]
    [InlineData("x", "3CD4FDF4", 0x3CD4FDF4u, 0u, 0u, 3, 0)]
    [InlineData("x", "3CD4FDF5", 0x3CD4FDF5u, 0u, 0u, 3, 0)]
    [InlineData("b", "3E8F5C28", 0u, 0x3E8F5C28u, 0x3E20902Cu, 0, 0)]
    [InlineData("b", "3E8F5C29", 0u, 0x3E8F5C29u, 0x3E20902Eu, 0, 1)]
    [InlineData("b", "3E8F5C2A", 0u, 0x3E8F5C2Au, 0x3E209030u, 0, 1)]
    [InlineData("b", "3EA8F5C2", 0u, 0x3EA8F5C2u, 0x3E5F06F5u, 0, 1)]
    [InlineData("b", "3EA8F5C3", 0u, 0x3EA8F5C3u, 0x3E5F06F8u, 0, 2)]
    [InlineData("b", "3EA8F5C4", 0u, 0x3EA8F5C4u, 0x3E5F06FAu, 0, 2)]
    [InlineData("b", "3EC28F5B", 0u, 0x3EC28F5Bu, 0x3E93DD96u, 0, 2)]
    [InlineData("b", "3EC28F5C", 0u, 0x3EC28F5Cu, 0x3E93DD98u, 0, 2)]
    [InlineData("b", "3EC28F5D", 0u, 0x3EC28F5Du, 0x3E93DD99u, 0, 3)]
    [InlineData("eb", "1090518", 0u, 0x40000000u, 0x3E051EB0u, 0, 2)]
    [InlineData("eb", "1090519", 0u, 0x40000000u, 0x3E051EB8u, 0, 2)]
    [InlineData("eb", "1090520", 0u, 0x40000000u, 0x3E051EC0u, 0, 3)]
    [InlineData("dprod", "3F333333", 0u, 0x3E99999Au, 0x3EA3D70Bu, 0, 1)]
    [InlineData("dprod", "3F1C28F6", 0u, 0x3EC7AE14u, 0x3F09374Bu, 0, 3)]
    [InlineData("dprod", "3F547AE1", 0u, 0x3E2E147Cu, 0x3C03129Au, 0, 0)]
    [InlineData("rand", "16x16:1:0.1", 0x3DC7DC00u, 0x3F970880u, 0x3EB14B04u, 3, 3)]
    [InlineData("rand", "64x40:2:0.1", 0x3DCC0800u, 0x3FA5DA00u, 0x3EFDF6CFu, 3, 3)]
    [InlineData("rand", "24x8:3:0.02", 0x3C96F333u, 0x3F9E5640u, 0x3E919DADu, 1, 3)]
    [InlineData("rand", "1x1:4:0.1", 0u, 0u, 0u, 0, 0)]
    [InlineData("rect", "64x40:2:0.1", 0x3DC64666u, 0x3F9F1780u, 0x3ED133C7u, 3, 3)]
    public void Calc_MatchesLibjxl(string kind, string argument, uint expectedDx, uint expectedDb, uint expectedExposedBlue, int expectedX, int expectedB)
    {
        (Buffer2D<float>[] opsin, JxlRect rect) = Planes(kind, argument);
        try
        {
            JxlPixelStatsForChromacityAdjustment stats = default;
            stats.Calc(opsin, rect);

            Assert.Equal(expectedDx, BitConverter.SingleToUInt32Bits(stats.Dx));
            Assert.Equal(expectedDb, BitConverter.SingleToUInt32Bits(stats.Db));
            Assert.Equal(expectedExposedBlue, BitConverter.SingleToUInt32Bits(stats.ExposedBlue));
            Assert.Equal(expectedX, stats.HowMuchIsXChannelPixelized());
            Assert.Equal(expectedB, stats.HowMuchIsBChannelPixelized());
        }
        finally
        {
            foreach (Buffer2D<float> plane in opsin)
            {
                plane.Dispose();
            }
        }
    }

    // Without Calc (above Squirrel) every statistic is 0, so both ladders give 0.
    [Fact]
    public void Default_IsNotPixelized()
    {
        JxlPixelStatsForChromacityAdjustment stats = default;

        Assert.Equal(0, stats.HowMuchIsXChannelPixelized());
        Assert.Equal(0, stats.HowMuchIsBChannelPixelized());
    }

    private static (Buffer2D<float>[] Opsin, JxlRect Rect) Planes(string kind, string argument)
    {
        switch (kind)
        {
            case "x":
            case "b":
            {
                Buffer2D<float>[] p = Zero(2, 2);
                p[kind == "x" ? 0 : 2][1, 1] = Bits(argument);
                return (p, new JxlRect(0, 0, 2, 2));
            }

            case "eb":
            {
                Buffer2D<float>[] p = Zero(3, 2);
                p[2][1, 0] = 1.0f;
                p[2][0, 1] = 1.0f + (int.Parse(argument, CultureInfo.InvariantCulture) / 8388608.0f);
                p[2][1, 1] = 1.0f;
                p[2][2, 1] = -1.0f;
                return (p, new JxlRect(0, 0, 3, 2));
            }

            case "dprod":
            {
                Buffer2D<float>[] p = Zero(2, 2);
                p[1][1, 1] = Bits(argument);
                p[2][1, 1] = 1.0f;
                return (p, new JxlRect(0, 0, 2, 2));
            }

            default:
            {
                string[] parts = argument.Split(':');
                string[] size = parts[0].Split('x');
                int xsize = int.Parse(size[0], CultureInfo.InvariantCulture);
                int ysize = int.Parse(size[1], CultureInfo.InvariantCulture);
                Buffer2D<float>[] p = Random(xsize, ysize, uint.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture));
                return (p, kind == "rect" ? new JxlRect(8, 8, 24, 16) : new JxlRect(0, 0, xsize, ysize));
            }
        }
    }

    private static float Bits(string hex) => BitConverter.UInt32BitsToSingle(uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static Buffer2D<float>[] Zero(int xsize, int ysize)
    {
        Buffer2D<float>[] planes = new Buffer2D<float>[3];
        for (int c = 0; c < 3; c++)
        {
            planes[c] = Configuration.Default.MemoryAllocator.Allocate2D<float>(xsize, ysize, AllocationOptions.Clean);
        }

        return planes;
    }

    // The harness's planes: an LCG s = s * 1664525 + 1013904223, u = ((s >> 8) & 0xFFFF) / 65536 - 0.5, in
    // channel, row, column order; X = scale * u, Y = u + 0.5, B = 0.5 * u + 0.4.
    private static Buffer2D<float>[] Random(int xsize, int ysize, uint seed, float xScale)
    {
        Buffer2D<float>[] planes = Zero(xsize, ysize);
        uint s = seed;
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < ysize; y++)
            {
                for (int x = 0; x < xsize; x++)
                {
                    s = unchecked((s * 1664525u) + 1013904223u);
                    float u = (((s >> 8) & 0xFFFF) / 65536.0f) - 0.5f;
                    planes[c][x, y] = c == 0 ? xScale * u : (c == 1 ? u + 0.5f : (0.5f * u) + 0.4f);
                }
            }
        }

        return planes;
    }
}
