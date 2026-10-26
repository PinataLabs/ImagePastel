// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/dec_ans.h and lib/jxl/dec_ans.cc (ANSSymbolReader).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Runtime.CompilerServices;
using PinataLabs.ImagePastel.Formats.Jxl.Base;

namespace PinataLabs.ImagePastel.Formats.Jxl.Entropy;

/// <summary>
/// Reads hybrid-uint values from an entropy-coded stream, with ANS or prefix codes.
/// </summary>
/// <remarks>
/// libjxl picks the LZ77 path with a template flag, which C# generics cannot express without a
/// type per path. This reader checks for a window instead; streams without LZ77 pay one null check.
/// </remarks>
internal sealed class JxlAnsSymbolReader
{
    private const int WindowSize = 1 << 20;
    private const uint WindowMask = WindowSize - 1;
    private const int NumSpecialDistances = 120;

    private readonly JxlAliasEntry[] aliasTables;
    private readonly JxlHuffmanDecodingData[] huffmanData;
    private readonly bool usePrefixCode;
    private readonly JxlHybridUintConfig[] configs;
    private readonly int logAlphaSize;
    private readonly int logEntrySize;
    private readonly uint entrySizeMinus1;
    private uint state = JxlAnsParams.AnsSignature << 16;

    // LZ77 state. The window is null when the stream does not use LZ77.
    private readonly uint[] lz77Window;
    private readonly int lz77Ctx;
    private readonly uint lz77MinLength;
    private readonly uint lz77Threshold = 1 << 20; // bigger than any symbol.
    private readonly JxlHybridUintConfig lz77LengthUint;
    private readonly uint[] specialDistances;
    private readonly uint numSpecialDistances;
    private uint numDecoded;
    private uint numToCopy;
    private uint copyPos;

