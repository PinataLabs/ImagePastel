// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.PixelFormats;
using PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// DC frames (D13c): a DC frame decodes at 1/8 of the size per DC level into its DC slot and is never
/// shown, and a frame that uses a DC frame takes its DC from the slot of the next level.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlDcFrameTests
{
    private const int Width = 16;
    private const int Height = 16;
    private const ulong UseDcFrame = (ulong)JxlFrameFlags.UseDcFrame;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The generated files (Generated/README.md) against djxl v0.12.0's float32 output, within 1e-4 RMSE and
    // 4e-3 peak per channel. The DC frames are not shown, so each file gives one frame.
    // Measured on x64 against djxl with AVX2: the worst channel's RMSE and the peak.
    [Theory]
    [InlineData("progressive_dc1")] // 9.7e-7, 1.7e-5
    [InlineData("progressive_dc2")] // 1.0e-6, 1.2e-5
    [InlineData("vardct_xyb_dc_frame")] // One group, -e 2: 9.1e-7, 1.2e-5
    public void DecodeToPlanes_MatchesDjxl(string name)
    {
        NpyArray expected = LoadReference(name);

        using FileStream stream = File.OpenRead(InputPath(name));
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        NpyArray actual = JxlConformanceImages.ToNpy(decoded);

        JxlConformanceFrameResult result = Assert.Single(JxlLossyComparer.AssertWithinThresholds(expected, actual));
        TestContext.Current.TestOutputHelper?.WriteLine($"Worst channel RMSE {result.WorstChannelRmse:G3}, peak {result.PeakError:G3}");
    }

    // cjxl --progressive_dc=1 writes a Modular DC frame at level 1, then the VarDCT frame that uses it.
    // --progressive_dc=2 writes a Modular DC frame at level 2, then a VarDCT DC frame at level 1 that itself
    // uses it, then the VarDCT frame that uses that. Neither
    // permutes its TOC (JxlCodestreamFrames would throw) or splits a frame into passes.
    [Fact]
    public void ProgressiveDc1_IsALevel1DcFrameThenAVarDctFrameThatUsesIt()
    {
        List<JxlFrameHeader> frames = JxlCodestreamFrames.Read(File.ReadAllBytes(InputPath("progressive_dc1")));

        Assert.Equal(2, frames.Count);
        Assert.Equal(JxlFrameType.Dc, frames[0].FrameType);
        Assert.Equal(1u, frames[0].DcLevel);
        Assert.Equal(JxlFrameEncoding.Modular, frames[0].Encoding);
        Assert.Equal((JxlFrameFlags)0, frames[0].Flags & JxlFrameFlags.UseDcFrame);
        Assert.Equal(JxlFrameType.Regular, frames[1].FrameType);
        Assert.Equal(JxlFrameEncoding.VarDct, frames[1].Encoding);
        Assert.Equal(JxlFrameFlags.UseDcFrame, frames[1].Flags & JxlFrameFlags.UseDcFrame);
        Assert.True(frames[1].IsLast);
        Assert.All(frames, f => Assert.Equal(1u, f.Passes.NumPasses));
    }

    [Fact]
    public void ProgressiveDc2_IsALevel2DcFrameThenALevel1DcFrameThatUsesItThenAVarDctFrame()
    {
        List<JxlFrameHeader> frames = JxlCodestreamFrames.Read(File.ReadAllBytes(InputPath("progressive_dc2")));

        Assert.Equal(3, frames.Count);
        Assert.Equal(JxlFrameType.Dc, frames[0].FrameType);
        Assert.Equal(2u, frames[0].DcLevel);
        Assert.Equal(JxlFrameEncoding.Modular, frames[0].Encoding);
        Assert.Equal((JxlFrameFlags)0, frames[0].Flags & JxlFrameFlags.UseDcFrame);
        Assert.Equal(JxlFrameType.Dc, frames[1].FrameType);
        Assert.Equal(1u, frames[1].DcLevel);
        Assert.Equal(JxlFrameEncoding.VarDct, frames[1].Encoding);
        Assert.Equal(JxlFrameFlags.UseDcFrame, frames[1].Flags & JxlFrameFlags.UseDcFrame);
        Assert.Equal(JxlFrameType.Regular, frames[2].FrameType);
        Assert.Equal(JxlFrameEncoding.VarDct, frames[2].Encoding);
        Assert.Equal(JxlFrameFlags.UseDcFrame, frames[2].Flags & JxlFrameFlags.UseDcFrame);
        Assert.True(frames[2].IsLast);
        Assert.All(frames, f => Assert.Equal(1u, f.Passes.NumPasses));
    }

    // Image.Load gives one frame for each file: the DC frames are not shown. Identify gives the image size,
    // not a DC frame's. Measured: largest difference 0.50 of one 8-bit step, which is the rounding.
    [Theory]
    [InlineData("progressive_dc1")]
    [InlineData("progressive_dc2")]
    public void Load_Rgb24_IsOneFrameWithinOneStepOfTheClampedReference(string name)
    {
        NpyArray expected = LoadReference(name);

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), InputPath(name));
        Assert.Equal(1, image.Frames.Count);
        Assert.Equal(new Size(582, 453), image.Size());

        NpyArray actual = JxlConformanceImages.ToNpy(image, expected.Shape[3]);
        JxlConformanceFrameResult result = Assert.Single(JxlConformanceComparer.Measure(expected, actual, JxlConformanceComparison.ClampToUnitRange));
        TestContext.Current.TestOutputHelper?.WriteLine($"Peak error {result.PeakError * 255:G3} of one 8-bit step");
        Assert.True(result.PeakError <= (1.0 / 255) + 1e-6, $"Peak error {result.PeakError * 255:G4} steps");
    }

    [Theory]
    [InlineData("progressive_dc1")]
    [InlineData("progressive_dc2")]
    public void Identify_ReportsTheImageSize(string name)
    {
        using FileStream stream = File.OpenRead(InputPath(name));
        IImageInfo info = new JxlDecoder().Identify(Configuration.Default, stream, CancellationToken);

        Assert.Equal(582, info.Width);
        Assert.Equal(453, info.Height);
    }

    // A DC frame is not shown and does not count as a decoded frame: DC frames before a frame give the frame
    // alone. Each frame that uses a DC frame finds it in its slot: a Modular frame reads no DC, so its pixels
    // do not change. "2, 1" is the chain of --progressive_dc=2: a level-2 DC frame, then a level-1 DC frame
    // that uses it, then the frame.
    [Theory]
    [InlineData(1u)]
    [InlineData(2u, 1u)]
    public void DecodeToPlanes_DcFramesBeforeAFrame_GiveTheFrameAlone(params uint[] levels)
    {
        JxlTestFrame frame = JxlTestFrame.Last(JxlTestLayers.Ramp);
        JxlTestLayers layers = new(Width, Height);
        for (int i = 0; i < levels.Length; i++)
        {
            layers.Add(new JxlTestFrame(JxlTestLayers.ColourB) { Type = JxlFrameType.Dc, DcLevel = levels[i], Flags = i > 0 ? UseDcFrame : 0 });
        }

        NpyArray expected = DecodeToNpy(new JxlTestLayers(Width, Height), frame);
        NpyArray actual = DecodeToNpy(layers, frame with { Flags = UseDcFrame });

        Assert.Equal(expected.Shape, actual.Shape);
        Assert.Equal(expected.Data, actual.Data);
    }

    // For the noise seeds a DC frame counts as not shown, as libjxl's InitFrame counts it: the frame after it
    // is still the first shown frame, so its noise is the noise of the frame alone.
    [Fact]
    public void DecodeToPlanes_DcFrameBeforeANoisyFrame_IsNotCountedAsShown()
    {
        JxlTestFrame frame = JxlTestFrame.Last(JxlTestLayers.Ramp) with { NoiseLut = Enumerable.Repeat(1023u, 8).ToArray() };

        NpyArray expected = DecodeToNpy(new JxlTestLayers(Width, Height), frame);
        NpyArray actual = DecodeToNpy(new JxlTestLayers(Width, Height).Add(new JxlTestFrame(JxlTestLayers.ColourB) { Type = JxlFrameType.Dc }), frame);

        Assert.Equal(expected.Shape, actual.Shape);
        Assert.Equal(expected.Data, actual.Data);
    }

    // The DC-frame bounds of libjxl's InitializePassesSharedState (passes_state.cc). The levels list the DC
    // frames before the last frame, which uses a DC frame; a negative level is a DC frame that uses one too.
    [Theory]
    [InlineData("Invalid DC level for kUseDcFrame: 4", -4)] // A level-4 DC frame that uses a DC frame.
    [InlineData("kUseDcFrame specified for dc_level 0, but no frame was decoded with level 1")] // No DC frame.
    [InlineData("kUseDcFrame specified for dc_level 0, but no frame was decoded with level 1", 2)] // Only a level-2 one.
    [InlineData("kUseDcFrame specified for dc_level 1, but no frame was decoded with level 2", -1)] // A level-1 DC frame with no level 2.
    public void DecodeToPlanes_UseDcFrameWithNoDcFrame_Throws(string message, params int[] levels)
    {
        JxlTestLayers layers = new(Width, Height);
        foreach (int level in levels)
        {
            layers.Add(new JxlTestFrame(JxlTestLayers.ColourB) { Type = JxlFrameType.Dc, DcLevel = (uint)Math.Abs(level), Flags = level < 0 ? UseDcFrame : 0 });
        }

        using MemoryStream stream = new(layers.Add(JxlTestFrame.Last(JxlTestLayers.Ramp) with { Flags = UseDcFrame }).ToArray());

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken));
        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    // libjxl's dec_group.cc checks that each AC group's blocks lie inside the DC image, which for a VarDCT
    // frame that uses a DC frame is the DC frame's. A frame larger than the image (a custom size) fails it.
    // This port checks the DC frame's size before the frame's sections are read, with libjxl's message. The
    // level-1 DC frame of the 16 x 16 image is 2 x 2; the 24 x 16 frame has 3 x 2 blocks.
    [Fact]
    public void DecodeToPlanes_VarDctFrameLargerThanItsDcFrame_Throws()
    {
        JxlTestLayers layers = new JxlTestLayers(Width, Height).Add(new JxlTestFrame(JxlTestLayers.ColourB) { Type = JxlFrameType.Dc });
        JxlTestFrame frame = JxlTestFrame.Last(JxlTestLayers.Ramp) with { Flags = UseDcFrame, Encoding = JxlFrameEncoding.VarDct, XSize = Width + 8, YSize = Height };
        using MemoryStream stream = new(layers.Add(frame).ToArray());

        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken));
        Assert.Contains("Frame dimensions are too big for the image.", ex.Message, StringComparison.Ordinal);
    }

    private static NpyArray DecodeToNpy(JxlTestLayers layers, JxlTestFrame frame)
    {
        using MemoryStream stream = new(layers.Add(frame).ToArray());
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken);
        return JxlConformanceImages.ToNpy(decoded);
    }

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }

    private static NpyArray LoadReference(string name) => NpyArray.Load(Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", name + ".npy"));

    private static string InputPath(string name) => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", name + ".jxl");
}
