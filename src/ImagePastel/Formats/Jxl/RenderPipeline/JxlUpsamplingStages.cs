// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Structure after libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: the chroma-upsampling and
// upsampling stages of PassesDecoderState::PreparePipeline in lib/jxl/dec_cache.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// The stages of a frame that upsample its channels, which libjxl's <c>PreparePipeline</c> adds around the
/// loop filters and the patches. They are the only stages with a shift, so the channel shifts of the
/// render pipeline (<see cref="JxlRenderPipeline.ChannelShifts"/>) come from them alone. Unlike the loop
/// filters, they depend on the frame header only, so they are built before the sections are decoded.
/// </summary>
/// <remarks>
/// The channels are numbered as libjxl's: three colour channels, then the extra channels.
/// </remarks>
internal sealed class JxlUpsamplingStages
{
    private JxlUpsamplingStages(IJxlNeighbourStage[] chroma, IJxlNeighbourStage[] beforePatches, IJxlNeighbourStage[] afterPatches)
    {
        this.Chroma = chroma;
        this.BeforePatches = beforePatches;
        this.AfterPatches = afterPatches;
    }

    /// <summary>
    /// Gets the chroma-upsampling stages, which run first, before the loop filters: for each subsampled
    /// colour channel, in the order 0, 1, 2, a horizontal stage and then a vertical one, each of which
    /// takes one of the channel's chroma shifts. The channels the frame codes at full size have none.
    /// </summary>
    public IJxlNeighbourStage[] Chroma { get; }

    /// <summary>
    /// Gets the early upsampling of the extra channels, which runs after the loop filters and before the
    /// patches: one stage for each upsampled extra channel, unless they are upsampled late.
    /// </summary>
    public IJxlNeighbourStage[] BeforePatches { get; }

    /// <summary>
    /// Gets the upsampling after the patches: one stage for each colour channel of an upsampled frame, and
    /// for each extra channel too when they are upsampled late.
    /// </summary>
    public IJxlNeighbourStage[] AfterPatches { get; }

    /// <summary>
    /// Builds the stages of the frame. Each upsampled channel gets one upsampling stage, which takes the
    /// shift its chroma-upsampling stages leave, so every channel leaves them at the upsampled frame
    /// size. When every extra channel has the frame's upsampling, the extra channels go through the
    /// patches at the coded size, as the colour does, and are upsampled with it after them ("late").
    /// Otherwise each upsampled extra channel has its own stage before the patches ("early"). A frame
    /// with patches takes that path only when its colour is not upsampled: the patches check in
    /// <see cref="JxlFrameDecoder.ProcessDcGlobal"/> rejects the rest.
    /// </summary>
    /// <param name="frameHeader">The frame header.</param>
    /// <returns>The stages.</returns>
    public static JxlUpsamplingStages Create(JxlFrameHeader frameHeader)
    {
        JxlYCbCrChromaSubsampling chroma = frameHeader.ChromaSubsampling;
        List<IJxlNeighbourStage> chromaStages = new();
        for (int c = 0; c < 3; c++)
        {
            if (chroma.HShift(c) != 0)
            {
                chromaStages.Add(new JxlChromaUpsamplingStage(c, horizontal: true));
            }

            if (chroma.VShift(c) != 0)
            {
                chromaStages.Add(new JxlChromaUpsamplingStage(c, horizontal: false));
            }
        }

        JxlCustomTransformData transformData = frameHeader.NonserializedMetadata.TransformData;
        List<uint> extraChannelUpsampling = frameHeader.ExtraChannelUpsampling;
        uint upsampling = frameHeader.Upsampling;
        bool lateExtraChannelUpsampling = upsampling != 1 && extraChannelUpsampling.TrueForAll(ecups => ecups == upsampling);

        List<IJxlNeighbourStage> before = new();
        if (!lateExtraChannelUpsampling)
        {
            for (int ec = 0; ec < extraChannelUpsampling.Count; ec++)
            {
                if (extraChannelUpsampling[ec] != 1)
                {
                    before.Add(new JxlUpsamplingStage(transformData, 3 + ec, JxlBits.CeilLog2Nonzero(extraChannelUpsampling[ec])));
                }
            }
        }

        List<IJxlNeighbourStage> after = new();
        if (upsampling != 1)
        {
            int numChannels = 3 + (lateExtraChannelUpsampling ? extraChannelUpsampling.Count : 0);
            for (int c = 0; c < numChannels; c++)
            {
                after.Add(new JxlUpsamplingStage(transformData, c, JxlBits.CeilLog2Nonzero(upsampling)));
            }
        }

        return new JxlUpsamplingStages(chromaStages.ToArray(), before.ToArray(), after.ToArray());
    }

    /// <summary>
    /// Returns the neighbour stages before the patches, in the order they run: the chroma upsampling,
    /// then the loop filters, then the early upsampling of the extra channels.
    /// </summary>
    /// <param name="loopFilters">The loop filters (<see cref="JxlLoopFilterStages.Create"/>), possibly none.</param>
    /// <returns>The stages.</returns>
    public IJxlNeighbourStage[] WithLoopFilters(IJxlNeighbourStage[] loopFilters)
    {
        IJxlNeighbourStage[] stages = new IJxlNeighbourStage[this.Chroma.Length + loopFilters.Length + this.BeforePatches.Length];
        this.Chroma.CopyTo(stages, 0);
        loopFilters.CopyTo(stages, this.Chroma.Length);
        this.BeforePatches.CopyTo(stages, this.Chroma.Length + loopFilters.Length);
        return stages;
    }
}