    /// <summary>
    /// Initializes a new instance of the <see cref="JxlAnsSymbolReader"/> class. For ANS, this reads
    /// the initial 32-bit state.
    /// </summary>
    /// <param name="code">The histograms.</param>
    /// <param name="br">The reader.</param>
    /// <param name="distanceMultiplier">
    /// The row length that LZ77 special distances count in, or 0 for a stream with no rows.
    /// </param>
    public JxlAnsSymbolReader(JxlAnsCode code, JxlBitReader br, int distanceMultiplier = 0)
    {
        this.aliasTables = code.AliasTables;
        this.huffmanData = code.HuffmanData;
        this.usePrefixCode = code.UsePrefixCode;
        this.configs = code.UintConfig;
        if (!this.usePrefixCode)
        {
            this.state = (uint)br.ReadBits(32);
            this.logAlphaSize = code.LogAlphaSize;
            this.logEntrySize = JxlAnsParams.AnsLogTabSize - code.LogAlphaSize;
            this.entrySizeMinus1 = (1u << this.logEntrySize) - 1;
        }

        if (!code.Lz77.Enabled)
        {
            return;
        }

        this.lz77Window = new uint[WindowSize];
        this.lz77Ctx = code.Lz77.NonserializedDistanceContext;
        this.lz77LengthUint = code.Lz77.LengthUintConfig;
        this.lz77Threshold = code.Lz77.MinSymbol;
        this.lz77MinLength = code.Lz77.MinLength;
        this.numSpecialDistances = distanceMultiplier == 0 ? 0u : NumSpecialDistances;
        this.specialDistances = new uint[NumSpecialDistances];
        for (int i = 0; i < this.numSpecialDistances; i++)
        {
            this.specialDistances[i] = (uint)SpecialDistance(i, distanceMultiplier);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the ANS state is back at its initial value, which a valid
    /// stream guarantees at its end. Always <see langword="true"/> for prefix codes.
    /// </summary>
    public bool CheckAnsFinalState => this.state == JxlAnsParams.AnsSignature << 16;

    // Table of special distance codes from WebP lossless, as pairs: the offset within a row and the
    // number of rows.
    private static ReadOnlySpan<sbyte> SpecialDistances => new sbyte[]
    {
        0, 1, 1, 0, 1, 1, -1, 1, 0, 2, 2, 0, 1, 2, -1, 2,
        2, 1, -2, 1, 2, 2, -2, 2, 0, 3, 3, 0, 1, 3, -1, 3,
        3, 1, -3, 1, 2, 3, -2, 3, 3, 2, -3, 2, 0, 4, 4, 0,
        1, 4, -1, 4, 4, 1, -4, 1, 3, 3, -3, 3, 2, 4, -2, 4,
        4, 2, -4, 2, 0, 5, 3, 4, -3, 4, 4, 3, -4, 3, 5, 0,
        1, 5, -1, 5, 5, 1, -5, 1, 2, 5, -2, 5, 5, 2, -5, 2,
        4, 4, -4, 4, 3, 5, -3, 5, 5, 3, -5, 3, 0, 6, 6, 0,
        1, 6, -1, 6, 6, 1, -6, 1, 2, 6, -2, 6, 6, 2, -6, 2,
        4, 5, -4, 5, 5, 4, -5, 4, 3, 6, -3, 6, 6, 3, -6, 3,
        0, 7, 7, 0, 1, 7, -1, 7, 5, 5, -5, 5, 7, 1, -7, 1,
        4, 6, -4, 6, 6, 4, -6, 4, 2, 7, -2, 7, 7, 2, -7, 2,
        3, 7, -3, 7, 7, 3, -7, 3, 5, 6, -5, 6, 6, 5, -6, 5,
        8, 0, 4, 7, -4, 7, 7, 4, -7, 4, 8, 1, 8, 2, 6, 6,
        -6, 6, 8, 3, 5, 7, -5, 7, 7, 5, -7, 5, 8, 4, 6, 7,
        -6, 7, 7, 6, -7, 6, 8, 5, 7, 7, -7, 7, 8, 6, 8, 7,
    };

    /// <summary>
    /// Reads the bits of a value after its token. Tokens below the split are the value itself.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="token">The token.</param>
    /// <param name="br">The reader, refilled by the caller.</param>
    /// <returns>The value.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public static uint ReadHybridUintConfig(in JxlHybridUintConfig config, uint token, JxlBitReader br)
    {
        uint splitToken = config.SplitToken;
        int msbInToken = (int)config.MsbInToken;
        int lsbInToken = (int)config.LsbInToken;
        uint splitExponent = config.SplitExponent;

        // Fast-track version of hybrid integer decoding.
        if (token < splitToken)
        {
            return token;
        }

        uint nbits = splitExponent - (uint)(msbInToken + lsbInToken) + ((token - splitToken) >> (msbInToken + lsbInToken));

        // A count above 31 is invalid; libjxl clamps it rather than rejecting it.
        nbits &= 31u;
        ulong low = token & ((1u << lsbInToken) - 1);
        ulong t = token >> lsbInToken;
        ulong bits = br.PeekBits((int)nbits);
        br.Consume((int)nbits);
        ulong ret = (((((1UL << msbInToken) | (t & ((1UL << msbInToken) - 1))) << (int)nbits) | bits) << lsbInToken) | low;
        return unchecked((uint)ret);
    }

    /// <summary>Reads one value with the histogram <paramref name="ctx"/> (a cluster, after the context map).</summary>
    /// <param name="ctx">The histogram index.</param>
    /// <param name="br">The reader.</param>
    /// <returns>The value.</returns>
    [MethodImpl(InliningOptions.ShortMethod)]
    public uint ReadHybridUintClustered(int ctx, JxlBitReader br)
    {
        if (this.lz77Window != null)
        {
            return this.ReadHybridUintClusteredLz77(ctx, br);
        }

        // Covers ReadSymbolWithoutRefill and PeekBits.
        br.Refill();
        uint token = this.ReadSymbolWithoutRefill(ctx, br);
        return ReadHybridUintConfig(in this.configs[ctx], token, br);
    }

    /// <summary>Reads one value for context <paramref name="ctx"/>, mapped to a histogram by <paramref name="contextMap"/>.</summary>
    /// <param name="ctx">The context.</param>
    /// <param name="br">The reader.</param>
    /// <param name="contextMap">The context map.</param>
    /// <returns>The value.</returns>
    public uint ReadHybridUint(int ctx, JxlBitReader br, byte[] contextMap)
        => this.ReadHybridUintClustered(contextMap[ctx], br);

    private static int SpecialDistance(int index, int multiplier)
    {
        int dist = SpecialDistances[2 * index] + (multiplier * SpecialDistances[(2 * index) + 1]);
        return dist > 1 ? dist : 1;
    }

    private uint ReadHybridUintClusteredLz77(int ctx, JxlBitReader br)
    {
        uint[] window = this.lz77Window;
        if (this.numToCopy > 0)
        {
            return this.CopyFromWindow(window);
        }

        // Covers ReadSymbolWithoutRefill and PeekBits.
        br.Refill();
        uint token = this.ReadSymbolWithoutRefill(ctx, br);
        if (token >= this.lz77Threshold)
        {
            this.numToCopy = unchecked(ReadHybridUintConfig(in this.lz77LengthUint, token - this.lz77Threshold, br) + this.lz77MinLength);

            // Covers ReadSymbolWithoutRefill and PeekBits.
            br.Refill();

            // Distance code.
            uint dToken = this.ReadSymbolWithoutRefill(this.lz77Ctx, br);
            uint distance = ReadHybridUintConfig(in this.configs[this.lz77Ctx], dToken, br);
            if (distance < this.numSpecialDistances)
            {
                distance = this.specialDistances[distance];
            }
            else
            {
                distance = unchecked(distance + 1 - this.numSpecialDistances);
            }

            if (distance > this.numDecoded)
            {
                distance = this.numDecoded;
            }

            if (distance > WindowSize)
            {
                distance = WindowSize;
            }

            this.copyPos = this.numDecoded - distance;
            if (distance == 0)
            {
                // distance 0 -> numDecoded == copyPos == 0
                Array.Clear(window, 0, (int)Math.Min(this.numToCopy, WindowSize));
            }

            if (this.numToCopy < this.lz77MinLength)
            {
                // The length wrapped when lz77MinLength was added. Stop accepting further data so the
                // corruption surfaces at the next bounds check, instead of letting the decoder keep
                // producing phantom symbols from the window.
                this.numToCopy = 0;
                br.MarkUnhealthy();
                return 0;
            }

            return this.CopyFromWindow(window);
        }

        uint ret = ReadHybridUintConfig(in this.configs[ctx], token, br);
        window[unchecked(this.numDecoded++) & WindowMask] = ret;
        return ret;
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private uint CopyFromWindow(uint[] window)
    {
        uint ret = window[unchecked(this.copyPos++) & WindowMask];
        this.numToCopy--;
        window[unchecked(this.numDecoded++) & WindowMask] = ret;
        return ret;
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private uint ReadSymbolWithoutRefill(int histoIdx, JxlBitReader br)
    {
        if (this.usePrefixCode)
        {
            return (uint)this.huffmanData[histoIdx].ReadSymbol(br);
        }

        return this.ReadSymbolAnsWithoutRefill(histoIdx, br);
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private uint ReadSymbolAnsWithoutRefill(int histoIdx, JxlBitReader br)
    {
        uint res = this.state & (JxlAnsParams.AnsTabSize - 1u);

        JxlAliasSymbol symbol = JxlAliasTable.Lookup(this.aliasTables, histoIdx << this.logAlphaSize, res, this.logEntrySize, this.entrySizeMinus1);
        this.state = unchecked((symbol.Freq * (this.state >> JxlAnsParams.AnsLogTabSize)) + symbol.Offset);

        // Renormalize: shift in 16 bits when the state drops below 2^16.
        if (this.state < (1u << 16))
        {
            this.state = (this.state << 16) | (uint)br.PeekBits(16);
            br.Consume(16);
        }

        return (uint)symbol.Value;
    }
}
