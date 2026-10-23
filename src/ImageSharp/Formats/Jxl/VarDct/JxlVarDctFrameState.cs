// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// the VarDCT parts of lib/jxl/passes_state.h and lib/jxl/passes_state.cc (PassesSharedState,
// InitializePassesSharedState), lib/jxl/dec_cache.h and lib/jxl/dec_cache.cc (PassesDecoderState: Init,
// InitForAC) and lib/jxl/dec_frame.cc (DecodeGlobalDCInfo, FinalizeDC, ProcessACGlobal), with kOrderEnc
// from lib/jxl/frame_header.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Fields;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.Memory;
using static SixLabors.ImageSharp.Formats.Jxl.Fields.JxlU32Distr;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The state of a VarDCT frame that its sections share: what the DC global section and the DC groups
/// decode, and what the AC global section adds for the AC groups.
/// </summary>
/// <remarks>
/// The DC, quant field, quantised-DC and EPF sharpness images have one entry per 8x8 block and come
/// from the configured allocator; the strategy image and the chroma-from-luma maps are plain arrays.
/// </remarks>
internal sealed class JxlVarDctFrameState : IDisposable
{
    /// <summary>The largest number of passes.</summary>
    public const int MaxNumPasses = JxlPasses.MaxNumPasses;

    // Shared by enc/dec. 5F and 13 are by far the most common for d1/2/4/8, 0 ensures low overhead for
    // small images.
    internal static readonly JxlU32Enc OrderEnc = new(Val(0x5F), Val(0x13), Val(0), Bits(JxlBlockCtxMap.NumOrders));

    private readonly MemoryAllocator allocator;

    /// <summary>Initializes a new instance of the <see cref="JxlVarDctFrameState"/> class.</summary>
    /// <param name="allocator">The allocator of the block images.</param>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="frameDim">The frame dimensions.</param>
    /// <param name="matrices">The frame's dequantization matrices.</param>
    /// <param name="dcFrame">
    /// For a frame that uses a DC frame, the DC slot it takes its DC from, at least
    /// <see cref="JxlFrameDimensions.XSizeBlocks"/> by <see cref="JxlFrameDimensions.YSizeBlocks"/>;
    /// otherwise <see langword="null"/>.
    /// </param>
    public JxlVarDctFrameState(MemoryAllocator allocator, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlDequantMatrices matrices, JxlReferenceFrame dcFrame = null)
        : this(allocator, frameHeader, frameDim, matrices, new JxlQuantizer(matrices), new JxlAcStrategyImage((int)frameDim.XSizeBlocks, (int)frameDim.YSizeBlocks), new JxlColorCorrelation(frameDim.XSize, frameDim.YSize), new JxlBlockCtxMap(), dcFrame)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlVarDctFrameState"/> class for the encoder's
    /// reconstruction of its own frame (<see cref="JxlEncReconstruct"/>), as libjxl points its decoder state
    /// at the encoder's shared state: the quantiser, the strategies, the chroma-from-luma maps and the block
    /// context map are the encoder's own. The block images are new and clear; the caller fills them.
    /// </summary>
    /// <param name="allocator">The allocator of the block images.</param>
    /// <param name="frameHeader">The frame header.</param>
    /// <param name="frameDim">The frame dimensions.</param>
    /// <param name="matrices">The dequantization matrices.</param>
    /// <param name="quantizer">The quantiser.</param>
    /// <param name="acStrategy">The strategy of each block.</param>
    /// <param name="cmap">The chroma-from-luma parameters and maps.</param>
    /// <param name="blockCtxMap">The block context map.</param>
    public JxlVarDctFrameState(MemoryAllocator allocator, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlDequantMatrices matrices, JxlQuantizer quantizer, JxlAcStrategyImage acStrategy, JxlColorCorrelation cmap, JxlBlockCtxMap blockCtxMap)
        : this(allocator, frameHeader, frameDim, matrices, quantizer, acStrategy, cmap, blockCtxMap, null)
    {
    }

