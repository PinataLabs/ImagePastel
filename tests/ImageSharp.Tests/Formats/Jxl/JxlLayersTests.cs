// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
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
        byte[] codestream = new JxlTestLayers(Width, Height).Add(Last(JxlTestLayers.ColourA)).ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (200, 100, 50, 128));
    }

    [Fact]
    public void Add_SumsTheColourAndReplacesTheAlpha()
    {
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(Last(JxlTestLayers.ColourB) with { Color = new(JxlBlendMode.Add) })
            .ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (240, 320, 210, 64));
    }

    [Fact]
    public void CroppedFrame_OntoASlotNoFrameSaved_BlendsOntoZeros()
    {
        // libjxl blends a frame whose slot is empty against zeros (stage_blending.cc), and does not reject it.
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(Last(JxlTestLayers.ColourA) with { X0 = 4, Y0 = 3, XSize = 10, YSize = 6 })
            .ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => x is >= 4 and < 14 && y is >= 3 and < 9 ? (200, 100, 50, 128) : (0, 0, 0, 0));
    }

    [Fact]
    public void Blend_FromASlotNoFrameSaved_BlendsOntoZeros()
    {
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(Last(JxlTestLayers.ColourB) with { Color = new(JxlBlendMode.Add, Source: 3), ExtraChannel = new(JxlBlendMode.Add, Source: 3) })
            .ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (40, 220, 160, 64));
    }

    [Fact]
    public void CroppedFrame_WithANegativeOriginAndLargerThanTheImage_IsClipped()
    {
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(Last(JxlTestLayers.Ramp) with { X0 = -5, Y0 = -7, XSize = 40, YSize = 30 })
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
            .Add(Last(JxlTestLayers.ColourB) with { Color = new(JxlBlendMode.Add, Source: 2), ExtraChannel = new(JxlBlendMode.Add, Source: 1) })
            .ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (240, 320, 210, 128));
    }

    [Fact]
    public void ReferenceOnlyFrame_IsNotShownButCanBeBlendedOnto()
    {
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourB))
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { Type = JxlFrameType.ReferenceOnly, SaveAsReference = 1 })
            .Add(Last(JxlTestLayers.ColourB) with { Color = new(JxlBlendMode.Add, Source: 1) })
            .ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (240, 320, 210, 64));
    }

    [Fact]
    public void Load_LayeredImage_GivesTheCoalescedFrame()
    {
        byte[] codestream = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(Last(JxlTestLayers.ColourA) with { X0 = 4, Y0 = 3, XSize = 10, YSize = 6, Color = new(JxlBlendMode.Mul), ExtraChannel = new(JxlBlendMode.Mul) })
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
            .Add(Last(JxlTestLayers.ColourB) with { Duration = 10, Color = new(JxlBlendMode.Add) })
            .ToArray();

        AssertEveryPixel(DecodeToPlanes(codestream), (x, y) => (240, 320, 210, 64));
    }

    [Fact]
    public void Animation_WithMoreThanOneDisplayedFrame_ThrowsNotSupported()
    {
        // Animations are the next slice (#191).
        byte[] codestream = new JxlTestLayers(Width, Height, animation: true)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { Duration = 10 })
            .Add(Last(JxlTestLayers.ColourB) with { Duration = 10 })
            .ToArray();

        Assert.Throws<NotSupportedException>(() => DecodeToPlanes(codestream).Dispose());
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
        Assert.Equal(expected.Shape, actual.Shape);
        for (int i = 0; i < expected.Data.Length; i++)
        {
            Assert.True(MathF.Abs(expected.Data[i] - actual.Data[i]) <= 1e-6F, $"Sample {i}: djxl {expected.Data[i]}, ImageSharp {actual.Data[i]}.");
        }
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
            .Add(Last(JxlTestLayers.Ramp) with { X0 = 3, Y0 = 2, XSize = 9, YSize = 7, Color = new(JxlBlendMode.Blend), ExtraChannel = new(JxlBlendMode.Blend) }),
        ["crop_negative_origin_oversized"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(Last(JxlTestLayers.Ramp) with { X0 = -5, Y0 = -7, XSize = 40, YSize = 30, Color = new(JxlBlendMode.Blend) }),
        ["crop_past_the_far_edges"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(Last(JxlTestLayers.Ramp) with { X0 = 20, Y0 = 10, XSize = 12, YSize = 9, Color = new(JxlBlendMode.Add) }),
        ["crop_wholly_outside"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(Last(JxlTestLayers.Ramp) with { X0 = 30, Y0 = -20, XSize = 6, YSize = 6, Color = new(JxlBlendMode.Add) }),
        ["crop_onto_empty_slot"] = new JxlTestLayers(Width, Height)
            .Add(Last(JxlTestLayers.Ramp) with { X0 = 4, Y0 = 3, XSize = 10, YSize = 6 }),
        ["cropped_layers_saved_and_reused"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { X0 = -2, Y0 = 1, XSize = 12, YSize = 9, SaveAsReference = 1 })
            .Add(new JxlTestFrame(JxlTestLayers.ColourB) { X0 = 6, Y0 = -3, XSize = 20, YSize = 8, Color = new(JxlBlendMode.Blend, Source: 1), ExtraChannel = new(JxlBlendMode.Blend, Source: 1), SaveAsReference = 2 })
            .Add(Last(JxlTestLayers.Ramp) with { X0 = 1, Y0 = 1, XSize = 5, YSize = 5, Color = new(JxlBlendMode.AlphaWeightedAdd, Source: 2), ExtraChannel = new(JxlBlendMode.Add, Source: 1) }),
        ["slot_per_channel"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { SaveAsReference = 2 })
            .Add(new JxlTestFrame(JxlTestLayers.ColourB) { SaveAsReference = 1 })
            .Add(Last(JxlTestLayers.Ramp) with { Color = new(JxlBlendMode.Mul, Source: 2), ExtraChannel = new(JxlBlendMode.Add, Source: 1) }),
        ["empty_slot_source"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(Last(JxlTestLayers.ColourB) with { Color = new(JxlBlendMode.Add, Source: 3), ExtraChannel = new(JxlBlendMode.Blend, Source: 3) }),
        ["reference_only"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourB))
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { Type = JxlFrameType.ReferenceOnly, SaveAsReference = 1 })
            .Add(Last(JxlTestLayers.Ramp) with { Color = new(JxlBlendMode.Blend, Source: 1), ExtraChannel = new(JxlBlendMode.Blend, Source: 1) }),
        ["saved_before_colour_transform_as_alpha_background"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { SaveAsReference = 1, SaveBeforeColorTransform = true })
            .Add(Last(JxlTestLayers.ColourB) with { ExtraChannel = new(JxlBlendMode.Add, Source: 1) }),
        ["animation_one_displayed_frame"] = new JxlTestLayers(Width, Height, animation: true)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA) { X0 = 2, Y0 = 3, XSize = 8, YSize = 8 })
            .Add(Last(JxlTestLayers.Ramp) with { Duration = 7, Color = new(JxlBlendMode.Blend), ExtraChannel = new(JxlBlendMode.Blend) }),

        // One colour plane: libjxl blends three equal planes.
        ["grey_add"] = GreyLayers(new(JxlBlendMode.Add), new(JxlBlendMode.Replace)),
        ["grey_blend"] = GreyLayers(new(JxlBlendMode.Blend), new(JxlBlendMode.Blend)),
        ["grey_alpha_weighted_add"] = GreyLayers(new(JxlBlendMode.AlphaWeightedAdd, Clamp: true), new(JxlBlendMode.AlphaWeightedAdd)),
        ["grey_mul"] = GreyLayers(new(JxlBlendMode.Mul, Clamp: true), new(JxlBlendMode.Mul)),
        ["skip_progressive"] = new JxlTestLayers(Width, Height)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(Last(JxlTestLayers.Ramp) with { Type = JxlFrameType.SkipProgressive, Color = new(JxlBlendMode.Blend) }),
    };

    private static JxlTestFrame Last(byte[] section) => new(section) { IsLast = true };

    // Two full-size frames of the generated 64 x 64 16-bit grey and alpha file.
    private static JxlTestLayers GreyLayers(JxlTestBlending color, JxlTestBlending extraChannel)
    {
        string path = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", "greyscale_alpha.jxl");
        JxlTestLayers layers = JxlTestLayers.FromFile(File.ReadAllBytes(path), out byte[] section);
        return layers
            .Add(new JxlTestFrame(section))
            .Add(Last(section) with { Color = color, ExtraChannel = extraChannel });
    }

    private static JxlTestLayers TwoLayers(JxlTestBlending color, JxlTestBlending extraChannel, bool premultiplied = false, byte[] top = null)
        => new JxlTestLayers(Width, Height, premultiplied: premultiplied)
            .Add(new JxlTestFrame(JxlTestLayers.ColourA))
            .Add(Last(top ?? JxlTestLayers.ColourB) with { Color = color, ExtraChannel = extraChannel });

    private static JxlDecodedImage DecodeToPlanes(byte[] codestream)
    {
        using MemoryStream stream = new(codestream);
        return new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
    }

    // Checks every sample against 8-bit values, which may pass 255: the planes are not clamped.
    private static void AssertEveryPixel(JxlDecodedImage decoded, Func<int, int, (int R, int G, int B, int A)> expected)
    {
        using (decoded)
        {
            Assert.Single(decoded.Frames);
            Assert.Equal(Width, decoded.Width);
            Assert.Equal(Height, decoded.Height);
            IReadOnlyList<Buffer2D<float>> planes = decoded.Frames[0].Planes;
            Assert.Equal(4, planes.Count);
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    (int r, int g, int b, int a) = expected(x, y);
                    int[] want = { r, g, b, a };
                    for (int c = 0; c < 4; c++)
                    {
                        float sample = planes[c].DangerousGetRowSpan(y)[x];
                        Assert.True(MathF.Abs(sample - (want[c] / 255F)) < 1e-6F, $"({x}, {y}) channel {c}: {sample * 255F}, expected {want[c]}.");
                    }
                }
            }
        }
    }

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }
}
