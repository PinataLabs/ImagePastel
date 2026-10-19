// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_modular.h and lib/jxl/enc_modular.cc (do_transform, maybe_do_transform, try_palettes, and
// ModularFrameEncoder: Init, ComputeEncodingData, PrepareStreamParams, ComputeTree, ComputeTokens,
// EncodeGlobalInfo, EncodeStream).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Modular;
using SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <summary>
/// Encodes the Modular streams of a frame: the global stream and one per LF group and per group,
/// with one global MA tree and one set of histograms that every stream shares. The lossless paths of
/// libjxl's efforts 3 and 5 are ported: the predefined <c>kWPFixedDC</c> tree with the Weighted
/// predictor at effort 3, and a learned tree with the Gradient and Weighted predictors at effort 5.
/// Squeeze, quantisation, local palettes and the per-group RCT search are not ported.
/// It is the mirror of <see cref="JxlModularFrameDecoder"/>.
/// </summary>
internal sealed class JxlModularFrameEncoder : IDisposable
{
    // The level-10 limit. libjxl uses 16 for level 5, but cjxl leaves the level unset (-1) when it
    // queues the frame, so the frame encoder always sees this.
    private const int LevelMaxBitdepth = 32;

    // libjxl's CompressParams defaults (enc_params.h): channel_colors_pre_transform_percent, palette_colors
    // and lossy_palette. Lossless keeps them.
    private const float ChannelColorsPreTransformPercent = 95f;
    private const int PaletteColors = 1 << 10;
    private const bool LossyPalette = false;

    // libjxl's do_color: this encoder always codes the colour channels in the Modular image.
    private const bool DoColor = true;

    // libjxl's CompressParams default decoding_speed_tier, which the encoder has no option for.
    private const int DecodingSpeedTier = 0;

    // Properties in order of their likelihood of being useful, for the non-Squeeze case.
    private static readonly uint[] PropOrder = { 0, 1, 15, 9, 10, 11, 12, 13, 14, 2, 3, 4, 5, 6, 7, 8 };

    private readonly MemoryAllocator allocator;
    private readonly JxlFrameDimensions frameDim;
    private readonly JxlSpeedTier speedTier;
    private readonly JxlModularImage[] streamImages;
    private readonly JxlModularOptions[] streamOptions;
    private readonly JxlHistogramParams histogramParams;
    private readonly List<StreamParams> streamParams = new();
    private readonly List<List<JxlToken>> treeTokens = new();
    private readonly JxlEntropyEncodingData code = new();
    private List<JxlPropertyDecisionNode> tree;
    private JxlGroupHeader[] streamHeaders;
    private List<JxlToken>[] tokens;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlModularFrameEncoder"/> class, as libjxl's
    /// <c>ModularFrameEncoder::Init</c> does for a lossless frame: no responsive mode, the tree-learning
    /// parameters and predictor of the speed tier, the <c>kWPFixedDC</c> tree at effort 3, and the
    /// Modular histogram parameters.
    /// </summary>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="speedTier">The speed tier.</param>
    /// <param name="allocator">The allocator.</param>
    public JxlModularFrameEncoder(JxlFrameHeader frameHeader, JxlSpeedTier speedTier, MemoryAllocator allocator)
    {
        this.allocator = allocator;
        this.speedTier = speedTier;
        this.frameDim = frameHeader.ToFrameDimensions();

        long numStreams = JxlModularStreamId.ModularAc(this.frameDim, 0, frameHeader.Passes.NumPasses);
        this.streamImages = new JxlModularImage[numStreams];
        for (long i = 0; i < numStreams; ++i)
        {
            this.streamImages[i] = JxlModularImage.Create(allocator, 0, 0, 8, 0);
        }

        // libjxl's lossless decoding_speed_tier 0 only sets fast_decode_multiplier; responsive mode
        // stays off for lossless.
        JxlModularOptions options = new()
        {
            FastDecodeMultiplier = 1.001f,
            SplittingHeuristicsNodeThreshold = 75 + (14 * (int)speedTier) + (10 * DecodingSpeedTier),
        };
        SetTreeLearningParameters(options, speedTier, numStreams, frameHeader.NonserializedMetadata.M);
        options.NbRepeats = Math.Min(1.0f, options.NbRepeats);
        options.Predictor = DefaultPredictor(speedTier);
        options.MaxChanSize = this.frameDim.GroupDimension;
        options.GroupDim = this.frameDim.GroupDimension;

        // TODO(veluca): figure out how to use different predictor sets per channel.
        this.streamOptions = new JxlModularOptions[numStreams];
        Array.Fill(this.streamOptions, options);
        this.streamOptions[0] = options.Clone();
        if (speedTier == JxlSpeedTier.Falcon)
        {
            this.streamOptions[0].Kind = JxlModularOptions.TreeKind.WPFixedDC;
        }

        this.histogramParams = JxlHistogramParams.ForModular(this.speedTier, modularMode: true, options.Predictor);
    }

