// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// The undo-orientation step follows libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// WriteToOutputStage in lib/jxl/render_pipeline/stage_write.cc (ShouldFlipX, ShouldFlipY,
// ShouldTranspose, ProcessRow, FlipX and WriteToOutput), and SetImageOutput in lib/jxl/dec_frame.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// Undoes the image orientation as the planes are written out, then hands the oriented planes to the
/// next sink. libjxl does this in its write-to-output stage, for the frames it shows; the frames it
/// keeps as references stay unoriented. Orientations 5 to 8 swap width and height.
/// </summary>
/// <remarks>
/// A deliberate departure from libjxl: libjxl writes each pixel straight to its oriented place in the
/// caller's buffer. Here the oriented planes are a copy, because <see cref="JxlDecoderCore.DecodeToPlanes"/>
/// has no output buffer and must get oriented planes too; the pixel mapping is libjxl's.
/// </remarks>
internal sealed class JxlOrientingSink : IJxlRenderSink
{
    private readonly MemoryAllocator allocator;
    private readonly IJxlRenderSink next;
    private readonly bool flipX;
    private readonly bool flipY;
    private readonly bool transpose;

    private JxlOrientingSink(MemoryAllocator allocator, JxlOrientation undoOrientation, IJxlRenderSink next)
    {
        this.allocator = allocator;
        this.next = next;
        this.flipX = ShouldFlipX(undoOrientation);
        this.flipY = ShouldFlipY(undoOrientation);
        this.transpose = ShouldTranspose(undoOrientation);
    }

    public bool DisplayEncoded => this.next.DisplayEncoded;

    /// <summary>
    /// Returns a sink that undoes <paramref name="undoOrientation"/> before <paramref name="next"/>, or
    /// <paramref name="next"/> itself for the identity.
    /// </summary>
    /// <param name="allocator">The allocator for the oriented planes.</param>
    /// <param name="undoOrientation">The image orientation.</param>
    /// <param name="next">The sink that receives the oriented planes.</param>
    /// <returns>The sink.</returns>
    public static IJxlRenderSink Create(MemoryAllocator allocator, JxlOrientation undoOrientation, IJxlRenderSink next)
        => undoOrientation == JxlOrientation.Identity ? next : new JxlOrientingSink(allocator, undoOrientation, next);

    public bool ConsumeFrame(Buffer2D<float>[] planes, int width, int height)
    {
        int outWidth = this.transpose ? height : width;
        int outHeight = this.transpose ? width : height;
        Buffer2D<float>[] oriented = new Buffer2D<float>[planes.Length];
        try
        {
            for (int c = 0; c < planes.Length; c++)
            {
                oriented[c] = this.allocator.Allocate2D<float>(outWidth, outHeight);
                this.Orient(planes[c], oriented[c], width, height);
            }

            if (this.next.ConsumeFrame(oriented, outWidth, outHeight))
            {
                oriented = Array.Empty<Buffer2D<float>>();
            }
        }
        finally
        {
            JxlPlanes.DisposeAll(oriented);
        }

        // The unoriented planes stay with the pipeline, which disposes them.
        return false;
    }

    private static bool ShouldFlipX(JxlOrientation undoOrientation)
        => undoOrientation is JxlOrientation.FlipHorizontal
            or JxlOrientation.Rotate180
            or JxlOrientation.Rotate270
            or JxlOrientation.AntiTranspose;

    private static bool ShouldFlipY(JxlOrientation undoOrientation)
        => undoOrientation is JxlOrientation.FlipVertical
            or JxlOrientation.Rotate180
            or JxlOrientation.Rotate90
            or JxlOrientation.AntiTranspose;

    private static bool ShouldTranspose(JxlOrientation undoOrientation)
        => undoOrientation is JxlOrientation.Transpose
            or JxlOrientation.Rotate90
            or JxlOrientation.Rotate270
            or JxlOrientation.AntiTranspose;

    // As libjxl's ProcessRow: the row is flipped vertically first, then each pixel horizontally (FlipX),
    // and WriteToOutput puts pixel (x, y) at column y of row x when it transposes. width and height are
    // the unoriented frame's.
    private void Orient(Buffer2D<float> source, Buffer2D<float> destination, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            Span<float> row = source.DangerousGetRowSpan(y);
            int ypos = this.flipY ? height - 1 - y : y;
            if (this.transpose)
            {
                for (int x = 0; x < width; x++)
                {
                    int xpos = this.flipX ? width - 1 - x : x;
                    destination.DangerousGetRowSpan(xpos)[ypos] = row[x];
                }
            }
            else
            {
                Span<float> outRow = destination.DangerousGetRowSpan(ypos);
                if (this.flipX)
                {
                    for (int x = 0; x < width; x++)
                    {
                        outRow[width - 1 - x] = row[x];
                    }
                }
                else
                {
                    row.Slice(0, width).CopyTo(outRow);
                }
            }
        }
    }
}
