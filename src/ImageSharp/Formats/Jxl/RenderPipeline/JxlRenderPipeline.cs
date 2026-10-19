// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Structure after libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/render_pipeline/simple_render_pipeline.h and simple_render_pipeline.cc (with the
// SwitchToImageDimensions step of ProcessBuffers), RenderPipelineInput from
// lib/jxl/render_pipeline/render_pipeline.h, and the stage order of PassesDecoderState::PreparePipeline
// in lib/jxl/dec_cache.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// The serial, full-frame render pipeline: libjxl's <c>SimpleRenderPipeline</c>, its reference path.
/// It holds one float plane per channel; groups write their rectangles into them, and once every
/// group is in, the stages run over the whole frame and the sink receives the planes.
/// </summary>
/// <remarks>
/// Deliberate departures from libjxl: the planes carry no border. Instead of the border, which in libjxl
/// takes the whole 8x8 blocks that VarDCT writes past the right and bottom edges, the colour planes are
/// padded to the frame's padded size. The stages, the reference slots and the sink are given the frame
/// size, and the planes keep the padding past it. Only the loop-filter stages (Gaborish and EPF) read
/// neighbouring pixels: they get them through <see cref="JxlNeighbourRows"/>, which mirrors the pixels
/// past the frame edges in from inside the frame, as libjxl fills its border, so the padding is never
/// read. Stages run in place, or ping-pong between two sets of planes where they read neighbours, rather
/// than allocate new planes each, as decided in issue #113. And the stages are fixed steps of
/// <see cref="Finish"/> rather than a list built per frame, because the frames this port decodes need
/// only the loop-filter, patches, save, colour-transform, blending and output stages.
/// </remarks>
internal sealed class JxlRenderPipeline : IDisposable
{
    private readonly MemoryAllocator allocator;
    private readonly JxlFrameDimensions frameDim;
    private int numColorPlanes;
    private Buffer2D<float>[] planes;
    private int width;
    private int height;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlRenderPipeline"/> class, allocating one plane per
    /// channel: the colour planes at the frame's padded size, which is the frame size for a Modular frame
    /// and whole 8x8 blocks for a VarDCT frame, and the extra channels at the frame size.
    /// </summary>
    /// <param name="allocator">The allocator.</param>
    /// <param name="frameDim">The frame dimensions.</param>
    /// <param name="numColorPlanes">
    /// The number of colour planes: <see cref="Headers.JxlImageMetadata.NumColorPlanes"/>, or three for a
    /// VarDCT frame, which always codes three.
    /// </param>
    /// <param name="numExtraChannels">The number of extra channels.</param>
    public JxlRenderPipeline(MemoryAllocator allocator, JxlFrameDimensions frameDim, int numColorPlanes, int numExtraChannels)
    {
        this.allocator = allocator;
        this.frameDim = frameDim;
        this.numColorPlanes = numColorPlanes;
        this.width = (int)frameDim.XSizeUpsampled;
        this.height = (int)frameDim.YSizeUpsampled;
        this.planes = new Buffer2D<float>[numColorPlanes + numExtraChannels];
        try
        {
            for (int c = 0; c < this.planes.Length; c++)
            {
                this.planes[c] = c < numColorPlanes
                    ? allocator.Allocate2D<float>((int)frameDim.XSizeUpsampledPadded, (int)frameDim.YSizeUpsampledPadded)
                    : allocator.Allocate2D<float>(this.width, this.height);
            }
        }
        catch
        {
            // The constructor throws, so no caller can dispose the planes already allocated.
            JxlPlanes.DisposeAll(this.planes);
            throw;
        }
    }

    /// <summary>Gets the number of channels.</summary>
    public int NumChannels => this.planes.Length;

    /// <summary>Gets the number of colour planes, which come before the extra channels.</summary>
    public int NumColorPlanes => this.numColorPlanes;

    /// <summary>
    /// Returns the plane of channel <paramref name="c"/>. A colour plane may be larger than the frame:
    /// a VarDCT group writes whole blocks into it, past the frame's right and bottom edges.
    /// </summary>
    /// <param name="c">The channel.</param>
    /// <returns>The plane.</returns>
    public Buffer2D<float> GetPlane(int c) => this.planes[c];

