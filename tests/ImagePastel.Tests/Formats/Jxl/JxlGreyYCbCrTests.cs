// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The YCbCr frames of a grey image that need three colour planes before the colour transform (D14b): a
/// frame saved before its colour transform, whose reference slot keeps its Cb, Y and Cr planes, and a
/// frame with patches, which blend all three. libjxl keeps three colour planes through the YCbCr stage and
/// outputs R. The hand-built frames use <see cref="JxlTestLayers.ColourA"/>, whose section codes Cb = 200,
/// Y = 100 and Cr = 50 for a YCbCr frame, and the 64 x 64 8-bit grey image of <c>greyscale.jxl</c>. The
/// committed files are hand-built by <see cref="Build"/> and checked against djxl v0.12.0.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlGreyYCbCrTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The slot of a grey YCbCr frame saved before its colour transform holds Cb, Y and Cr. A later YCbCr
    // frame adds the slot to its own planes with a patch: Cb = 400, Y = 200 and Cr = 100, so R is
    // (Y + 128 + 1.402 Cr) / 255 = 468.2 / 255 inside the patch, and 298.1 / 255 outside it.
    [Fact]
    public void DecodeToPlanes_GreyYCbCrFrameSavedBeforeTheColourTransform_KeepsItsThreePlanes()
    {
        JxlTestRefPatch refPatch = new(1, 0, 0, 10, 8, new JxlTestPatch(20, 30, new JxlTestPatchBlending(JxlPatchBlendMode.Add)));
        JxlTestLayers layers = GreyLayers(out _)
            .Add(YCbCrSavedBeforeTheColourTransform(1))
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { YCbCr = true, Patches = new JxlTestPatchDictionary(refPatch) });

        using JxlDecodedImage decoded = DecodeToPlanes(layers.ToArray());

        Buffer2D<float> grey = Assert.Single(Assert.Single(decoded.Frames).Planes);
        Assert.Equal(468.2F / 255, grey[25, 33], 1e-6F);
        Assert.Equal(298.1F / 255, grey[5, 7], 1e-6F);
    }

    // A grey frame with no colour transform takes the slot's first plane, Cb, as libjxl's patches blend
    // colour channel 0 with the slot's channel 0.
    [Fact]
    public void DecodeToPlanes_GreyFrameWithPatchesFromAYCbCrSlot_TakesCb()
    {
        JxlTestRefPatch refPatch = new(1, 2, 3, 10, 8, new JxlTestPatch(20, 30, new JxlTestPatchBlending(JxlPatchBlendMode.Add)));
        JxlTestLayers plainLayers = GreyLayers(out byte[] section);
        JxlTestLayers layers = GreyLayers(out _)
            .Add(YCbCrSavedBeforeTheColourTransform(1))
            .Add(JxlTestFrame.Last(section) with { Patches = new JxlTestPatchDictionary(refPatch) });

        using JxlDecodedImage plain = DecodeToPlanes(plainLayers.Add(JxlTestFrame.Last(section)).ToArray());
        using JxlDecodedImage decoded = DecodeToPlanes(layers.ToArray());

        Buffer2D<float> expected = Assert.Single(Assert.Single(plain.Frames).Planes);
        Buffer2D<float> grey = Assert.Single(Assert.Single(decoded.Frames).Planes);
        Assert.Equal(expected[25, 33] + (200F / 255), grey[25, 33], 1e-6F);
        Assert.Equal(expected[5, 7], grey[5, 7], 1e-6F);
    }

    // A grey YCbCr frame with patches from a grey slot, which holds one plane: each of Cb, Y and Cr adds
    // it, as libjxl adds each of its slot's three equal planes. With g the grey sample, in 8-bit units,
    // R = (100 + g + 128 + 1.402 (50 + g)) / 255 = (298.1 + 2.402 g) / 255.
    [Fact]
    public void DecodeToPlanes_GreyYCbCrFrameWithPatchesFromAGreySlot_AddsTheGreyToEachPlane()
    {
        JxlTestRefPatch refPatch = new(3, 0, 0, 10, 8, new JxlTestPatch(20, 30, new JxlTestPatchBlending(JxlPatchBlendMode.Add)));
        JxlTestLayers plainLayers = GreyLayers(out byte[] section);
        JxlTestLayers layers = GreyLayers(out _)
            .Add(new JxlTestFrame(section) { Type = JxlFrameType.ReferenceOnly, SaveAsReference = 3, SaveBeforeColorTransform = true })
            .Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with { YCbCr = true, Patches = new JxlTestPatchDictionary(refPatch) });

        using JxlDecodedImage plain = DecodeToPlanes(plainLayers.Add(JxlTestFrame.Last(section)).ToArray());
        using JxlDecodedImage decoded = DecodeToPlanes(layers.ToArray());

        Buffer2D<float> source = Assert.Single(Assert.Single(plain.Frames).Planes);
        Buffer2D<float> grey = Assert.Single(Assert.Single(decoded.Frames).Planes);
        float g = source[5, 3] * 255;
        Assert.Equal((298.1F + (2.402F * g)) / 255, grey[25, 33], 1e-5F);
        Assert.Equal(298.1F / 255, grey[5, 7], 1e-6F);
    }

    // An extra channel blends from its own slot, which may be a grey YCbCr frame saved before its colour
    // transform: libjxl's slot keeps the extra channels apart from the three colour channels, so the alpha
    // added here is the slot's alpha, 128, and not its Y plane, 100. greyscale_alpha is 16-bit. The frames
    // are those of the committed file grey_ycbcr_alpha_blending.
    [Fact]
    public void DecodeToPlanes_ExtraChannelBlendedFromAGreyYCbCrSlot_TakesTheSlotsExtraChannel()
    {
        JxlTestLayers plainLayers = JxlTestLayers.FromFile(File.ReadAllBytes(InputPath("greyscale_alpha")), out byte[] section);

        using JxlDecodedImage plain = DecodeToPlanes(plainLayers.Add(JxlTestFrame.Last(section)).ToArray());
        using JxlDecodedImage decoded = DecodeToPlanes(Build("grey_ycbcr_alpha_blending"));

        JxlDecodedFrame expected = Assert.Single(plain.Frames);
        JxlDecodedFrame frame = Assert.Single(decoded.Frames);
        Assert.Equal(2, frame.Planes.Count);
        Assert.Equal(expected.Planes[0][9, 4], frame.Planes[0][9, 4], 1e-6F);
        Assert.Equal(expected.Planes[1][9, 4] + (128F / 65535), frame.Planes[1][9, 4], 1e-6F);
    }

    // A grey frame that is not YCbCr, with patches from a grey YCbCr slot and saved before its colour
    // transform, keeps three planes in its slot, as libjxl (D14 review): its grey g plus Cb, Y and Cr,
    // 200, 100 and 50, inside its patch, and g three times outside it. A later grey YCbCr frame adds that
    // slot to its own Cb, Y and Cr with a patch, so in 8-bit units R = (Y + 128 + 1.402 Cr) / 255 is
    // (468.2 + 2.402 g) / 255 where both patches cover the pixel, and (298.1 + 2.402 g) / 255 where only
    // the second does. A slot of one plane, g + 200, would give (778.5 + 2.402 g) / 255. The frames are
    // those of the committed file grey_ycbcr_patch_chain.
    [Fact]
    public void DecodeToPlanes_GreyFrameSavedBeforeTheColourTransformWithYCbCrPatches_KeepsThreePlanesInItsSlot()
    {
        JxlTestLayers plainLayers = GreyLayers(out byte[] section);

        using JxlDecodedImage plain = DecodeToPlanes(plainLayers.Add(JxlTestFrame.Last(section)).ToArray());
        using JxlDecodedImage decoded = DecodeToPlanes(Build("grey_ycbcr_patch_chain"));

        // The last frame's pixel (x, y) inside its patch takes the slot's (x + 12, y + 18); the middle
        // frame's patch covers its columns 20 to 35 and rows 30 to 41.
        Buffer2D<float> source = Assert.Single(Assert.Single(plain.Frames).Planes);
        Buffer2D<float> grey = Assert.Single(Assert.Single(decoded.Frames).Planes);
        float inBoth = source[24, 38] * 255;
        float inSecond = source[17, 25] * 255;
        Assert.Equal((468.2F + (2.402F * inBoth)) / 255, grey[12, 20], 1e-5F);
        Assert.Equal((298.1F + (2.402F * inSecond)) / 255, grey[5, 7], 1e-5F);
        Assert.Equal(298.1F / 255, grey[40, 40], 1e-6F);
    }

    // The hand-built files against djxl v0.12.0's float32 output. All are lossless Modular frames, so
    // the decoders agree to float rounding.
    [Theory]
    [InlineData("grey_ycbcr_saved_before_ct")]
    [InlineData("grey_ycbcr_patches")]
    [InlineData("grey_ycbcr_alpha_blending")]
    [InlineData("grey_ycbcr_patch_chain")]
    public void DecodeToPlanes_MatchesDjxl(string name)
    {
        NpyArray expected = NpyArray.Load(Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".npy"));

        using FileStream stream = File.OpenRead(InputPath(name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);

        JxlConformanceFrameResult result = Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, JxlConformanceImages.ToNpy(decoded)));
        TestContext.Current.TestOutputHelper?.WriteLine($"Worst channel RMSE {result.WorstChannelRmse:G3}, peak {result.PeakError:G3}");
        Assert.True(result.WorstChannelRmse <= 1e-6 && result.PeakError <= 1e-5, $"Worst channel RMSE {result.WorstChannelRmse:G3}, peak {result.PeakError:G3}");
    }

    // The committed files are the bytes Build writes (Generated/README.md). Each is also written to the
    // test output directory, from where it was copied.
    [Theory]
    [InlineData("grey_ycbcr_saved_before_ct")]
    [InlineData("grey_ycbcr_patches")]
    [InlineData("grey_ycbcr_alpha_blending")]
    [InlineData("grey_ycbcr_patch_chain")]
    public void CommittedFile_IsTheHandBuiltCodestream(string name)
    {
        byte[] codestream = Build(name);
        File.WriteAllBytes(Path.Combine(TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlGreyYCbCrTests)), name + ".jxl"), codestream);

        Assert.Equal(codestream, File.ReadAllBytes(InputPath(name)));
    }

    /// <summary>Builds the committed file <paramref name="name"/>.</summary>
    /// <param name="name">The file name, without its extension.</param>
    /// <returns>The codestream.</returns>
    internal static byte[] Build(string name)
    {
        JxlTestLayers layers = GreyLayers(out byte[] section);
        JxlTestPatchBlending add = new(JxlPatchBlendMode.Add);
        JxlTestPatchBlending replace = new(JxlPatchBlendMode.Replace);
        switch (name)
        {
            // A grey YCbCr frame saved before its colour transform in slot 1, then the grey frame of
            // greyscale.jxl with two patches from that slot: one added, one replacing. Both take Cb.
            case "grey_ycbcr_saved_before_ct":
                return layers
                    .Add(YCbCrSavedBeforeTheColourTransform(1))
                    .Add(JxlTestFrame.Last(section) with
                    {
                        Patches = new JxlTestPatchDictionary(
                            new JxlTestRefPatch(1, 2, 3, 20, 12, new JxlTestPatch(30, 40, add)),
                            new JxlTestRefPatch(1, 0, 0, 9, 7, new JxlTestPatch(5, 6, replace))),
                    })
                    .ToArray();

            // The grey frame of greyscale.jxl in slot 2 and a grey YCbCr frame in slot 1, both saved before
            // their colour transform, then a grey YCbCr frame with a patch from each.
            case "grey_ycbcr_patches":
                return layers
                    .Add(new JxlTestFrame(section) { Type = JxlFrameType.ReferenceOnly, SaveAsReference = 2, SaveBeforeColorTransform = true })
                    .Add(YCbCrSavedBeforeTheColourTransform(1))
                    .Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with
                    {
                        YCbCr = true,
                        Patches = new JxlTestPatchDictionary(
                            new JxlTestRefPatch(2, 10, 12, 30, 20, new JxlTestPatch(30, 40, add)),
                            new JxlTestRefPatch(1, 0, 0, 12, 9, new JxlTestPatch(4, 6, add))),
                    })
                    .ToArray();

            // The 16-bit grey and alpha image of greyscale_alpha.jxl: a grey YCbCr frame saved before its
            // colour transform in slot 1, then the frame of greyscale_alpha.jxl, whose alpha adds slot 1's.
            case "grey_ycbcr_alpha_blending":
                return JxlTestLayers.FromFile(File.ReadAllBytes(InputPath("greyscale_alpha")), out byte[] alphaSection)
                    .Add(YCbCrSavedBeforeTheColourTransform(1))
                    .Add(JxlTestFrame.Last(alphaSection) with { ExtraChannel = new(JxlBlendMode.Add, Source: 1) })
                    .ToArray();

            // A grey YCbCr frame saved before its colour transform in slot 1. Then the grey frame of
            // greyscale.jxl, reference-only and saved before its colour transform in slot 2, with an added
            // patch from slot 1. Then a grey YCbCr frame with an added patch from slot 2, which covers part
            // of the middle frame's patch.
            case "grey_ycbcr_patch_chain":
                return layers
                    .Add(YCbCrSavedBeforeTheColourTransform(1))
                    .Add(new JxlTestFrame(section)
                    {
                        Type = JxlFrameType.ReferenceOnly,
                        SaveAsReference = 2,
                        SaveBeforeColorTransform = true,
                        Patches = new JxlTestPatchDictionary(new JxlTestRefPatch(1, 0, 0, 16, 12, new JxlTestPatch(20, 30, add))),
                    })
                    .Add(JxlTestFrame.Last(JxlTestLayers.ColourA) with
                    {
                        YCbCr = true,
                        Patches = new JxlTestPatchDictionary(new JxlTestRefPatch(2, 16, 24, 24, 20, new JxlTestPatch(4, 6, add))),
                    })
                    .ToArray();

            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
    }

    // A reference-only YCbCr frame of ColourA, saved before its colour transform: libjxl's only way to keep
    // a frame for patches.
    private static JxlTestFrame YCbCrSavedBeforeTheColourTransform(uint slot)
        => new(JxlTestLayers.ColourA) { YCbCr = true, Type = JxlFrameType.ReferenceOnly, SaveAsReference = slot, SaveBeforeColorTransform = true };

    // The generated 64 x 64 8-bit grey file's image header, with no frame.
    private static JxlTestLayers GreyLayers(out byte[] section)
        => JxlTestLayers.FromFile(File.ReadAllBytes(InputPath("greyscale")), out section);

    private static JxlDecodedImage DecodeToPlanes(byte[] codestream)
    {
        using MemoryStream stream = new(codestream);
        return new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
    }

    private static string InputPath(string name) => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", name + ".jxl");
}
