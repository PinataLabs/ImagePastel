// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2: what the EPF0Stage,
// EPF1Stage and EPF2Stage of lib/jxl/render_pipeline/stage_epf.cc share (Weight, the SAD multipliers,
// the sigma test, the weighted sum and its normalisation, and GetChannelMode).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.RenderPipeline;

/// <summary>
/// One pass of the edge-preserving filter: a weighted average of the pixels around each pixel, whose
/// weights fall with the difference between the patches around them (their sum of absolute differences,
/// SAD) and rise with the block's sigma. It mixes the three colour channels.
/// </summary>
/// <remarks>
/// libjxl's Highway code uses <c>MulAdd</c>, an FMA on AVX2, which ADR 0004 forbids here: each one is a
/// multiplication and then an addition. The reciprocal of the weight sum is a division, as in libjxl's
/// default build (<c>JXL_HIGH_PRECISION</c> is 1), not <c>ApproximateReciprocal</c>. As libjxl, a pass
/// filters a vector of pixels at a time, all of one block, where <see cref="Vector{T}"/> is accelerated and
/// holds a half or a whole block (#315). Each lane does the scalar code's operations in its order, so the
/// two give the same bits, and the scalar code filters the rest of the row.
/// </remarks>
internal abstract class JxlEpfStage : IJxlNeighbourStage
{
    /// <summary>The side of a block, libjxl's <c>kBlockDim</c>.</summary>
    protected const int BlockDim = 8;

    private readonly float sadMul;
    private readonly float borderSadMul;

    // libjxl's sad_mul_center, as vectors: the multiplier of each lane of a block's vectors, on a row
    // that is not a block's edge row. A block's first and last columns get the border multiplier.
    private readonly Vector<float>[] sadMulCenter;

    /// <summary>Initializes a new instance of the <see cref="JxlEpfStage"/> class.</summary>
    /// <param name="lf">The loop-filter fields.</param>
    /// <param name="sigma">The sigma image.</param>
    /// <param name="border">The pass's border.</param>
    /// <param name="sadMul">The pass's SAD multiplier inside a block, libjxl's <c>sm</c>.</param>
    protected JxlEpfStage(JxlLoopFilter lf, JxlEpfSigma sigma, int border, float sadMul)
    {
        this.LoopFilter = lf;
        this.Sigma = sigma;
        this.Border = border;
        this.sadMul = sadMul;
        this.borderSadMul = sadMul * lf.EpfBorderSadMul;

        if (IsVectorized)
        {
            float[] center = new float[BlockDim];
            Array.Fill(center, sadMul);
            center[0] = this.borderSadMul;
            center[BlockDim - 1] = this.borderSadMul;
            this.sadMulCenter = new Vector<float>[BlockDim / Vector<float>.Count];
            for (int i = 0; i < this.sadMulCenter.Length; i++)
            {
                this.sadMulCenter[i] = new Vector<float>(center, i * Vector<float>.Count);
            }
        }
    }