    /// <summary>
    /// Fills the Modular image of the global stream from <paramref name="image"/>, then the images of
    /// the streams that code parts of it.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="metadata">The image metadata.</param>
    /// <param name="image">The image.</param>
    public void ComputeEncodingData<TPixel>(JxlFrameHeader frameHeader, JxlImageMetadata metadata, Image<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Convert ImageBundle to modular Image object
        int xsize = (int)this.frameDim.XSize;
        int ysize = (int)this.frameDim.YSize;

        int nbChans = 3;
        if (metadata.ColorEncoding.IsGray && frameHeader.ColorTransform == JxlColorTransform.None)
        {
            nbChans = 1;
        }

        nbChans += metadata.NumExtraChannels;

        bool fp = metadata.BitDepth.FloatingPointSample;

        // bits_per_sample is just metadata for XYB-encoded images.
        if (metadata.BitDepth.BitsPerSample >= 32)
        {
            if (metadata.BitDepth.BitsPerSample == 32 && !fp)
            {
                JxlThrowHelper.ThrowImageFormatException("uint32_t not supported in enc_modular");
            }
            else if (metadata.BitDepth.BitsPerSample > 32)
            {
                JxlThrowHelper.ThrowImageFormatException("bits_per_sample > 32 not supported");
            }
        }

        // in the non-float case, there is an implicit 0 sign bit
        int maxBitdepth = (int)metadata.BitDepth.BitsPerSample + (fp ? 0 : 1);
        JxlModularImage gi = JxlModularImage.Create(this.allocator, xsize, ysize, (int)metadata.BitDepth.BitsPerSample, nbChans);
        this.streamImages[0].Dispose();
        this.streamImages[0] = gi;

        JxlEncoderInput<TPixel>.CopyColorChannels(image, metadata.BitDepth, gi);

        // The extra channels follow the colour channels. A pixel type has at most one, its alpha, and
        // the frame header keeps every extra channel at the frame's resolution.
        for (int ec = 0, c = nbChans - metadata.NumExtraChannels; ec < metadata.NumExtraChannels; ec++, c++)
        {
            JxlExtraChannelInfo eci = metadata.ExtraChannelInfo[ec];
            bool ecFp = eci.BitDepth.FloatingPointSample;
            maxBitdepth = Math.Max(maxBitdepth, (int)eci.BitDepth.BitsPerSample + (ecFp ? 0 : 1));
            JxlEncoderInput<TPixel>.CopyAlphaChannel(image, eci, gi, c);
        }

        if (maxBitdepth > LevelMaxBitdepth)
        {
            JxlThrowHelper.ThrowImageFormatException($"Bitdepth too high for level {10} (need {maxBitdepth} bits, have only {LevelMaxBitdepth} in this codestream level)");
        }

        // libjxl's maxval only sets the quantisation of lossy channels.
        int maxval = gi.BitDepth < 32 ? (int)((1u << gi.BitDepth) - 1) : 0;

        // Set options and apply transformations. Lossless keeps libjxl's palette settings.
        // Global palette transforms
        float channelColorsPercent = 0;
        if (!LossyPalette && (this.speedTier <= JxlSpeedTier.Thunder || (DoColor && metadata.BitDepth.BitsPerSample > 8)))
        {
            channelColorsPercent = ChannelColorsPreTransformPercent;
        }

        // Global palette causes bad progressive loading due to interpolation but lossy palette is
        // still required for JXL art. Lossless Fastest is neither streamed (groupwise) nor responsive.
        this.TryPalettes(gi, ref maxBitdepth, ref maxval, channelColorsPercent);

        // don't do an RCT if we're short on bits
        if (DoColor && gi.Channel.Count - gi.NbMetaChannels >= 3 && maxBitdepth + 1 < LevelMaxBitdepth)
        {
            // libjxl's colorspace is -1 (choose) unless the caller forces an RCT, which this encoder
            // has no option for. Lossless above Hare gets the fixed YCoCg.
            if (this.speedTier > JxlSpeedTier.Hare)
            {
                JxlTransform ycocg = new() { Id = JxlTransformId.Rct, RctType = 6, BeginC = (uint)gi.NbMetaChannels };
                DoTransform(gi, ycocg);
                maxBitdepth++;
            }
        }

        // libjxl's move-to-front and squeeze steps are not taken by lossless Fastest. When no spare
        // bit is left, libjxl turns off the per-group RCT search, which only higher efforts run.
        if (maxBitdepth > LevelMaxBitdepth)
        {
            JxlThrowHelper.ThrowImageFormatException($"Bitdepth {maxBitdepth} too high after the transforms");
        }

        this.FillStreamParams();
    }

