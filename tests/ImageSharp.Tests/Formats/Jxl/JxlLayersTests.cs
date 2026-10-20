// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Layered still images: the frames are blended onto their reference slots and coalesced into the one
/// displayed frame, as djxl does by default. The codestreams are built by <see cref="JxlTestLayers"/>.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlLayersTests
{
    private const int Width = 24;
    private const int Height = 16;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void SingleFrame_DecodesTheLayer()
    {
        byte[] codestream = new JxlTestLayers(Width, Height).Add(JxlTestFrame.Last(JxlTestLayers.ColourA)).ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (200, 100, 50, 128));
    }

    // Sections from the smallest size of the third and fourth TOC size selectors: BitsOffset(22, 17408)
    // and BitsOffset(30, 4211712). The decoder ignores the zeros after the Modular data, and the next
    // frame starts only where the TOC says the long section ends.
    [Theory]
    [InlineData(17408)]
    [InlineData(4211712)]
    public void FrameWithALongSection_IsFollowedByTheNextFrame(int sectionLength)
    {
        byte[] section = new byte[sectionLength];
        JxlTestLayers.ColourA.CopyTo(section, 0);
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(section))
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourB) with { Color = new(JxlBlendMode.Add) })
            .ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (240, 320, 210, 64));
    }

    [Fact]
    public void Add_SumsTheColourAndReplacesTheAlpha()
    {
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourB) with { Color = new(JxlBlendMode.Add) })
            .ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (240, 320, 210, 64));
    }

    [Fact]
    public void CroppedFrame_OntoASlotNoFrameSaved_BlendsOntoZeros()
    {
        // libjxl blends a frame whose slot is empty against zeros (stage_blending.cc), and does not reject it.
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { X0 = 4, Y0 = 3, XSize = 10, YSize = 6 })
            .ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => x is >= 4 and < 14 && y is >= 3 and < 9 ? (200, 100, 50, 128) : (0, 0, 0, 0));
    }

    [Fact]
    public void Blend_FromASlotNoFrameSaved_BlendsOntoZeros()
    {
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourB) with { Color = new(JxlBlendMode.Add, Source: 3), ExtraChannel = new(JxlBlendMode.Add, Source: 3) })
            .ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (40, 220, 160, 64));
    }

    [Fact]
    public void CroppedFrame_WithANegativeOriginAndLargerThanTheImage_IsClipped()
    {
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { X0 = -5, Y0 = -7, XSize = 40, YSize = 30 })
            .ToArray();

        // Image pixel (x, y) is frame pixel (x + 5, y + 7).
        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (7 * (x + y + 13), 90, 5 * (x + y + 13), 9 * (x + y + 13)));
    }

    [Fact]
    public void Blend_FromTheSourceSlot()
    {
        // ColourA goes to slot 2 and ColourB to slot 1; the last frame adds itself to slot 2.
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { SaveAsReference = 2 })
            .Add(new JxlTestFrame(JxlTestLayers.ColourB) { SaveAsReference = 1 })
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourB) with { Color = new(JxlBlendMode.Add, Source: 2), ExtraChannel = new(JxlBlendMode.Add, Source: 1) })
            .ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (240, 320, 210, 128));
    }

    [Fact]
    public void ReferenceOnlyFrame_IsNotShownButCanBeBlendedOnto()
    {
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourB))
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { Type = JxlFrameType.ReferenceOnly, SaveAsReference = 1 })
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourB) with { Color = new(JxlBlendMode.Add, Source: 1) })
            .ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (240, 320, 210, 64));
    }

    [Fact]
    public void Load_LayeredImage_GivesTheCoalescedFrame()
    {
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { X0 = 4, Y0 = 3, XSize = 10, YSize = 6, Color = new(JxlBlendMode.Mul), ExtraChannel = new(JxlBlendMode.Mul) })
            .ToArray();

        using Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(), codestream);

        Assert.Single(image.Frames);
        Assert.Equal(Width, image.Width);
        Assert.Equal(Height, image.Height);
        Assert.Equal(new Rgba32(200, 100, 50, 128), image[0, 0]);

        // 200 * 200 / 255 = 156.9, and so on.
        Assert.Equal(new Rgba32(157, 39, 10, 64), image[4, 3]);
    }

    [Fact]
    public void Animation_WithOneDisplayedFrame_GivesThatFrame()
    {
        // A zero-duration frame is not shown; it is blended into the next one.
        byte[] codestream = new JxlTestLayers(Width, Height, animation: true)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourB) with { Duration = 10, Color = new(JxlBlendMode.Add) })
            .ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (240, 320, 210, 64));
    }

    [Fact]
    public void Animation_WithTwoDisplayedFrames_GivesBothFrames()
    {
        // The second frame is cropped, so it is blended onto the first, which slot 1 holds.
        byte[] codestream = new JxlTestLayers(Width, Height, animation: true)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { Duration = 10, SaveAsReference = 1 })
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourB) with { Duration = 10, X0 = 4, Y0 = 3, XSize = 10, YSize = 6, Color = new(JxlBlendMode.Replace, Source: 1), ExtraChannel = new(JxlBlendMode.Replace, Source: 1) })
            .ToArray();

        using JxlDecodedImage decoded = DecodeToPlanes(codestream);

        Assert.Equal(2, decoded.Frames.Count);
        AssertEveryPixel(decoded.Frames[0], (x, y) => (200, 100, 50, 128));
        AssertEveryPixel(decoded.Frames[1], (x, y) => x is >= 4 and < 14 && y is >= 3 and < 9 ? (40, 220, 160, 64) : (200, 100, 50, 128));
    }

    // The tests below check the blend modes without djxl, which the SonarQube job does not have. The
    // expected values are djxl's, from DecodeToPlanes_MatchesDjxl on the same codestreams.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Blend_PremultipliedAlpha_AddsTheBackgroundWeightedByOneMinusTheAlpha(bool clamp)
    {
        // R = 40 + 200 (1 - 64 / 255). The colour blend also blends the alpha.
        byte[] codestream = TwoLayers(new(JxlBlendMode.Blend, Clamp: clamp), new(JxlBlendMode.Replace), premultiplied: true).ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (189.80393F, 294.90199F, 197.45099F, 159.87451F));
    }

    [Theory]
    [InlineData(false, 258.22411F, 88.80306F, 195.56026F, 289.36472F)]
    [InlineData(true, 252F, 90F, 180F, 255F)]
    public void Blend_StraightAlpha_ClampsTheAlphaOnlyWithClamp(bool clamp, float r, float g, float b, float a)
    {
        byte[] codestream = TwoLayers(new(JxlBlendMode.Blend, Clamp: clamp), new(JxlBlendMode.Replace), top: JxlTestLayers.Ramp).ToArray();

        using JxlDecodedImage decoded = DecodeToPlanes(codestream);

        // At (0, 0) the ramp's alpha is 9, so the clamp changes nothing. At (20, 15) it is 324: clamped
        // to 255, the ramp replaces the background.
        AssertPixel(decoded.Frames[0], 0, 0, (186.88884F, 99.32067F, 46.94299F, 132.48235F));
        AssertPixel(decoded.Frames[0], 20, 15, (r, g, b, a));
    }

    [Fact]
    public void AlphaWeightedAdd_AddsTheColourWeightedByTheAlpha()
    {
        // R = 200 + 40 * 64 / 255. The alpha is replaced.
        byte[] codestream = TwoLayers(new(JxlBlendMode.AlphaWeightedAdd), new(JxlBlendMode.Replace)).ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (210.03923F, 155.21570F, 90.15687F, 64F));
    }

    [Fact]
    public void AlphaWeightedAdd_WithClamp_ClampsTheAlphaToOne()
    {
        byte[] codestream = TwoLayers(new(JxlBlendMode.AlphaWeightedAdd, Clamp: true), new(JxlBlendMode.Replace), top: JxlTestLayers.Ramp).ToArray();

        using JxlDecodedImage decoded = DecodeToPlanes(codestream);

        // At (20, 15) the ramp's alpha is 324, clamped to 255: R = 200 + 252 rather than 200 + 252 * 324 / 255.
        AssertPixel(decoded.Frames[0], 0, 0, (200.24708F, 103.17648F, 50.17647F, 9F));
        AssertPixel(decoded.Frames[0], 20, 15, (452F, 190F, 230F, 324F));
    }

    [Fact]
    public void AlphaWeightedAdd_OfTheAlphaChannel_KeepsTheBackgroundAlpha()
    {
        byte[] codestream = TwoLayers(new(JxlBlendMode.Replace), new(JxlBlendMode.AlphaWeightedAdd)).ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (40, 220, 160, 128));
    }

    [Fact]
    public void Mul_WithClamp_ClampsTheForegroundToOne()
    {
        byte[] codestream = TwoLayers(new(JxlBlendMode.Mul, Clamp: true), new(JxlBlendMode.Replace), top: JxlTestLayers.Ramp).ToArray();

        using JxlDecodedImage decoded = DecodeToPlanes(codestream);

        // R = 200 * 7 / 255 at (0, 0). At (20, 15) the ramp's red is 252 and its blue 180, both under
        // 255; the alpha is replaced, and is not clamped.
        AssertPixel(decoded.Frames[0], 0, 0, (5.49020F, 35.29412F, 0.98039F, 9F));
        AssertPixel(decoded.Frames[0], 20, 15, (197.64709F, 35.29412F, 35.29412F, 324F));
    }

    [Theory]
    [InlineData(false, 1)] // kBlend
    [InlineData(true, 2)] // kAlphaWeightedAdd
    public void BlendModesThatNeedAlpha_OnAnImageWithoutAlpha_ReplaceOrAdd(bool alphaWeightedAdd, int factor)
    {
        JxlBlendMode mode = alphaWeightedAdd ? JxlBlendMode.AlphaWeightedAdd : JxlBlendMode.Blend;

        // libjxl's kBlendAbove copies the foreground and kAlphaWeightedAddAbove adds it when the image has
        // no alpha. Both frames hold the same grey image, so the result is that image, or twice it.
        string path = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", "greyscale.jxl");
        JxlTestLayers single = JxlTestLayers.FromFile(File.ReadAllBytes(path), out byte[] section).Add(JxlTestFrame.Last(section));
        JxlTestLayers layers = JxlTestLayers.FromFile(File.ReadAllBytes(path), out _)
            .Add(new JxlTestFrame(section))
            .Add(JxlTestFrame.Last(section) with { Color = new(mode) });

        using JxlDecodedImage expected = DecodeToPlanes(single.ToArray());
        using JxlDecodedImage actual = DecodeToPlanes(layers.ToArray());

        Buffer2D<float> expectedPlane = Assert.Single(expected.Frames[0].Planes);
        Buffer2D<float> actualPlane = Assert.Single(actual.Frames[0].Planes);
        for (int y = 0; y < actual.Height; y++)
        {
            for (int x = 0; x < actual.Width; x++)
            {
                Assert.Equal(factor * expectedPlane[x, y], actualPlane[x, y], 1e-6F);
            }
        }
    }

    [Fact]
    public void Blend_OfASecondAlphaChannel_IsWeightedByTheAlphaItNames()
    {
        // Two generated files of one colour each, with two alpha channels: (200, 100, 50, 128, 255) and
        // (40, 220, 160, 64, 192). The top frame blends the second alpha channel weighted by the first.
        JxlTestLayers layers = TwoAlphaLayers(new JxlTestBlending[] { new(JxlBlendMode.Replace), new(JxlBlendMode.Blend, AlphaChannel: 0) });

        using JxlDecodedImage decoded = DecodeToPlanes(layers.ToArray());

        // (192 * 64 + 255 * 128 * (255 - 64) / 255) / (255 - (255 - 64) * (255 - 128) / 255) = 229.78.
        JxlDecodedFrame frame = Assert.Single(decoded.Frames);
        Assert.Equal(5, frame.Planes.Count);
        float[] expected = { 40, 220, 160, 64, 229.78025F };
        for (int c = 0; c < expected.Length; c++)
        {
            Assert.Equal(expected[c], frame.Planes[c][3, 5] * 255F, 1e-3F);
        }
    }

    [Theory]
    [InlineData(30, 4, 6, 6)] // starts right of the image
    [InlineData(-10, 4, 6, 6)] // ends left of the image
    public void CroppedFrame_WhollyOutsideTheImage_LeavesTheBackground(int x0, int y0, int xsize, int ysize)
    {
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { X0 = x0, Y0 = y0, XSize = xsize, YSize = ysize, Color = new(JxlBlendMode.Add) })
            .ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (200, 100, 50, 128));
    }

    // Each allocation of a decode, in turn, fails. Whichever it is, the decode throws and every buffer it
    // took is released: the frame planes, the image-sized planes of a blended frame, the copies kept in
    // reference slots and the image itself.
    [Fact]
    public void Load_RunningOutOfMemoryAtAnyAllocation_ReleasesEveryBuffer()
    {
        byte[] codestream = BufferHungryLayers().ToArray();

        FailingMemoryAllocator counter = new(failAt: int.MaxValue);
        using (Image<Rgba32> image = Image.Load<Rgba32>(CreateConfiguration(counter), codestream))
        {
            Assert.Equal(3, image.Frames.Count);
        }

        Assert.Equal(0, counter.Outstanding);
        Assert.True(counter.Allocations > 0);
        for (int failAt = 1; failAt <= counter.Allocations; failAt++)
        {
            FailingMemoryAllocator allocator = new(failAt);
            Assert.Throws<InvalidMemoryOperationException>(() => Image.Load<Rgba32>(CreateConfiguration(allocator), codestream));
            Assert.True(allocator.Outstanding == 0, $"Allocation {failAt} of {counter.Allocations} failed and left {allocator.Outstanding} buffers.");
        }
    }

    public static TheoryData<string> DjxlCases => new(Cases.Keys);

    // Every blend mode on the colour and on the alpha extra channel, with its clamp flag, and the
    // crops, slots and frame types; djxl gives the expected planes.
    [Theory]
    [MemberData(nameof(DjxlCases))]
    public async Task DecodeToPlanes_MatchesDjxl(string name)
    {
        byte[] codestream = Cases[name].ToArray();
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlLayersTests));
        string input = Path.Combine(directory, name + ".jxl");
        string output = Path.Combine(directory, name + ".npy");
        await File.WriteAllBytesAsync(input, codestream, CancellationToken);

        await JxlTools.RunAsync("djxl", CancellationToken, input, output);

        NpyArray expected = NpyArray.Load(output);
        using JxlDecodedImage decoded = DecodeToPlanes(codestream);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);
        JxlConformanceComparer.AssertMatchesDjxl(expected, actual);
    }

    private static Dictionary<string, JxlTestLayers> Cases => new()
    {
        ["colour_add"] = TwoLayers(new(JxlBlendMode.Add), new(JxlBlendMode.Replace)),
        ["colour_blend"] = TwoLayers(new(JxlBlendMode.Blend), new(JxlBlendMode.Replace)),
        ["colour_blend_premultiplied"] = TwoLayers(new(JxlBlendMode.Blend), new(JxlBlendMode.Replace), premultiplied: true),
        ["colour_blend_clamp"] = TwoLayers(new(JxlBlendMode.Blend, Clamp: true), new(JxlBlendMode.Replace), top: JxlTestLayers.Ramp),
        ["colour_blend_noclamp"] = TwoLayers(new(JxlBlendMode.Blend), new(JxlBlendMode.Replace), top: JxlTestLayers.Ramp),
        ["colour_alpha_weighted_add"] = TwoLayers(new(JxlBlendMode.AlphaWeightedAdd), new(JxlBlendMode.Replace)),
        ["colour_alpha_weighted_add_clamp"] = TwoLayers(new(JxlBlendMode.AlphaWeightedAdd, Clamp: true), new(JxlBlendMode.Replace), top: JxlTestLayers.Ramp),
        ["colour_mul"] = TwoLayers(new(JxlBlendMode.Mul), new(JxlBlendMode.Replace), top: JxlTestLayers.Ramp),
        ["colour_mul_clamp"] = TwoLayers(new(JxlBlendMode.Mul, Clamp: true), new(JxlBlendMode.Replace), top: JxlTestLayers.Ramp),
        ["alpha_add"] = TwoLayers(new(JxlBlendMode.Replace), new(JxlBlendMode.Add)),
        ["alpha_blend"] = TwoLayers(new(JxlBlendMode.Replace), new(JxlBlendMode.Blend)),
        ["alpha_blend_clamp"] = TwoLayers(new(JxlBlendMode.Replace), new(JxlBlendMode.Blend, Clamp: true), top: JxlTestLayers.Ramp),
        ["alpha_alpha_weighted_add"] = TwoLayers(new(JxlBlendMode.Replace), new(JxlBlendMode.AlphaWeightedAdd)),
        ["alpha_mul"] = TwoLayers(new(JxlBlendMode.Replace), new(JxlBlendMode.Mul), top: JxlTestLayers.Ramp),
        ["alpha_mul_clamp"] = TwoLayers(new(JxlBlendMode.Replace), new(JxlBlendMode.Mul, Clamp: true), top: JxlTestLayers.Ramp),
        ["blend_both"] = TwoLayers(new(JxlBlendMode.Blend), new(JxlBlendMode.Blend), top: JxlTestLayers.Ramp),
        ["crop_inside"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { X0 = 3, Y0 = 2, XSize = 9, YSize = 7, Color = new(JxlBlendMode.Blend), ExtraChannel = new(JxlBlendMode.Blend) }),
        ["crop_negative_origin_oversized"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { X0 = -5, Y0 = -7, XSize = 40, YSize = 30, Color = new(JxlBlendMode.Blend) }),
        ["crop_past_the_far_edges"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { X0 = 20, Y0 = 10, XSize = 12, YSize = 9, Color = new(JxlBlendMode.Add) }),
        ["crop_wholly_outside"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { X0 = 30, Y0 = -20, XSize = 6, YSize = 6, Color = new(JxlBlendMode.Add) }),
        ["crop_onto_empty_slot"] = new JxlTestLayers(Width, Height)
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { X0 = 4, Y0 = 3, XSize = 10, YSize = 6 }),
        ["cropped_layers_saved_and_reused"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { X0 = -2, Y0 = 1, XSize = 12, YSize = 9, SaveAsReference = 1 })
            .Add(new JxlTestFrame(JxlTestLayers.ColourB) { X0 = 6, Y0 = -3, XSize = 20, YSize = 8, Color = new(JxlBlendMode.Blend, Source: 1), ExtraChannel = new(JxlBlendMode.Blend, Source: 1), SaveAsReference = 2 })
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { X0 = 1, Y0 = 1, XSize = 5, YSize = 5, Color = new(JxlBlendMode.AlphaWeightedAdd, Source: 2), ExtraChannel = new(JxlBlendMode.Add, Source: 1) }),
        ["slot_per_channel"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { SaveAsReference = 2 })
            .Add(new JxlTestFrame(JxlTestLayers.ColourB) { SaveAsReference = 1 })
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { Color = new(JxlBlendMode.Mul, Source: 2), ExtraChannel = new(JxlBlendMode.Add, Source: 1) }),
        ["empty_slot_source"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourB) with { Color = new(JxlBlendMode.Add, Source: 3), ExtraChannel = new(JxlBlendMode.Blend, Source: 3) }),
        ["reference_only"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourB))
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { Type = JxlFrameType.ReferenceOnly, SaveAsReference = 1 })
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { Color = new(JxlBlendMode.Blend, Source: 1), ExtraChannel = new(JxlBlendMode.Blend, Source: 1) }),
        ["saved_before_colour_transform_as_alpha_background"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { SaveAsReference = 1, SaveBeforeColorTransform = true })
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourB) with { ExtraChannel = new(JxlBlendMode.Add, Source: 1) }),
        ["animation_one_displayed_frame"] = new JxlTestLayers(Width, Height, animation: true)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { X0 = 2, Y0 = 3, XSize = 8, YSize = 8 })
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { Duration = 7, Color = new(JxlBlendMode.Blend), ExtraChannel = new(JxlBlendMode.Blend) }),
        ["animation_three_displayed_frames"] = new JxlTestLayers(Width, Height, animation: true)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { Duration = 5, SaveAsReference = 1 })
            .Add(new JxlTestFrame(JxlTestLayers.Ramp) { X0 = 2, Y0 = 3, XSize = 8, YSize = 8, Color = new(JxlBlendMode.Blend, Source: 1), ExtraChannel = new(JxlBlendMode.Blend, Source: 1) })
            .Add(new JxlTestFrame(JxlTestLayers.ColourB) { Duration = 4, X0 = 6, Y0 = 1, XSize = 12, YSize = 9, Color = new(JxlBlendMode.Add), ExtraChannel = new(JxlBlendMode.Add), SaveAsReference = 2 })
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { Duration = 9, X0 = -3, Y0 = 5, XSize = 10, YSize = 20, Color = new(JxlBlendMode.Mul, Source: 2), ExtraChannel = new(JxlBlendMode.Replace, Source: 1) }),

        // One colour plane: libjxl blends three equal planes.
        ["grey_add"] = GreyLayers(new(JxlBlendMode.Add), new(JxlBlendMode.Replace)),
        ["grey_blend"] = GreyLayers(new(JxlBlendMode.Blend), new(JxlBlendMode.Blend)),
        ["grey_alpha_weighted_add"] = GreyLayers(new(JxlBlendMode.AlphaWeightedAdd, Clamp: true), new(JxlBlendMode.AlphaWeightedAdd)),
        ["grey_mul"] = GreyLayers(new(JxlBlendMode.Mul, Clamp: true), new(JxlBlendMode.Mul)),
        ["skip_progressive"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { Type = JxlFrameType.SkipProgressive, Color = new(JxlBlendMode.Blend) }),
        ["colour_blend_premultiplied_clamp"] = TwoLayers(new(JxlBlendMode.Blend, Clamp: true), new(JxlBlendMode.Replace), premultiplied: true),
        ["crop_ends_left_of_the_image"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { X0 = -10, Y0 = 4, XSize = 6, YSize = 6, Color = new(JxlBlendMode.Add) }),
        ["buffer_hungry"] = BufferHungryLayers(),

        // Two alpha channels: the second is blended, added or multiplied with the first as its alpha.
        ["two_alpha_blend_by_first"] = TwoAlphaLayers(new JxlTestBlending[] { new(JxlBlendMode.Replace), new(JxlBlendMode.Blend, AlphaChannel: 0) }),
        ["two_alpha_alpha_weighted_add_by_first"] = TwoAlphaLayers(new JxlTestBlending[] { new(JxlBlendMode.Blend), new(JxlBlendMode.AlphaWeightedAdd, AlphaChannel: 0) }),
        ["two_alpha_colour_blend_by_second"] = TwoAlphaLayers(new JxlTestBlending[] { new(JxlBlendMode.Replace), new(JxlBlendMode.Replace) }, new(JxlBlendMode.Blend, AlphaChannel: 1)),

        // No alpha: kBlend copies the foreground and kAlphaWeightedAdd adds it.
        ["grey_no_alpha_blend"] = GreyNoAlphaLayers(JxlBlendMode.Blend),
        ["grey_no_alpha_alpha_weighted_add"] = GreyNoAlphaLayers(JxlBlendMode.AlphaWeightedAdd),
    };

    // An animation whose frames take every kind of buffer: a displayed frame saved to a slot, a cropped
    // frame blended onto it and saved, a frame saved before its colour transform and blended, and a last
    // frame that blends from both.
    private static JxlTestLayers BufferHungryLayers()
        => new JxlTestLayers(Width, Height, animation: true)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { Duration = 10, SaveAsReference = 1 })
            .Add(new JxlTestFrame(JxlTestLayers.ColourB) { Duration = 10, X0 = 4, Y0 = 3, XSize = 10, YSize = 6, Color = new(JxlBlendMode.Blend, Source: 1), ExtraChannel = new(JxlBlendMode.Blend, Source: 1), SaveAsReference = 2 })
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { ExtraChannel = new(JxlBlendMode.Add, Source: 2), SaveAsReference = 3, SaveBeforeColorTransform = true })
            .Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { Duration = 10, Color = new(JxlBlendMode.Add, Source: 2), ExtraChannel = new(JxlBlendMode.Add, Source: 3) });

    // Two full-size frames of the generated 64 x 64 8-bit grey file, which has no alpha.
    private static JxlTestLayers GreyNoAlphaLayers(JxlBlendMode mode)
    {
        string path = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", "greyscale.jxl");
        JxlTestLayers layers = JxlTestLayers.FromFile(File.ReadAllBytes(path), out byte[] section);
        return layers
            .Add(new JxlTestFrame(section))
            .Add(JxlTestFrame.Last(section) with { Color = new(mode) });
    }

    // The generated 16 x 16 files with two alpha channels: two_alpha_a under two_alpha_b.
    private static JxlTestLayers TwoAlphaLayers(JxlTestBlending[] extraChannels, JxlTestBlending color = default)
    {
        string directory = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated");
        JxlTestLayers layers = JxlTestLayers.FromFile(File.ReadAllBytes(Path.Combine(directory, "two_alpha_a.jxl")), out byte[] bottom);
        JxlTestLayers.FromFile(File.ReadAllBytes(Path.Combine(directory, "two_alpha_b.jxl")), out byte[] top);
        return layers
            .Add(new JxlTestFrame(bottom))
            .Add(JxlTestFrame.Last(top) with { Color = color, ExtraChannels = extraChannels });
    }

    // Two full-size frames of the generated 64 x 64 16-bit grey and alpha file.
    private static JxlTestLayers GreyLayers(JxlTestBlending color, JxlTestBlending extraChannel)
    {
        string path = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", "greyscale_alpha.jxl");
        JxlTestLayers layers = JxlTestLayers.FromFile(File.ReadAllBytes(path), out byte[] section);
        return layers
            .Add(new JxlTestFrame(section))
            .Add(JxlTestFrame.Last(section) with { Color = color, ExtraChannel = extraChannel });
    }

    private static JxlTestLayers TwoLayers(JxlTestBlending color, JxlTestBlending extraChannel, bool premultiplied = false, byte[] top = null)
        => new JxlTestLayers(Width, Height, premultiplied: premultiplied)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(JxlTestFrame.Last(top ?? JxlTestLayers.ColourB) with { Color = color, ExtraChannel = extraChannel });

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
            Assert.Single(decoded.Frames);
            Assert.Equal(Width, decoded.Width);
            Assert.Equal(Height, decoded.Height);
            AssertEveryPixel(decoded.Frames[0], expected);
        }
    }

    private static void AssertEveryPixel(JxlDecodedFrame frame, Func<int, int, (float R, float G, float B, float A)> expected)
    {
        Assert.Equal(4, frame.Planes.Count);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                AssertPixel(frame, x, y, expected(x, y));
            }
        }
    }

    private static void AssertPixel(JxlDecodedFrame frame, int x, int y, (float R, float G, float B, float A) expected)
    {
        float[] want = { expected.R, expected.G, expected.B, expected.A };
        for (int c = 0; c < 4; c++)
        {
            float sample = frame.Planes[c].DangerousGetRowSpan(y)[x];
            Assert.True(MathF.Abs(sample - (want[c] / 255F)) < 1e-6F, $"({x}, {y}) channel {c}: {sample * 255F}, expected {want[c]}.");
        }
    }

    private static Configuration CreateConfiguration(MemoryAllocator allocator = null)
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        if (allocator != null)
        {
            // One thread, so the allocations come in the same order on every decode.
            configuration.MemoryAllocator = allocator;
            configuration.MaxDegreeOfParallelism = 1;
        }

        return configuration;
    }

    // Counts the buffers it hands out and not yet returned, and throws on allocation failAt (from 1).
    // Only the float and pixel buffers count: those of the render pipeline, the reference slots and the
    // image. The Modular decoder's int buffers never fail here, as it does not yet release them all
    // when an allocation fails.
    private sealed class FailingMemoryAllocator : MemoryAllocator
    {
        private readonly int failAt;

        public FailingMemoryAllocator(int failAt) => this.failAt = failAt;

        public int Allocations { get; private set; }

        public int Outstanding { get; private set; }

        public override IMemoryOwner<T> Allocate<T>(int length, AllocationOptions options = AllocationOptions.None)
        {
            if (typeof(T) != typeof(int) && ++this.Allocations == this.failAt)
            {
                throw new InvalidMemoryOperationException($"Allocation {this.failAt} fails.");
            }

            this.Outstanding++;
            return new Owner<T>(this, new T[length]);
        }

        protected internal override int GetBufferCapacityInBytes() => int.MaxValue;

        private sealed class Owner<T> : IMemoryOwner<T>
        {
            private FailingMemoryAllocator allocator;

            public Owner(FailingMemoryAllocator allocator, T[] array)
            {
                this.allocator = allocator;
                this.Memory = array;
            }

            public Memory<T> Memory { get; }

            public void Dispose()
            {
                if (this.allocator != null)
                {
                    this.allocator.Outstanding--;
                    this.allocator = null;
                }
            }
        }
    }
}