    /// <inheritdoc/>
    public int Border { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the pass filters every pixel with the scalar code, for the
    /// tests that compare it with the vector code.
    /// </summary>
    internal bool ScalarOnly { get; set; }

    /// <summary>
    /// Gets a value indicating whether the passes filter vectors of pixels: <see cref="Vector{T}"/> is
    /// accelerated, and a block is a whole number of vectors, so that the pixels of a vector share a sigma.
    /// </summary>
    private static bool IsVectorized => Vector.IsHardwareAccelerated && BlockDim % Vector<float>.Count == 0;

    /// <summary>Gets the loop-filter fields.</summary>
    protected JxlLoopFilter LoopFilter { get; }

    /// <summary>Gets the sigma image.</summary>
    protected JxlEpfSigma Sigma { get; }

    /// <inheritdoc/>
    public bool ChangesChannel(int c) => c < 3;

    /// <inheritdoc/>
    public void ProcessRow(JxlNeighbourRows input, Buffer2D<float>[] output, int xsize, int y)
    {
        // Where pixel 0 of each row the pass reads lies in the window: channel c, row dy at
        // c * (2 * Border + 1) + Border + dy.
        int rowsPerChannel = (2 * this.Border) + 1;
        Span<int> pixel0 = stackalloc int[3 * rowsPerChannel];
        for (int c = 0; c < 3; c++)
        {
            for (int dy = -this.Border; dy <= this.Border; dy++)
            {
                pixel0[(c * rowsPerChannel) + this.Border + dy] = input.GetRowStart(c, dy) + this.Border;
            }
        }

        Rows rows = new(input.Window, pixel0, this.Border);
        ReadOnlySpan<float> rowSigma = this.Sigma.GetRow(y);

        // The pixels on a block's edge rows and columns get the border multiplier.
        bool borderRow = y % BlockDim == 0 || y % BlockDim == BlockDim - 1;
        Span<float> outX = output[0].DangerousGetRowSpan(y);
        Span<float> outY = output[1].DangerousGetRowSpan(y);
        Span<float> outB = output[2].DangerousGetRowSpan(y);
        int x = 0;
        if (IsVectorized && !this.ScalarOnly)
        {
            x = this.ProcessVectors(rows, rowSigma, borderRow, outX, outY, outB, xsize);
        }

        for (; x < xsize; x++)
        {
            float blockSigma = rowSigma[x / BlockDim];
            if (blockSigma < JxlEpfSigma.MinSigma)
            {
                outX[x] = rows[0, 0, x];
                outY[x] = rows[1, 0, x];
                outB[x] = rows[2, 0, x];
                continue;
            }

            int ix = x % BlockDim;
            float vsm = borderRow || ix == 0 || ix == BlockDim - 1 ? this.borderSadMul : this.sadMul;
            float invSigma = blockSigma * vsm;
            this.FilterPixel(rows, x, invSigma, out outX[x], out outY[x], out outB[x]);
        }
    }

    // The whole vectors of the row, as libjxl's loop. Returns the first pixel left for the scalar code.
    private int ProcessVectors(Rows rows, ReadOnlySpan<float> rowSigma, bool borderRow, Span<float> outX, Span<float> outY, Span<float> outB, int xsize)
    {
        int lanes = Vector<float>.Count;
        Vector<float> borderSadMul = new(this.borderSadMul);
        int x = 0;
        for (; x <= xsize - lanes; x += lanes)
        {
            float blockSigma = rowSigma[x / BlockDim];
            if (blockSigma < JxlEpfSigma.MinSigma)
            {
                Store(rows.Load(0, 0, x), outX, x);
                Store(rows.Load(1, 0, x), outY, x);
                Store(rows.Load(2, 0, x), outB, x);
                continue;
            }

            Vector<float> vsm = borderRow ? borderSadMul : this.sadMulCenter[(x % BlockDim) / lanes];
            Vector<float> invSigma = new Vector<float>(blockSigma) * vsm;
            this.FilterVector(rows, x, invSigma, out Vector<float> vx, out Vector<float> vy, out Vector<float> vb);
            Store(vx, outX, x);
            Store(vy, outY, x);
            Store(vb, outB, x);
        }

        return x;
    }

    /// <summary>Filters one pixel.</summary>
    /// <param name="rows">The rows around the pixel, of every channel.</param>
    /// <param name="x">The pixel's column.</param>
    /// <param name="invSigma">The inverse sigma of the pixel, with its SAD multiplier.</param>
    /// <param name="outX">The filtered X.</param>
    /// <param name="outY">The filtered Y.</param>
    /// <param name="outB">The filtered B.</param>
    protected abstract void FilterPixel(Rows rows, int x, float invSigma, out float outX, out float outY, out float outB);

    /// <summary>
    /// Filters the <see cref="Vector{T}.Count"/> pixels from <paramref name="x"/>, all of one block, as
    /// <see cref="FilterPixel"/> filters each of them.
    /// </summary>
    /// <param name="rows">The rows around the pixels, of every channel.</param>
    /// <param name="x">The first pixel's column.</param>
    /// <param name="invSigma">The inverse sigma of each pixel, with its SAD multiplier.</param>
    /// <param name="outX">The filtered X.</param>
    /// <param name="outY">The filtered Y.</param>
    /// <param name="outB">The filtered B.</param>
    protected abstract void FilterVector(Rows rows, int x, Vector<float> invSigma, out Vector<float> outX, out Vector<float> outY, out Vector<float> outB);

    /// <summary>libjxl's <c>Weight</c>: <c>max(0, sad * inv_sigma + 1)</c>, without the FMA.</summary>
    /// <param name="sad">The SAD of the two patches.</param>
    /// <param name="invSigma">The inverse sigma, which is negative.</param>
    /// <returns>The weight.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static float Weight(float sad, float invSigma)
    {
        float v = (sad * invSigma) + 1.0f;

        // ZeroIfNegative.
        return v < 0 ? 0 : v;
    }

    /// <summary>
    /// <see cref="Weight(float, float)"/> in each lane. The select keeps a NaN, as the scalar test does,
    /// where a maximum might not.
    /// </summary>
    /// <param name="sad">The SADs of the two patches.</param>
    /// <param name="invSigma">The inverse sigmas, which are negative.</param>
    /// <returns>The weights.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static Vector<float> Weight(Vector<float> sad, Vector<float> invSigma)
    {
        Vector<float> v = (sad * invSigma) + Vector<float>.One;
        return Vector.ConditionalSelect(Vector.LessThan(v, Vector<float>.Zero), Vector<float>.Zero, v);
    }

    // Stores a vector at pixel x of a row that holds it.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(Vector<float> value, Span<float> row, int x)
    {
        DebugGuard.MustBeLessThanOrEqualTo(x + Vector<float>.Count, row.Length, nameof(x));
        Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref MemoryMarshal.GetReference(row), x)), value);
    }

    /// <summary>
    /// The rows a pass reads around the row it writes, of the three colour channels, as libjxl's
    /// <c>GetInputRow</c> gives them: straight from the <see cref="JxlNeighbourRows"/> window.
    /// </summary>
    protected readonly ref struct Rows
    {
        private readonly ReadOnlySpan<float> window;
        private readonly ReadOnlySpan<int> pixel0;
        private readonly int rowsPerChannel;
        private readonly int border;

        /// <summary>Initializes a new instance of the <see cref="Rows"/> struct.</summary>
        /// <param name="window">The window of rows.</param>
        /// <param name="pixel0">Where pixel 0 of each row is in the window, by channel and then row.</param>
        /// <param name="border">The pass's border.</param>
        public Rows(ReadOnlySpan<float> window, ReadOnlySpan<int> pixel0, int border)
        {
            this.window = window;
            this.pixel0 = pixel0;
            this.rowsPerChannel = (2 * border) + 1;
            this.border = border;
        }

        /// <summary>Gets pixel <paramref name="x"/> of row <paramref name="dy"/>, from the row written, of channel <paramref name="c"/>.</summary>
        /// <param name="c">The channel.</param>
        /// <param name="dy">The row offset, from <c>-Border</c> to <c>Border</c>.</param>
        /// <param name="x">The column, from <c>-Border</c> to <c>xsize + Border - 1</c>.</param>
        /// <returns>The pixel.</returns>
        public float this[int c, int dy, int x]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.window[this.pixel0[(c * this.rowsPerChannel) + this.border + dy] + x];
        }

        /// <summary>
        /// Loads the <see cref="Vector{T}.Count"/> pixels from <paramref name="x"/> of row <paramref name="dy"/>
        /// of channel <paramref name="c"/>: <c>this[c, dy, x + i]</c> in lane <c>i</c>.
        /// </summary>
        /// <param name="c">The channel.</param>
        /// <param name="dy">The row offset, from <c>-Border</c> to <c>Border</c>.</param>
        /// <param name="x">The first column, from <c>-Border</c>, with the last at most <c>xsize + Border - 1</c>.</param>
        /// <returns>The pixels.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector<float> Load(int c, int dy, int x)
        {
            int index = this.pixel0[(c * this.rowsPerChannel) + this.border + dy] + x;
            DebugGuard.MustBeBetweenOrEqualTo(index, 0, this.window.Length - Vector<float>.Count, nameof(x));
            return Unsafe.ReadUnaligned<Vector<float>>(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref MemoryMarshal.GetReference(this.window), index)));
        }
    }

    /// <summary>
    /// The weighted sum of the pixels of one output pixel, which starts with the pixel itself at weight
    /// 1: libjxl's <c>w</c>, <c>X</c>, <c>Y</c> and <c>B</c>, and their normalisation by <c>inv_w</c>.
    /// </summary>
    protected struct WeightedSum
    {
        private float w;
        private float x;
        private float y;
        private float b;

        /// <summary>Initializes a new instance of the <see cref="WeightedSum"/> struct.</summary>
        /// <param name="x">The pixel's X.</param>
        /// <param name="y">The pixel's Y.</param>
        /// <param name="b">The pixel's B.</param>
        public WeightedSum(float x, float y, float b)
        {
            this.w = 1;
            this.x = x;
            this.y = y;
            this.b = b;
        }

        /// <summary>Adds a neighbouring pixel, as libjxl's <c>AddPixel</c> does, without the FMA.</summary>
        /// <param name="weight">The pixel's weight.</param>
        /// <param name="cx">Its X.</param>
        /// <param name="cy">Its Y.</param>
        /// <param name="cb">Its B.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(float weight, float cx, float cy, float cb)
        {
            this.w += weight;
            this.x = (weight * cx) + this.x;
            this.y = (weight * cy) + this.y;
            this.b = (weight * cb) + this.b;
        }

        /// <summary>Divides the sums by the sum of the weights.</summary>
        /// <param name="outX">The filtered X.</param>
        /// <param name="outY">The filtered Y.</param>
        /// <param name="outB">The filtered B.</param>
        public readonly void Normalize(out float outX, out float outY, out float outB)
        {
            float invW = 1.0f / this.w;
            outX = this.x * invW;
            outY = this.y * invW;
            outB = this.b * invW;
        }
    }

    /// <summary><see cref="WeightedSum"/> for a vector of pixels, with the same operations in each lane.</summary>
    protected struct VectorWeightedSum
    {
        private Vector<float> w;
        private Vector<float> x;
        private Vector<float> y;
        private Vector<float> b;

        /// <summary>Initializes a new instance of the <see cref="VectorWeightedSum"/> struct.</summary>
        /// <param name="x">The pixels' X.</param>
        /// <param name="y">The pixels' Y.</param>
        /// <param name="b">The pixels' B.</param>
        public VectorWeightedSum(Vector<float> x, Vector<float> y, Vector<float> b)
        {
            this.w = Vector<float>.One;
            this.x = x;
            this.y = y;
            this.b = b;
        }

        /// <summary>Adds a neighbouring pixel to each sum, as <see cref="WeightedSum.Add"/> does.</summary>
        /// <param name="weight">The pixels' weights.</param>
        /// <param name="cx">Their X.</param>
        /// <param name="cy">Their Y.</param>
        /// <param name="cb">Their B.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(Vector<float> weight, Vector<float> cx, Vector<float> cy, Vector<float> cb)
        {
            this.w += weight;
            this.x = (weight * cx) + this.x;
            this.y = (weight * cy) + this.y;
            this.b = (weight * cb) + this.b;
        }

        /// <summary>Divides the sums by the sums of the weights, as <see cref="WeightedSum.Normalize"/> does.</summary>
        /// <param name="outX">The filtered X.</param>
        /// <param name="outY">The filtered Y.</param>
        /// <param name="outB">The filtered B.</param>
        public readonly void Normalize(out Vector<float> outX, out Vector<float> outY, out Vector<float> outB)
        {
            Vector<float> invW = Vector<float>.One / this.w;
            outX = this.x * invW;
            outY = this.y * invW;
            outB = this.b * invW;
        }
    }
}