    /// <summary>
    /// Builds the global tree and turns it into tokens: below effort 3 a tree learned from every
    /// stream, and at effort 3 libjxl's predefined <c>kWPFixedDC</c> one. The tree is then kept as the
    /// decoder will read it, so its leaf ids are the token contexts.
    /// </summary>
    public void ComputeTree()
    {
        // libjxl gives the tree learning the multipliers of quantised channels here; lossless has none.
        List<JxlModularMultiplierInfo> multiplierInfo = new();
        if (this.speedTier < JxlSpeedTier.Falcon)
        {
            // libjxl splits the streams into chunks (tree_splits_) and learns one tree per chunk, then
            // merges them under splits on the stream id. A Modular frame has one chunk, all the streams,
            // so MergeTrees only copies its tree.
            // Avoid creating a tree with leaves that don't correspond to any pixels.
            int start = 0;
            int stop = this.streamImages.Length;
            while (start < stop && this.streamImages[start].IsEmpty)
            {
                ++start;
            }

            while (start < stop && this.streamImages[stop - 1].IsEmpty)
            {
                --stop;
            }

            // Don't do anything if modular mode does not have any pixels in this image
            if (start == stop)
            {
                return;
            }

            // libjxl builds a predefined tree here for a chunk whose first stream does not learn one,
            // which only VarDCT frames have.
            List<JxlPropertyDecisionNode> learned = JxlModularEncoder.LearnTree(this.streamImages, this.streamOptions, start, stop, multiplierInfo);
            this.treeTokens.Clear();
            this.treeTokens.Add(new List<JxlToken>());
            this.tree = JxlModularEncoder.TokenizeTree(learned, this.treeTokens[0]);
            return;
        }

        // Fixed tree.
        long totalPixels = 0;
        int maxBitdepth = 0;
        foreach (JxlModularImage img in this.streamImages)
        {
            maxBitdepth = Math.Max(maxBitdepth, img.BitDepth);
            foreach (JxlModularChannel ch in img.Channel)
            {
                totalPixels += (long)ch.W * ch.H;
            }
        }

        // libjxl passes stream_options_[0].max_properties, which kWPFixedDC does not use.
        List<JxlPropertyDecisionNode> fixedTree = JxlModularEncoder.PredefinedTreeWpFixedDc(totalPixels, maxBitdepth);
        this.treeTokens.Clear();
        this.treeTokens.Add(new List<JxlToken>());
        this.tree = JxlModularEncoder.TokenizeTree(fixedTree, this.treeTokens[0]);
    }

    /// <summary>
    /// Turns each stream into tokens with the global tree. The streams are independent: this loop is
    /// where libjxl encodes them in parallel.
    /// </summary>
    public void ComputeTokens()
    {
        int numStreams = this.streamImages.Length;
        this.streamHeaders = new JxlGroupHeader[numStreams];
        this.tokens = new List<JxlToken>[numStreams];
        for (int streamId = 0; streamId < numStreams; streamId++)
        {
            this.streamHeaders[streamId] = new JxlGroupHeader();
            this.tokens[streamId] = new List<JxlToken>();

            // libjxl also keeps the width of the widest channel, which only LZ77 uses.
            JxlModularEncoder.ModularCompress(this.streamImages[streamId], this.streamOptions[streamId], streamId, this.tree, this.streamHeaders[streamId], this.tokens[streamId]);
        }
    }

