// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Structure after libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: the render_noise stages
// of PassesDecoderState::PreparePipeline in lib/jxl/dec_cache.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;

namespace SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;

/// <summary>
/// The two noise stages of a frame with the noise flag, which run after the upsampling and before the
/// save before the colour transform: the convolve-noise stage, then the add-noise stage.
/// </summary>
internal sealed class JxlNoiseStages
{
    /// <summary>Initializes a new instance of the <see cref="JxlNoiseStages"/> class.</summary>
    /// <param name="noiseParams">The noise parameters, from the frame's DC global section.</param>
    /// <param name="metadata">The image metadata.</param>
    /// <param name="cmap">
    /// The colour-correlation map of the frame: the decoded one of a VarDCT frame, and the default one of
    /// a Modular frame, as libjxl makes a new map for each frame.
    /// </param>
    public JxlNoiseStages(JxlNoiseParams noiseParams, JxlImageMetadata metadata, JxlColorCorrelation cmap)
    {
        // libjxl's num_c: the noise channels follow the colour channels and the extra channels.
        int firstC = 3 + metadata.NumExtraChannels;
        this.Convolve = new JxlConvolveNoiseStage(firstC);
        this.Add = new JxlAddNoiseStage(noiseParams, cmap.YtoXRatio(0), cmap.YtoBRatio(0), firstC);
    }

    /// <summary>Gets the convolve-noise stage.</summary>
    public IJxlNeighbourStage Convolve { get; }

    /// <summary>Gets the add-noise stage.</summary>
    public JxlAddNoiseStage Add { get; }
}
