// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_modular.h and lib/jxl/enc_modular.cc (ModularFrameEncoder: Init, ComputeEncodingData,
// PrepareStreamParams, ComputeTree, ComputeTokens, EncodeGlobalInfo, EncodeStream).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Formats.Jxl.Modular;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jxl.Frame;

/// <summary>
/// Encodes the Modular streams of a frame: the global stream and one per LF group and per group,
/// with one global MA tree and one set of histograms that every stream shares. Only the path libjxl's
/// effort 3 lossless takes is ported: the predefined <c>kWPFixedDC</c> tree with the Weighted
/// predictor, and no tree learning, squeeze, quantisation, local palettes or per-group RCT search.
/// It is the mirror of <see cref="JxlModularFrameDecoder"/>.
/// </summary>
internal sealed class JxlModularFrameEncoder : IDisposable
{
    // The level-10 limit. libjxl uses 16 for level 5, but cjxl leaves the level unset (-1) when it
    // queues the frame, so the frame encoder always sees this.
    private const int LevelMaxBitdepth = 32;

    private readonly MemoryAllocator allocator;
    private readonly JxlFrameDimensions frameDim;
    private readonly JxlSpeedTier speedTier = JxlSpeedTier.Falcon;
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
    /// <c>ModularFrameEncoder::Init</c> does for lossless effort 3: no responsive mode, the Weighted
    /// predictor, the <c>kWPFixedDC</c> tree and the Modular histogram parameters.
    /// </summary>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="allocator">The allocator.</param>
    public JxlModularFrameEncoder(JxlFrameHeader frameHeader, MemoryAllocator allocator)
    {
        this.allocator = allocator;
        this.frameDim = frameHeader.ToFrameDimensions();

        long numStreams = JxlModularStreamId.ModularAc(this.frameDim, 0, frameHeader.Passes.NumPasses);
        this.streamImages = new JxlModularImage[numStreams];
        for (long i = 0; i < numStreams; ++i)
        {
            this.streamImages[i] = JxlModularImage.Create(allocator, 0, 0, 8, 0);
        }

        // libjxl's lossless decoding_speed_tier 0 only sets fast_decode_multiplier, which tree
        // learning uses; responsive mode stays off for lossless. Effort 3 sets the Weighted predictor
        // and the kWPFixedDC tree. The options of every stream copy those of stream 0.
        JxlModularOptions options = new()
        {
            MaxChanSize = this.frameDim.GroupDimension,
            GroupDim = this.frameDim.GroupDimension,
        };
        this.streamOptions = new JxlModularOptions[numStreams];
        Array.Fill(this.streamOptions, options);
        this.histogramParams = JxlHistogramParams.ForModular(this.speedTier, modularMode: true, JxlPredictor.Weighted);
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

        // bits_per_sample is just metadata for XYB images.
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

        JxlEncoderInput<TPixel>.CopyColorChannels(image, gi);

        // libjxl copies the extra channels here, after the colour channels; slice E1d adds alpha.
        if (maxBitdepth > LevelMaxBitdepth)
        {
            JxlThrowHelper.ThrowImageFormatException($"Bitdepth too high for level {10} (need {maxBitdepth} bits, have only {LevelMaxBitdepth} in this codestream level)");
        }

        // libjxl tries the global palette, the channel palettes and YCoCg here (enc_modular.cc:889-914),
        // which slice E1e adds. When a transform leaves no spare bit, libjxl turns off the per-group
        // RCT search, which only the higher efforts run.
        this.FillStreamParams();
    }

    /// <summary>
    /// Builds the global tree, libjxl's predefined <c>kWPFixedDC</c> one, and turns it into tokens.
    /// The tree is then kept as the decoder will read it, so its leaf ids are the token contexts.
    /// </summary>
    public void ComputeTree()
    {
        // libjxl learns a tree here below effort 3, and builds trees for quantised channels; neither
        // is ported. Fixed tree.
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
        JxlHistogramParams parameters = JxlHistogramParams.ForModular(this.speedTier, modularMode: true, JxlPredictor.Weighted);
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