    /// <summary>
    /// Writes the global part of the Modular data, at the start of the LF global section: the tree
    /// and its histograms, then the histograms every stream's tokens share.
    /// </summary>
    /// <param name="writer">The writer.</param>
    public void EncodeGlobalInfo(JxlBitWriter writer)
    {
        // If we are using brotli, or not using modular mode.
        if (this.treeTokens.Count == 0 || this.treeTokens[0].Count == 0)
        {
            writer.Write(1, 0);
            return;
        }

        writer.Write(1, 1);

        // Write tree
        JxlHistogramParams parameters = JxlHistogramParams.ForModular(this.speedTier, modularMode: true, this.streamOptions[0].Predictor);
        JxlEntropyEncodingData treeCode = new();
        JxlEntropyEncoder.BuildAndEncodeHistograms(parameters, JxlMaTree.NumTreeContexts, this.treeTokens, treeCode, writer);
        JxlEntropyEncoder.WriteTokens(this.treeTokens[0], treeCode, 0, writer);

        // Write histograms.
        JxlEntropyEncoder.BuildAndEncodeHistograms(this.histogramParams, (this.tree.Count + 1) / 2, this.tokens, this.code, writer);
    }

    /// <summary>Writes the Modular stream <paramref name="streamId"/>: nothing if it has no channels.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="streamId">The stream id.</param>
    public void EncodeStream(JxlBitWriter writer, long streamId)
    {
        if (this.streamImages[streamId].Channel.Count == 0)
        {
            // Image with no channels, header never gets decoded.
            return;
        }

        // libjxl compresses the stream on its own here when there are no tokens, which only its
        // streaming mode needs.
        JxlBundle.Write(this.streamHeaders[streamId], writer);
        JxlEntropyEncoder.WriteTokens(this.tokens[streamId], this.code, 0, writer);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (JxlModularImage image in this.streamImages)
        {
            image.Dispose();
        }
    }

    // The tail of libjxl's ComputeEncodingData: the stream of each LF group and each group, then the
    // channels of the global image that the groups code.
    private void FillStreamParams()
    {
        // Fill other groups.
        // DC
        for (long groupId = 0; groupId < this.frameDim.NumDcGroups; groupId++)
        {
            long rgx = groupId % this.frameDim.XSizeDcGroups;
            long rgy = groupId / this.frameDim.XSizeDcGroups;
            JxlRect rect = new(rgx * this.frameDim.DcGroupDim, rgy * this.frameDim.DcGroupDim, this.frameDim.DcGroupDim, this.frameDim.DcGroupDim);

            // minShift==3 because (frame_dim.dc_group_dim >> 3) == frame_dim.group_dim
            // maxShift==1000 is infinity
            this.streamParams.Add(new StreamParams(rect, 3, 1000, JxlModularStreamId.ModularDc(this.frameDim, groupId)));
        }

        // AC global -> nothing.
        // AC: one pass, so the downsampling bracket is all shifts.
        for (long groupId = 0; groupId < this.frameDim.NumGroups; groupId++)
        {
            long rgx = groupId % this.frameDim.XSizeGroups;
            long rgy = groupId / this.frameDim.XSizeGroups;
            JxlRect mrect = new(rgx * this.frameDim.GroupDimension, rgy * this.frameDim.GroupDimension, this.frameDim.GroupDimension, this.frameDim.GroupDimension);
            this.streamParams.Add(new StreamParams(mrect, 0, 1000, JxlModularStreamId.ModularAc(this.frameDim, groupId, 0)));
        }

        // if there's only one group, everything ends up in GlobalModular in that case, also try
        // RCTs/WP params for the one group
        if (this.streamParams.Count == 2)
        {
            this.streamParams.Add(new StreamParams(new JxlRect(0, 0, this.frameDim.XSize, this.frameDim.YSize), 0, 1000, JxlModularStreamId.Global));
        }

        // The streams are independent: this loop is where libjxl prepares them in parallel.
        foreach (StreamParams stream in this.streamParams)
        {
            this.PrepareStreamParams(stream.Rect, stream.MinShift, stream.MaxShift, stream.Id);
        }

        // Clear out channels that have been copied to groups.
        JxlModularImage fullImage = this.streamImages[0];
        int ch = fullImage.NbMetaChannels;
        for (; ch < fullImage.Channel.Count; ch++)
        {
            JxlModularChannel fc = fullImage.Channel[ch];
            if (fc.W > this.frameDim.GroupDimension || fc.H > this.frameDim.GroupDimension)
            {
                break;
            }
        }

        for (; ch < fullImage.Channel.Count; ch++)
        {
            // The channel keeps its size, as libjxl's does, so the tree counts its pixels.
            fullImage.Channel[ch].DropPlane();
        }

        // libjxl validates the channel dimensions here, which only squeeze can make inconsistent.
    }

