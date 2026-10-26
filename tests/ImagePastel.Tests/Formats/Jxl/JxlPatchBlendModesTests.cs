// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// The expected values follow PerformBlending in lib/jxl/blending.cc and PerformAlphaBlending,
// PerformAlphaWeightedAdd and PerformMulBlending in lib/jxl/alpha.cc, of libjxl
// (https://github.com/libjxl/libjxl) at commit ef67fde2.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Patches in Mul and the four blend modes that use alpha. <c>cjxl</c> cannot write these modes, so the
/// expected values are computed here from libjxl's formulas, in units of 1 (8-bit samples / 255). The
/// background is the displayed frame and the foreground the patch: the Above modes draw the patch over
/// the frame, and the Below modes the frame over the patch.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlPatchBlendModesTests
{
    private const int Width = 24;
    private const int Height = 16;
    private const uint Slot = 3;

    private static readonly double[] ColourA = { 200 / 255D, 100 / 255D, 50 / 255D, 128 / 255D };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The 6 x 4 rectangle at (2, 1) of the ramp, placed at (5, 6) on ColourA, with the same mode for
    // the colour and the alpha. The ramp's alpha there is in range.
    [Theory]
    [InlineData("mul")]
    [InlineData("blend_above")]
    [InlineData("blend_below")]
    [InlineData("alpha_weighted_add_above")]
    [InlineData("alpha_weighted_add_below")]
    public void Patch_InEachMode_MatchesLibjxlsFormula(string name) => AssertCase(name);

    // The formulas, worked by hand at the patch's first pixel: the ramp's (28, 90, 20, 36) and ColourA's
    // (200, 100, 50, 128). Above, R = (28 a + 200 b (1 - a)) / A with a = 36 / 255, b = 128 / 255 and
    // A = 1 - (1 - a) (1 - b). Below swaps the two layers. The alpha is A both ways.
    [Theory]
    [InlineData("blend_above", 157.5685F, 97.5331F, 42.5992F, 145.9294F)]
    [InlineData("blend_below", 178.8675F, 98.7714F, 46.3141F, 145.9294F)]
    public void BlendAboveAndBelow_GiveDifferentResults(string name, float r, float g, float b, float a)
    {
        using JxlDecodedImage decoded = DecodeToPlanes(Case(name).Codestream);

        float[] want = { r, g, b, a };
        for (int c = 0; c < 4; c++)
        {
            float sample = Assert.Single(decoded.Frames).Planes[c].DangerousGetRowSpan(6)[5] * 255F;
            Assert.True(MathF.Abs(sample - want[c]) < 1e-3F, $"Channel {c}: {sample}, expected {want[c]}.");
        }
    }

    // A clamp flag on the colour and on the alpha. The Above modes and Mul take the 4 x 3 rectangle at
    // (20, 13) of the ramp, where its alpha is 306 to 351 and its red 238 to 273, onto ColourA. The Below
    // modes weight by the frame's alpha, so the ramp is the frame there, and the patch is ColourA at
    // (18, 12), where the ramp's alpha is 279 to 333.
    [Theory]
    [InlineData("mul_clamp")]
    [InlineData("mul_no_clamp")]
    [InlineData("blend_above_clamp")]
    [InlineData("blend_above_no_clamp")]
    [InlineData("blend_below_clamp")]
    [InlineData("blend_below_no_clamp")]
    [InlineData("alpha_weighted_add_above_clamp")]
    [InlineData("alpha_weighted_add_above_no_clamp")]
    [InlineData("alpha_weighted_add_below_clamp")]
    [InlineData("alpha_weighted_add_below_no_clamp")]
    [InlineData("blend_above_premultiplied_clamp")]
    [InlineData("blend_above_premultiplied_no_clamp")]
    [InlineData("blend_below_premultiplied_clamp")]
    [InlineData("blend_below_premultiplied_no_clamp")]
    public void ClampFlag_ClampsOutOfRangeValuesOnlyWhenSet(string name) => AssertCase(name);

    // The two cases of each mode decode differently, so the rectangles do hold out-of-range values.
    [Theory]
    [InlineData("mul")]
    [InlineData("blend_above")]
    [InlineData("blend_below")]
    [InlineData("alpha_weighted_add_above")]
    [InlineData("alpha_weighted_add_below")]
    [InlineData("blend_above_premultiplied")]
    [InlineData("blend_below_premultiplied")]
    public void ClampFlag_ChangesTheResult(string mode)
    {
        using JxlDecodedImage clamped = DecodeToPlanes(Case(mode + "_clamp").Codestream);
        using JxlDecodedImage unclamped = DecodeToPlanes(Case(mode + "_no_clamp").Codestream);

        Assert.True(Differ(clamped, unclamped), "The clamp flag changes nothing.");
    }

    [Theory]
    [InlineData("blend_above", "blend_below")]
    [InlineData("blend_above_premultiplied", "blend_below_premultiplied")]
    public void BlendAboveAndBelow_DecodeDifferently(string above, string below)
    {
        using JxlDecodedImage a = DecodeToPlanes(Case(above).Codestream);
        using JxlDecodedImage b = DecodeToPlanes(Case(below).Codestream);

        Assert.True(Differ(a, b), "BlendAbove and BlendBelow give the same planes.");
    }

    // The generated files with two alpha channels: two_alpha_b, (40, 220, 160) with alphas 64 and 192,
    // is the patch, and two_alpha_a, (200, 100, 50) with alphas 128 and 255, the frame.
    [Theory]
    [InlineData("two_alpha_colour_by_0")]
    [InlineData("two_alpha_colour_by_1")]
    [InlineData("two_alpha_extra_channel_by_0")]
    public void Patch_OnTwoAlphaChannels_BlendsWithTheAlphaChannelTheDictionaryNames(string name) => AssertCase(name);

    // With no alpha channel, libjxl adds for the AlphaWeightedAdd modes and copies the patch for both
    // Blend modes.
    [Theory]
    [InlineData("no_alpha_blend_above")]
    [InlineData("no_alpha_blend_below")]
    [InlineData("no_alpha_alpha_weighted_add_above")]
    [InlineData("no_alpha_alpha_weighted_add_below")]
    public void Patch_OnAnImageWithNoAlpha_GivesLibjxlsFallback(string name) => AssertCase(name);

    [Theory]
    [InlineData("blend_above_premultiplied")]
    [InlineData("blend_below_premultiplied")]
    public void BlendPatch_OnPremultipliedAlpha_UsesTheAssociatedAlphaFormula(string name) => AssertCase(name);

    // djxl decodes the same streams to the same planes, which checks the formulas above.
    [Theory]
    [InlineData("mul")]
    [InlineData("blend_above")]
    [InlineData("blend_below")]
    [InlineData("alpha_weighted_add_above")]
    [InlineData("alpha_weighted_add_below")]
    [InlineData("mul_clamp")]
    [InlineData("mul_no_clamp")]
    [InlineData("blend_above_clamp")]
    [InlineData("blend_above_no_clamp")]
    [InlineData("blend_below_clamp")]
    [InlineData("blend_below_no_clamp")]
    [InlineData("alpha_weighted_add_above_clamp")]
    [InlineData("alpha_weighted_add_above_no_clamp")]
    [InlineData("alpha_weighted_add_below_clamp")]
    [InlineData("alpha_weighted_add_below_no_clamp")]
    [InlineData("blend_above_premultiplied")]
    [InlineData("blend_below_premultiplied")]
    [InlineData("blend_above_premultiplied_clamp")]
    [InlineData("blend_above_premultiplied_no_clamp")]
    [InlineData("blend_below_premultiplied_clamp")]
    [InlineData("blend_below_premultiplied_no_clamp")]
    [InlineData("two_alpha_colour_by_0")]
    [InlineData("two_alpha_colour_by_1")]
    [InlineData("two_alpha_extra_channel_by_0")]
    [InlineData("no_alpha_blend_above")]
    [InlineData("no_alpha_blend_below")]
    [InlineData("no_alpha_alpha_weighted_add_above")]
    [InlineData("no_alpha_alpha_weighted_add_below")]
    public async Task DecodeToPlanes_MatchesDjxl(string name)
    {
        byte[] codestream = Case(name).Codestream;
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlPatchBlendModesTests));
        string input = Path.Combine(directory, name + ".jxl");
        string output = Path.Combine(directory, name + ".npy");
        await File.WriteAllBytesAsync(input, codestream, CancellationToken);

        await JxlTools.RunAsync("djxl", CancellationToken, input, output);

        NpyArray expected = NpyArray.Load(output);
        using JxlDecodedImage decoded = DecodeToPlanes(codestream);
        JxlConformanceComparer.AssertMatchesDjxl(expected, JxlConformanceImages.ToNpy(decoded));
    }

    private static void AssertCase(string name)
    {
        BlendCase blendCase = Case(name);
        AssertEveryPixel(DecodeToPlanes(blendCase.Codestream), blendCase.NumPlanes, blendCase.Expected);
    }

    private static BlendCase Case(string name) => name switch
    {
        "mul" => InRange(JxlPatchBlendMode.Mul),
        "blend_above" => InRange(JxlPatchBlendMode.BlendAbove),
        "blend_below" => InRange(JxlPatchBlendMode.BlendBelow),
        "alpha_weighted_add_above" => InRange(JxlPatchBlendMode.AlphaWeightedAddAbove),
        "alpha_weighted_add_below" => InRange(JxlPatchBlendMode.AlphaWeightedAddBelow),
        "mul_clamp" => OutOfRange(JxlPatchBlendMode.Mul, true),
        "mul_no_clamp" => OutOfRange(JxlPatchBlendMode.Mul, false),
        "blend_above_clamp" => OutOfRange(JxlPatchBlendMode.BlendAbove, true),
        "blend_above_no_clamp" => OutOfRange(JxlPatchBlendMode.BlendAbove, false),
        "blend_below_clamp" => OutOfRange(JxlPatchBlendMode.BlendBelow, true),
        "blend_below_no_clamp" => OutOfRange(JxlPatchBlendMode.BlendBelow, false),
        "alpha_weighted_add_above_clamp" => OutOfRange(JxlPatchBlendMode.AlphaWeightedAddAbove, true),
        "alpha_weighted_add_above_no_clamp" => OutOfRange(JxlPatchBlendMode.AlphaWeightedAddAbove, false),
        "alpha_weighted_add_below_clamp" => OutOfRange(JxlPatchBlendMode.AlphaWeightedAddBelow, true),
        "alpha_weighted_add_below_no_clamp" => OutOfRange(JxlPatchBlendMode.AlphaWeightedAddBelow, false),
        "blend_above_premultiplied_clamp" => OutOfRange(JxlPatchBlendMode.BlendAbove, true, premultiplied: true),
        "blend_above_premultiplied_no_clamp" => OutOfRange(JxlPatchBlendMode.BlendAbove, false, premultiplied: true),
        "blend_below_premultiplied_clamp" => OutOfRange(JxlPatchBlendMode.BlendBelow, true, premultiplied: true),
        "blend_below_premultiplied_no_clamp" => OutOfRange(JxlPatchBlendMode.BlendBelow, false, premultiplied: true),
        "blend_above_premultiplied" => InRange(JxlPatchBlendMode.BlendAbove, premultiplied: true),
        "blend_below_premultiplied" => InRange(JxlPatchBlendMode.BlendBelow, premultiplied: true),
        "two_alpha_colour_by_0" => TwoAlphaColourBlend(0),
        "two_alpha_colour_by_1" => TwoAlphaColourBlend(1),
        "two_alpha_extra_channel_by_0" => TwoAlphaExtraChannelBlend(),
        "no_alpha_blend_above" => NoAlpha(JxlPatchBlendMode.BlendAbove),
        "no_alpha_blend_below" => NoAlpha(JxlPatchBlendMode.BlendBelow),
        "no_alpha_alpha_weighted_add_above" => NoAlpha(JxlPatchBlendMode.AlphaWeightedAddAbove),
        "no_alpha_alpha_weighted_add_below" => NoAlpha(JxlPatchBlendMode.AlphaWeightedAddBelow),
        _ => throw new ArgumentException(name, nameof(name)),
    };

    private static BlendCase InRange(JxlPatchBlendMode mode, bool premultiplied = false)
    {
        JxlTestPatchBlending blending = new(mode);
        byte[] codestream = Layers(
            new JxlTestLayers(Width, Height, premultiplied: premultiplied),
            JxlTestLayers.Ramp,
            JxlTestLayers.ColourA,
            new JxlTestRefPatch(Slot, 2, 1, 6, 4, new JxlTestPatch(5, 6, blending, blending)));
        return new BlendCase(codestream, 4, (x, y) => Inside(x, y, 5, 6, 6, 4) ? Blend(mode, ColourA, Ramp(x - 5 + 2, y - 6 + 1), false, premultiplied) : ColourA);
    }

    private static BlendCase OutOfRange(JxlPatchBlendMode mode, bool clamp, bool premultiplied = false)
    {
        JxlTestPatchBlending blending = new(mode, Clamp: clamp);
        if (mode is JxlPatchBlendMode.BlendBelow or JxlPatchBlendMode.AlphaWeightedAddBelow)
        {
            byte[] below = Layers(new JxlTestLayers(Width, Height, premultiplied: premultiplied), JxlTestLayers.ColourA, JxlTestLayers.Ramp, new JxlTestRefPatch(Slot, 0, 0, 4, 3, new JxlTestPatch(18, 12, blending, blending)));
            return new BlendCase(below, 4, (x, y) => Inside(x, y, 18, 12, 4, 3) ? Blend(mode, Ramp(x, y), ColourA, clamp, premultiplied) : Ramp(x, y));
        }

        byte[] above = Layers(new JxlTestLayers(Width, Height, premultiplied: premultiplied), JxlTestLayers.Ramp, JxlTestLayers.ColourA, new JxlTestRefPatch(Slot, 20, 13, 4, 3, new JxlTestPatch(2, 2, blending, blending)));
        return new BlendCase(above, 4, (x, y) => Inside(x, y, 2, 2, 4, 3) ? Blend(mode, ColourA, Ramp(x - 2 + 20, y - 2 + 13), clamp, premultiplied) : ColourA);
    }

    // A BlendAbove patch whose colour is blended by alpha channel k. Both extra channels are replaced,
    // but blend_weighted then writes the blended alpha k over channel k.
    private static BlendCase TwoAlphaColourBlend(uint k)
    {
        JxlTestPatchBlending replace = new(JxlPatchBlendMode.Replace);
        byte[] codestream = TwoAlphaLayers(new JxlTestPatch(4, 6, new(JxlPatchBlendMode.BlendAbove, k), replace, replace));
        double[] patch = TwoAlphaB;
        double[] frame = TwoAlphaA;
        double[] blended = new double[5];
        for (int c = 0; c < 3; c++)
        {
            blended[c] = BlendColour(frame[c], frame[3 + k], patch[c], patch[3 + k], false, false);
        }

        blended[3] = patch[3];
        blended[4] = patch[4];
        blended[3 + k] = BlendAlpha(frame[3 + k], patch[3 + k], false);
        return new BlendCase(codestream, 5, (x, y) => Inside(x, y, 4, 6, 5, 3) ? blended : frame);
    }

    // A Replace patch whose second extra channel is blended with the first as its alpha.
    private static BlendCase TwoAlphaExtraChannelBlend()
    {
        JxlTestPatchBlending replace = new(JxlPatchBlendMode.Replace);
        byte[] codestream = TwoAlphaLayers(new JxlTestPatch(4, 6, replace, replace, new(JxlPatchBlendMode.BlendAbove, 0)));
        double[] patch = TwoAlphaB;
        double[] frame = TwoAlphaA;
        double[] blended = { patch[0], patch[1], patch[2], patch[3], BlendColour(frame[4], frame[3], patch[4], patch[3], false, false) };
        return new BlendCase(codestream, 5, (x, y) => Inside(x, y, 4, 6, 5, 3) ? blended : frame);
    }

    private static double[] TwoAlphaA => new[] { 200 / 255D, 100 / 255D, 50 / 255D, 128 / 255D, 255 / 255D };

    private static double[] TwoAlphaB => new[] { 40 / 255D, 220 / 255D, 160 / 255D, 64 / 255D, 192 / 255D };

    // The 5 x 3 rectangle at (0, 0) of two_alpha_b in slot 3, placed on two_alpha_a.
    private static byte[] TwoAlphaLayers(JxlTestPatch patch)
    {
        string directory = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated");
        JxlTestLayers layers = JxlTestLayers.FromFile(File.ReadAllBytes(Path.Combine(directory, "two_alpha_a.jxl")), out byte[] frame);
        JxlTestLayers.FromFile(File.ReadAllBytes(Path.Combine(directory, "two_alpha_b.jxl")), out byte[] reference);
        return Layers(layers, reference, frame, new JxlTestRefPatch(Slot, 0, 0, 5, 3, patch));
    }

    // RGB layers: the 6 x 4 rectangle at (2, 1) of the ramp, placed at (5, 6) on ColourA.
    private static BlendCase NoAlpha(JxlPatchBlendMode mode)
    {
        byte[] codestream = Layers(
            new JxlTestLayers(Width, Height, alpha: false),
            JxlTestLayers.Ramp,
            JxlTestLayers.ColourA,
            new JxlTestRefPatch(Slot, 2, 1, 6, 4, new JxlTestPatch(5, 6, new JxlTestPatchBlending(mode))));
        return new BlendCase(codestream, 3, (x, y) =>
        {
            if (!Inside(x, y, 5, 6, 6, 4))
            {
                return ColourA;
            }

            double[] fg = Ramp(x - 5 + 2, y - 6 + 1);
            bool add = mode is JxlPatchBlendMode.AlphaWeightedAddAbove or JxlPatchBlendMode.AlphaWeightedAddBelow;
            return add ? new[] { ColourA[0] + fg[0], ColourA[1] + fg[1], ColourA[2] + fg[2] } : fg;
        });
    }

    // One layer of RGBA patch fg on frame bg, with the same mode and clamp for the colour and the alpha.
    private static double[] Blend(JxlPatchBlendMode mode, double[] bg, double[] fg, bool clamp, bool premultiplied)
    {
        double[] result = new double[4];
        for (int c = 0; c < 3; c++)
        {
            result[c] = mode switch
            {
                JxlPatchBlendMode.Mul => Mul(bg[c], fg[c], clamp),
                JxlPatchBlendMode.BlendAbove => BlendColour(bg[c], bg[3], fg[c], fg[3], clamp, premultiplied),
                JxlPatchBlendMode.BlendBelow => BlendColour(fg[c], fg[3], bg[c], bg[3], clamp, premultiplied),
                JxlPatchBlendMode.AlphaWeightedAddAbove => WeightedAdd(bg[c], fg[c], fg[3], clamp),
                _ => WeightedAdd(fg[c], bg[c], bg[3], clamp),
            };
        }

        // The alpha, blended in the same mode with itself as its alpha: kAlphaWeightedAdd keeps the
        // bottom alpha (PerformAlphaWeightedAdd copies it when fg == fga).
        result[3] = mode switch
        {
            JxlPatchBlendMode.Mul => Mul(bg[3], fg[3], clamp),
            JxlPatchBlendMode.BlendAbove => BlendAlpha(bg[3], fg[3], clamp),
            JxlPatchBlendMode.BlendBelow => BlendAlpha(fg[3], bg[3], clamp),
            JxlPatchBlendMode.AlphaWeightedAddAbove => bg[3],
            _ => fg[3],
        };
        return result;
    }

    // PerformAlphaBlending for a channel that is not alpha: bottom b of alpha ba, top t of alpha ta.
    private static double BlendColour(double b, double ba, double t, double ta, bool clamp, bool premultiplied)
    {
        double fa = clamp ? Clamp01(ta) : ta;
        if (premultiplied)
        {
            return t + (b * (1 - fa));
        }

        double newA = 1 - ((1 - fa) * (1 - ba));
        double rnewA = newA > 0 ? 1 / newA : 0;
        return ((t * fa) + (b * ba * (1 - fa))) * rnewA;
    }

    // PerformAlphaBlending for the alpha channel.
    private static double BlendAlpha(double ba, double ta, bool clamp)
    {
        double fa = clamp ? Clamp01(ta) : ta;
        return 1 - ((1 - fa) * (1 - ba));
    }

    // PerformAlphaWeightedAdd.
    private static double WeightedAdd(double b, double t, double ta, bool clamp) => b + (t * (clamp ? Clamp01(ta) : ta));

    // PerformMulBlending.
    private static double Mul(double b, double t, bool clamp) => b * (clamp ? Clamp01(t) : t);

    private static double Clamp01(double v) => Math.Min(Math.Max(v, 0), 1);

    // The reference frame of section refSection in slot 3, saved before its colour transform, then the
    // displayed frame of section frameSection with the patches.
    private static byte[] Layers(JxlTestLayers layers, byte[] refSection, byte[] frameSection, params JxlTestRefPatch[] refPatches)
        => layers
            .Add(new JxlTestFrame(refSection) { Type = JxlFrameType.ReferenceOnly, SaveAsReference = Slot, SaveBeforeColorTransform = true })
            .Add(JxlTestFrame.Last(frameSection) with { Patches = new JxlTestPatchDictionary(refPatches) })
            .ToArray();

    private static bool Inside(int x, int y, int x0, int y0, int xsize, int ysize) => x >= x0 && x < x0 + xsize && y >= y0 && y < y0 + ysize;

    // The Ramp layer at (x, y): (7 k, 90, 5 k, 9 k) / 255 with k = x + y + 1.
    private static double[] Ramp(int x, int y)
    {
        int k = x + y + 1;
        return new[] { 7 * k / 255D, 90 / 255D, 5 * k / 255D, 9 * k / 255D };
    }

    private static JxlDecodedImage DecodeToPlanes(byte[] codestream)
    {
        using MemoryStream stream = new(codestream);
        return new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
    }

    private static bool Differ(JxlDecodedImage a, JxlDecodedImage b)
    {
        JxlDecodedFrame fa = Assert.Single(a.Frames);
        JxlDecodedFrame fb = Assert.Single(b.Frames);
        for (int c = 0; c < fa.Planes.Count; c++)
        {
            for (int y = 0; y < fa.Planes[c].Height; y++)
            {
                if (!fa.Planes[c].DangerousGetRowSpan(y).SequenceEqual(fb.Planes[c].DangerousGetRowSpan(y)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Checks every sample of numPlanes planes. The planes are not clamped.
    private static void AssertEveryPixel(JxlDecodedImage decoded, int numPlanes, Func<int, int, double[]> expected)
    {
        using (decoded)
        {
            JxlDecodedFrame frame = Assert.Single(decoded.Frames);
            Assert.Equal(numPlanes, frame.Planes.Count);
            for (int y = 0; y < frame.Planes[0].Height; y++)
            {
                for (int x = 0; x < frame.Planes[0].Width; x++)
                {
                    double[] want = expected(x, y);
                    for (int c = 0; c < numPlanes; c++)
                    {
                        float sample = frame.Planes[c].DangerousGetRowSpan(y)[x];
                        Assert.True(Math.Abs(sample - want[c]) < 1e-5, $"({x}, {y}) channel {c}: {sample * 255F}, expected {want[c] * 255D}.");
                    }
                }
            }
        }
    }

    private sealed record BlendCase(byte[] Codestream, int NumPlanes, Func<int, int, double[]> Expected);
}
