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
/// The size of each channel in <see cref="JxlRenderPipeline"/>, as libjxl's pipeline gives it: every
/// channel has a shift, and at each stage its size is the upsampled frame size divided by 2 to the shift
/// it has left, rounded up.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlChannelSizesTests
{
    private const int NumColorPlanes = 3;
    private const float Padding = -7f;

    // The expected shifts are worked by hand from the header: a colour channel's chroma shift plus
    // log2 of the upsampling, and an extra channel's log2 of its own upsampling. The chroma modes are
    // Cb, Y, Cr; a channel's shift is the largest shift of any mode less its own mode's shift, so with
    // Y at 4:2:0 and Cb, Cr at 4:4:4, the chroma channels are the subsampled ones.
    [Theory]
    [InlineData(false, null, 1u, 0u, new[] { 0, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(false, null, 2u, 0u, new[] { 1, 1, 1, 1, 1, 1, 1, 1 })]
    [InlineData(false, null, 2u, 8u, new[] { 1, 1, 1, 1, 1, 1, 3, 3 })]
    [InlineData(false, null, 1u, 4u, new[] { 0, 0, 0, 0, 0, 0, 2, 2 })]
    [InlineData(true, new[] { 0, 1, 0 }, 1u, 0u, new[] { 1, 1, 0, 0, 1, 1, 0, 0 })]
    [InlineData(true, new[] { 0, 2, 0 }, 2u, 4u, new[] { 2, 1, 1, 1, 2, 1, 2, 2 })]
    [InlineData(true, new[] { 3, 0, 3 }, 4u, 0u, new[] { 2, 2, 2, 3, 2, 2, 2, 2 })]
    public void ChannelShifts_UpsamplingStagesOfTheFrame_GiveEachChannelItsShift(bool yCbCr, int[] chromaModes, uint upsampling, uint extraChannelUpsampling, int[] expected)
    {
        JxlTestFrame frame = JxlTestFrame.Last(JxlTestLayers.ColourA) with
        {
            YCbCr = yCbCr,
            ChromaSubsampling = chromaModes,
            Upsampling = upsampling,
            ExtraChannelUpsampling = extraChannelUpsampling,
        };
        JxlFrameHeader frameHeader = JxlTestLayers.ReadFirstFrameHeader(new JxlTestLayers(16, 16).Add(frame).ToArray());

        JxlUpsamplingStages stages = JxlUpsamplingStages.Create(frameHeader);

        JxlChannelShift[] shifts = JxlRenderPipeline.ChannelShifts(3, 1, stages.Chroma, stages.BeforePatches, stages.AfterPatches);

        Assert.Equal(4, shifts.Length);
        for (int c = 0; c < shifts.Length; c++)
        {
            Assert.Equal((expected[2 * c], expected[(2 * c) + 1]), (shifts[c].X, shifts[c].Y));
        }
    }

    // A 600x600 frame upsampled 2x is coded at 300x300: 2x2 groups of 256. libjxl's MakeChannelRect
    // scales the group size up by the upsampling (512) and down by the channel's shift, and clips it to
    // the channel's size, 600 / 2^shift rounded up. Channel 0 has shifts (2, 1): 128x256 groups of a
    // 150x300 plane. Channel 1 has (1, 1): 256x256 groups of 300x300. Channel 3 has (3, 3): 64x64
    // groups of 75x75.
    [Theory]
    [InlineData(0, 0, 0, 0, 128, 256)]
    [InlineData(0, 3, 128, 256, 22, 44)]
    [InlineData(1, 0, 0, 0, 256, 256)]
    [InlineData(1, 1, 256, 0, 44, 256)]
    [InlineData(1, 3, 256, 256, 44, 44)]
    [InlineData(3, 2, 0, 64, 64, 11)]
    [InlineData(3, 3, 64, 64, 11, 11)]
    public void GetGroupRect_ShiftedChannels_GivesEachChannelItsOwnRect(int c, long groupId, long x0, long y0, long xsize, long ysize)
    {
        JxlFrameDimensions frameDim = new(600, 600, 1, 0, 0, true, 2);
        JxlChannelShift[] shifts = { new(2, 1), new(1, 1), new(1, 1), new(3, 3) };

        using JxlRenderPipeline pipeline = new(Configuration.Default.MemoryAllocator, frameDim, 3, shifts);
        JxlRect rect = pipeline.GetGroupRect(groupId, c);

        Assert.Equal((x0, y0, xsize, ysize), (rect.X0, rect.Y0, rect.XSize, rect.YSize));
    }

    // Each plane holds its channel at its shift: the colour planes at the coded size, padded to whole
    // blocks for a VarDCT frame (10x13 upsampled 2x is coded at 5x7, so one 8x8 block), and an extra
    // channel at the upsampled size divided by 2 to its shift.
    [Fact]
    public void New_ShiftedChannels_AllocatesTheColorPlanesAtTheCodedPaddedSize()
    {
        JxlFrameDimensions frameDim = new(10, 13, 1, 0, 0, false, 2);
        JxlChannelShift[] shifts = { new(1, 1), new(1, 1), new(1, 1), new(1, 1), new(0, 0) };

        using JxlRenderPipeline pipeline = new(Configuration.Default.MemoryAllocator, frameDim, 3, shifts);

        for (int c = 0; c < 3; c++)
        {
            Assert.Equal((8, 8), (pipeline.GetPlane(c).Width, pipeline.GetPlane(c).Height));
        }

        Assert.Equal((5, 7), (pipeline.GetPlane(3).Width, pipeline.GetPlane(3).Height));
        Assert.Equal((10, 13), (pipeline.GetPlane(4).Width, pipeline.GetPlane(4).Height));
    }

    // A 10x13 VarDCT frame upsampled 2x is coded at 5x7, in one 8x8 block. A stage with shift 1 changes
    // the colour channels, which are at shift 1 (5x7); the extra channel is already at the frame size.
    // The stage writes, to each output pixel of the 2x2 that input pixel (x, y) gives, the input pixel
    // diagonally next to (x, y): left for the left column and right for the right one, below for the top
    // row and above for the bottom one. The expected pixels are worked by hand, mirrored at the channel's
    // 5x7 (libjxl's Mirror: 5 reads 4, 7 reads 6, -1 reads 0). Mirrored at the 10x13 frame, or read from
    // the block padding, x 5 and y 7 would read the padding.
    [Theory]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(5, 6, 3, 4)]
    [InlineData(9, 0, 4, 1)]
    [InlineData(9, 12, 4, 6)]
    [InlineData(8, 11, 3, 4)]
    public void Finish_StageWithShift1_ReadsMirroredAtTheChannelSizeAndWritesTwiceTheSize(int x, int y, int expectedX, int expectedY)
    {
        JxlFrameDimensions frameDim = new(10, 13, 1, 0, 0, false, 2);
        JxlChannelShift[] shifts = { new(1, 1), new(1, 1), new(1, 1), new(0, 0) };
        JxlFrameHeader frameHeader = JxlTestLayers.ReadFirstFrameHeader(new JxlTestLayers(16, 16).Add(JxlTestFrame.Last(JxlTestLayers.ColourA)).ToArray());
        using JxlRenderPipeline pipeline = new(Configuration.Default.MemoryAllocator, frameDim, NumColorPlanes, shifts);
        Fill(pipeline.GetPlane(0), 0, 5, 7);
        Fill(pipeline.GetPlane(1), 1, 5, 7);
        Fill(pipeline.GetPlane(2), 2, 5, 7);
        Fill(pipeline.GetPlane(3), 3, 10, 13);
        DiagonalUpsamplingStage stage = new();
        using RecordingSink sink = new();

        pipeline.Finish(frameHeader, Array.Empty<IJxlNeighbourStage>(), null, null, new IJxlNeighbourStage[] { stage }, null, Array.Empty<IJxlColorStage>(), null, sink, CancellationToken.None)?.Dispose();

        // The stage ran over the channel's 5x7, wrote 10x14, and the channel shrank to the frame's 10x13.
        Assert.Equal((5, 7), (stage.XSize, stage.Rows));
        Assert.Equal((10, 13), (sink.Width, sink.Height));
        for (int c = 0; c < NumColorPlanes; c++)
        {
            Assert.Equal((10, 14), (sink.Planes[c].Width, sink.Planes[c].Height));
            Assert.Equal(Sample(c, expectedX, expectedY), sink.Planes[c][x, y]);
        }

        // The extra channel is not upsampled.
        Assert.Equal(Sample(NumColorPlanes, x, y), sink.Planes[NumColorPlanes][x, y]);
    }

    private static float Sample(int c, int x, int y) => (1000 * c) + (20 * y) + x;

    // Fills the channel with Sample and the rest of the plane with Padding.
    private static void Fill(Buffer2D<float> plane, int c, int xsize, int ysize)
    {
        for (int y = 0; y < plane.Height; y++)
        {
            Span<float> row = plane.DangerousGetRowSpan(y);
            for (int x = 0; x < plane.Width; x++)
            {
                row[x] = x < xsize && y < ysize ? Sample(c, x, y) : Padding;
            }
        }
    }

    // A 2x upsampling stage of the colour channels: see Finish_StageWithShift1.
    private sealed class DiagonalUpsamplingStage : IJxlNeighbourStage
    {
        public int Border => 1;

        public int ShiftX => 1;

        public int ShiftY => 1;

        public int XSize { get; private set; }

        public int Rows { get; private set; }

        public bool ChangesChannel(int c) => c < NumColorPlanes;

        public void ProcessRow(JxlNeighbourRows input, Buffer2D<float>[] output, int xsize, int y)
        {
            this.XSize = xsize;
            this.Rows++;
            for (int c = 0; c < NumColorPlanes; c++)
            {
                for (int i = 0; i < 2; i++)
                {
                    ReadOnlySpan<float> row = input.GetRow(c, i == 0 ? 1 : -1);
                    Span<float> outRow = output[c].DangerousGetRowSpan((2 * y) + i);
                    for (int x = 0; x < xsize; x++)
                    {
                        outRow[2 * x] = row[input.Border + x - 1];
                        outRow[(2 * x) + 1] = row[input.Border + x + 1];
                    }
                }
            }
        }
    }

    // Keeps the planes it is given, as the planes sink does.
    private sealed class RecordingSink : IJxlRenderSink, IDisposable
    {
        public Buffer2D<float>[] Planes { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public bool DisplayEncoded => false;

        public bool ConsumeFrame(Buffer2D<float>[] planes, int width, int height)
        {
            this.Planes = planes;
            this.Width = width;
            this.Height = height;
            return true;
        }

        public void Dispose() => JxlPlanes.DisposeAll(this.Planes ?? Array.Empty<Buffer2D<float>>());
    }
}
