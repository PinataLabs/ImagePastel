// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// The expected values follow PerformBlending in lib/jxl/blending.cc and PerformAlphaBlending,
// PerformAlphaWeightedAdd and PerformMulBlending in lib/jxl/alpha.cc, of libjxl
// (https://github.com/libjxl/libjxl) at commit ef67fde2.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Patches in Mul and the four blend modes that use alpha. <c>cjxl</c> cannot write these modes, so the
/// expected values are computed here from libjxl's formulas, in units of 1 (8-bit samples / 255).
/// </summary>
[Trait("Format", "Jxl")]
public class JxlPatchBlendModesTests
{
    private const int Width = 24;
    private const int Height = 16;
    private const uint Slot = 3;

    private static readonly Pixel ColourA = new(200 / 255D, 100 / 255D, 50 / 255D, 128 / 255D);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void MulPatch_MultipliesTheFrameByThePatch()
    {
        // The 6 x 4 rectangle at (2, 1) of the ramp, placed at (5, 6) on ColourA.
        JxlTestPatchBlending mul = new(JxlPatchBlendMode.Mul);
        byte[] codestream = Layers(JxlTestLayers.Ramp, JxlTestLayers.ColourA, new JxlTestRefPatch(Slot, 2, 1, 6, 4, new JxlTestPatch(5, 6, mul, mul)));

        AssertEveryPixel(DecodeToPlanes(codestream), 4, (x, y) =>
        {
            if (!Inside(x, y, 5, 6, 6, 4))
            {
                return ColourA;
            }

            Pixel fg = Ramp(x - 5 + 2, y - 6 + 1);
            return new Pixel(ColourA.R * fg.R, ColourA.G * fg.G, ColourA.B * fg.B, ColourA.A * fg.A);
        });
    }

    // The reference frame of section refSection in slot 3, saved before its colour transform, then the
    // displayed frame of section frameSection with the patches.
    private static byte[] Layers(byte[] refSection, byte[] frameSection, params JxlTestRefPatch[] refPatches)
        => Layers(new JxlTestLayers(Width, Height), refSection, frameSection, refPatches);

    private static byte[] Layers(JxlTestLayers layers, byte[] refSection, byte[] frameSection, params JxlTestRefPatch[] refPatches)
        => layers
            .Add(new JxlTestFrame(refSection) { Type = JxlFrameType.ReferenceOnly, SaveAsReference = Slot, SaveBeforeColorTransform = true })
            .Add(JxlTestFrame.Last(frameSection) with { Patches = new JxlTestPatchDictionary(refPatches) })
            .ToArray();

    private static bool Inside(int x, int y, int x0, int y0, int xsize, int ysize) => x >= x0 && x < x0 + xsize && y >= y0 && y < y0 + ysize;

    // The Ramp layer at (x, y): (7 k, 90, 5 k, 9 k) / 255 with k = x + y + 1.
    private static Pixel Ramp(int x, int y)
    {
        int k = x + y + 1;
        return new Pixel(7 * k / 255D, 90 / 255D, 5 * k / 255D, 9 * k / 255D);
    }

    private static JxlDecodedImage DecodeToPlanes(byte[] codestream)
    {
        using MemoryStream stream = new(codestream);
        return new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
    }

    // Checks the first numPlanes planes of every pixel. The planes are not clamped.
    private static void AssertEveryPixel(JxlDecodedImage decoded, int numPlanes, Func<int, int, Pixel> expected)
    {
        using (decoded)
        {
            JxlDecodedFrame frame = Assert.Single(decoded.Frames);
            Assert.Equal(numPlanes, frame.Planes.Count);
            for (int y = 0; y < frame.Planes[0].Height; y++)
            {
                for (int x = 0; x < frame.Planes[0].Width; x++)
                {
                    Pixel want = expected(x, y);
                    for (int c = 0; c < numPlanes; c++)
                    {
                        float sample = frame.Planes[c].DangerousGetRowSpan(y)[x];
                        Assert.True(Math.Abs(sample - want[c]) < 1e-5, $"({x}, {y}) channel {c}: {sample * 255F}, expected {want[c] * 255D}.");
                    }
                }
            }
        }
    }

    private readonly record struct Pixel(double R, double G, double B, double A)
    {
        public double this[int c] => c switch
        {
            0 => this.R,
            1 => this.G,
            2 => this.B,
            _ => this.A,
        };
    }
}