    private JxlVarDctFrameState(MemoryAllocator allocator, JxlFrameHeader frameHeader, JxlFrameDimensions frameDim, JxlDequantMatrices matrices, JxlQuantizer quantizer, JxlAcStrategyImage acStrategy, JxlColorCorrelation cmap, JxlBlockCtxMap blockCtxMap, JxlReferenceFrame dcFrame)
    {
        this.allocator = allocator;
        this.FrameDim = frameDim;
        this.XSizeBlocks = (int)frameDim.XSizeBlocks;
        this.YSizeBlocks = (int)frameDim.YSizeBlocks;
        this.AcStrategy = acStrategy;
        this.Cmap = cmap;
        this.BlockCtxMap = blockCtxMap;
        try
        {
            this.RawQuantField = allocator.Allocate2D<int>(this.XSizeBlocks, this.YSizeBlocks, AllocationOptions.Clean);
            this.EpfSharpness = allocator.Allocate2D<byte>(this.XSizeBlocks, this.YSizeBlocks, AllocationOptions.Clean);

            // InitializePassesSharedState: a frame that uses a DC frame has a quantised DC of zeros (the
            // buckets of its block contexts), and its DC is the DC frame's image. libjxl points at the slot;
            // here the DC is a copy of it, as nothing in the frame changes it (FinalizeDc skips the smoothing,
            // and ProcessDcGroup the DC groups' DC).
            this.QuantDc = allocator.Allocate2D<byte>(this.XSizeBlocks, this.YSizeBlocks, AllocationOptions.Clean);
            for (int c = 0; c < 3; c++)
            {
                this.Dc[c] = allocator.Allocate2D<float>(this.XSizeBlocks, this.YSizeBlocks, AllocationOptions.Clean);
                if (dcFrame != null)
                {
                    DebugGuard.IsTrue(dcFrame.Width >= this.XSizeBlocks && dcFrame.Height >= this.YSizeBlocks, "The frame decoder checks the DC frame's size");
                    JxlPlanes.CopyRows(dcFrame.Planes[c], this.Dc[c], this.XSizeBlocks, this.YSizeBlocks);
                }
            }
        }
        catch
        {
            // The constructor throws, so no caller can dispose what is already allocated.
            this.Dispose();
            throw;
        }

        this.Matrices = matrices;
        this.Quantizer = quantizer;
        this.XDmMultiplier = MathF.Pow(1 / 1.25f, frameHeader.XQmScale - 2.0f);
        this.BDmMultiplier = MathF.Pow(1 / 1.25f, frameHeader.BQmScale - 2.0f);
        this.QuantBiases = frameHeader.NonserializedMetadata.TransformData.OpsinInverseMatrix.QuantBiases;
    }

    /// <summary>Gets the frame dimensions.</summary>
    public JxlFrameDimensions FrameDim { get; }

    /// <summary>Gets the width of the frame, in blocks, which is the stride of the block images.</summary>
    public int XSizeBlocks { get; }

    /// <summary>Gets the height of the frame, in blocks.</summary>
    public int YSizeBlocks { get; }

    /// <summary>Gets the strategy of each block.</summary>
    public JxlAcStrategyImage AcStrategy { get; }

    /// <summary>Gets the quant field of each block.</summary>
    public Buffer2D<int> RawQuantField { get; }

    /// <summary>Gets the EPF sharpness of each block, which only EPF (D9) uses.</summary>
    public Buffer2D<byte> EpfSharpness { get; }

    /// <summary>Gets the chroma-from-luma parameters and maps.</summary>
    public JxlColorCorrelation Cmap { get; }

    /// <summary>Gets the bucket of the quantised DC of each block.</summary>
    public Buffer2D<byte> QuantDc { get; }

    /// <summary>Gets the X, Y and B DC of each block. Smoothing replaces the planes.</summary>
    public Buffer2D<float>[] Dc { get; } = new Buffer2D<float>[3];

    /// <summary>Gets the dequantization matrices.</summary>
    public JxlDequantMatrices Matrices { get; }

    /// <summary>Gets the quantiser.</summary>
    public JxlQuantizer Quantizer { get; }

    /// <summary>Gets the block context map.</summary>
    public JxlBlockCtxMap BlockCtxMap { get; }

    /// <summary>Gets the multiplier of the X channel's dequantization matrices.</summary>
    public float XDmMultiplier { get; }

    /// <summary>Gets the multiplier of the B channel's dequantization matrices.</summary>
    public float BDmMultiplier { get; }

    /// <summary>Gets the quantisation biases (<see cref="JxlQuantizer.AdjustQuantBias"/>).</summary>
    public float[] QuantBiases { get; }

    /// <summary>Gets or sets the strategies used, one bit per raw strategy.</summary>
    public uint UsedAcs { get; set; }

    /// <summary>Gets the number of sets of histograms per pass.</summary>
    public int NumHistograms { get; private set; }

    /// <summary>Gets the number of coefficients of one pass's set of orders.</summary>
    public int CoeffOrderSize { get; private set; }

    /// <summary>Gets the coefficient orders of every pass.</summary>
    public uint[] CoeffOrders { get; private set; } = Array.Empty<uint>();

    /// <summary>Gets the histograms of each pass.</summary>
    public JxlAnsCode[] Code { get; } = new JxlAnsCode[MaxNumPasses];

    /// <summary>Gets the context map of each pass, padded for <see cref="JxlBlockCtxMap.ZeroDensityContext"/>.</summary>
    public byte[][] ContextMap { get; } = new byte[MaxNumPasses][];

