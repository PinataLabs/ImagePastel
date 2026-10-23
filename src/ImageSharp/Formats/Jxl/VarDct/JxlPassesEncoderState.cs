// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_cache.h (PassesEncoderState) and the VarDCT parts of lib/jxl/passes_state.h
// (PassesSharedState), for one pass and no streaming.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Frame;
using SixLabors.ImageSharp.Formats.Jxl.RenderPipeline;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// What the stages of a lossy VarDCT frame share in the encoder: the parameters, the quantiser and its
/// tables, the per-block choices, and the quantised coefficients and their tokens.
/// </summary>
/// <remarks>
/// libjxl keeps the coefficients and tokens of each pass; the encoder writes one pass (no progressive
/// mode), so this keeps one. The per-block images are plain arrays, one entry per 8x8 block.
/// </remarks>
internal sealed class JxlPassesEncoderState : IDisposable
{
    /// <summary>Initializes a new instance of the <see cref="JxlPassesEncoderState"/> class.</summary>
    /// <param name="frameDim">The frame dimensions.</param>
    /// <param name="speedTier">The speed tier.</param>
    /// <param name="distance">The butteraugli distance, after libjxl's clamps.</param>
    /// <param name="allocator">The allocator of the encoder's copy of the DC.</param>
    public JxlPassesEncoderState(JxlFrameDimensions frameDim, JxlSpeedTier speedTier, float distance, MemoryAllocator allocator)
    {
        this.FrameDim = frameDim;
        this.SpeedTier = speedTier;
        this.Distance = distance;
        this.XSizeBlocks = (int)frameDim.XSizeBlocks;
        this.YSizeBlocks = (int)frameDim.YSizeBlocks;
        this.Quantizer = new JxlQuantizer(this.Matrices);
        this.Cmap = new JxlColorCorrelation(frameDim.XSize, frameDim.YSize);
        this.AcStrategy = new JxlAcStrategyImage(this.XSizeBlocks, this.YSizeBlocks);
        this.RawQuantField = new int[this.XSizeBlocks * this.YSizeBlocks];
        this.EpfSharpness = new byte[this.XSizeBlocks * this.YSizeBlocks];
        this.Coeffs = new int[frameDim.NumGroups][][];
        this.AcTokens = new List<JxlToken>[frameDim.NumGroups];
        try
        {
            this.QuantDc = allocator.Allocate2D<byte>(this.XSizeBlocks, this.YSizeBlocks, AllocationOptions.Clean);
            for (int c = 0; c < 3; c++)
            {
                this.Dc[c] = allocator.Allocate2D<float>(this.XSizeBlocks, this.YSizeBlocks, AllocationOptions.Clean);
            }
        }
        catch
        {
            // The constructor throws, so no caller can dispose what is already allocated.
            this.Dispose();
            throw;
        }
    }

    /// <summary>Gets the frame dimensions.</summary>
    public JxlFrameDimensions FrameDim { get; }

    /// <summary>Gets the speed tier, libjxl's <c>cparams.speed_tier</c>.</summary>
    public JxlSpeedTier SpeedTier { get; }

    /// <summary>
    /// Gets the butteraugli distance, libjxl's <c>cparams.butteraugli_distance</c>. It is also libjxl's
    /// <c>cparams.original_butteraugli_distance</c>, which differs only when libjxl resamples (not ported).
    /// </summary>
    public float Distance { get; }

    /// <summary>Gets the width of the frame in blocks, the stride of the block images.</summary>
    public int XSizeBlocks { get; }

    /// <summary>Gets the height of the frame in blocks.</summary>
    public int YSizeBlocks { get; }

    /// <summary>Gets the dequantization matrices; the encoder keeps the library defaults.</summary>
    public JxlDequantMatrices Matrices { get; } = new();

    /// <summary>Gets the quantiser.</summary>
    public JxlQuantizer Quantizer { get; }

    /// <summary>Gets the chroma-from-luma parameters and maps.</summary>
    public JxlColorCorrelation Cmap { get; }

    /// <summary>Gets the block context map.</summary>
    public JxlBlockCtxMap BlockCtxMap { get; } = new();

    /// <summary>Gets the strategy of each block.</summary>
    public JxlAcStrategyImage AcStrategy { get; }

    /// <summary>Gets the raw quant field of each block.</summary>
    public int[] RawQuantField { get; }

