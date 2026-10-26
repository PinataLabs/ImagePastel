// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;
using PinataLabs.ImagePastel.Memory;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// <see cref="JxlRenderPipeline"/> with what no frame decodes yet: a colour-transform stage, which runs
/// where libjxl puts the XYB and from-linear stages, after the save before the colour transform and
/// before the save after it and the sink; and the colour planes of a VarDCT frame, padded to whole blocks.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlRenderPipelineTests
{
    private const int Width = 16;
    private const int Height = 16;
    private const int NumColorPlanes = 3;
    private const int NumExtraChannels = 1;
    private const float Sample = 0.25f;
    private const float Padding = -7f;

    // 10x13 pixels: 2x2 blocks, so 16x16 padded.
    private static readonly JxlFrameDimensions VarDctFrameDim = new(10, 13, 1, 0, 0, false, 1);

    [Fact]
    public void Finish_ColorStage_ChangesWhatTheSinkReceives()
    {
        JxlFrameHeader frameHeader = FrameHeader(JxlTestFrame.Last(JxlTestLayers.ColourA));
        using RecordingSink sink = new();

        Finish(frameHeader, frameHeader.ToFrameDimensions(), new AddOneToColor(), sink)?.Dispose();

        Assert.Equal(new[] { Sample + 1, Sample + 1, Sample + 1, Sample }, sink.Samples);
    }

    [Fact]
    public void Finish_SaveBeforeColorTransform_KeepsTheSamplesBeforeTheColorStage()
    {
        JxlFrameHeader frameHeader = FrameHeader(new JxlTestFrame(JxlTestLayers.ColourA) { SaveAsReference = 1, SaveBeforeColorTransform = true });
        using RecordingSink sink = new();

        using JxlReferenceFrame saved = Finish(frameHeader, frameHeader.ToFrameDimensions(), new AddOneToColor(), sink);

        Assert.Equal(new[] { Sample, Sample, Sample, Sample }, FirstSamples(saved.Planes));
        Assert.Equal(new[] { Sample + 1, Sample + 1, Sample + 1, Sample }, sink.Samples);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Finish_SaveAfterColorTransform_KeepsTheSamplesAfterTheColorStage(bool displayed)
    {
        JxlFrameHeader frameHeader = FrameHeader(new JxlTestFrame(JxlTestLayers.ColourA) { SaveAsReference = 1 });
        using RecordingSink sink = new();

        using JxlReferenceFrame saved = Finish(frameHeader, frameHeader.ToFrameDimensions(), new AddOneToColor(), displayed ? sink : null);

        Assert.Equal(new[] { Sample + 1, Sample + 1, Sample + 1, Sample }, FirstSamples(saved.Planes));
    }

    [Fact]
    public void New_VarDctFrame_PadsOnlyTheColorPlanesToWholeBlocks()
    {
        using JxlRenderPipeline pipeline = new(Configuration.Default.MemoryAllocator, VarDctFrameDim, NumColorPlanes, new JxlChannelShift[NumColorPlanes + NumExtraChannels]);

        for (int c = 0; c < NumColorPlanes; c++)
        {
            Assert.Equal((16, 16), (pipeline.GetPlane(c).Width, pipeline.GetPlane(c).Height));
        }

        Assert.Equal((10, 13), (pipeline.GetPlane(3).Width, pipeline.GetPlane(3).Height));
    }

    [Fact]
    public void New_ModularFrame_DoesNotPad()
    {
        using JxlRenderPipeline pipeline = new(Configuration.Default.MemoryAllocator, new JxlFrameDimensions(10, 13, 1, 0, 0, true, 1), NumColorPlanes, new JxlChannelShift[NumColorPlanes + NumExtraChannels]);

        for (int c = 0; c < NumColorPlanes + NumExtraChannels; c++)
        {
            Assert.Equal((10, 13), (pipeline.GetPlane(c).Width, pipeline.GetPlane(c).Height));
        }
    }

    [Fact]
    public void Finish_PaddedFrame_SinkReceivesTheFrameSizeAndItsSamples()
    {
        JxlFrameHeader frameHeader = FrameHeader(JxlTestFrame.Last(JxlTestLayers.ColourA));
        using RecordingSink sink = new();

        Finish(frameHeader, VarDctFrameDim, null, sink)?.Dispose();

        Assert.Equal((10, 13), (sink.Width, sink.Height));
        AssertFrameSamples(sink.Planes, 10, 13);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Finish_PaddedFrame_SavesTheFrameSizeAndItsSamples(bool saveBeforeColorTransform)
    {
        JxlFrameHeader frameHeader = FrameHeader(new JxlTestFrame(JxlTestLayers.ColourA) { SaveAsReference = 1, SaveBeforeColorTransform = saveBeforeColorTransform });

        using JxlReferenceFrame saved = Finish(frameHeader, VarDctFrameDim, null, null);

        Assert.Equal((10, 13), (saved.Width, saved.Height));
        AssertFrameSamples(saved.Planes, 10, 13);
    }

    private static JxlFrameHeader FrameHeader(JxlTestFrame frame)
        => JxlTestLayers.ReadFirstFrameHeader(new JxlTestLayers(Width, Height).Add(frame).ToArray());

    // Fills the frame with Sample and any padding past it with Padding, then finishes the frame.
    private static JxlReferenceFrame Finish(JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, IJxlColorStage colorStage, IJxlRenderSink sink)
    {
        using JxlRenderPipeline pipeline = new(Configuration.Default.MemoryAllocator, frameDim, NumColorPlanes, new JxlChannelShift[NumColorPlanes + NumExtraChannels]);
        for (int c = 0; c < pipeline.NumChannels; c++)
        {
            Buffer2D<float> plane = pipeline.GetPlane(c);
            for (int y = 0; y < plane.Height; y++)
            {
                plane.DangerousGetRowSpan(y).Fill(y < frameDim.YSizeUpsampled ? Sample : Padding);
                plane.DangerousGetRowSpan(y).Slice((int)frameDim.XSizeUpsampled).Fill(Padding);
            }
        }

        IJxlColorStage[] colorStages = colorStage != null ? new[] { colorStage } : Array.Empty<IJxlColorStage>();
        return pipeline.Finish(frameHeader, Array.Empty<IJxlNeighbourStage>(), null, null, Array.Empty<IJxlNeighbourStage>(), null, colorStages, null, sink, CancellationToken.None);
    }

    // The planes may keep their padding, so only the frame's samples are checked.
    private static void AssertFrameSamples(Buffer2D<float>[] planes, int width, int height)
    {
        Assert.Equal(NumColorPlanes + NumExtraChannels, planes.Length);
        foreach (Buffer2D<float> plane in planes)
        {
            Assert.True(plane.Width >= width && plane.Height >= height, $"The plane is {plane.Width}x{plane.Height}");
            for (int y = 0; y < height; y++)
            {
                Assert.All(plane.DangerousGetRowSpan(y).Slice(0, width).ToArray(), s => Assert.Equal(Sample, s));
            }
        }
    }

    private static float[] FirstSamples(Buffer2D<float>[] planes)
    {
        float[] samples = new float[planes.Length];
        for (int c = 0; c < planes.Length; c++)
        {
            samples[c] = planes[c][0, 0];
        }

        return samples;
    }

    // Adds 1 to the three colour channels.
    private sealed class AddOneToColor : IJxlColorStage
    {
        public void ProcessRow(Buffer2D<float>[] planes, int xsize, int y)
        {
            for (int c = 0; c < NumColorPlanes; c++)
            {
                foreach (ref float sample in planes[c].DangerousGetRowSpan(y).Slice(0, xsize))
                {
                    sample += 1;
                }
            }
        }
    }

    // Keeps the planes it is given, as the planes sink does.
    private sealed class RecordingSink : IJxlRenderSink, IDisposable
    {
        public float[] Samples { get; private set; }

        public Buffer2D<float>[] Planes { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public bool DisplayEncoded => false;

        public bool ConsumeFrame(Buffer2D<float>[] planes, int width, int height)
        {
            this.Samples = FirstSamples(planes);
            this.Planes = planes;
            this.Width = width;
            this.Height = height;
            return true;
        }

        public void Dispose() => JxlPlanes.DisposeAll(this.Planes ?? Array.Empty<Buffer2D<float>>());
    }
}