    /// <summary>
    /// Returns the rectangle of group <paramref name="groupId"/>, clipped to the frame, as libjxl's
    /// <c>MakeChannelRect</c>. libjxl takes a channel too, for channel shifts and upsampling; those
    /// arrive with later stages, so every channel is full size here.
    /// </summary>
    /// <param name="groupId">The group.</param>
    /// <returns>The rectangle.</returns>
    public JxlRect GetGroupRect(long groupId)
    {
        long gx = groupId % this.frameDim.XSizeGroups;
        long gy = groupId / this.frameDim.XSizeGroups;
        long groupDim = this.frameDim.GroupDimension;
        return new JxlRect(gx * groupDim, gy * groupDim, groupDim, groupDim, this.frameDim.XSizeUpsampled, this.frameDim.YSizeUpsampled);
    }

    /// <summary>
    /// Runs the stages over the whole frame, in libjxl's order: the loop filters, the patches, the save
    /// before the colour transform, the colour-transform stages, the blending, the save after blending,
    /// and the output. Returns the frame to keep in its reference slot, if it can be referenced; the
    /// caller saves it once the frame is done, as libjxl's <c>FinalizeFrame</c> does.
    /// </summary>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="loopFilters">
    /// The loop-filter stages (<see cref="JxlLoopFilterStages.Create"/>), in the order they run: Gaborish,
    /// then the EPF passes. Possibly none.
    /// </param>
    /// <param name="patches">The patches stage, or <see langword="null"/> if the frame has no patches.</param>
    /// <param name="colorStages">The colour-transform stages (<see cref="JxlColorStages.Create"/>), possibly none.</param>
    /// <param name="blending">The blending stage, or <see langword="null"/> if the frame is not blended.</param>
    /// <param name="sink">The sink for a displayed frame, or <see langword="null"/> if the frame is not displayed.</param>
    /// <param name="cancellationToken">The cancellation token, checked on each row of the loop filters and the patches.</param>
    /// <returns>The frame to save, or <see langword="null"/>.</returns>
    public JxlReferenceFrame Finish(JxlFrameHeader frameHeader, IJxlLoopFilterStage[] loopFilters, JxlPatchesStage patches, IJxlColorStage[] colorStages, JxlBlendingStage blending, IJxlRenderSink sink, CancellationToken cancellationToken)
    {
        bool canBeReferenced = frameHeader.CanBeReferenced;
        JxlReferenceFrame saved = null;
        try
        {
            // The loop-filter slot: libjxl adds Gaborish and the EPF passes after the chroma upsampling,
            // which comes in D10, and before the patches.
            this.RunLoopFilters(loopFilters, cancellationToken);

            // A VarDCT frame of a grey image that is not XYB has three colour planes, as every libjxl
            // frame. Everywhere else such an image has one (JxlImageMetadata.NumColorPlanes): its patches,
            // reference frames and blending. So the frame drops to grey before them, where libjxl keeps
            // three planes to the output, which takes the first. Every stage from here to the output works
            // on each colour channel alone, so the first plane comes out the same. EPF mixes the colour
            // channels, so the loop filters run on all three before the drop; a Modular frame of such an
            // image is given three planes for them (RunLoopFilters), and drops back to one here.
            JxlImageMetadata metadata = frameHeader.NonserializedMetadata.M;
            if (this.numColorPlanes == 3 && metadata.NumColorPlanes == 1)
            {
                this.DropToGrey();
            }

            if (patches != null)
            {
                // One row may take every patch of the frame, so cancellation is checked row by row.
                for (int y = 0; y < this.height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    patches.ProcessRow(this.planes, this.width, y);
                }
            }

            if (canBeReferenced && frameHeader.SaveBeforeColorTransform)
            {
                saved = this.CopyPlanes(isInXyb: true);
            }

            // SimpleRenderPipeline runs each stage over the whole frame before the next.
            foreach (IJxlColorStage stage in colorStages)
            {
                for (int y = 0; y < this.height; y++)
                {
                    stage.ProcessRow(this.planes, this.width, y);
                }
            }

            if (blending != null)
            {
                this.Blend(blending);
            }

            // The save after blending and the sink take the planes as they are, padding and all, with
            // the frame size.
            if (canBeReferenced && !frameHeader.SaveBeforeColorTransform)
            {
                // A frame that is not displayed hands its planes over rather than copying them.
                saved = sink == null ? this.TakePlanes() : this.CopyPlanes(isInXyb: false);
            }

            // The output of a grey image takes the first colour channel: an XYB grey image has three
            // equal colour planes until here, as libjxl's pipeline always has, and its sink gets one.
            if (sink != null && this.numColorPlanes == 3 && metadata.NumOutputColorPlanes == 1)
            {
                this.DropToGrey();
            }

            if (sink != null && sink.ConsumeFrame(this.planes, this.width, this.height))
            {
                this.planes = Array.Empty<Buffer2D<float>>();
            }

            return saved;
        }
        catch
        {
            saved?.Dispose();
            throw;
        }
    }