    /// <summary>
    /// Gets or sets the float quant field of each block before <see cref="RawQuantField"/> is set from it:
    /// libjxl's <c>initial_quant_field</c>, which the AC strategy search reads. The state owns it.
    /// </summary>
    public Buffer2D<float> InitialQuantField { get; set; }

    /// <summary>
    /// Gets or sets the masking image at full resolution (libjxl's <c>initial_quant_masking1x1</c>), which
    /// the AC strategy search reads; from Hare down only. The state owns it.
    /// </summary>
    public Buffer2D<float> InitialQuantMasking1x1 { get; set; }

    /// <summary>Gets the EPF sharpness of each block.</summary>
    public byte[] EpfSharpness { get; }

    /// <summary>
    /// Gets the X, Y and B DC of each block as the decoder will see it: libjxl's <c>shared.dc_storage</c>,
    /// dequantised from the quantised DC the file codes (<see cref="JxlModularFrameEncoder.AddVarDctDc"/>),
    /// then smoothed. Smoothing replaces the planes. Only the reconstruction of the frame reads it
    /// (<see cref="JxlEncReconstruct"/>).
    /// </summary>
    public Buffer2D<float>[] Dc { get; } = new Buffer2D<float>[3];

    /// <summary>
    /// Gets the bucket of the quantised DC of each block (libjxl's <c>shared.quant_dc</c>), which the block
    /// context map's DC thresholds read; the encoder has none at effort 7 and faster.
    /// </summary>
    public Buffer2D<byte> QuantDc { get; private set; }

    /// <summary>Gets or sets the multiplier of the X channel's quantisation weights.</summary>
    public float XQmMultiplier { get; set; }

    /// <summary>Gets or sets the multiplier of the B channel's quantisation weights.</summary>
    public float BQmMultiplier { get; set; }

    /// <summary>
    /// Gets the quantised coefficients of each group: for each channel, the blocks of the group in order,
    /// each block's coefficients in their natural layout.
    /// </summary>
    public int[][][] Coeffs { get; }

    /// <summary>Gets the AC tokens of each group.</summary>
    public List<JxlToken>[] AcTokens { get; }

    /// <summary>Gets the coefficient orders, laid out by <see cref="JxlCoeffOrder.Offset"/>.</summary>
    public uint[] CoeffOrders { get; } = new uint[JxlCoeffOrder.CoeffOrderMaxSize];

    /// <summary>Gets or sets the orders the AC global section codes, one bit per order bucket.</summary>
    public uint UsedOrders { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every coefficient order stays natural rather than customised (for
    /// tests; see <see cref="JxlEncoderTestHooks.NaturalCoeffOrders"/>).
    /// </summary>
    public bool NaturalCoeffOrders { get; set; }

    /// <summary>Gets or sets the strategies used, one bit per raw strategy.</summary>
    public uint UsedAcs { get; set; }

    /// <summary>Gets or sets the number of sets of AC histograms.</summary>
    public int NumHistograms { get; set; }

    /// <summary>Gets the AC histograms.</summary>
    public JxlEntropyEncodingData Codes { get; } = new();

    /// <summary>
    /// Returns the multiplier of channel <paramref name="c"/>'s quantisation weights: <see cref="XQmMultiplier"/>
    /// for X, 1 for Y and <see cref="BQmMultiplier"/> for B.
    /// </summary>
    /// <param name="c">The channel: 0 X, 1 Y, 2 B.</param>
    /// <returns>The multiplier.</returns>
    public float QmMultiplier(int c) => c == 0 ? this.XQmMultiplier : c == 1 ? 1.0f : this.BQmMultiplier;

    /// <summary>Returns the index of block (<paramref name="bx"/>, <paramref name="by"/>) in the block images.</summary>
    /// <param name="bx">The block column.</param>
    /// <param name="by">The block row.</param>
    /// <returns>The index.</returns>
    public int BlockIndex(long bx, long by) => (int)((by * this.XSizeBlocks) + bx);

    /// <inheritdoc/>
    public void Dispose()
    {
        this.InitialQuantField?.Dispose();
        this.InitialQuantField = null;
        this.InitialQuantMasking1x1?.Dispose();
        this.InitialQuantMasking1x1 = null;
        this.QuantDc?.Dispose();
        this.QuantDc = null;
        JxlPlanes.DisposeAll(this.Dc);
    }
}
