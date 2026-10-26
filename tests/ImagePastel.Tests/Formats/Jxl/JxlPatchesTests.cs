// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Patches: a reference-only frame, saved before its colour transform to slot 3 as <c>cjxl</c> does,
/// holds the <see cref="JxlTestLayers.Ramp"/> layer, and the displayed <see cref="JxlTestLayers.ColourA"/>
/// frame takes rectangles of it as patches.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlPatchesTests
{
    private const int Width = 24;
    private const int Height = 16;
    private const uint Slot = 3;

    private static readonly (float R, float G, float B, float A) ColourA = (200, 100, 50, 128);

    private static readonly JxlTestPatchBlending Replace = new(JxlPatchBlendMode.Replace);
    private static readonly JxlTestPatchBlending Add = new(JxlPatchBlendMode.Add);
    private static readonly JxlTestPatchBlending None = new(JxlPatchBlendMode.None);

    // The positions after the first are deltas, up and down, left and right.
    private static readonly (int X, int Y)[] SeveralPositions = { (1, 1), (9, 2), (3, 10), (18, 12), (17, 0) };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void ReplacePatch_CopiesTheRectangleOfTheReferenceFrame()
    {
        // The 4 x 3 rectangle at (2, 1) of the reference frame, placed at (5, 6).
        AssertEveryPixel(DecodeToPlanes(Case("replace")), (x, y) => Inside(x, y, 5, 6, 4, 3) ? Ramp(x - 5 + 2, y - 6 + 1) : ColourA);
    }

    [Fact]
    public void AddPatch_WithNoneOnTheExtraChannel_SumsTheColourAndLeavesTheAlpha()
    {
        AssertEveryPixel(DecodeToPlanes(Case("add_colour_none_alpha")), (x, y) =>
        {
            if (!Inside(x, y, 10, 3, 6, 4))
            {
                return ColourA;
            }

            (float r, float g, float b, _) = Ramp(x - 10, y - 3);
            return (ColourA.R + r, ColourA.G + g, ColourA.B + b, ColourA.A);
        });
    }

    [Fact]
    public void NonePatch_OnTheColour_KeepsTheColourAndReplacesTheAlpha()
    {
        AssertEveryPixel(DecodeToPlanes(Case("none_colour_replace_alpha")), (x, y) => Inside(x, y, 0, 0, 3, 3) ? (ColourA.R, ColourA.G, ColourA.B, Ramp(x + 1, y + 1).A) : ColourA);
    }

    [Fact]
    public void OverlappingPatches_AreAppliedInStreamOrder()
    {
        // A Replace patch at (4, 4), then an Add patch at (6, 5) over part of it: the overlap is the sum
        // of both rectangles. The other way round, the Replace patch would win.
        AssertEveryPixel(DecodeToPlanes(Case("replace_then_add")), (x, y) =>
        {
            bool first = Inside(x, y, 4, 4, 5, 5);
            bool second = Inside(x, y, 6, 5, 5, 5);
            (float R, float G, float B, float A) under = first ? Ramp(x - 4, y - 4) : ColourA;
            if (!second)
            {
                return under;
            }

            (float r, float g, float b, float a) = Ramp(x - 6 + 8, y - 5 + 2);
            return (under.R + r, under.G + g, under.B + b, a);
        });
    }

    [Fact]
    public void OverlappingPatches_LaterReplaceWins()
    {
        AssertEveryPixel(DecodeToPlanes(Case("add_then_replace")), (x, y) =>
        {
            if (Inside(x, y, 4, 4, 5, 5))
            {
                return Ramp(x - 4, y - 4);
            }

            if (!Inside(x, y, 6, 5, 5, 5))
            {
                return ColourA;
            }

            (float r, float g, float b, float a) = Ramp(x - 6 + 8, y - 5 + 2);
            return (ColourA.R + r, ColourA.G + g, ColourA.B + b, a);
        });
    }

    [Fact]
    public void Patch_TouchingTheRightAndBottomEdges_Decodes()
    {
        AssertEveryPixel(DecodeToPlanes(Case("right_bottom_edges")), (x, y) => Inside(x, y, Width - 3, Height - 2, 3, 2) ? Ramp(x - (Width - 3), y - (Height - 2)) : ColourA);
    }

    [Fact]
    public void OneReferencePatch_AtSeveralPositions_IsPlacedAtEach()
    {
        AssertEveryPixel(DecodeToPlanes(Case("several_positions")), (x, y) =>
        {
            foreach ((int px, int py) in SeveralPositions)
            {
                if (Inside(x, y, px, py, 2, 3))
                {
                    return Ramp(x - px + 4, y - py + 3);
                }
            }

            return ColourA;
        });
    }

    [Fact]
    public void EmptyDictionary_LeavesTheFrame()
    {
        AssertEveryPixel(DecodeToPlanes(Case("empty")), (x, y) => ColourA);
    }

    // djxl decodes the same streams to the same planes.
    [Theory]
    [InlineData("replace")]
    [InlineData("add_colour_none_alpha")]
    [InlineData("none_colour_replace_alpha")]
    [InlineData("replace_then_add")]
    [InlineData("add_then_replace")]
    [InlineData("right_bottom_edges")]
    [InlineData("several_positions")]
    [InlineData("empty")]
    public async Task DecodeToPlanes_MatchesDjxl(string name)
    {
        byte[] codestream = Case(name);
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlPatchesTests));
        string input = Path.Combine(directory, name + ".jxl");
        string output = Path.Combine(directory, name + ".npy");
        await File.WriteAllBytesAsync(input, codestream, CancellationToken);

        await JxlTools.RunAsync("djxl", CancellationToken, input, output);

        NpyArray expected = NpyArray.Load(output);
        using JxlDecodedImage decoded = DecodeToPlanes(codestream);
        JxlConformanceComparer.AssertMatchesDjxl(expected, JxlConformanceImages.ToNpy(decoded));
    }

    [Fact]
    public void Identify_FrameWithPatches_ReadsTheImageHeader()
    {
        byte[] codestream = Layers(new JxlTestRefPatch(Slot, 0, 0, 2, 2, new JxlTestPatch(0, 0, Replace, Replace)));

        using MemoryStream stream = new(codestream);
        IImageInfo info = new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken);

        Assert.Equal(Width, info.Width);
        Assert.Equal(Height, info.Height);
    }

    private static byte[] Case(string name) => name switch
    {
        // The 4 x 3 rectangle at (2, 1) of the reference frame, placed at (5, 6).
        "replace" => Layers(new JxlTestRefPatch(Slot, 2, 1, 4, 3, new JxlTestPatch(5, 6, Replace, Replace))),
        "add_colour_none_alpha" => Layers(new JxlTestRefPatch(Slot, 0, 0, 6, 4, new JxlTestPatch(10, 3, Add, None))),
        "none_colour_replace_alpha" => Layers(new JxlTestRefPatch(Slot, 1, 1, 3, 3, new JxlTestPatch(0, 0, None, Replace))),
        "replace_then_add" => Layers(
            new JxlTestRefPatch(Slot, 0, 0, 5, 5, new JxlTestPatch(4, 4, Replace, Replace)),
            new JxlTestRefPatch(Slot, 8, 2, 5, 5, new JxlTestPatch(6, 5, Add, Replace))),
        "add_then_replace" => Layers(
            new JxlTestRefPatch(Slot, 8, 2, 5, 5, new JxlTestPatch(6, 5, Add, Replace)),
            new JxlTestRefPatch(Slot, 0, 0, 5, 5, new JxlTestPatch(4, 4, Replace, Replace))),
        "right_bottom_edges" => Layers(new JxlTestRefPatch(Slot, 0, 0, 3, 2, new JxlTestPatch(Width - 3, Height - 2, Replace, Replace))),
        "several_positions" => Layers(new JxlTestRefPatch(Slot, 4, 3, 2, 3, SeveralPositions.Select(p => new JxlTestPatch(p.X, p.Y, Replace, Replace)).ToArray())),
        _ => Layers(),
    };

    /// <summary>The reference frame in slot 3, then the displayed frame with these patches.</summary>
    /// <param name="refPatches">The reference patches.</param>
    /// <returns>The codestream.</returns>
    internal static byte[] Layers(params JxlTestRefPatch[] refPatches)
        => Layers(new JxlTestPatchDictionary(refPatches));

    internal static byte[] Layers(JxlTestPatchDictionary patches)
        => new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.Ramp) { Type = JxlFrameType.ReferenceOnly, SaveAsReference = Slot, SaveBeforeColorTransform = true })
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { Patches = patches })
            .ToArray();

    private static bool Inside(int x, int y, int x0, int y0, int xsize, int ysize) => x >= x0 && x < x0 + xsize && y >= y0 && y < y0 + ysize;

    // The Ramp layer at (x, y), in 8-bit units.
    private static (float R, float G, float B, float A) Ramp(int x, int y) => (7 * (x + y + 1), 90, 5 * (x + y + 1), 9 * (x + y + 1));

    private static JxlDecodedImage DecodeToPlanes(byte[] codestream)
    {
        using MemoryStream stream = new(codestream);
        return new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
    }

    // Checks every sample against values in 8-bit units, which may pass 255: the planes are not clamped.
    private static void AssertEveryPixel(JxlDecodedImage decoded, Func<int, int, (float R, float G, float B, float A)> expected)
    {
        using (decoded)
        {
            JxlDecodedFrame frame = Assert.Single(decoded.Frames);
            Assert.Equal(4, frame.Planes.Count);
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    (float r, float g, float b, float a) = expected(x, y);
                    float[] want = { r, g, b, a };
                    for (int c = 0; c < 4; c++)
                    {
                        float sample = frame.Planes[c].DangerousGetRowSpan(y)[x];
                        Assert.True(MathF.Abs(sample - (want[c] / 255F)) < 1e-6F, $"({x}, {y}) channel {c}: {sample * 255F}, expected {want[c]}.");
                    }
                }
            }
        }
    }
}
