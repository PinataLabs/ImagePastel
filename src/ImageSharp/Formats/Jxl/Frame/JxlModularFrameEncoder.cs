// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_modular.h and lib/jxl/enc_modular.cc (MergeTrees, do_transform, maybe_do_transform,
// try_palettes, and ModularFrameEncoder: Init, ComputeEncodingData, PrepareStreamParams, ComputeTree,
// ComputeTokens, EncodeGlobalInfo, EncodeStream, and the 4:4:4 paths of AddVarDCTDC, with QuantizeWP for
// near-lossless DC, and AddACMetadata from effort 3 to 7).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Modular;
using SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;
using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <summary>
/// Encodes the Modular streams of a frame: the global stream and one per LF group and per group,
/// with one global MA tree and one set of histograms that every stream shares. The lossless paths of
/// libjxl's efforts 3, 5 and 7 (Fastest, Default and Thorough) are ported: the predefined
/// <c>kWPFixedDC</c> tree with the Weighted predictor at effort 3, and a learned tree with the
/// Gradient and Weighted predictors at efforts 5 and 7, with the per-group RCT search and local
/// palettes. Effort 7 learns the tree over more properties, tries more RCTs and caps the global
/// palette by the estimated cost. Squeeze and quantisation are not ported.
/// For a lossy VarDCT frame it codes the quantised DC and the AC metadata of each LF group, and the extra
/// channels losslessly, with one tree per kind of stream merged into one by stream id: libjxl's predefined
/// trees for the DC and the AC metadata, and for the extra channels <c>kWPFixedDC</c> at effort 3 and a
/// learned tree at effort 5. It is the mirror of <see cref="JxlModularFrameDecoder"/>.
/// </summary>
internal sealed class JxlModularFrameEncoder : IDisposable
{
    // The level-10 limit. libjxl uses 16 for level 5, but cjxl leaves the level unset (-1) when it
    // queues the frame, so the frame encoder always sees this.
    private const int LevelMaxBitdepth = 32;

    // libjxl's CompressParams defaults (enc_params.h): channel_colors_pre_transform_percent,
    // channel_colors_percent, palette_colors and lossy_palette. Lossless keeps them.
    private const float ChannelColorsPreTransformPercent = 95f;
    private const float ChannelColorsPercent = 80f;
    private const int PaletteColors = 1 << 10;
    private const bool LossyPalette = false;

    // libjxl's CompressParams default decoding_speed_tier, which the encoder has no option for.
    private const int DecodingSpeedTier = 0;

    // The dead zone of near-lossless DC (libjxl's q_deadzone).
    private const float QDeadzone = 0.62f;

    // Properties in order of their likelihood of being useful, for the non-Squeeze case.
    private static readonly uint[] PropOrder = { 0, 1, 15, 9, 10, 11, 12, 13, 14, 2, 3, 4, 5, 6, 7, 8 };

    // The RCTs the per-group search tries, in libjxl's order; each speed tier tries the first
    // NbRctsToTry of them. These should be 19 actually different transforms; the remaining ones are
    // equivalent to one of these (note that the first two are do-nothing and YCoCg) modulo channel
    // reordering (which only matters in the case of MA-with-prev-channels-properties) and/or sign
    // (e.g. RmG vs GmR)
    private static readonly int[] RctCandidates =
    {
        (0 * 7) + 0, (0 * 7) + 6, (0 * 7) + 5, (1 * 7) + 3, (3 * 7) + 5, (5 * 7) + 5, (1 * 7) + 5, (2 * 7) + 5, (1 * 7) + 1, (0 * 7) + 4,
        (1 * 7) + 2, (2 * 7) + 1, (2 * 7) + 2, (2 * 7) + 3, (4 * 7) + 4, (4 * 7) + 5, (0 * 7) + 2, (0 * 7) + 1, (0 * 7) + 3,
    };

    private readonly MemoryAllocator allocator;
    private readonly JxlFrameDimensions frameDim;
    private readonly JxlSpeedTier speedTier;
    private readonly JxlModularImage[] streamImages;
    private readonly JxlModularOptions[] streamOptions;
    private readonly JxlModularOptions baseOptions;
    private readonly bool modularMode;
    private readonly List<long> treeSplits = new();
    private readonly List<StreamParams> streamParams = new();
    private readonly List<List<JxlToken>> treeTokens = new();
    private readonly JxlEntropyEncodingData code = new();
    private List<JxlPropertyDecisionNode> tree;
    private JxlGroupHeader[] streamHeaders;
    private List<JxlToken>[] tokens;
    private int[] imageWidths;

