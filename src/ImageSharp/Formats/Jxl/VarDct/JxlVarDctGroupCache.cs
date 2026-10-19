// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// GroupDecCache in lib/jxl/dec_cache.h and GroupDecCache::InitOnce in lib/jxl/dec_cache.cc.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using SixLabors.ImageSharp.Formats.Jxl.Base;
using SixLabors.ImageSharp.Formats.Jxl.Entropy;
using SixLabors.ImageSharp.Formats.Jxl.Frame;

namespace SixLabors.ImageSharp.Formats.Jxl.VarDct;

/// <summary>
/// The scratch space of one AC group: made once per frame and reused by every group, as libjxl's
/// <c>GroupDecCache</c>, which it keeps per thread.
/// </summary>
/// <remarks>
/// As in libjxl, the non-zero counts are not cleared between groups: a group reads only the counts it
/// has written. A departure: libjxl sizes them for 32x32 blocks; here they are sized for the frame's
/// group. The blocks are sized for DCT8 alone, the only strategy until D9, where libjxl sizes them for
/// the largest strategy used.
/// </remarks>
internal sealed class JxlVarDctGroupCache
{
    private const int DctBlockSize = JxlFrameDimensions.BlockDim * JxlFrameDimensions.BlockDim;

    /// <summary>Initializes a new instance of the <see cref="JxlVarDctGroupCache"/> class.</summary>
    /// <param name="frameDim">The frame dimensions.</param>
    /// <param name="numPasses">The number of passes.</param>
    public JxlVarDctGroupCache(JxlFrameDimensions frameDim, int numPasses)
    {
        this.NzerosStride = frameDim.GroupDimension >> 3;
        int nzerosSize = this.NzerosStride * this.NzerosStride;
        this.NumNzeroes = new int[numPasses][][];
        for (int pass = 0; pass < numPasses; pass++)
        {
            this.NumNzeroes[pass] = new[] { new int[nzerosSize], new int[nzerosSize], new int[nzerosSize] };
        }

        this.CtxOffset = new int[numPasses];
        this.Decoders = new JxlAnsSymbolReader[numPasses];
        this.Readers = new JxlBitReader[numPasses];
    }

    /// <summary>Gets the stride of the non-zero counts: the width of a group, in blocks.</summary>
    public int NzerosStride { get; }

    /// <summary>Gets the non-zero counts of each pass and channel, one per block of the group.</summary>
    public int[][][] NumNzeroes { get; }

    /// <summary>Gets the quantised coefficients of the current varblock, one block per channel.</summary>
    public int[][] QBlock { get; } = { new int[DctBlockSize], new int[DctBlockSize], new int[DctBlockSize] };

    /// <summary>Gets the dequantised coefficients of the current varblock, the three channels one after another.</summary>
    public float[] Block { get; } = new float[3 * DctBlockSize];

    /// <summary>Gets the offset of each pass's histogram set in its contexts.</summary>
    public int[] CtxOffset { get; }

    /// <summary>Gets the ANS decoder of each pass.</summary>
    public JxlAnsSymbolReader[] Decoders { get; }

    /// <summary>Gets the reader of each pass's section.</summary>
    public JxlBitReader[] Readers { get; }
}
