// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Structure after libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/render_pipeline/simple_render_pipeline.h and simple_render_pipeline.cc (MakeChannelRect, the
// channel sizes of PrepareForThreadsInternal, and ProcessBuffers with its SwitchToImageDimensions step),
// the channel shifts of RenderPipeline::Builder::Finalize in lib/jxl/render_pipeline/render_pipeline.cc
// (summed over the upsampling stages only: see ChannelShifts),
// RenderPipelineInput from lib/jxl/render_pipeline/render_pipeline.h, the stage order of
// PassesDecoderState::PreparePipeline in lib/jxl/dec_cache.cc, and WriteToImage3FStage from
// lib/jxl/render_pipeline/stage_write.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl.Base;
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
/// <para>
/// Each channel has a shift, as in libjxl (<see cref="ChannelShifts"/>): log2 of how much the stages
/// still to run will upsample it. At shift <c>s</c> a channel is the upsampled frame size divided by
/// <c>2^s</c>, rounded up. A neighbour stage (<see cref="IJxlNeighbourStage"/>) with a shift doubles the
/// planes it changes and lowers their shift; once the neighbour stages are done, every channel is at the
/// upsampled frame size. So the loop filters and the patches run at the coded size, before the
/// upsampling, and the saves, the colour transform, the blending and the sink at the upsampled size.
/// </para>
/// <para>
/// Deliberate departures from libjxl: the planes carry no border. Instead of the border, which in libjxl
/// takes the whole 8x8 blocks that VarDCT writes past the right and bottom edges, the colour planes are
/// allocated at the frame's coded padded size. The stages, the reference slots and the sink are given
/// each channel's size, and the planes keep the padding past it. Only the neighbour stages read
/// neighbouring pixels: they get them through <see cref="JxlNeighbourRows"/>, which mirrors the pixels
/// past the channel's edges in from inside it, as libjxl fills its border, so the padding is never read.
/// Stages run in place, or ping-pong between two sets of planes where they read neighbours, rather than
/// allocate new planes each, as decided in issue #113. And the stages are fixed steps of
/// <see cref="Finish"/> rather than a list built per frame, because the frames this port decodes need
/// only the neighbour, patches, noise, DC-slot write, save, colour-transform, blending and output stages.
/// </para>
/// </remarks>
internal sealed class JxlRenderPipeline : IDisposable
{
    private const int NumNoisePlanes = 3;

    private readonly MemoryAllocator allocator;
    private readonly JxlFrameDimensions frameDim;

    // The shift of each channel at the start of the pipeline, by its index then: libjxl's
    // channel_shifts_[0], from which the group rectangles are made.
    private readonly JxlChannelShift[] inputShifts;
    private int numColorPlanes;
    private Buffer2D<float>[] planes;