    // libjxl's do_color: whether the Modular image codes the colour channels (a Modular frame) or only
    // the extra channels (a VarDCT frame).
    private bool doColor = true;

    // libjxl's cparams_.colorspace: -1 lets the encoder choose the RCTs, 0 turns the per-group RCT
    // search off. This encoder has no option to force an RCT.
    private int colorspace = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlModularFrameEncoder"/> class, as libjxl's
    /// <c>ModularFrameEncoder::Init</c> does for a lossless Modular frame or a VarDCT frame whose Modular
    /// part is lossless: no responsive mode, the tree-learning parameters and predictor of the speed tier,
    /// the <c>kWPFixedDC</c> tree at effort 3, the chunks of streams that each get their own tree, and
    /// the Modular histogram parameters.
    /// </summary>
    /// <param name="frameHeader">The frame header; a VarDCT frame is libjxl's <c>modular_mode == false</c>.</param>
    /// <param name="speedTier">The speed tier.</param>
    /// <param name="allocator">The allocator.</param>
    public JxlModularFrameEncoder(JxlFrameHeader frameHeader, JxlSpeedTier speedTier, MemoryAllocator allocator)
    {
        this.allocator = allocator;
        this.speedTier = speedTier;
        this.frameDim = frameHeader.ToFrameDimensions();
        this.modularMode = frameHeader.Encoding == JxlFrameEncoding.Modular;

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
        options.Predictor = DefaultPredictor(speedTier, this.modularMode);
        this.treeSplits.Add(0);
        if (!this.modularMode)
        {
            options.FastDecodeMultiplier = 1.0f;
            this.treeSplits.Add(JxlModularStreamId.VarDctDc(this.frameDim, 0));
            this.treeSplits.Add(JxlModularStreamId.ModularDc(this.frameDim, 0));
            this.treeSplits.Add(JxlModularStreamId.AcMetadata(this.frameDim, 0));
            this.treeSplits.Add(JxlModularStreamId.QuantTable(this.frameDim, 0));
            this.treeSplits.Add(JxlModularStreamId.ModularAc(this.frameDim, 0, 0));
            this.AcMetadataSize = new int[this.frameDim.NumDcGroups];
            this.ExtraDcPrecision = new int[this.frameDim.NumDcGroups];
        }

        this.treeSplits.Add(numStreams);
        options.MaxChanSize = this.frameDim.GroupDimension;
        options.GroupDim = this.frameDim.GroupDimension;

        // TODO(veluca): figure out how to use different predictor sets per channel. Every stream but the
        // first shares these options until a step gives it a copy of its own (libjxl copies them by value).
        this.baseOptions = options;
        this.streamOptions = new JxlModularOptions[numStreams];
        Array.Fill(this.streamOptions, options);
        this.streamOptions[0] = options.Clone();
        if (speedTier == JxlSpeedTier.Falcon)
        {
            this.streamOptions[0].Kind = JxlModularOptions.TreeKind.WPFixedDC;
        }

        // libjxl keeps the Modular histogram parameters (without the extra DC precision, not known yet) in
        // stream 0's options here. Only a stream compressed on its own (streaming mode, raw quant tables)
        // reads them, which this port never does; EncodeGlobalInfo makes its own.
    }

    /// <summary>
    /// Gets the VarDCT channels (0 X, 1 Y, 2 B) in the order libjxl's encoder quantises them, for the DC and
    /// for <c>AdjustQuantBlockAC</c>: Y first, which X and B are predicted from.
    /// </summary>
    internal static int[] YxbOrder { get; } = { 1, 0, 2 };

    /// <summary>Gets the number of AC metadata entries (varblocks) of each LF group of a VarDCT frame.</summary>
    public int[] AcMetadataSize { get; } = Array.Empty<int>();

    /// <summary>Gets the extra DC precision of each LF group of a VarDCT frame.</summary>
    public int[] ExtraDcPrecision { get; } = Array.Empty<int>();

    /// <summary>
    /// Fills the Modular image of the global stream from <paramref name="image"/>, then the images of
    /// the streams that code parts of it. A Modular frame codes the colour and the extra channels; a
    /// VarDCT frame (<paramref name="doColor"/> off) only the extra channels, losslessly.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="metadata">The image metadata.</param>
    /// <param name="image">The image.</param>
    /// <param name="doColor">Whether the Modular image codes the colour channels: libjxl's <c>modular_mode</c>.</param>
    public void ComputeEncodingData<TPixel>(JxlFrameHeader frameHeader, JxlImageMetadata metadata, Image<TPixel> image, bool doColor)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.doColor = doColor;