    /// <summary>Gets the scratch space the AC groups share, which <see cref="InitForAc"/> makes.</summary>
    public JxlVarDctGroupCache GroupCache { get; private set; }

    /// <summary>
    /// Reads the VarDCT part of the DC global section: the quantiser, the block context map and the
    /// chroma-from-luma DC.
    /// </summary>
    /// <param name="reader">The reader.</param>
    public void DecodeGlobalDcInfo(JxlBitReader reader)
    {
        this.Quantizer.Decode(reader);
        this.BlockCtxMap.Decode(reader);
        this.Cmap.DecodeDc(reader);

        // libjxl clears the DC multipliers here when it reconstructs a JPEG, which this port does not do.
        this.AcStrategy.FillInvalid();
    }

    /// <summary>
    /// Smooths the DC unless the frame skips it. It must run after every DC group and before any AC
    /// group.
    /// </summary>
    /// <param name="frameHeader">The frame header.</param>
    public void FinalizeDc(JxlFrameHeader frameHeader)
    {
        if ((frameHeader.Flags & JxlFrameFlags.SkipAdaptiveDcSmoothing) == 0 && !frameHeader.UsesDcFrame)
        {
            JxlCompressedDc.AdaptiveDcSmoothing(this.allocator, this.Quantizer.MulDc, this.Dc, this.XSizeBlocks, this.YSizeBlocks);
        }
    }

    /// <summary>
    /// Sizes the coefficient orders for the strategies the DC groups used, and makes the scratch space
    /// of the AC groups.
    /// </summary>
    /// <param name="numPasses">The number of passes.</param>
    public void InitForAc(int numPasses)
    {
        this.GroupCache = new JxlVarDctGroupCache(this.FrameDim, numPasses, this.UsedAcs);
        this.CoeffOrderSize = JxlCoeffOrder.OrderSize(this.UsedAcs);
        int sz = numPasses * this.CoeffOrderSize;
        if (sz > this.CoeffOrders.Length)
        {
            this.CoeffOrders = new uint[sz];
        }
    }

    /// <summary>
    /// Reads the AC global section: the dequantization matrices, the number of histogram sets, and the
    /// coefficient orders and histograms of each pass.
    /// </summary>
    /// <param name="br">The reader.</param>
    /// <param name="numPasses">The number of passes.</param>
    /// <param name="modularFrameDecoder">The frame's Modular decoder, for the raw quantization tables.</param>
    public void ProcessAcGlobal(JxlBitReader br, int numPasses, JxlModularFrameDecoder modularFrameDecoder)
    {
        this.Matrices.Decode(br, modularFrameDecoder);
        this.Matrices.EnsureComputed(this.UsedAcs);

        int numHistoBits = JxlBits.CeilLog2Nonzero((uint)this.FrameDim.NumGroups);
        this.NumHistograms = 1 + (int)br.ReadBits(numHistoBits);

        // Read coefficient orders and histograms.
        for (int i = 0; i < numPasses; i++)
        {
            uint usedOrders = JxlFieldCoders.ReadU32(OrderEnc, br);
            JxlCoeffOrder.DecodeCoeffOrders(usedOrders, this.UsedAcs, this.CoeffOrders.AsSpan(i * this.CoeffOrderSize), br);
            int numContexts = this.NumHistograms * this.BlockCtxMap.NumAcContexts;
            JxlAnsCode code = new();
            byte[] contextMap = JxlEntropyDecoder.DecodeHistograms(br, numContexts, code);

            // Add extra values to enable the cheat in hot loop of DecodeACVarBlock.
            byte[] padded = new byte[numContexts + JxlBlockCtxMap.ZeroDensityContextLimit - JxlBlockCtxMap.ZeroDensityContextCount];
            contextMap.AsSpan(0, Math.Min(contextMap.Length, padded.Length)).CopyTo(padded);
            this.Code[i] = code;
            this.ContextMap[i] = padded;
        }

        // libjxl picks 16- or 32-bit coefficients here from the largest token width (max_num_bits) plus
        // CeilLog2(num_passes). This port always keeps them as int, a deliberate departure. The values are
        // the same unless a pass's shift (up to 3 bits, which max_num_bits does not count) pushes the sum
        // of a coefficient's passes past 16 bits, where libjxl's int16 coefficients wrap. The cjxl files of
        // JxlProgressivePassesTests, with shifts of 1, match djxl.
    }

    public void Dispose()
    {
        this.RawQuantField?.Dispose();
        this.EpfSharpness?.Dispose();
        this.QuantDc?.Dispose();
        JxlPlanes.DisposeAll(this.Dc);
    }
}