    // The shift each plane has left: libjxl's channel_shifts_ at the next stage.
    private JxlChannelShift[] shifts;
    private int width;
    private int height;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlRenderPipeline"/> class, allocating one plane per
    /// channel: the colour planes at the frame's coded padded size, which is the coded size for a Modular
    /// frame and whole 8x8 blocks for a VarDCT frame, and each extra channel at its size at its shift.
    /// </summary>
    /// <param name="allocator">The allocator.</param>
    /// <param name="frameDim">The frame dimensions.</param>
    /// <param name="numColorPlanes">
    /// The number of colour planes: <see cref="Headers.JxlImageMetadata.NumColorPlanes"/>, or three for a
    /// VarDCT frame, which always codes three.
    /// </param>
    /// <param name="channelShifts">
    /// The shift of each channel (<see cref="ChannelShifts"/>): the colour planes, then the extra channels.
    /// </param>
    /// <param name="withNoisePlanes">
    /// Whether the frame has noise: then three noise planes follow the extra channels, at the upsampled
    /// frame size (libjxl's three temporary channels, which no stage upsamples). See
    /// <see cref="NoisePlaneStart"/> and <see cref="NoiseChannel"/>.
    /// </param>
    public JxlRenderPipeline(MemoryAllocator allocator, JxlFrameDimensions frameDim, int numColorPlanes, JxlChannelShift[] channelShifts, bool withNoisePlanes = false)
    {
        this.allocator = allocator;
        this.frameDim = frameDim;
        this.numColorPlanes = numColorPlanes;
        if (withNoisePlanes)
        {
            this.NoisePlaneStart = channelShifts.Length;
            this.NoiseChannel = 3 + (channelShifts.Length - numColorPlanes);
            Array.Resize(ref channelShifts, channelShifts.Length + NumNoisePlanes);
        }

        this.inputShifts = (JxlChannelShift[])channelShifts.Clone();
        this.shifts = (JxlChannelShift[])channelShifts.Clone();
        this.width = (int)frameDim.XSizeUpsampled;
        this.height = (int)frameDim.YSizeUpsampled;
        this.planes = new Buffer2D<float>[channelShifts.Length];
        try
        {
            for (int c = 0; c < this.planes.Length; c++)
            {
                // A colour channel's shift is at least log2 of the upsampling, so the channel fits the
                // coded size. VarDCT writes whole blocks there, and a subsampled channel in the top left.
                this.planes[c] = c < numColorPlanes
                    ? allocator.Allocate2D<float>((int)frameDim.XSizePadded, (int)frameDim.YSizePadded)
                    : allocator.Allocate2D<float>(this.ChannelXSize(c), this.ChannelYSize(c));
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

    /// <summary>
    /// Gets the index of the first of the three noise planes at the start of the pipeline, for
    /// <see cref="GetPlane"/> and <see cref="GetGroupRect"/>, or -1 if the frame has no noise.
    /// </summary>
    public int NoisePlaneStart { get; } = -1;

    /// <summary>
    /// Gets the first of the three noise channels as the noise stages number them, libjxl's <c>num_c</c>:
    /// three colour channels, then the extra channels. It differs from <see cref="NoisePlaneStart"/> for a
    /// grey image that is not XYB, which may start with one colour plane but has three by the noise stages
    /// (<see cref="Finish"/>). -1 if the frame has no noise.
    /// </summary>
    public int NoiseChannel { get; } = -1;

    /// <summary>Gets the number of colour planes, which come before the extra channels.</summary>
    public int NumColorPlanes => this.numColorPlanes;

    /// <summary>
    /// Returns the shift of each channel at the start of the pipeline, as libjxl's
    /// <c>channel_shifts_[0]</c> in <c>RenderPipeline::Builder::Finalize</c>: the sum of the shifts of the
    /// stages that change the channel. For a colour channel that is its chroma shift (the
    /// chroma-upsampling stages) plus log2 of the upsampling (the upsampling stage). For an extra channel
    /// it is log2 of its own upsampling, whether its upsampling stage runs before the patches or with the
    /// colour channels after them.
    /// </summary>
    /// <remarks>
    /// A departure from libjxl: <c>Finalize</c> sums over every stage of the pipeline, once it has them all.
    /// Here the pipeline is built before the sections are decoded, so before its loop filters, which take
    /// what the sections decode (<see cref="JxlLoopFilterStages.Create"/>). Only the stages that upsample
    /// have a shift, and they depend on the frame header alone, so they are built first
    /// (<see cref="JxlUpsamplingStages.Create"/>) and the shifts are summed over them.
    /// </remarks>
    /// <param name="numColorPlanes">The number of colour planes the pipeline is given.</param>
    /// <param name="numExtraChannels">The number of extra channels.</param>
    /// <param name="stages">The stages with a shift, in any order.</param>
    /// <returns>The shifts: the colour planes, then the extra channels.</returns>
    public static JxlChannelShift[] ChannelShifts(int numColorPlanes, int numExtraChannels, params IJxlNeighbourStage[][] stages)
    {
        JxlChannelShift[] shifts = new JxlChannelShift[numColorPlanes + numExtraChannels];
        for (int c = 0; c < shifts.Length; c++)
        {
            // The stages number the channels as libjxl: three colour channels, then the extra channels. A
            // grey image's one colour plane is their channel 0 (RunNeighbourStages).
            int stageChannel = c < numColorPlanes ? c : 3 + (c - numColorPlanes);
            int x = 0;
            int y = 0;
            foreach (IJxlNeighbourStage[] list in stages)
            {
                foreach (IJxlNeighbourStage stage in list)
                {
                    if (stage.ChangesChannel(stageChannel))
                    {
                        x += stage.ShiftX;
                        y += stage.ShiftY;
                    }
                }
            }

            shifts[c] = new JxlChannelShift(x, y);
        }

        return shifts;
    }

    /// <summary>
    /// Returns the plane of channel <paramref name="c"/>. A plane may be larger than its channel: a
    /// VarDCT group writes whole blocks into a colour plane, past the frame's right and bottom edges.
    /// </summary>
    /// <param name="c">The channel.</param>
    /// <returns>The plane.</returns>
    public Buffer2D<float> GetPlane(int c) => this.planes[c];

    /// <summary>
    /// Returns the rectangle of group <paramref name="groupId"/> in channel <paramref name="c"/>, clipped
    /// to the channel, as libjxl's <c>MakeChannelRect</c>: the group size is scaled up by the upsampling
    /// and down by the channel's shift.
    /// </summary>
    /// <param name="groupId">The group.</param>
    /// <param name="c">The channel: the colour planes, then the extra channels.</param>
    /// <returns>The rectangle.</returns>
    public JxlRect GetGroupRect(long groupId, int c)
    {
        int baseColorShift = JxlBits.CeilLog2Nonzero((ulong)(this.frameDim.XSizeUpsampledPadded / this.frameDim.XSizePadded));
        JxlChannelShift shift = this.inputShifts[c];
        long gx = groupId % this.frameDim.XSizeGroups;
        long gy = groupId / this.frameDim.XSizeGroups;
        long xgroupdim = ((long)this.frameDim.GroupDimension << baseColorShift) >> shift.X;
        long ygroupdim = ((long)this.frameDim.GroupDimension << baseColorShift) >> shift.Y;
        return new JxlRect(
            gx * xgroupdim,
            gy * ygroupdim,
            xgroupdim,
            ygroupdim,
            JxlBits.DivCeil(this.frameDim.XSizeUpsampled, 1L << shift.X),
            JxlBits.DivCeil(this.frameDim.YSizeUpsampled, 1L << shift.Y));
    }

    /// <summary>
    /// Runs the stages over the whole frame, in libjxl's order: the neighbour stages before the patches
    /// (the chroma upsampling, the loop filters, then the early upsampling of the extra channels), the
    /// patches, the splines, the neighbour stages after them (the upsampling), the noise, the copy of a DC
    /// frame for its DC slot, the save before the colour transform, the colour-transform stages, the
    /// blending, the save after blending, and the output. Returns the frame to keep: a DC frame's copy for
    /// its DC slot, or the frame for its reference slot, if it can be referenced. The caller saves it once
    /// the frame is done, as libjxl's <c>FinalizeFrame</c> does.
    /// </summary>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="beforePatches">
    /// The neighbour stages before the patches, in the order they run: the chroma-upsampling stages
    /// (<see cref="JxlChromaUpsamplingStage"/>), the loop filters (<see cref="JxlLoopFilterStages.Create"/>),
    /// Gaborish then the EPF passes, and the early upsampling of the extra channels. Possibly none.
    /// </param>
    /// <param name="patches">The patches stage, or <see langword="null"/> if the frame has no patches.</param>
    /// <param name="splines">The splines stage, or <see langword="null"/> if the frame has no splines.</param>
    /// <param name="afterPatches">The neighbour stages after the patches, in the order they run. Possibly none.</param>
    /// <param name="noise">
    /// The noise stages, or <see langword="null"/> if the frame has no noise. The pipeline must have been
    /// built with its noise planes, and the stages with <see cref="NoiseChannel"/>.
    /// </param>
    /// <param name="colorStages">The colour-transform stages (<see cref="JxlColorStages.Create"/>), possibly none.</param>
    /// <param name="blending">The blending stage, or <see langword="null"/> if the frame is not blended.</param>
    /// <param name="sink">The sink for a displayed frame, or <see langword="null"/> if the frame is not displayed.</param>
    /// <param name="cancellationToken">The cancellation token, checked on each row of the neighbour stages, the patches and the splines.</param>
    /// <returns>
    /// The frame to save, or <see langword="null"/>: for a DC frame (<see cref="JxlFrameHeader.DcLevel"/> not
    /// 0), its X, Y and B planes for DC slot <c>DcLevel - 1</c>; otherwise the frame for its reference slot.
    /// </returns>
    public JxlReferenceFrame Finish(JxlFrameHeader frameHeader, IJxlNeighbourStage[] beforePatches, JxlPatchesStage patches, JxlSplinesStage splines, IJxlNeighbourStage[] afterPatches, JxlNoiseStages noise, IJxlColorStage[] colorStages, JxlBlendingStage blending, IJxlRenderSink sink, CancellationToken cancellationToken)
    {
        bool canBeReferenced = frameHeader.CanBeReferenced;
        JxlReferenceFrame saved = null;
        try
        {
            this.RunNeighbourStages(beforePatches, cancellationToken);

            // A VarDCT frame of a grey image that is not XYB has three colour planes, as every libjxl
            // frame. Where nothing mixes or keeps the colour planes, such an image has one
            // (JxlImageMetadata.NumColorPlanes): its blending, its slots saved after blending and its
            // output. libjxl keeps three planes to the output, which takes the first.
            // EPF mixes the colour channels, so the loop filters run on all three; a Modular frame of such
            // an image is given three for them (RunNeighbourStages), copies of its grey plane, as libjxl's
            // rgb_from_gray makes them.
            // The add-noise stage mixes the colour channels too: it takes the noise strength from the
            // first two. So a frame with noise keeps three planes up to it, and a Modular frame is given
            // three here if it has none yet. The patches blend each of them with the slot's plane of the
            // same channel (JxlPatchesStage), and the splines add their X, Y and B colours to them, as in
            // libjxl.
            // A DC frame keeps three planes up to its DC-slot copy for the same reason: the slot holds
            // libjxl's three colour channels, which the splines and the noise make differ.
            // A frame saved before its colour transform keeps three planes up to that save, so its slot
            // holds libjxl's three colour channels (D14 review). The patches of a later frame read such a
            // slot channel by channel, and a later YCbCr frame mixes what they read. The three differ
            // wherever the VarDCT decode, the patches from another such slot, the splines or the noise
            // made them differ. A Modular frame is given three copies of its grey plane, as rgb_from_gray.
            // A YCbCr frame mixes the colour channels in its colour transform, so it keeps three planes
            // through it and drops to its first plane, R, after it (JxlFrameHeader.DropsToGreyAfterColorTransform).
            // So its patches blend all three, and a save before the colour transform keeps Cb, Y and Cr in
            // the slot, as libjxl does.
            // Every other frame of such an image drops to grey once nothing mixes its colour planes or keeps
            // them: before the patches, or after the noise or the save before the colour transform. The
            // patches, the splines and the upsampling work on each colour channel alone, so the first plane
            // comes out the same.
            // A departure from libjxl: a slot saved after blending holds the one grey plane, where libjxl
            // keeps three. Only blending reads such a slot (patches reject it), and blending, the save after
            // it and the output work on each colour channel alone, so only the first channel is ever read.
            // The patches and the extra-channel blending count a slot's colour planes from its own planes.
            JxlImageMetadata metadata = frameHeader.NonserializedMetadata.M;
            bool dropsToGreyAfterColorTransform = frameHeader.DropsToGreyAfterColorTransform;
            bool isDcFrame = frameHeader.DcLevel != 0;
            bool savesBeforeColorTransform = canBeReferenced && frameHeader.SaveBeforeColorTransform;
            if (noise != null || isDcFrame || savesBeforeColorTransform)
            {
                if (this.numColorPlanes == 1)
                {
                    this.ExpandGreyToThree();
                }
            }
            else if (!dropsToGreyAfterColorTransform)
            {
                this.DropToGreyIfGreyImage(metadata);
            }

            if (patches != null)
            {
                // The patches run at the coded size, where every channel is now: the patches check in
                // JxlFrameDecoder.ProcessDcGlobal keeps the extra channels at the colour channels' size.
                int xsize = this.ChannelXSize(0);
                int ysize = this.ChannelYSize(0);

                // One row may take every patch of the frame, so cancellation is checked row by row.
                for (int y = 0; y < ysize; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    patches.ProcessRow(this.planes, this.numColorPlanes, xsize, y);
                }
            }

            if (splines != null)
            {
                // The splines run at the coded size, on the colour channels only.
                int xsize = this.ChannelXSize(0);
                int ysize = this.ChannelYSize(0);
                for (int y = 0; y < ysize; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    splines.ProcessRow(this.planes, this.numColorPlanes, xsize, y);
                }
            }

            this.RunNeighbourStages(afterPatches, cancellationToken);
            if (noise != null)
            {
                this.AddNoise(noise, cancellationToken);
            }

            // WriteToImage3FStage: a DC frame copies its three colour planes, before the colour transform,
            // for its DC slot, where a later frame takes them as its DC. A DC frame cannot be referenced or
            // blended, and it is not displayed, so the stages after this one change nothing that is kept.
            if (isDcFrame)
            {
                DebugGuard.IsTrue(!canBeReferenced, "A DC frame cannot be referenced");
                DebugGuard.IsTrue(this.numColorPlanes == 3, "A DC frame keeps three colour planes up to its DC-slot copy");
                saved = this.CopyPlanes(3, isInXyb: true);
            }

            foreach (JxlChannelShift shift in this.shifts)
            {
                DebugGuard.IsTrue(shift.X == 0 && shift.Y == 0, "The neighbour stages upsample every channel to the frame size");
            }

            if (savesBeforeColorTransform)
            {
                saved = this.CopyPlanes(this.planes.Length, isInXyb: true);
            }

            // A grey image that is not XYB has no colour transform here but YCbCr, so dropping after the
            // save is dropping before the colour stages.
            if (!dropsToGreyAfterColorTransform)
            {
                this.DropToGreyIfGreyImage(metadata);
            }

            // SimpleRenderPipeline runs each stage over the whole frame before the next.
            foreach (IJxlColorStage stage in colorStages)
            {
                for (int y = 0; y < this.height; y++)
                {
                    stage.ProcessRow(this.planes, this.width, y);
                }
            }

            if (dropsToGreyAfterColorTransform)
            {
                this.DropToGreyIfGreyImage(metadata);
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
                saved = sink == null ? this.TakePlanes() : this.CopyPlanes(this.planes.Length, isInXyb: false);
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

    /// <summary>
    /// Hands over the planes once <see cref="Finish"/> has run without a sink, as libjxl's write stage into an
    /// image bundle that the encoder's reconstruction of its frame reads (<see cref="VarDct.JxlEncReconstruct"/>).
    /// Every colour plane is kept, even for a grey image, which only the output to a sink drops to one. The
    /// planes keep the padding past the frame size.
    /// </summary>
    /// <returns>The planes: the colour planes, then the extra channels. The caller disposes them.</returns>
    public Buffer2D<float>[] TakeFinishedPlanes()
    {
        Buffer2D<float>[] planes = this.planes;
        this.planes = Array.Empty<Buffer2D<float>>();
        return planes;
    }

    public void Dispose() => this.DisposePlanes();

    // SimpleRenderPipeline::ProcessBuffers for stages with kInOut channels: each stage reads the planes,
    // with its border mirrored in at the edges of each channel's size, and writes a second set, which
    // then takes their place. The planes it replaced are the next stage's output, so two sets ping-pong.
    // A stage with a shift writes planes 2^shift times the size of its input, and the channels it
    // changes then shrink to their size at the shift they have left.
    private void RunNeighbourStages(IJxlNeighbourStage[] stages, CancellationToken cancellationToken)
    {
        if (stages.Length == 0)
        {
            return;
        }

        // A grey image that is not XYB may have one colour plane here. libjxl's Modular decoding copies the
        // grey channel into all three (rgb_from_gray in ModularFrameDecoder::ModularImageToDecodedRect),
        // and its stages see three colour channels: Gaborish has weights for each, and EPF sums its
        // distances over all three, with a scale for each. So the plane is copied into three before the
        // stages, which number the channels as libjxl does, and Finish keeps the first afterwards, as
        // libjxl's output does.
        if (this.numColorPlanes == 1)
        {
            this.ExpandGreyToThree();
        }

        Buffer2D<float>[] spare = new Buffer2D<float>[this.planes.Length];
        try
        {
            foreach (IJxlNeighbourStage stage in stages)
            {
                // libjxl runs a stage over the largest of its channels. Every stage here changes channels
                // of one size, at which JxlNeighbourRows mirrors them.
                int xsize = -1;
                int ysize = -1;
                for (int c = 0; c < this.planes.Length; c++)
                {
                    if (stage.ChangesChannel(c))
                    {
                        DebugGuard.IsTrue(xsize < 0 || (xsize == this.ChannelXSize(c) && ysize == this.ChannelYSize(c)), "The channels a stage changes have one size");
                        xsize = this.ChannelXSize(c);
                        ysize = this.ChannelYSize(c);
                    }
                }

                if (xsize < 0)
                {
                    continue;
                }

                int shiftX = stage.ShiftX;
                int shiftY = stage.ShiftY;
                Buffer2D<float>[] output = new Buffer2D<float>[this.planes.Length];
                for (int c = 0; c < this.planes.Length; c++)
                {
                    if (stage.ChangesChannel(c))
                    {
                        // A stage with no shift writes planes the same size as those it replaces. It
                        // writes only the channel, so after such stages the padding past it is zero after
                        // an odd number of stages (the first spare is allocated clean) and the decoded
                        // padding after an even number. Nothing reads it: every later stage, save and sink
                        // takes the channel's size, and a neighbour stage mirrors the channel at its edges,
                        // as JxlNeighbourRows and libjxl do. A stage with a shift writes planes of its
                        // input size times 2^shift, as libjxl allocates them.
                        int outputWidth = shiftX == 0 && shiftY == 0 ? this.planes[c].Width : xsize << shiftX;
                        int outputHeight = shiftX == 0 && shiftY == 0 ? this.planes[c].Height : ysize << shiftY;
                        if (spare[c] != null && (spare[c].Width != outputWidth || spare[c].Height != outputHeight))
                        {
                            spare[c].Dispose();
                            spare[c] = null;
                        }

                        spare[c] ??= this.allocator.Allocate2D<float>(outputWidth, outputHeight, AllocationOptions.Clean);
                        output[c] = spare[c];
                    }
                }

                using (JxlNeighbourRows input = new(this.allocator, this.planes, stage, xsize, ysize))
                {
                    for (int y = 0; y < ysize; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        input.MoveTo(y);
                        stage.ProcessRow(input, output, xsize, y);
                    }
                }

                // Move the new planes in, and shrink their channels to their size at the next stage.
                for (int c = 0; c < this.planes.Length; c++)
                {
                    if (output[c] != null)
                    {
                        spare[c] = this.planes[c];
                        this.planes[c] = output[c];
                        JxlChannelShift shift = this.shifts[c];
                        DebugGuard.IsTrue(shift.X >= shiftX && shift.Y >= shiftY, "A stage upsamples a channel no further than its shift");
                        this.shifts[c] = new JxlChannelShift(shift.X - shiftX, shift.Y - shiftY);
                    }
                }
            }
        }
        finally
        {
            JxlPlanes.DisposeAll(spare);
        }
    }

    // The convolve-noise stage, then the add-noise stage, which libjxl's PreparePipeline adds after the
    // upsampling. A frame with noise has three colour planes here (Finish), so the planes are numbered as
    // the stages number the channels. No later stage reads the noise planes (libjxl's kIgnored), so they
    // are disposed of after it, and the planes are the colour planes and the extra channels again.
    private void AddNoise(JxlNoiseStages noise, CancellationToken cancellationToken)
    {
        DebugGuard.IsTrue(this.numColorPlanes == 3 && this.planes.Length == this.NoiseChannel + NumNoisePlanes, "The noise planes follow three colour planes and the extra channels");
        this.RunNeighbourStages(new[] { noise.Convolve }, cancellationToken);
        for (int y = 0; y < this.height; y++)
        {
            noise.Add.ProcessRow(this.planes, this.width, y);
        }

        int numChannels = this.planes.Length - NumNoisePlanes;
        for (int c = numChannels; c < this.planes.Length; c++)
        {
            this.planes[c].Dispose();
        }

        Array.Resize(ref this.planes, numChannels);
        Array.Resize(ref this.shifts, numChannels);
    }

    // The size of channel c at the shift it has left: libjxl's DivCeil(xsize_upsampled, 1 << shift).
    private int ChannelXSize(int c) => (int)JxlBits.DivCeil(this.frameDim.XSizeUpsampled, 1L << this.shifts[c].X);

    private int ChannelYSize(int c) => (int)JxlBits.DivCeil(this.frameDim.YSizeUpsampled, 1L << this.shifts[c].Y);

    // The first drop to grey of a grey image that is not XYB: see Finish.
    private void DropToGreyIfGreyImage(JxlImageMetadata metadata)
    {
        if (this.numColorPlanes == 3 && metadata.NumColorPlanes == 1)
        {
            this.DropToGrey();
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

        JxlChannelShift[] shifts = new JxlChannelShift[rgb.Length];
        shifts[0] = shifts[1] = shifts[2] = this.shifts[0];
        Array.Copy(this.shifts, 1, shifts, 3, this.shifts.Length - 1);
        this.shifts = shifts;
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

        JxlChannelShift[] shifts = new JxlChannelShift[grey.Length];
        shifts[0] = this.shifts[0];
        Array.Copy(this.shifts, 3, shifts, 1, this.shifts.Length - 3);
        this.shifts = shifts;
    }

    // The first numPlanes planes at the frame size: every plane for WriteToImageBundleStage into
    // frame_storage_for_referencing, and the three colour planes for WriteToImage3FStage into a DC slot.
    private JxlReferenceFrame CopyPlanes(int numPlanes, bool isInXyb)
    {
        Buffer2D<float>[] copy = new Buffer2D<float>[numPlanes];
        try
        {
            for (int c = 0; c < copy.Length; c++)
            {
                copy[c] = this.allocator.Allocate2D<float>(this.width, this.height);
                JxlPlanes.CopyRows(this.planes[c], copy[c], this.width, this.height);
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