    public void Dispose() => this.DisposePlanes();

    // SimpleRenderPipeline::ProcessBuffers for stages with kInOut channels: each stage reads the planes,
    // with its border mirrored in at the frame edges, and writes a second set, which then takes their
    // place. The planes it replaced are the next stage's output, so two sets ping-pong.
    private void RunLoopFilters(IJxlLoopFilterStage[] loopFilters, CancellationToken cancellationToken)
    {
        if (loopFilters.Length == 0)
        {
            return;
        }

        // A grey image that is not XYB has one colour plane here. libjxl's Modular decoding copies the
        // grey channel into all three (rgb_from_gray in ModularFrameDecoder::ModularImageToDecodedRect),
        // and the loop filters treat them as three channels: Gaborish has weights for each, and EPF sums
        // its distances over all three, with a scale for each. So the plane is copied into three before
        // the filters, and Finish keeps the first afterwards, as libjxl's output does.
        if (this.numColorPlanes == 1)
        {
            this.ExpandGreyToThree();
        }

        Buffer2D<float>[] spare = new Buffer2D<float>[this.planes.Length];
        try
        {
            foreach (IJxlLoopFilterStage stage in loopFilters)
            {
                Buffer2D<float>[] output = new Buffer2D<float>[this.planes.Length];
                for (int c = 0; c < this.planes.Length; c++)
                {
                    if (stage.ChangesChannel(c))
                    {
                        // The same size as the plane it replaces. The stages write only the frame, so
                        // the padding past it holds whatever the plane held before; nothing reads it.
                        spare[c] ??= this.allocator.Allocate2D<float>(this.planes[c].Width, this.planes[c].Height, AllocationOptions.Clean);
                        output[c] = spare[c];
                    }
                }

                using (JxlNeighbourRows input = new(this.allocator, this.planes, stage, this.width, this.height))
                {
                    for (int y = 0; y < this.height; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        input.MoveTo(y);
                        stage.ProcessRow(input, output, this.width, y);
                    }
                }

                for (int c = 0; c < this.planes.Length; c++)
                {
                    if (output[c] != null)
                    {
                        spare[c] = this.planes[c];
                        this.planes[c] = output[c];
                    }
                }
            }
        }
        finally
        {
            JxlPlanes.DisposeAll(spare);
        }
    }