        // Convert ImageBundle to modular Image object
        int xsize = (int)this.frameDim.XSize;
        int ysize = (int)this.frameDim.YSize;

        int nbChans = 3;
        if (metadata.ColorEncoding.IsGray && frameHeader.ColorTransform == JxlColorTransform.None)
        {
            nbChans = 1;
        }

        if (!doColor)
        {
            nbChans = 0;
        }

        nbChans += metadata.NumExtraChannels;

        bool fp = metadata.BitDepth.FloatingPointSample && frameHeader.ColorTransform != JxlColorTransform.Xyb;

        // bits_per_sample is just metadata for XYB-encoded images.
        if (metadata.BitDepth.BitsPerSample >= 32 && doColor && frameHeader.ColorTransform != JxlColorTransform.Xyb)
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
        int maxBitdepth = doColor ? (int)metadata.BitDepth.BitsPerSample + (fp ? 0 : 1) : 0;
        JxlModularImage gi = JxlModularImage.Create(this.allocator, xsize, ysize, (int)metadata.BitDepth.BitsPerSample, nbChans);
        this.streamImages[0].Dispose();
        this.streamImages[0] = gi;

        if (doColor)
        {
            JxlEncoderInput<TPixel>.CopyColorChannels(image, metadata.BitDepth, gi);
        }

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

        // Set options and apply transformations. A lossless Modular part (every frame of this encoder: the
        // extra channels of a VarDCT frame are lossless too) keeps libjxl's palette settings.
        // Global palette transforms
        float channelColorsPercent = 0;
        if (!LossyPalette && (this.speedTier <= JxlSpeedTier.Thunder || (doColor && metadata.BitDepth.BitsPerSample > 8)))
        {
            channelColorsPercent = ChannelColorsPreTransformPercent;
        }

        // Global palette causes bad progressive loading due to interpolation but lossy palette is
        // still required for JXL art. Lossless Fastest is neither streamed (groupwise) nor responsive.
        this.TryPalettes(gi, ref maxBitdepth, ref maxval, channelColorsPercent);

        // don't do an RCT if we're short on bits
        if (doColor && gi.Channel.Count - gi.NbMetaChannels >= 3 && maxBitdepth + 1 < LevelMaxBitdepth)
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

        // libjxl's move-to-front and squeeze steps are not taken by lossless.
        if (maxBitdepth + 1 > LevelMaxBitdepth)
        {
            // force no group RCTs if we don't have a spare bit
            this.colorspace = 0;
        }

        if (maxBitdepth > LevelMaxBitdepth)
        {
            JxlThrowHelper.ThrowImageFormatException($"Bitdepth {maxBitdepth} too high after the transforms");
        }

