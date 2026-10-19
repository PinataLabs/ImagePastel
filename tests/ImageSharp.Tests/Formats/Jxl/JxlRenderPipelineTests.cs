// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.Memory;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// <see cref="JxlRenderPipeline.Finish"/> with a colour-transform stage, which no frame has yet: the
/// stage runs where libjxl puts the XYB and from-linear stages, after the save before the colour
/// transform and before the save after it and the sink.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlRenderPipelineTests
{
    private const int Width = 16;
    private const int Height = 16;
    private const int NumChannels = 4;
    private const float Sample = 0.25f;

    [Fact]
    public void Finish_ColorStage_ChangesWhatTheSinkReceives()
    {
        JxlFrameHeader frameHeader = FrameHeader(JxlTestFrame.Last(JxlTestLayers.ColourA));
        RecordingSink sink = new();

        Finish(frameHeader, sink)?.Dispose();

        Assert.Equal(new[] { Sample + 1, Sample + 1, Sample + 1, Sample }, sink.Samples);
    }

    [Fact]
    public void Finish_SaveBeforeColorTransform_KeepsTheSamplesBeforeTheColorStage()
    {
        JxlFrameHeader frameHeader = FrameHeader(new JxlTestFrame(JxlTestLayers.ColourA) { SaveAsReference = 1, SaveBeforeColorTransform = true });
        RecordingSink sink = new();

        using JxlReferenceFrame saved = Finish(frameHeader, sink);

        Assert.Equal(new[] { Sample, Sample, Sample, Sample }, FirstSamples(saved.Planes));
        Assert.Equal(new[] { Sample + 1, Sample + 1, Sample + 1, Sample }, sink.Samples);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Finish_SaveAfterColorTransform_KeepsTheSamplesAfterTheColorStage(bool displayed)
    {
        JxlFrameHeader frameHeader = FrameHeader(new JxlTestFrame(JxlTestLayers.ColourA) { SaveAsReference = 1 });

        using JxlReferenceFrame saved = Finish(frameHeader, displayed ? new RecordingSink() : null);

        Assert.Equal(new[] { Sample + 1, Sample + 1, Sample + 1, Sample }, FirstSamples(saved.Planes));
    }

    private static JxlFrameHeader FrameHeader(JxlTestFrame frame)
        => JxlTestLayers.ReadFirstFrameHeader(new JxlTestLayers(Width, Height).Add(frame).ToArray());

    private static JxlReferenceFrame Finish(JxlFrameHeader frameHeader, IJxlRenderSink sink)
    {
        using JxlRenderPipeline pipeline = new(Configuration.Default.MemoryAllocator, frameHeader.ToFrameDimensions(), NumChannels);
        for (int c = 0; c < NumChannels; c++)
        {
            for (int y = 0; y < pipeline.GetPlane(c).Height; y++)
            {
                pipeline.GetPlane(c).DangerousGetRowSpan(y).Fill(Sample);
            }
        }

        return pipeline.Finish(frameHeader, null, new IJxlColorStage[] { new AddOneToColor() }, null, sink, CancellationToken.None);
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
            for (int c = 0; c < 3; c++)
            {
                foreach (ref float sample in planes[c].DangerousGetRowSpan(y).Slice(0, xsize))
                {
                    sample += 1;
                }
            }
        }
    }

    private sealed class RecordingSink : IJxlRenderSink
    {
        public float[] Samples { get; private set; }

        public bool ConsumeFrame(Buffer2D<float>[] planes, int width, int height)
        {
            this.Samples = FirstSamples(planes);
            return false;
        }
    }
}