    // The blending stage, then SimpleRenderPipeline's switch to the image dimensions: the frame rows
    // are blended in place, the image-sized planes are filled from the background, and the part of
    // the frame inside the image is copied in at the frame origin.
    private void Blend(JxlBlendingStage blending)
    {
        for (int y = 0; y < this.height; y++)
        {
            blending.ProcessRow(this.planes, this.width, y);
        }

        int imageXSize = blending.ImageXSize;
        int imageYSize = blending.ImageYSize;
        Buffer2D<float>[] imagePlanes = new Buffer2D<float>[this.planes.Length];
        try
        {
            for (int c = 0; c < imagePlanes.Length; c++)
            {
                imagePlanes[c] = this.allocator.Allocate2D<float>(imageXSize, imageYSize);
            }

            for (int y = 0; y < imageYSize; y++)
            {
                blending.ProcessPaddingRow(imagePlanes, y);
            }

            // libjxl clips with unsigned sizes, so a frame wholly outside the image wraps them and
            // fails the copy. Its blending stage skips such rows instead ("TODO(eustas): or fail?"),
            // as does the pipeline djxl uses. Here the sizes are clipped at zero, so nothing is copied.
            long x0 = blending.FrameOriginX0;
            long y0 = blending.FrameOriginY0;
            long xsize = this.width;
            long ysize = this.height;
            long x0Fg = 0;
            long y0Fg = 0;
            if (x0 < 0)
            {
                xsize += x0;
                x0Fg -= x0;
                x0 = 0;
            }

            if (x0 + xsize > imageXSize)
            {
                xsize = imageXSize - x0;
            }

            if (y0 < 0)
            {
                ysize += y0;
                y0Fg -= y0;
                y0 = 0;
            }

            if (y0 + ysize > imageYSize)
            {
                ysize = imageYSize - y0;
            }

            if (xsize > 0 && ysize > 0)
            {
                for (int c = 0; c < imagePlanes.Length; c++)
                {
                    for (long y = 0; y < ysize; y++)
                    {
                        this.planes[c].DangerousGetRowSpan((int)(y0Fg + y)).Slice((int)x0Fg, (int)xsize)
                            .CopyTo(imagePlanes[c].DangerousGetRowSpan((int)(y0 + y)).Slice((int)x0, (int)xsize));
                    }
                }
            }
        }
        catch
        {
            JxlPlanes.DisposeAll(imagePlanes);
            throw;
        }

        this.DisposePlanes();
        this.planes = imagePlanes;
        this.width = imageXSize;
        this.height = imageYSize;
    }

    // Copies the grey plane into a second and a third colour plane, before the extra channels.
    private void ExpandGreyToThree()
    {
        Buffer2D<float> grey = this.planes[0];
        Buffer2D<float>[] rgb = new Buffer2D<float>[this.planes.Length + 2];
        try
        {
            for (int c = 1; c < 3; c++)
            {
                rgb[c] = this.allocator.Allocate2D<float>(grey.Width, grey.Height);
                for (int y = 0; y < grey.Height; y++)
                {
                    grey.DangerousGetRowSpan(y).CopyTo(rgb[c].DangerousGetRowSpan(y));
                }
            }
        }
        catch
        {
            JxlPlanes.DisposeAll(rgb);
            throw;
        }

        rgb[0] = grey;
        Array.Copy(this.planes, 1, rgb, 3, this.planes.Length - 1);
        this.planes = rgb;
        this.numColorPlanes = 3;
    }

    // Disposes the second and third colour planes, so the planes are grey, then the extra channels.
    private void DropToGrey()
    {
        Buffer2D<float>[] grey = new Buffer2D<float>[this.planes.Length - 2];
        grey[0] = this.planes[0];
        Array.Copy(this.planes, 3, grey, 1, this.planes.Length - 3);
        this.planes[1].Dispose();
        this.planes[2].Dispose();
        this.planes = grey;
        this.numColorPlanes = 1;
    }

    // WriteToImageBundleStage into frame_storage_for_referencing.
    private JxlReferenceFrame CopyPlanes(bool isInXyb)
    {
        Buffer2D<float>[] copy = new Buffer2D<float>[this.planes.Length];
        try
        {
            for (int c = 0; c < copy.Length; c++)
            {
                copy[c] = this.allocator.Allocate2D<float>(this.width, this.height);
                for (int y = 0; y < this.height; y++)
                {
                    this.planes[c].DangerousGetRowSpan(y).Slice(0, this.width).CopyTo(copy[c].DangerousGetRowSpan(y));
                }
            }
        }
        catch
        {
            JxlPlanes.DisposeAll(copy);
            throw;
        }

        return new JxlReferenceFrame(copy, this.width, this.height, isInXyb);
    }

    private JxlReferenceFrame TakePlanes()
    {
        JxlReferenceFrame frame = new(this.planes, this.width, this.height, isInXyb: false);
        this.planes = Array.Empty<Buffer2D<float>>();
        return frame;
    }

    private void DisposePlanes()
    {
        JxlPlanes.DisposeAll(this.planes);
        this.planes = Array.Empty<Buffer2D<float>>();
    }
}
