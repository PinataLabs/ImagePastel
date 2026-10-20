// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.Memory;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The loop-filter slot of <see cref="JxlRenderPipeline"/>, where Gaborish and EPF run: its stages read
/// the pixels around the one they write, mirrored at the frame edges as libjxl's pipeline does, and
/// never the block padding past them.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlLoopFilterSlotTests
{
    private const int NumColorPlanes = 3;
    private const int NumExtraChannels = 1;
    private const float Padding = -7f;

    // Each stage copies, to every pixel, the pixel at (dx, dy) from it. The expected source pixels are
    // libjxl's Mirror, worked by hand: the edge pixel repeats once (-1 reads 0, xsize reads xsize - 1),
    // and a border wider than the frame reflects again.
    [Theory]
    [InlineData(10, 13, -3, -3, 0, 0, 2, 2)]
    [InlineData(10, 13, 3, 3, 9, 12, 7, 10)]
    [InlineData(10, 13, -1, 0, 0, 5, 0, 5)]
    [InlineData(10, 13, 1, 0, 9, 5, 9, 5)]
    [InlineData(10, 13, 0, 2, 4, 12, 4, 11)]
    [InlineData(10, 13, 2, -2, 4, 6, 6, 4)]
    [InlineData(2, 2, -3, 0, 0, 0, 1, 0)]
    [InlineData(2, 2, 3, 0, 1, 1, 0, 1)]
    [InlineData(2, 2, 0, 3, 0, 1, 0, 0)]
    public void Finish_LoopFilterStage_ReadsNeighboursMirroredAtTheFrameEdges(int width, int height, int dx, int dy, int x, int y, int expectedX, int expectedY)
    {
        // A VarDCT frame, so the colour planes are padded to whole blocks past the frame.
        JxlFrameDimensions frameDim = new(width, height, 1, 0, 0, false, 1);

        using RecordingSink sink = new();
        Finish(frameDim, sink, new CopyStage(3, dx, dy));

        for (int c = 0; c < NumColorPlanes; c++)
        {
            Assert.Equal(Sample(c, expectedX, expectedY), sink.Planes[c][x, y]);
        }

        // The extra channels are not loop-filtered.
        Assert.Equal(Sample(NumColorPlanes, x, y), sink.Planes[NumColorPlanes][x, y]);
    }

    [Fact]
    public void Finish_TwoLoopFilterStages_EachReadsTheOutputOfTheOneBefore()
    {
        JxlFrameDimensions frameDim = new(10, 13, 1, 0, 0, false, 1);

        using RecordingSink sink = new();
        Finish(frameDim, sink, new CopyStage(1, -1, 0), new CopyStage(1, 0, -1));

        // (3, 4) takes (3, 3) of the first stage's output, which took (2, 3). A stage that wrote into
        // the planes it reads would see its own output instead: (0, 4) left to right, (3, 0) top down.
        Assert.Equal(Sample(0, 2, 3), sink.Planes[0][3, 4]);
    }

    [Fact]
    public void Finish_LoopFilterStage_RunsBeforeTheSaveBeforeTheColourTransform()
    {
        JxlFrameDimensions frameDim = new(10, 13, 1, 0, 0, false, 1);
        JxlFrameHeader frameHeader = FrameHeader(new JxlTestFrame(JxlTestLayers.ColourA) { SaveAsReference = 1, SaveBeforeColorTransform = true });

        using JxlReferenceFrame saved = Finish(frameHeader, frameDim, null, new CopyStage(1, 1, 0));

        Assert.Equal(Sample(0, 5, 2), saved.Planes[0][4, 2]);
    }

    // Colour channel c holds 1000c + 20y + x inside the frame; the extra channel continues the pattern.
    private static float Sample(int c, int x, int y) => (1000 * c) + (20 * y) + x;

    private static JxlFrameHeader FrameHeader(JxlTestFrame frame)
        => JxlTestLayers.ReadFirstFrameHeader(new JxlTestLayers(16, 16).Add(frame).ToArray());

    private static void Finish(JxlFrameDimensions frameDim, RecordingSink sink, params IJxlNeighbourStage[] loopFilters)
        => Finish(FrameHeader(JxlTestFrame.Last(JxlTestLayers.ColourA)), frameDim, sink, loopFilters)?.Dispose();

    // Fills the frame with Sample and the padding past it with Padding, then finishes the frame.
    private static JxlReferenceFrame Finish(JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, RecordingSink sink, params IJxlNeighbourStage[] loopFilters)
    {
        using JxlRenderPipeline pipeline = new(Configuration.Default.MemoryAllocator, frameDim, NumColorPlanes, new JxlChannelShift[NumColorPlanes + NumExtraChannels]);
        for (int c = 0; c < pipeline.NumChannels; c++)
        {
            Buffer2D<float> plane = pipeline.GetPlane(c);
            for (int y = 0; y < plane.Height; y++)
            {
                Span<float> row = plane.DangerousGetRowSpan(y);
                for (int x = 0; x < plane.Width; x++)
                {
                    row[x] = x < frameDim.XSizeUpsampled && y < frameDim.YSizeUpsampled ? Sample(c, x, y) : Padding;
                }
            }
        }

        return pipeline.Finish(frameHeader, loopFilters, null, null, Array.Empty<IJxlNeighbourStage>(), null, Array.Empty<IJxlColorStage>(), null, sink, CancellationToken.None);
    }

    // Writes, to each pixel of the colour channels, the input pixel at (dx, dy) from it.
    private sealed class CopyStage : IJxlNeighbourStage
    {
        private readonly int dx;
        private readonly int dy;

        public CopyStage(int border, int dx, int dy)
        {
            this.Border = border;
            this.dx = dx;
            this.dy = dy;
        }

        public int Border { get; }

        public bool ChangesChannel(int c) => c < NumColorPlanes;

        public void ProcessRow(JxlNeighbourRows input, Buffer2D<float>[] output, int xsize, int y)
        {
            for (int c = 0; c < NumColorPlanes; c++)
            {
                ReadOnlySpan<float> row = input.GetRow(c, this.dy);
                Span<float> outRow = output[c].DangerousGetRowSpan(y);
                for (int x = 0; x < xsize; x++)
                {
                    outRow[x] = row[input.Border + x + this.dx];
                }
            }
        }
    }

    // Keeps the planes it is given, as the planes sink does.
    private sealed class RecordingSink : IJxlRenderSink, IDisposable
    {
        public Buffer2D<float>[] Planes { get; private set; }

        public bool DisplayEncoded => false;

        public bool ConsumeFrame(Buffer2D<float>[] planes, int width, int height)
        {
            this.Planes = planes;
            return true;
        }

        public void Dispose() => JxlPlanes.DisposeAll(this.Planes ?? Array.Empty<Buffer2D<float>>());
    }
}