    private void PrepareStreamParams(JxlRect rect, int minShift, int maxShift, long streamId)
    {
        if (streamId == 0 && this.frameDim.NumGroups != 1)
        {
            // If we have multiple groups, then the stream with ID 0 holds the full image and we do not
            // want to apply transforms or in general change the pixel values.
            return;
        }

        JxlModularImage fullImage = this.streamImages[0];
        if (streamId > 0)
        {
            JxlModularImage gi = JxlModularImage.Create(this.allocator, (int)rect.XSize, (int)rect.YSize, fullImage.BitDepth, 0);
            this.streamImages[streamId].Dispose();
            this.streamImages[streamId] = gi;

            // start at the first bigger-than-frame_dim.group_dim non-metachannel
            int c = fullImage.NbMetaChannels;
            for (; c < fullImage.Channel.Count; c++)
            {
                JxlModularChannel fc = fullImage.Channel[c];
                if (fc.W > this.frameDim.GroupDimension || fc.H > this.frameDim.GroupDimension)
                {
                    break;
                }
            }

            for (; c < fullImage.Channel.Count; c++)
            {
                JxlModularChannel fc = fullImage.Channel[c];
                int shift = Math.Min(fc.HShift, fc.VShift);
                if (shift > maxShift || shift < minShift)
                {
                    continue;
                }

                JxlRect r = new(rect.X0 >> fc.HShift, rect.Y0 >> fc.VShift, rect.XSize >> fc.HShift, rect.YSize >> fc.VShift, fc.W, fc.H);
                if (r.XSize == 0 || r.YSize == 0)
                {
                    continue;
                }

                JxlModularChannel gc = JxlModularChannel.Create(this.allocator, (int)r.XSize, (int)r.YSize, fc.HShift, fc.VShift);
                for (int y = 0; y < r.YSize; ++y)
                {
                    fc.Row((int)r.Y0 + y).Slice((int)r.X0, (int)r.XSize).CopyTo(gc.Row(y));
                }

                gi.Channel.Add(gc);
            }

            if (gi.Channel.Count == 0)
            {
                return;
            }
        }

        // libjxl does some per-group transforms here: a local palette, the RCT search and the
        // weighted predictor mode search. Effort 3 runs none of them.
    }