        this.FillStreamParams();
    }

    /// <summary>
    /// Builds the global tree and turns it into tokens: above effort 3 (below Falcon) a tree learned
    /// from every stream, and at effort 3 libjxl's predefined <c>kWPFixedDC</c> one. The tree is then kept as the
    /// decoder will read it, so its leaf ids are the token contexts.
    /// </summary>
    public void ComputeTree()
    {
        // libjxl gives the tree learning the multipliers of quantised channels here; a lossless Modular
        // part has none.
        List<JxlModularMultiplierInfo> multiplierInfo = new();
        if (this.speedTier < JxlSpeedTier.Falcon || !this.modularMode)
        {
            // Avoid creating a tree with leaves that don't correspond to any pixels. A Modular frame has
            // one chunk, all the streams; a VarDCT frame has one per kind of stream.
            List<long> usefulSplits = new(this.treeSplits.Count);
            for (int chunk = 0; chunk < this.treeSplits.Count - 1; chunk++)
            {
                bool hasPixels = false;
                for (long i = this.treeSplits[chunk]; i < this.treeSplits[chunk + 1]; i++)
                {
                    if (!this.streamImages[i].IsEmpty)
                    {
                        hasPixels = true;
                    }
                }

                if (hasPixels)
                {
                    usefulSplits.Add(this.treeSplits[chunk]);
                }
            }

            // Don't do anything if modular mode does not have any pixels in this image
            if (usefulSplits.Count == 0)
            {
                return;
            }

            usefulSplits.Add(this.treeSplits[^1]);

            // The chunks are independent: this loop is where libjxl learns their trees in parallel.
            List<JxlPropertyDecisionNode>[] trees = new List<JxlPropertyDecisionNode>[usefulSplits.Count - 1];
            for (int chunk = 0; chunk < trees.Length; chunk++)
            {
                int start = (int)usefulSplits[chunk];
                int stop = (int)usefulSplits[chunk + 1];
                while (start < stop && this.streamImages[start].IsEmpty)
                {
                    ++start;
                }

                while (start < stop && this.streamImages[stop - 1].IsEmpty)
                {
                    --stop;
                }

                if (this.streamOptions[start].Kind == JxlModularOptions.TreeKind.Learn)
                {
                    trees[chunk] = JxlModularEncoder.LearnTree(this.streamImages, this.streamOptions, start, stop, multiplierInfo);
                }
                else
                {
                    long chunkPixels = 0;
                    for (int i = start; i < stop; i++)
                    {
                        foreach (JxlModularChannel ch in this.streamImages[i].Channel)
                        {
                            chunkPixels += (long)ch.W * ch.H;
                        }
                    }

                    chunkPixels = Math.Max(chunkPixels, 1);

                    // libjxl passes a bit depth of 8 here, whatever the depth of the samples.
                    trees[chunk] = JxlModularEncoder.PredefinedTree(this.streamOptions[start].Kind, chunkPixels, 8);
                }
            }

            List<JxlPropertyDecisionNode> merged = new();
            MergeTrees(trees, usefulSplits, 0, trees.Length, merged);
            this.treeTokens.Clear();
            this.treeTokens.Add(new List<JxlToken>());
            this.tree = JxlModularEncoder.TokenizeTree(merged, this.treeTokens[0]);
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
    /// Fills the VarDCT DC stream of LF group <paramref name="groupIndex"/> with the quantised DC of its
    /// blocks, as libjxl's <c>AddVarDCTDC</c> does for a 4:4:4 frame: the channels in Y, X, B order, X and
    /// B after the chroma-from-luma prediction from the quantised Y. Without near-lossless DC (effort 3 and
    /// faster) each value is rounded half away from zero. With it (effort 4 to 7), the DC has one bit of
    /// extra precision, and each value is the weighted predictor's guess plus a residual quantised with a
    /// dead zone (libjxl's <c>QuantizeWP</c>). Then, as libjxl's <c>DequantDC</c> there, it dequantises the
    /// group's DC into the encoder's own copy (<see cref="JxlPassesEncoderState.Dc"/>) and sets its
    /// quantised-DC buckets (<see cref="JxlPassesEncoderState.QuantDc"/>), as the decoder will.
    /// </summary>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="dc">The X, Y and B DC of each block of the frame, row by row.</param>
    /// <param name="r">The LF group's rectangle, in blocks.</param>
    /// <param name="groupIndex">The LF group.</param>
    /// <param name="encState">
    /// The encoder state: its width in blocks is the stride of <paramref name="dc"/>, its speed tier sets
    /// near-lossless DC, its quantiser sets the DC steps, its chroma-from-luma DC factors predict X and B,
    /// and its block context map gives the DC buckets.
    /// </param>
    public void AddVarDctDc(JxlFrameHeader frameHeader, float[][] dc, JxlRect r, long groupIndex, JxlPassesEncoderState encState)
    {
        JxlQuantizer quantizer = encState.Quantizer;
        JxlColorCorrelation colorCorrelation = encState.Cmap;
        int stride = encState.XSizeBlocks;

        // libjxl's caller passes nl_dc as speed_tier < kFalcon: the DC is quantised near-losslessly below
        // Falcon.
        bool nlDc = encState.SpeedTier < JxlSpeedTier.Falcon;
        this.ExtraDcPrecision[groupIndex] = nlDc ? 1 : 0;
        float mul = 1 << this.ExtraDcPrecision[groupIndex];

        long streamId = JxlModularStreamId.VarDctDc(this.frameDim, groupIndex);
        JxlModularOptions options = this.baseOptions.Clone();
        this.streamOptions[streamId] = options;
        options.MaxChanSize = 0xFFFFFF;
        options.Predictor = JxlPredictor.Weighted;
        options.WpTreeMode = JxlModularOptions.TreeMode.WPOnly;
        if (this.speedTier >= JxlSpeedTier.Squirrel)
        {
            options.Kind = JxlModularOptions.TreeKind.WPFixedDC;
        }
        else
        {
            JxlThrowHelper.ThrowNotSupportedException("A learned tree for the VarDCT DC (libjxl effort 8 and up)");
        }

        int xs = (int)r.XSize;
        int ys = (int)r.YSize;
        JxlModularImage image = JxlModularImage.Create(this.allocator, xs, ys, 8, 3);
        this.streamImages[streamId].Dispose();
        this.streamImages[streamId] = image;

        // Departure (ADR 0001): libjxl (enc_modular.cc:1615-1726) has four loops here: near-lossless DC with
        // kGradientFixedDC, near-lossless DC with the weighted predictor, plain 4:4:4, and chroma subsampled.
        // The first needs a decoding speed tier of 1 or more, and the last a subsampled frame; this encoder
        // has neither option, so it never takes them. The two loops left differ only in how one value is
        // quantised, so this runs them as one loop that branches on nlDc for each value. Each value goes
        // through the same arithmetic, in the same channel order, as libjxl's, so the output is the same.
        // The decoder's 4:4:4 order: Y, X, B.
        ReadOnlySpan<float> dcFactors = colorCorrelation.DcFactors;
        bool hasOutliers = false;
        foreach (int c in YxbOrder)
        {
            float invFactor = quantizer.GetInvDcStep(c) * mul;
            float yFactor = quantizer.GetDcStep(1) / mul;
            float cflFactor = dcFactors[c];
            JxlModularChannel channel = image.Channel[c < 2 ? c ^ 1 : c];

            // Near-lossless DC predicts each value with the weighted predictor, whose state is new for
            // each channel, and quantises the residual with a dead zone.
            JxlWeightedState wpState = nlDc ? new JxlWeightedState(new JxlWeightedHeader(), xs) : null;
            for (int y = 0; y < ys; y++)
            {
                Span<int> quantRow = channel.Row(y);
                ReadOnlySpan<int> top = y > 0 ? channel.Row(y - 1) : Span<int>.Empty;
                ReadOnlySpan<int> topTop = y > 1 ? channel.Row(y - 2) : Span<int>.Empty;
                ReadOnlySpan<float> row = dc[c].AsSpan((int)(((r.Y0 + y) * stride) + r.X0), xs);
                ReadOnlySpan<int> quantRowY = image.Channel[0].Row(y);
                for (int x = 0; x < xs; x++)
                {
                    float value = c == 1 ? row[x] : row[x] - (quantRowY[x] * (yFactor * cflFactor));
                    if (nlDc)
                    {
                        quantRow[x] = QuantizeWp(quantRow, top, topTop, x, y, xs, wpState, value, invFactor, ref hasOutliers);
                        wpState.UpdateErrors(quantRow[x], x, y, xs);
                    }
                    else if (c == 1)
                    {
                        quantRow[x] = (int)MathF.Round(row[x] * invFactor, MidpointRounding.AwayFromZero);
                    }
                    else
                    {
                        quantRow[x] = (int)MathF.Round(value * invFactor, MidpointRounding.AwayFromZero);
                    }
                }
            }
        }

        if (hasOutliers)
        {
            JxlThrowHelper.ThrowImageFormatException("Unsupported range of DC values");
        }

        // The encoder's own reconstruction of the DC and the quantised-DC buckets. The buckets are not read
        // at effort 7 and faster (the block context map has no DC thresholds there).
        // As libjxl, this runs at every effort, but only the reconstruction (JxlEncReconstruct) reads the DC copy.
        JxlCompressedDc.DequantDc(r, encState.Dc, encState.QuantDc, image, quantizer.MulDc, 1.0f / mul, colorCorrelation.DcFactors, frameHeader.ChromaSubsampling, encState.BlockCtxMap);
    }

    /// <summary>
    /// Fills the AC metadata stream of LF group <paramref name="groupIndex"/>, as libjxl's
    /// <c>AddACMetadata</c>: the chroma-from-luma factors of each colour tile, the strategy and quant field
    /// of each varblock, and the EPF sharpness of each block.
    /// </summary>
    /// <param name="r">The LF group's rectangle, in blocks.</param>
    /// <param name="groupIndex">The LF group.</param>
    /// <param name="encState">The encoder state with the per-block choices.</param>
    public void AddAcMetadata(JxlRect r, long groupIndex, JxlPassesEncoderState encState)
    {
        long streamId = JxlModularStreamId.AcMetadata(this.frameDim, groupIndex);
        JxlModularOptions options = this.baseOptions.Clone();
        this.streamOptions[streamId] = options;
        options.MaxChanSize = 0xFFFFFF;
        if (options.Predictor != JxlPredictor.Weighted)
        {
            options.WpTreeMode = JxlModularOptions.TreeMode.NoWP;
        }

        if (this.speedTier >= JxlSpeedTier.Falcon)
        {
            options.Kind = JxlModularOptions.TreeKind.FalconACMeta;
        }
        else if (this.speedTier > JxlSpeedTier.Kitten)
        {
            options.Kind = JxlModularOptions.TreeKind.ACMeta;
        }
        else
        {
            // From Kitten down libjxl learns the tree of the AC metadata.
            JxlThrowHelper.ThrowNotSupportedException("A learned tree for the AC metadata (libjxl effort 8 and up)");
        }

        // YToX, YToB, ACS + QF, EPF
        int xs = (int)r.XSize;
        int ys = (int)r.YSize;
        JxlModularImage image = JxlModularImage.Create(this.allocator, xs, ys, 8, 4);
        this.streamImages[streamId].Dispose();
        this.streamImages[streamId] = image;

        // The colour tiles are 8 blocks.
        JxlRect cr = new(r.X0 >> 3, r.Y0 >> 3, (r.XSize + 7) >> 3, (r.YSize + 7) >> 3);
        ReplaceChannel(image, 0, JxlModularChannel.Create(this.allocator, (int)cr.XSize, (int)cr.YSize, 3, 3));
        ReplaceChannel(image, 1, JxlModularChannel.Create(this.allocator, (int)cr.XSize, (int)cr.YSize, 3, 3));
        ReplaceChannel(image, 2, JxlModularChannel.Create(this.allocator, xs * ys, 2));
        JxlColorCorrelation cmap = encState.Cmap;
        for (int y = 0; y < cr.YSize; y++)
        {
            Span<int> rowX = image.Channel[0].Row(y);
            Span<int> rowB = image.Channel[1].Row(y);
            int tileRow = (int)((cr.Y0 + y) * cmap.XSizeTiles);
            for (int x = 0; x < cr.XSize; x++)
            {
                rowX[x] = cmap.YtoXMap[tileRow + cr.X0 + x];
                rowB[x] = cmap.YtoBMap[tileRow + cr.X0 + x];
            }
        }

        int num = 0;
        Span<int> outAcs = image.Channel[2].Row(0);
        Span<int> outQf = image.Channel[2].Row(1);
        for (int y = 0; y < ys; y++)
        {
            Span<int> rowOutEpf = image.Channel[3].Row(y);
            for (int x = 0; x < xs; x++)
            {
                int blockIndex = encState.BlockIndex(r.X0 + x, r.Y0 + y);
                rowOutEpf[x] = encState.EpfSharpness[blockIndex];
                JxlAcStrategy acs = encState.AcStrategy.Get((int)(r.X0 + x), (int)(r.Y0 + y));
                if (!acs.IsFirstBlock)
                {
                    continue;
                }

                outAcs[num] = acs.RawStrategy;

                // AdjustQuantBlockAC can raise a multi-block strategy's 256 to 257, which libjxl writes as
                // it is (256 here); decoders clamp it to 256.
                outQf[num] = encState.RawQuantField[blockIndex] - 1;
                num++;
            }
        }

        // The channel keeps its plane; only its width shrinks, as libjxl's does.
        image.Channel[2].W = num;
        this.AcMetadataSize[groupIndex] = num;
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
        this.imageWidths = new int[numStreams];
        for (int streamId = 0; streamId < numStreams; streamId++)
        {
            this.streamHeaders[streamId] = new JxlGroupHeader();
            this.tokens[streamId] = new List<JxlToken>();

            // The width of the widest channel, which LZ77 uses.
            this.imageWidths[streamId] = JxlModularEncoder.ModularCompress(this.streamImages[streamId], this.streamOptions[streamId], streamId, this.tree, this.streamHeaders[streamId], this.tokens[streamId]);
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

        // Write tree. The parameters take the extra DC precision of the LF groups: near-lossless DC makes
        // both the tree's and the streams' histograms choose the hybrid uint configuration (kFast).
        JxlHistogramParams parameters = JxlHistogramParams.ForModular(this.speedTier, this.modularMode, this.baseOptions.Predictor, this.ExtraDcPrecision);
        JxlEntropyEncodingData treeCode = new();
        JxlEntropyEncoder.BuildAndEncodeHistograms(parameters, JxlMaTree.NumTreeContexts, this.treeTokens, treeCode, writer);
        JxlEntropyEncoder.WriteTokens(this.treeTokens[0], treeCode, 0, writer);

        // Write histograms.
        parameters.ImageWidths = this.imageWidths;
        JxlEntropyEncoder.BuildAndEncodeHistograms(parameters, (this.tree.Count + 1) / 2, this.tokens, this.code, writer);
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

    // libjxl's QuantizeWP: the residual of the scaled value from the weighted predictor's guess, 0 inside
    // the dead zone, rounded half away from zero, and to an even step when it is larger than 2. A value
    // that is NaN or out of the int range sets hasOutliers.
    private static int QuantizeWp(ReadOnlySpan<int> row, ReadOnlySpan<int> top, ReadOnlySpan<int> topTop, int x, int y, int w, JxlWeightedState wpState, float value, float invFactor, ref bool hasOutliers)
    {
        float svalue = value * invFactor;
        JxlPredictionResult pred = JxlModularPredict.PredictNoTree(w, row, top, topTop, x, y, JxlPredictor.Weighted, wpState);
        svalue -= pred.Guess;
        if (svalue > -QDeadzone && svalue < QDeadzone)
        {
            svalue = 0;
        }

        int residual = 0;
        if (float.IsNaN(svalue) || svalue > int.MaxValue || svalue < int.MinValue)
        {
            hasOutliers = true;
        }
        else
        {
            residual = (int)MathF.Round(svalue, MidpointRounding.AwayFromZero);
        }

        if (residual > 2 || residual < -2)
        {
            residual = (int)(MathF.Round(svalue * 0.5f, MidpointRounding.AwayFromZero) * 2);
        }

        return (int)(residual + pred.Guess);
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
            if (stream.Id != 0)
            {
                this.streamOptions[stream.Id] = this.streamOptions[0].Clone();
            }

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
        JxlModularImage gi = this.streamImages[streamId];
        if (streamId > 0)
        {
            gi = JxlModularImage.Create(this.allocator, (int)rect.XSize, (int)rect.YSize, fullImage.BitDepth, 0);
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

            // Do some per-group transforms

            // Local palette transforms
            // TODO(veluca): make this work with quantize-after-prediction in lossy mode.
            if (!LossyPalette && this.speedTier < JxlSpeedTier.Cheetah)
            {
                // don't care about that here
                int maxBitdepth = 0;
                int maxval = 0;
                this.TryPalettes(gi, ref maxBitdepth, ref maxval, ChannelColorsPercent);
            }
        }

        // lossless and no specific color transform specified: try Nothing, YCoCg, and 17 RCTs
        if (this.colorspace < 0 && gi.Channel.Count - gi.NbMetaChannels >= 3 && this.doColor && this.speedTier <= JxlSpeedTier.Hare)
        {
            TryRcts(gi, NbRctsToTry(this.speedTier));
        }

        // libjxl searches the weighted predictor modes here from Kitten down.
    }

    // The tail of libjxl's PrepareStreamParams RCT step: estimate the cost of the image with each of
    // the first nbRctsToTry candidates on its first three non-meta channels, and apply the cheapest.
    private static void TryRcts(JxlModularImage gi, int nbRctsToTry)
    {
        float bestCost = float.MaxValue;
        int bestRct = 0;
        bool needToRestore = nbRctsToTry > 1;
        JxlModularChannel[] orig = new JxlModularChannel[3];
        JxlModularChannel[] output = new JxlModularChannel[3];
        for (int c = 0; c < 3; ++c)
        {
            output[c] = gi.Channel[gi.NbMetaChannels + c];
        }

        try
        {
            foreach (int rctType in RctCandidates)
            {
                if (nbRctsToTry == 0)
                {
                    break;
                }

                nbRctsToTry--;

                // no-op rct_type; use as baseline cost
                if (rctType == 0)
                {
                    bestCost = JxlModularCostEstimate.EstimateCost(gi);
                    for (int c = 0; c < 3; ++c)
                    {
                        JxlModularChannel genuine = output[c];
                        orig[c] = JxlModularChannel.Create(genuine.Allocator, genuine.W, genuine.H, genuine.HShift, genuine.VShift);
                        genuine.SwapPlane(orig[c]);
                    }
                }
                else
                {
                    JxlRctEncoder.FwdRct(orig, output, rctType);
                    float cost = JxlModularCostEstimate.EstimateCost(gi);
                    if (cost < bestCost)
                    {
                        bestRct = rctType;
                        bestCost = cost;
                    }
                }
            }

            if (needToRestore)
            {
                for (int c = 0; c < 3; ++c)
                {
                    output[c].SwapPlane(orig[c]);
                }
            }
        }
        finally
        {
            foreach (JxlModularChannel channel in orig)
            {
                channel?.Dispose();
            }
        }

        // Apply the best RCT to the image for future encoding.
        if (bestRct != 0)
        {
            DoTransform(gi, new JxlTransform { Id = JxlTransformId.Rct, BeginC = (uint)gi.NbMetaChannels, RctType = (uint)bestRct });
        }
    }

    // How many of RctCandidates libjxl's per-group RCT search tries at each speed tier. The tiers from
    // Cheetah up just do the global YCoCg.
    private static int NbRctsToTry(JxlSpeedTier speedTier) => speedTier switch
    {
        JxlSpeedTier.Hare => 4,
        JxlSpeedTier.Wombat => 5,
        JxlSpeedTier.Squirrel => 7,
        JxlSpeedTier.Kitten => 9,
        JxlSpeedTier.TectonicPlate or JxlSpeedTier.Glacier or JxlSpeedTier.Tortoise => 19,
        _ => 0,
    };

    // The tree-learning parameters of each speed tier, from libjxl's Init for a lossless, non-responsive
    // frame: the properties tree learning may split on, how many values each is quantised to, and the
    // fraction of pixels sampled. Effort 3 and below (Falcon and faster) use a fixed tree, so their
    // row (the default) is not read; it is kept as libjxl has it.
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

    // Merges the trees in `trees` using nodes that decide on stream_id, as defined by `treeSplits`.
    private static void MergeTrees(List<JxlPropertyDecisionNode>[] trees, List<long> treeSplits, int begin, int end, List<JxlPropertyDecisionNode> tree)
    {
        DebugGuard.IsTrue(trees.Length + 1 == treeSplits.Count, "One split more than trees");
        DebugGuard.IsTrue(end > begin && end <= trees.Length, "A range of trees");
        if (end == begin + 1)
        {
            // Insert the tree, adding the opportune offset to all child nodes. This will make the leaf
            // IDs wrong, but subsequent roundtripping will fix them.
            uint sz = (uint)tree.Count;
            foreach (JxlPropertyDecisionNode node in trees[begin])
            {
                JxlPropertyDecisionNode moved = node;
                moved.LChild += sz;
                moved.RChild += sz;
                tree.Add(moved);
            }

            return;
        }

        int mid = (begin + end) / 2;
        int splitval = (int)(treeSplits[mid] - 1);
        int cur = tree.Count;

        // Property 1 is the stream id.
        tree.Add(new JxlPropertyDecisionNode(1, splitval, 0, 0, JxlPredictor.Zero, 0, 1));
        JxlPropertyDecisionNode split = tree[cur];
        split.LChild = (uint)tree.Count;
        tree[cur] = split;
        MergeTrees(trees, treeSplits, mid, end, tree);
        split = tree[cur];
        split.RChild = (uint)tree.Count;
        tree[cur] = split;
        MergeTrees(trees, treeSplits, begin, mid, tree);
    }

    private static void ReplaceChannel(JxlModularImage image, int index, JxlModularChannel channel)
    {
        image.Channel[index].Dispose();
        image.Channel[index] = channel;
    }

    // libjxl's default predictor for a non-responsive frame with no lossy palette: for a lossless Modular
    // frame, every predictor in the slowest modes, Gradient and Weighted below Falcon, Weighted at Falcon
    // and Gradient above it; for a VarDCT frame (which is not libjxl's IsLossless), Gradient.
    private static JxlPredictor DefaultPredictor(JxlSpeedTier speedTier, bool modularMode)
    {
        if (!modularMode)
        {
            // If not responsive and lossy. TODO(veluca): use near_lossless instead?
            return JxlPredictor.Gradient;
        }

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

    // libjxl's maybe_do_transform. From Squirrel up, which includes every effort JxlEffort has, a
    // transform that applies is kept. Below Squirrel (libjxl effort 8 and up) libjxl estimates the cost
    // after the transform and, when the cost went up, undoes it with Transform::Inverse
    // (JxlTransform.Inverse here) and drops it from the list. That estimate (JxlModularCostEstimate) is
    // ported, but undoing a transform by it is not, because no effort reaches it.
    private bool MaybeDoTransform(JxlModularImage image, JxlTransform tr)
    {
        if (this.speedTier >= JxlSpeedTier.Squirrel)
        {
            return DoTransform(image, tr);
        }

        JxlThrowHelper.ThrowNotSupportedException("Keeping or undoing a transform by its estimated cost (libjxl effort 8 and up)");
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
            // when not estimating, assume some arbitrary bpp
            if (this.speedTier <= JxlSpeedTier.Squirrel)
            {
                costBefore = JxlModularCostEstimate.EstimateCost(gi);
            }
            else
            {
                costBefore = nbPixels * arbitraryBppEstimate;
            }

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
