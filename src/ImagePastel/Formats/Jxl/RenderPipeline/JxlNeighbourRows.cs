// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: the mirroring of the kInOut
// channels in SimpleRenderPipeline::ProcessBuffers (lib/jxl/render_pipeline/simple_render_pipeline.cc),
// GetInputRow from lib/jxl/render_pipeline/render_pipeline_stage.h, and Mirror from lib/jxl/image_ops.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Buffers;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// The input of a neighbour stage (<see cref="IJxlNeighbourStage"/>) for one output row: for each
/// channel the stage changes, the rows <c>y - Border</c> to <c>y + Border</c>, each widened by
/// <see cref="Border"/> pixels on both sides. Pixels outside the channel are mirrored in from inside it,
/// at the channel's current size, as libjxl's pipeline does before each stage with a border; the block
/// padding past its right and bottom edges is never read.
/// </summary>
/// <remarks>
/// libjxl's simple pipeline keeps a border of <c>kRenderPipelineXOffset</c> pixels around every plane and
/// fills it before each stage. The planes here have no border, so this keeps a window of
/// <c>2 * Border + 1</c> widened rows per channel instead, and fills each row once as the window moves
/// down the frame. The values the stage reads are the same.
/// </remarks>
internal sealed class JxlNeighbourRows : IDisposable
{
    private readonly Buffer2D<float>[] planes;
    private readonly int xsize;
    private readonly int ysize;
    private readonly int windowRows;
    private readonly int rowLength;
    private readonly int[] channelSlot;
    private readonly int[] windowY;
    private readonly IMemoryOwner<float> buffer;

    /// <summary>Initializes a new instance of the <see cref="JxlNeighbourRows"/> class.</summary>
    /// <param name="allocator">The allocator of the window.</param>
    /// <param name="planes">The planes the stage reads, one per channel.</param>
    /// <param name="stage">The stage, which gives the border and the channels it reads.</param>
    /// <param name="xsize">The width of the channels the stage changes, at their current shift, where they are mirrored.</param>
    /// <param name="ysize">The height of the channels the stage changes, at their current shift.</param>
    public JxlNeighbourRows(MemoryAllocator allocator, Buffer2D<float>[] planes, IJxlNeighbourStage stage, int xsize, int ysize)
    {
        this.planes = planes;
        this.xsize = xsize;
        this.ysize = ysize;
        this.Border = stage.Border;
        this.windowRows = (2 * this.Border) + 1;
        this.rowLength = xsize + (2 * this.Border);
        this.channelSlot = new int[planes.Length];
        int numChannels = 0;
        for (int c = 0; c < planes.Length; c++)
        {
            this.channelSlot[c] = stage.ChangesChannel(c) ? numChannels++ : -1;
        }

        // Which frame row each window row holds, per channel slot; none yet.
        this.windowY = new int[numChannels * this.windowRows];
        this.windowY.AsSpan().Fill(int.MinValue);
        this.buffer = allocator.Allocate<float>(numChannels * this.windowRows * this.rowLength);
    }

    /// <summary>Gets how far the rows reach past the pixel the stage writes: <see cref="IJxlNeighbourStage.Border"/>.</summary>
    public int Border { get; }

    /// <summary>Gets the row the stage writes.</summary>
    public int Y { get; private set; }

    /// <summary>
    /// Gets every row of the window, one after another: the row <see cref="GetRow"/> returns starts at
    /// <see cref="GetRowStart"/>. A stage that reads many rows for each pixel indexes it directly.
    /// </summary>
    public ReadOnlySpan<float> Window => this.buffer.Memory.Span;

    /// <summary>
    /// Returns the input row <paramref name="dy"/> rows from <see cref="Y"/> of channel <paramref name="c"/>,
    /// as libjxl's <c>GetInputRow</c>, widened by <see cref="Border"/> pixels on both sides: element
    /// <c>Border + x</c> is pixel <c>x</c>, for <c>x</c> from <c>-Border</c> to <c>xsize + Border - 1</c>.
    /// </summary>
    /// <param name="c">A channel the stage changes.</param>
    /// <param name="dy">The row offset, from <c>-Border</c> to <c>Border</c>.</param>
    /// <returns>The widened row, of <c>xsize + 2 * Border</c> samples.</returns>
    public ReadOnlySpan<float> GetRow(int c, int dy) => this.Window.Slice(this.GetRowStart(c, dy), this.rowLength);

    /// <summary>Returns where in <see cref="Window"/> the row that <see cref="GetRow"/> returns starts.</summary>
    /// <param name="c">A channel the stage changes.</param>
    /// <param name="dy">The row offset, from <c>-Border</c> to <c>Border</c>.</param>
    /// <returns>The index of the row's first element, pixel <c>-Border</c>.</returns>
    public int GetRowStart(int c, int dy)
    {
        // libjxl's JXL_DASSERTs in GetInputRow.
        int slot = this.channelSlot[c];
        DebugGuard.IsTrue(slot >= 0, "The stage reads only the channels it changes");
        DebugGuard.IsTrue(dy >= -this.Border && dy <= this.Border, "The stage reads only the rows within its border");
        return this.RowStart(slot, this.Y + dy);
    }

    public void Dispose() => this.buffer.Dispose();

    /// <summary>
    /// Mirrors a coordinate outside <c>[0, size)</c> back inside, as libjxl's <c>Mirror</c>: the edge
    /// pixel repeats once, and a coordinate further out than the size reflects again.
    /// </summary>
    /// <param name="x">The coordinate.</param>
    /// <param name="size">The size, at least 1.</param>
    /// <returns>The coordinate inside <c>[0, size)</c>.</returns>
    internal static int Mirror(int x, int size)
    {
        while (x < 0 || x >= size)
        {
            if (x < 0)
            {
                x = -x - 1;
            }
            else
            {
                x = (2 * size) - 1 - x;
            }
        }

        return x;
    }

    /// <summary>Moves the window to output row <paramref name="y"/>, filling the rows it did not hold.</summary>
    /// <param name="y">The row the stage writes next.</param>
    internal void MoveTo(int y)
    {
        this.Y = y;
        Span<float> window = this.buffer.Memory.Span;
        for (int c = 0; c < this.planes.Length; c++)
        {
            int slot = this.channelSlot[c];
            if (slot < 0)
            {
                continue;
            }

            for (int frameY = y - this.Border; frameY <= y + this.Border; frameY++)
            {
                int windowIndex = (slot * this.windowRows) + WindowRow(frameY, this.windowRows);
                if (this.windowY[windowIndex] == frameY)
                {
                    continue;
                }

                this.windowY[windowIndex] = frameY;
                Span<float> row = window.Slice(this.RowStart(slot, frameY), this.rowLength);
                this.planes[c].DangerousGetRowSpan(Mirror(frameY, this.ysize)).Slice(0, this.xsize).CopyTo(row.Slice(this.Border));
                for (int ix = 0; ix < this.Border; ix++)
                {
                    row[this.Border - ix - 1] = row[this.Border + Mirror(-ix - 1, this.xsize)];
                    row[this.Border + this.xsize + ix] = row[this.Border + Mirror(this.xsize + ix, this.xsize)];
                }
            }
        }
    }

    private static int WindowRow(int frameY, int windowRows)
    {
        int r = frameY % windowRows;
        return r < 0 ? r + windowRows : r;
    }

    private int RowStart(int slot, int frameY) => ((slot * this.windowRows) + WindowRow(frameY, this.windowRows)) * this.rowLength;
}