    // The tree-learning parameters of each speed tier, from libjxl's Init for a lossless, non-responsive
    // frame: the properties tree learning may split on, how many values each is quantised to, and the
    // fraction of pixels sampled. Effort 3 and up use a fixed tree, so their row (the default) is not
    // read; it is kept as libjxl has it.
    private static void SetTreeLearningParameters(JxlModularOptions options, JxlSpeedTier speedTier, long numStreams, JxlImageMetadata metadata)
    {
        List<uint> propOrder = new(PropOrder);

        // if few groups, don't use group as a property
        if (numStreams < 30 && speedTier > JxlSpeedTier.Tortoise)
        {
            propOrder.RemoveAt(1);
        }

        int maxProperties = Math.Min(options.MaxProperties, metadata.NumExtraChannels + (metadata.ColorEncoding.IsGray ? 0 : 2));
        switch (speedTier)
        {
            case JxlSpeedTier.Hare:
                options.SplittingHeuristicsProperties = propOrder.GetRange(0, 4);
                options.MaxPropertyValues = 48;
                options.NbRepeats *= 0.5f;
                break;
            case JxlSpeedTier.Wombat:
                options.SplittingHeuristicsProperties = propOrder.GetRange(0, 5);
                options.MaxPropertyValues = 64;
                options.NbRepeats *= 0.7f;
                break;
            case JxlSpeedTier.Squirrel:
                options.SplittingHeuristicsProperties = propOrder.GetRange(0, 7);
                options.MaxPropertyValues = 96;
                break;
            case JxlSpeedTier.Kitten:
                options.SplittingHeuristicsProperties = propOrder.GetRange(0, 10);
                options.MaxPropertyValues = 128;
                options.NbRepeats *= 1.1f;
                break;
            case JxlSpeedTier.Glacier:
            case JxlSpeedTier.Tortoise:
                options.SplittingHeuristicsProperties = propOrder;
                options.MaxPropertyValues = 256;
                options.NbRepeats *= 1.3f;
                break;
            default:
                options.SplittingHeuristicsProperties = propOrder.GetRange(0, 3);
                options.MaxPropertyValues = 32;
                options.NbRepeats *= 0.3f;
                break;
        }

        if (speedTier > JxlSpeedTier.Tortoise)
        {
            // Gradient in previous channels.
            for (int i = 0; i < maxProperties; i++)
            {
                options.SplittingHeuristicsProperties.Add((uint)(JxlModularPredict.NumNonrefProperties + (i * 4) + 3));
            }
        }
        else
        {
            // All the extra properties in Tortoise mode.
            for (int i = 0; i < maxProperties * 4; i++)
            {
                options.SplittingHeuristicsProperties.Add((uint)(JxlModularPredict.NumNonrefProperties + i));
            }
        }
    }

    // libjxl's default predictor for a lossless, non-responsive Modular frame with no lossy palette:
    // every predictor in the slowest modes, Gradient and Weighted below Falcon, Weighted at Falcon and
    // Gradient above it.
    private static JxlPredictor DefaultPredictor(JxlSpeedTier speedTier)
    {
        if (speedTier <= JxlSpeedTier.Glacier)
        {
            // TODO(veluca): allow all predictors that don't break residual multipliers in lossy mode.
            return JxlPredictor.Variable;
        }

        if (speedTier < JxlSpeedTier.Falcon)
        {
            // try median and weighted predictor for anything else
            return JxlPredictor.Best;
        }

        // just weighted predictor in falcon mode, and just gradient predictor in thunder mode
        return speedTier == JxlSpeedTier.Falcon ? JxlPredictor.Weighted : JxlPredictor.Gradient;
    }

    // libjxl's do_transform: applies the transform and, when it took, adds it to the image's list.
    // libjxl copies the transform first; callers here pass a new one.
    private static bool DoTransform(JxlModularImage image, JxlTransform tr)
    {
        bool didIt = JxlTransformEncoder.TransformForward(tr, image);
        if (didIt)
        {
            image.Transform.Add(tr);
        }

        return didIt;
    }

    // libjxl's maybe_do_transform. From Squirrel up, which includes Fastest, a transform that applies
    // is kept. Below Squirrel libjxl estimates the cost after the transform and, when the cost went up,
    // undoes it with Transform::Inverse (JxlTransform.Inverse here) and drops it from the list. That
    // estimate (EstimateCost) belongs to slice E2.
    private bool MaybeDoTransform(JxlModularImage image, JxlTransform tr)
    {
        if (this.speedTier >= JxlSpeedTier.Squirrel)
        {
            return DoTransform(image, tr);
        }

        JxlThrowHelper.ThrowNotSupportedException("The transform cost estimate of higher efforts (slice E2)");
        return false;
    }

    // libjxl's try_palettes, without the lossy palette: the global palette over every channel, then
    // over all but the last, then a channel palette (channel compaction) for each channel left.
    private void TryPalettes(JxlModularImage gi, ref int maxBitdepth, ref int maxval, float channelColorsPercent)
    {
        float costBefore = 0f;
        int didPalette = 0;
        float nbPixels = gi.Channel[0].W * gi.Channel[0].H;
        int nbChans = gi.Channel.Count - gi.NbMetaChannels;

        // arbitrary estimate: 4.8 bpp for 8-bit RGB
        float arbitraryBppEstimate = 0.2f * gi.BitDepth * nbChans;

        if (PaletteColors != 0 || LossyPalette)
        {
            // when not estimating, assume some arbitrary bpp. libjxl estimates the cost at Squirrel
            // and below, which slice E2 adds.
            if (this.speedTier <= JxlSpeedTier.Squirrel)
            {
                JxlThrowHelper.ThrowNotSupportedException("The palette cost estimate of higher efforts (slice E2)");
            }

            costBefore = nbPixels * arbitraryBppEstimate;

            // all-channel palette (e.g. RGBA)
            if (nbChans > 1)
            {
                // Heuristic choice of max colors for a palette:
                // max_colors = nb_pixels * estimated_bpp_without_palette * 0.0005 +
                //              + nb_pixels / 128 + 128
                //       (estimated_bpp_without_palette = cost_before / nb_pixels)
                // Rationale: small image with large palette is not effective;
                // also if the entropy (estimated bpp) is low (e.g. mostly solid/gradient
                // areas), palette is less useful and may even be counterproductive.
                JxlTransform maybePalette = new()
                {
                    Id = JxlTransformId.Palette,
                    BeginC = (uint)gi.NbMetaChannels,
                    NumC = (uint)nbChans,
                    NbColors = (uint)Math.Min((int)((costBefore * 0.0005f) + (nbPixels / 128) + 128), Math.Abs(PaletteColors)),
                    OrderedPalette = PaletteColors >= 0,
                    LossyPalette = LossyPalette && nbChans == 3,
                };
                didPalette = this.MaybeDoTransform(gi, maybePalette) ? 1 : 0;
            }

            // all-minus-one-channel palette (RGB with separate alpha, or CMY with separate K)
            if (didPalette == 0 && nbChans > 3)
            {
                JxlTransform maybePalette3 = new()
                {
                    Id = JxlTransformId.Palette,
                    BeginC = (uint)gi.NbMetaChannels,
                    NumC = (uint)(nbChans - 1),
                    NbColors = (uint)Math.Min((int)((costBefore * 0.0005f) + (nbPixels / 128) + 128), Math.Abs(PaletteColors)),
                    OrderedPalette = PaletteColors >= 0,
                    LossyPalette = LossyPalette,
                };
                didPalette = this.MaybeDoTransform(gi, maybePalette3) ? 1 : 0;
            }
        }

        if (channelColorsPercent > 0)
        {
            // single channel palette (like FLIF's ChannelCompact)
            int nbChannels = gi.Channel.Count - gi.NbMetaChannels - didPalette;
            int origBitdepth = maxBitdepth;
            maxBitdepth = 0;

            // libjxl re-estimates the cost here below Squirrel and otherwise sets it to 0; it is not
            // read again from Squirrel up.
            for (int i = didPalette; i < nbChannels + didPalette; i++)
            {
                JxlTransformEncoder.ComputeMinMax(gi.Channel[gi.NbMetaChannels + i], out int min, out int max);
                long colors = (long)max - min + 1;

                // simple heuristic: if less than X percent of the values in the range actually occur,
                // it is probably worth it to do a compaction (but only if the channel palette is less
                // than 6% the size of the image itself)
                JxlTransform maybePalette1 = new()
                {
                    Id = JxlTransformId.Palette,
                    BeginC = (uint)(i + gi.NbMetaChannels),
                    NumC = 1,
                    NbColors = (uint)Math.Min((int)(nbPixels / 16), (int)(channelColorsPercent / 100.0 * colors)),
                };
                bool didChPalette = this.MaybeDoTransform(gi, maybePalette1);
                if (didChPalette)
                {
                    // effective bit depth is lower, adjust quantization accordingly
                    JxlTransformEncoder.ComputeMinMax(gi.Channel[gi.NbMetaChannels + i], out min, out max);
                    if (max < maxval)
                    {
                        maxval = max;
                    }

                    int chBitdepth = max > 0 ? JxlBits.CeilLog2Nonzero((uint)max) : 0;
                    if (chBitdepth > maxBitdepth)
                    {
                        maxBitdepth = chBitdepth;
                    }
                }
                else
                {
                    maxBitdepth = origBitdepth;
                }
            }
        }
    }

    private readonly struct StreamParams
    {
        public StreamParams(JxlRect rect, int minShift, int maxShift, long id)
        {
            this.Rect = rect;
            this.MinShift = minShift;
            this.MaxShift = maxShift;
            this.Id = id;
        }

        public JxlRect Rect { get; }

        public int MinShift { get; }

        public int MaxShift { get; }

        public long Id { get; }
    }
}
